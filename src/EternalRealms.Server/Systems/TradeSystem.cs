using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Direkter Handel zwischen zwei Spielern (Kapitel 46). Ablauf: Anfrage → Annahme → beide legen bis zu sechs Stapel
    /// und Geld hinein → beide bestätigen denselben Stand → der Server prüft alles erneut und tauscht in einem Schritt.
    /// Gegen Verdopplung: angebotene Stapel bleiben in der Tasche und werden beim Abschluss per Referenz geprüft;
    /// beide Charaktere und das Protokoll werden in einer Datenbanktransaktion geschrieben.
    /// </summary>
    public sealed class TradeSystem
    {
        public const float MaxDistance = 10f;
        public const double RequestTimeout = 30;

        private sealed class Side
        {
            public Player Player = null!;
            /// <summary>Taschenplatz und der zum Zeitpunkt des Anbietens dort liegende Stapel (Referenz).</summary>
            public readonly (int bag, ItemStack? stack)[] Slots = new (int, ItemStack?)[S_TradeState.Slots];
            public long Money;
            public bool Accepted;
            public Side() { for (int i = 0; i < Slots.Length; i++) Slots[i] = (-1, null); }
            public IEnumerable<(int bag, ItemStack stack)> Offered => Slots.Where(s => s.stack != null).Select(s => (s.bag, s.stack!));
        }

        private sealed class Trade
        {
            public Side A = new(), B = new();
            public int Version = 1;
            public Side Of(Player p) => A.Player == p ? A : B;
            public Side Other(Player p) => A.Player == p ? B : A;
        }

        private readonly GameWorld _w;
        private readonly Dictionary<Player, Trade> _trades = new();
        private readonly Dictionary<Player, (Player from, double at)> _requests = new();

        public TradeSystem(GameWorld world) { _w = world; }

        public bool IsTrading(Player p) => _trades.ContainsKey(p);

        private string? CanTrade(Player a, Player b)
        {
            if (a == b) return "Ihr könnt nicht mit Euch selbst handeln.";
            if (a.Faction != b.Faction) return "Mit der anderen Fraktion kann nicht gehandelt werden.";
            if (a.IsDead || b.IsDead) return "Tote können nicht handeln.";
            if (a.Zone != b.Zone || a.DistanceTo(b) > MaxDistance) return "Ihr seid zu weit entfernt.";
            if (a.InCombat || b.InCombat) return "Im Kampf kann nicht gehandelt werden.";
            if (IsTrading(a) || IsTrading(b)) return "Einer von Euch handelt bereits.";
            return null;
        }

        public string? Request(Player from, int targetId)
        {
            if (from.Zone?.GetUnit(targetId) is not Player to) return "Kein gültiges Ziel.";
            var err = CanTrade(from, to);
            if (err != null) return err;
            if (_w.Social.Ignores(to, from)) return $"{to.Name} ignoriert Euch.";
            if (to.IsBot)
            {
                // KI-Mitspieler handeln nur mit ihrer Gruppe und nehmen nur Geschenke an (siehe BotAccepts)
                if (to.Group == null || to.Group != from.Group) return $"{to.Name} handelt nur mit der eigenen Gruppe.";
                Open(from, to);
                return null;
            }
            _requests[to] = (from, _w.Time);
            to.Send(new S_TradeRequest { FromId = from.Id, FromName = from.Name });
            from.Notice($"Handelsanfrage an {to.Name} gesendet.");
            return null;
        }

        public string? Respond(Player p, bool accept)
        {
            if (!_requests.Remove(p, out var req) || _w.Time - req.at > RequestTimeout || req.from.Removed) return "Keine offene Handelsanfrage.";
            if (!accept) { req.from.Notice($"{p.Name} lehnt den Handel ab."); return null; }
            var err = CanTrade(req.from, p);
            if (err != null) return err;
            Open(req.from, p);
            return null;
        }

        private void Open(Player a, Player b)
        {
            var t = new Trade();
            t.A.Player = a; t.B.Player = b;
            _trades[a] = t; _trades[b] = t;
            SendState(t);
        }

        public string? SetItem(Player p, int slot, int bagIndex)
        {
            if (!_trades.TryGetValue(p, out var t)) return "Ihr handelt gerade nicht.";
            if (slot < 0 || slot >= S_TradeState.Slots) return "Ungültiger Handelsplatz.";
            var side = t.Of(p);
            if (bagIndex < 0) { side.Slots[slot] = (-1, null); Changed(t); return null; }
            if (bagIndex >= p.Data.Bag.Count || p.Data.Bag[bagIndex] is not { } stack) return "Dort liegt nichts.";
            if (side.Slots.Any(s => s.bag == bagIndex && s.stack != null)) return "Dieser Gegenstand liegt schon im Handel.";
            var def = _w.Content.Items[stack.ItemId];
            if (def.BindOnPickup || def.Category == ItemCategory.Quest) return $"{def.Name} ist gebunden und kann nicht gehandelt werden.";
            side.Slots[slot] = (bagIndex, stack);
            Changed(t);
            return null;
        }

        public string? SetMoney(Player p, long money)
        {
            if (!_trades.TryGetValue(p, out var t)) return "Ihr handelt gerade nicht.";
            if (money < 0 || money > p.Money) return "So viel Geld habt Ihr nicht.";
            t.Of(p).Money = money;
            Changed(t);
            return null;
        }

        /// <summary>Bestätigen gilt nur für den Stand, den der Spieler gesehen hat (Version), damit niemand im letzten Moment etwas austauscht.</summary>
        public string? Accept(Player p, bool accept, int version)
        {
            if (!_trades.TryGetValue(p, out var t)) return "Ihr handelt gerade nicht.";
            if (accept && version != t.Version) { SendState(t); return "Der Handel hat sich geändert – bitte erneut prüfen."; }
            t.Of(p).Accepted = accept;
            var other = t.Other(p);
            if (accept && other.Player.IsBot) other.Accepted = BotAccepts(other.Player, t.Of(p), other);
            if (t.A.Accepted && t.B.Accepted) { Execute(t); return null; }
            SendState(t);
            return null;
        }

        public void Cancel(Player p, string reason = "Handel abgebrochen.")
        {
            _requests.Remove(p);
            if (!_trades.TryGetValue(p, out var t)) return;
            Close(t, false, reason);
        }

        private void Changed(Trade t)
        {
            t.Version++;
            t.A.Accepted = t.B.Accepted = false;
            SendState(t);
        }

        /// <summary>Jeden Tick: Entfernung, Tod, Kampf, Abmelden und veränderte Taschen prüfen.</summary>
        public void Update()
        {
            if (_trades.Count == 0 && _requests.Count == 0) return;
            foreach (var t in _trades.Values.Distinct().ToList())
            {
                var a = t.A.Player; var b = t.B.Player;
                if (a.Removed || b.Removed) { Close(t, false, "Der Handelspartner ist nicht mehr da."); continue; }
                if (a.IsDead || b.IsDead || a.Zone != b.Zone || a.DistanceTo(b) > MaxDistance * 1.5f) { Close(t, false, "Handel abgebrochen: zu weit entfernt."); continue; }
                if (a.InCombat || b.InCombat) { Close(t, false, "Handel abgebrochen: Kampf."); continue; }
                bool changed = false;
                foreach (var side in new[] { t.A, t.B })
                    for (int i = 0; i < side.Slots.Length; i++)
                    {
                        var (bag, stack) = side.Slots[i];
                        if (stack == null) continue;
                        // Gegenstand wurde verschoben, verkauft, verschickt oder verbraucht: aus dem Handel nehmen
                        if (bag >= side.Player.Data.Bag.Count || !ReferenceEquals(side.Player.Data.Bag[bag], stack)) { side.Slots[i] = (-1, null); changed = true; }
                    }
                if (t.A.Money > a.Money) { t.A.Money = a.Money; changed = true; }
                if (t.B.Money > b.Money) { t.B.Money = b.Money; changed = true; }
                if (changed) Changed(t);
            }
            foreach (var kv in _requests.Where(r => _w.Time - r.Value.at > RequestTimeout).ToList()) _requests.Remove(kv.Key);
        }

        // ------------------------------------------------------------ Abschluss

        private string? Validate(Side give, Side receive)
        {
            var p = give.Player;
            if (give.Money > p.Money) return $"{p.Name} hat nicht genug Geld.";
            foreach (var (bag, stack) in give.Offered)
                if (bag >= p.Data.Bag.Count || !ReferenceEquals(p.Data.Bag[bag], stack)) return $"Ein Gegenstand von {p.Name} hat sich verändert.";
            // Platz beim Empfänger: freie Plätze plus die Plätze, die er selbst abgibt
            int incoming = give.Offered.Count();
            int free = receive.Player.FreeBagSlots + receive.Offered.Count();
            if (incoming > free) return $"{receive.Player.Name} hat nicht genug Platz in den Taschen.";
            foreach (var (_, stack) in give.Offered)
            {
                var def = _w.Content.Items[stack.ItemId];
                if (def.Unique && (receive.Player.CountItem(def.Id) > 0 || receive.Player.Data.Equipment.Values.Any(e => e.ItemId == def.Id))
                    && !receive.Offered.Any(o => o.stack.ItemId == def.Id))
                    return $"{receive.Player.Name} besitzt {def.Name} bereits (einzigartig).";
            }
            return null;
        }

        private void Execute(Trade t)
        {
            var err = Validate(t.A, t.B) ?? Validate(t.B, t.A);
            if (err != null) { t.A.Accepted = t.B.Accepted = false; t.Version++; SendState(t); Notice(t, err); return; }
            var a = t.A.Player; var b = t.B.Player;
            // 1. Beide Seiten räumen ihre Stapel aus, 2. Stapel wandern als Ganzes (mit Haltbarkeit/Verzauberung) auf freie Plätze
            var fromA = t.A.Offered.ToList(); var fromB = t.B.Offered.ToList();
            foreach (var (bag, _) in fromA) a.Data.Bag[bag] = null;
            foreach (var (bag, _) in fromB) b.Data.Bag[bag] = null;
            Place(b, fromA.Select(x => x.stack));
            Place(a, fromB.Select(x => x.stack));
            // Geld: Abbuchung und Gutschrift mit demselben Grund – netto keine Geldschöpfung
            if (t.A.Money > 0 && _w.Economy.Debit(a, t.A.Money, "trade", b.Name)) _w.Economy.Credit(b, t.A.Money, "trade", a.Name);
            if (t.B.Money > 0 && _w.Economy.Debit(b, t.B.Money, "trade", a.Name)) _w.Economy.Credit(a, t.B.Money, "trade", b.Name);
            var commit = new EconomyCommit();
            foreach (var (_, s) in fromA) commit.Ledger.Add(ItemLog(a, b, s));
            foreach (var (_, s) in fromB) commit.Ledger.Add(ItemLog(b, a, s));
            _w.Economy.Commit(commit, a, b);
            _w.Log($"Handel {a.Name} <-> {b.Name}: {fromA.Count} Stapel + {t.A.Money} Kupfer gegen {fromB.Count} Stapel + {t.B.Money} Kupfer.");
            foreach (var (bot, received) in new[] { (b, fromA), (a, fromB) })
                if (bot.IsBot)
                    for (int i = 0; i < bot.Data.Bag.Count; i++)
                        if (bot.Data.Bag[i] is { } s && received.Any(x => ReferenceEquals(x.stack, s))) _w.Bots.ConsiderEquip(bot, i);
            foreach (var p in new[] { a, b })
            {
                p.MarkStatsDirty();
                p.Send(p.BuildInventory());
                p.Send(p.BuildPlayerState());
            }
            if (b.IsBot) b.Bot?.Say("trade_thanks", a, force: true);
            _w.ReportProgress(a, ProgressEvent.Trade);
            _w.ReportProgress(b, ProgressEvent.Trade);
            Close(t, true, "Handel abgeschlossen.");
        }

        private static void Place(Player p, IEnumerable<ItemStack> stacks)
        {
            p.EnsureBagSize();
            foreach (var s in stacks)
            {
                int free = p.Data.Bag.IndexOf(null);
                // Validate garantiert den Platz; diese Prüfung ist nur die letzte Sicherung gegen Verlust
                if (free < 0) throw new InvalidOperationException("Handel: kein Platz trotz Prüfung.");
                p.Data.Bag[free] = s;
            }
        }

        /// <summary>Gegenstandsbewegungen landen ebenfalls im Wirtschaftsprotokoll (Betrag 0), damit GMs Handel nachvollziehen können.</summary>
        private static LedgerEntry ItemLog(Player from, Player to, ItemStack s) => new()
        {
            CharacterId = from.Data.Id, CharacterName = from.Name, Delta = 0, Balance = from.Money, Reason = "trade_item",
            Reference = $"{s.Count}x {s.ItemId} an {to.Name}",
        };

        /// <summary>KI-Mitspieler nehmen Geschenke an, wenn sie Platz haben; selbst geben sie nichts her.</summary>
        private bool BotAccepts(Player bot, Side human, Side botSide)
        {
            if (botSide.Offered.Any() || botSide.Money > 0) return false;
            if (!human.Offered.Any() && human.Money == 0) return false;
            if (human.Offered.Count() > bot.FreeBagSlots) { bot.Bot?.Say("trade_full", human.Player, force: true); return false; }
            return true;
        }

        private void Close(Trade t, bool completed, string reason)
        {
            _trades.Remove(t.A.Player);
            _trades.Remove(t.B.Player);
            foreach (var p in new[] { t.A.Player, t.B.Player }) p.Send(new S_TradeClosed { Completed = completed, Reason = reason });
        }

        private static void Notice(Trade t, string text) { t.A.Player.Notice(text, true); t.B.Player.Notice(text, true); }

        private void SendState(Trade t)
        {
            foreach (var me in new[] { t.A, t.B })
            {
                var other = me == t.A ? t.B : t.A;
                if (me.Player.Connection == null) continue;
                var st = new S_TradeState
                {
                    Version = t.Version, PartnerId = other.Player.Id, PartnerName = other.Player.Name, PartnerIsBot = other.Player.IsBot,
                    MyMoney = me.Money, TheirMoney = other.Money, MyAccepted = me.Accepted, TheirAccepted = other.Accepted,
                };
                for (int i = 0; i < S_TradeState.Slots; i++)
                {
                    st.MyItems.Add(ToData(me.Slots[i].stack));
                    st.MyBagIndices.Add(me.Slots[i].stack != null ? me.Slots[i].bag : -1);
                    st.TheirItems.Add(ToData(other.Slots[i].stack));
                }
                me.Player.Send(st);
            }
        }

        private static ItemStackData ToData(ItemStack? s) => s == null ? new ItemStackData { ItemId = "", Count = 0 } :
            new ItemStackData { ItemId = s.ItemId, Count = s.Count, Durability = s.Durability, Enchant = s.Enchant };
    }
}
