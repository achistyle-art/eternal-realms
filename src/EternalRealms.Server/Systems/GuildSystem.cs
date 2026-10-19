using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Gilden (Kapitel 22): gründen, einladen, Ränge, entfernen, Nachricht des Tages, Gildenchat. Ränge: 0 Gildenmeister,
    /// 1 Offizier (einladen, entfernen, befördern bis Veteran), 2 Veteran, 3 Mitglied, 4 Rekrut. KI-Begleiter können
    /// Mitglied werden und sprechen dann gelegentlich im Gildenchat.
    /// </summary>
    public sealed class GuildSystem
    {
        public const long FoundingCost = 10_000; // 1 Gold
        public const int MaxMembers = 500;
        public const int Master = 0, Officer = 1, Veteran = 2, Member = 3, Recruit = 4;

        private readonly GameWorld _w;
        private readonly Dictionary<long, GuildData> _guilds = new();
        private readonly Dictionary<long, GuildData> _byMember = new();
        private readonly Dictionary<long, (long guildId, string from, double until)> _invites = new();
        private long _nextId = 1;

        public GuildSystem(GameWorld world) { _w = world; }

        public void Load(IEnumerable<GuildData> guilds)
        {
            foreach (var g in guilds)
            {
                _guilds[g.Id] = g;
                foreach (var m in g.Members) _byMember[m.CharacterId] = g;
                _nextId = Math.Max(_nextId, g.Id + 1);
            }
        }

        public GuildData? GuildOf(Player p) => p.Data.Id > 0 && _byMember.TryGetValue(p.Data.Id, out var g) ? g : null;
        public GuildData? GuildOf(long characterId) => _byMember.TryGetValue(characterId, out var g) ? g : null;
        private GuildMemberData? Me(GuildData g, Player p) => g.Members.FirstOrDefault(m => m.CharacterId == p.Data.Id);

        private void Persist(GuildData g)
        {
            if (_w.Characters is not ISocialRepository repo || _w.Persistence == null) return;
            var snap = g.Clone();
            _w.Persistence.Enqueue(() => repo.SaveGuildAsync(snap));
        }

        public IEnumerable<Player> OnlineMembers(GuildData g) =>
            g.Members.Select(m => _w.AllPlayers.FirstOrDefault(p => p.Data.Id == m.CharacterId)).Where(p => p != null && !p.Removed)!;

        public void Broadcast(GuildData g, Message m) { foreach (var p in OnlineMembers(g)) p.Send(m); }

        private void Announce(GuildData g, string text) => Broadcast(g, new S_Chat { Channel = ChatChannel.Guild, Text = text, SenderName = "" });

        public string? Create(Player p, string name)
        {
            name = (name ?? "").Trim();
            if (GuildOf(p) != null) return "Ihr seid bereits in einer Gilde.";
            if (p.Data.Id <= 0) return "Charakter noch nicht gespeichert.";
            if (name.Length < 3 || name.Length > 24 || !name.All(c => char.IsLetter(c) || c == ' ' || c == '-' || c == '\'')) return "Gildennamen: 3–24 Buchstaben, Leerzeichen, Bindestrich.";
            if (_guilds.Values.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))) return "Diesen Namen trägt bereits eine Gilde.";
            if (!_w.Economy.Debit(p, FoundingCost, "guild_found", name)) return $"Die Gründung kostet {LootSystem.Money(FoundingCost)}.";
            var g = new GuildData { Id = _nextId++, Name = name, Faction = p.Faction, LeaderId = p.Data.Id };
            g.Members.Add(Entry(p, Master));
            _guilds[g.Id] = g;
            _byMember[p.Data.Id] = g;
            Persist(g);
            p.Notice($"Die Gilde <{name}> wurde gegründet.");
            p.Send(BuildRoster(p));
            return null;
        }

        private static GuildMemberData Entry(Player p, int rank) => new() { CharacterId = p.Data.Id, Name = p.Name, Rank = rank, Level = p.Level, Class = p.Class, IsBot = p.IsBot };

        public string? Invite(Player from, string name)
        {
            var g = GuildOf(from);
            if (g == null) return "Ihr seid in keiner Gilde.";
            if (Me(g, from)!.Rank > Officer) return "Nur Offiziere und der Gildenmeister können einladen.";
            if (g.Members.Count >= MaxMembers) return "Die Gilde ist voll.";
            var target = _w.FindPlayer(name ?? "");
            if (target == null) return $"Niemand namens '{name}' ist online.";
            if (target.Faction != from.Faction) return "Nur Mitglieder der eigenen Fraktion.";
            if (target.Data.Id <= 0) return $"{target.Name} kann gerade nicht beitreten.";
            if (GuildOf(target) != null) return $"{target.Name} ist bereits in einer Gilde.";
            if (target.Data.Ignored.Any(i => i.CharacterId == from.Data.Id)) return $"{target.Name} möchte keine Einladungen von Euch.";
            if (target.IsBot)
            {
                // KI-Begleiter entscheiden nach Vertrautheit.
                bool willing = target.Group != null && target.Group == from.Group || target.Data.Bot!.FavoriteOf.Contains(from.Data.AccountId ?? -1)
                               || target.Data.Bot.Familiarity.GetValueOrDefault(from.Data.AccountId ?? -1) >= 3;
                target.Bot!.Say(willing ? "guild_accept" : "guild_decline", from, force: true);
                if (!willing) return null;
                Join(g, target);
                return null;
            }
            _invites[target.Data.Id] = (g.Id, from.Name, _w.Time + 60);
            target.Send(new S_GuildInvite { GuildName = g.Name, From = from.Name });
            from.Notice($"{target.Name} wurde in die Gilde eingeladen.");
            return null;
        }

        public string? Respond(Player p, bool accept)
        {
            if (!_invites.Remove(p.Data.Id, out var inv) || inv.until < _w.Time || !_guilds.TryGetValue(inv.guildId, out var g)) return "Keine offene Gildeneinladung.";
            if (!accept) { _w.FindPlayer(inv.from)?.Notice($"{p.Name} hat die Gildeneinladung abgelehnt."); return null; }
            if (GuildOf(p) != null) return "Ihr seid bereits in einer Gilde.";
            Join(g, p);
            return null;
        }

        private void Join(GuildData g, Player p)
        {
            g.Members.Add(Entry(p, Recruit));
            _byMember[p.Data.Id] = g;
            Persist(g);
            Announce(g, $"{p.Name} ist der Gilde beigetreten.");
            foreach (var m in OnlineMembers(g)) m.Send(BuildRoster(m));
        }

        public string? Leave(Player p)
        {
            var g = GuildOf(p);
            if (g == null) return "Ihr seid in keiner Gilde.";
            if (g.LeaderId == p.Data.Id && g.Members.Count > 1) return "Übergebt zuerst die Gildenleitung (Rang 0 an ein anderes Mitglied) oder löst die Gilde auf.";
            if (g.Members.Count == 1) return Disband(p);
            Remove(g, p.Data.Id, $"{p.Name} hat die Gilde verlassen.");
            p.Send(new S_GuildRoster());
            return null;
        }

        private void Remove(GuildData g, long characterId, string text)
        {
            g.Members.RemoveAll(m => m.CharacterId == characterId);
            _byMember.Remove(characterId);
            Persist(g);
            Announce(g, text);
            foreach (var m in OnlineMembers(g)) m.Send(BuildRoster(m));
        }

        public string? Kick(Player p, long characterId)
        {
            var g = GuildOf(p);
            if (g == null) return "Ihr seid in keiner Gilde.";
            var me = Me(g, p)!;
            var target = g.Members.FirstOrDefault(m => m.CharacterId == characterId);
            if (target == null) return "Dieses Mitglied gibt es nicht.";
            if (me.Rank > Officer || target.Rank <= me.Rank) return "Dazu fehlt Euch der Rang.";
            Remove(g, characterId, $"{target.Name} wurde von {p.Name} aus der Gilde entfernt.");
            _w.AllPlayers.FirstOrDefault(x => x.Data.Id == characterId)?.Send(new S_GuildRoster());
            return null;
        }

        public string? SetRank(Player p, long characterId, int rank)
        {
            var g = GuildOf(p);
            if (g == null) return "Ihr seid in keiner Gilde.";
            var me = Me(g, p)!;
            var target = g.Members.FirstOrDefault(m => m.CharacterId == characterId);
            if (target == null || target == me) return "Ungültiges Mitglied.";
            if (rank < Master || rank > Recruit) return "Ungültiger Rang.";
            if (rank == Master)
            {
                if (me.Rank != Master) return "Nur der Gildenmeister kann die Leitung übergeben.";
                if (target.IsBot) return "Die Gildenleitung kann nur ein Mensch übernehmen.";
                target.Rank = Master;
                me.Rank = Officer;
                g.LeaderId = target.CharacterId;
            }
            else
            {
                // Offiziere dürfen nur Ränge unter ihrem eigenen vergeben und nur Mitglieder unter sich verändern.
                if (me.Rank > Officer || target.Rank <= me.Rank || rank <= me.Rank) return "Dazu fehlt Euch der Rang.";
                target.Rank = rank;
            }
            Persist(g);
            Announce(g, $"{target.Name} ist jetzt {g.RankNames[target.Rank]}.");
            foreach (var m in OnlineMembers(g)) m.Send(BuildRoster(m));
            return null;
        }

        public string? SetMotd(Player p, string text)
        {
            var g = GuildOf(p);
            if (g == null) return "Ihr seid in keiner Gilde.";
            if (Me(g, p)!.Rank > Officer) return "Nur Offiziere und der Gildenmeister.";
            g.Motd = (text ?? "").Length > 200 ? text!.Substring(0, 200) : text ?? "";
            Persist(g);
            Announce(g, "Nachricht des Tages: " + g.Motd);
            return null;
        }

        public string? Disband(Player p)
        {
            var g = GuildOf(p);
            if (g == null) return "Ihr seid in keiner Gilde.";
            if (g.LeaderId != p.Data.Id) return "Nur der Gildenmeister kann die Gilde auflösen.";
            var online = OnlineMembers(g).ToList();
            Announce(g, $"Die Gilde <{g.Name}> wurde aufgelöst.");
            foreach (var m in g.Members) _byMember.Remove(m.CharacterId);
            _guilds.Remove(g.Id);
            foreach (var m in online) m.Send(new S_GuildRoster());
            if (_w.Characters is ISocialRepository repo && _w.Persistence != null) _w.Persistence.Enqueue(() => repo.DeleteGuildAsync(g.Id));
            return null;
        }

        public void Chat(Player from, S_Chat msg)
        {
            var g = GuildOf(from);
            if (g == null) { from.Notice("Ihr seid in keiner Gilde.", true); return; }
            foreach (var m in OnlineMembers(g))
                if (!_w.Social.Ignores(m, from)) m.Send(msg);
        }

        /// <summary>Beim Einloggen: Stufe/Klasse aktualisieren, Mitgliederliste und Nachricht des Tages senden.</summary>
        public void OnLogin(Player p)
        {
            var g = GuildOf(p);
            if (g == null) return;
            var me = Me(g, p);
            if (me != null) { me.Level = p.Level; me.Name = p.Name; me.Class = p.Class; }
            p.Send(BuildRoster(p));
            if (g.Motd != "") p.Send(new S_Chat { Channel = ChatChannel.Guild, Text = "Nachricht des Tages: " + g.Motd });
        }

        public void OnLevelUp(Player p)
        {
            var g = GuildOf(p);
            var me = g == null ? null : Me(g, p);
            if (me == null) return;
            me.Level = p.Level;
            if (p.Level % 10 == 0)
            {
                Persist(g!);
                if (p.IsBot) p.Bot?.Say("guild_levelup", null, 1.5f, force: true, channel: ChatChannel.Guild);
            }
        }

        public S_GuildRoster BuildRoster(Player p)
        {
            var g = GuildOf(p);
            if (g == null) return new S_GuildRoster();
            return new S_GuildRoster
            {
                GuildId = g.Id, Name = g.Name, Motd = g.Motd, RankNames = g.RankNames.ToList(),
                Members = g.Members.OrderBy(m => m.Rank).ThenBy(m => m.Name).Select(m =>
                {
                    var on = _w.AllPlayers.FirstOrDefault(x => x.Data.Id == m.CharacterId);
                    return new GuildMemberInfo
                    {
                        CharacterId = m.CharacterId, Name = m.Name, Level = on?.Level ?? m.Level, Rank = m.Rank, Class = m.Class, Online = on != null,
                        IsBot = m.IsBot, Note = m.Note, Zone = on != null && on.Data.ShareLocation ? on.Zone?.Def.Name ?? "" : "",
                    };
                }).ToList(),
            };
        }
    }
}
