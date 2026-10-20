using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Auktionshaus je Fraktion (Kapitel 16). Gebote werden sofort vom Bietenden abgezogen (treuhänderisch) und beim
    /// Überbieten per Post erstattet. Waren und Erlöse werden ausschließlich per Post zugestellt, immer in derselben
    /// Transaktion wie die Änderung der Auktion. KI-Bewohner bieten nur ein begrenztes Angebot an und bieten nie mit.
    /// </summary>
    public sealed class AuctionSystem
    {
        public static readonly int[] Durations = { 12, 24, 48 };
        public const double CutRate = 0.05;
        public const int PageSize = 50, MaxPerSeller = 50, MaxBotAuctionsPerHouse = 40, MaxPerBot = 3;

        private readonly GameWorld _w;
        private readonly Dictionary<long, AuctionData> _all = new();
        private long _nextId = 1;
        private float _timer;

        public AuctionSystem(GameWorld world) { _w = world; }

        public IEnumerable<AuctionData> All => _all.Values;

        public void Load(IEnumerable<AuctionData> list)
        {
            foreach (var a in list) { _all[a.Id] = a; _nextId = Math.Max(_nextId, a.Id + 1); }
        }

        private static byte House(Player p) => (byte)p.Faction;

        /// <summary>Einstellgebühr: 15 % / 30 % / 60 % des Händlerwerts für 12 / 24 / 48 Stunden (mindestens 1 Kupfer).</summary>
        public long Deposit(ItemDef def, int count, int hours) => Math.Max(1, (long)Math.Round(def.SellPrice * (long)count * 0.15 * (hours / 12.0)));

        public static long MinBid(AuctionData a) => a.CurrentBid <= 0 ? a.StartBid : a.CurrentBid + Math.Max(1, (long)(a.CurrentBid * 0.05));

        public string? Post(Player p, int bagIndex, int count, long startBid, long buyout, int hours)
        {
            if (!Durations.Contains(hours)) return "Laufzeit 12, 24 oder 48 Stunden.";
            if (bagIndex < 0 || bagIndex >= p.Data.Bag.Count || p.Data.Bag[bagIndex] is not { } stack) return "Kein Gegenstand.";
            var def = _w.Content.Items[stack.ItemId];
            if (def.Category == ItemCategory.Quest || def.BindOnPickup) return "Gebundene Gegenstände können nicht versteigert werden.";
            if (count <= 0 || count > stack.Count) return "Ungültige Anzahl.";
            if (startBid <= 0 || startBid > 10_000_000_000) return "Ungültiges Mindestgebot.";
            if (buyout != 0 && buyout < startBid) return "Der Sofortkaufpreis muss mindestens dem Mindestgebot entsprechen.";
            if (_all.Values.Count(a => a.SellerId == p.Data.Id) >= MaxPerSeller) return "Ihr habt bereits zu viele Angebote.";
            if (p.Data.Id <= 0) return "Charakter noch nicht gespeichert.";
            long deposit = Deposit(def, count, hours);
            if (!_w.Economy.Debit(p, deposit, "auction_deposit", def.Id)) return $"Die Einstellgebühr beträgt {LootSystem.Money(deposit)}.";
            stack.Count -= count;
            if (stack.Count <= 0) p.Data.Bag[bagIndex] = null;
            var a = new AuctionData
            {
                Id = _nextId++, House = House(p), SellerId = p.Data.Id, SellerName = p.Name, SellerIsBot = p.IsBot, ItemId = def.Id, Count = count,
                Durability = stack.Durability, Enchant = stack.Enchant, StartBid = startBid, Buyout = buyout, Deposit = deposit, DurationHours = hours, ExpiresAt = DateTime.UtcNow.AddHours(hours),
            };
            _all[a.Id] = a;
            var c = new EconomyCommit();
            c.UpsertAuctions.Add(a);
            _w.Economy.Commit(c, p);
            p.Send(p.BuildInventory());
            p.Send(p.BuildPlayerState());
            return null;
        }

        public string? Bid(Player p, long auctionId, long amount)
        {
            if (!_all.TryGetValue(auctionId, out var a) || a.ExpiresAt <= DateTime.UtcNow) return "Dieses Angebot gibt es nicht mehr.";
            if (a.House != House(p)) return "Dieses Angebot gehört zu einem anderen Auktionshaus.";
            if (a.SellerId == p.Data.Id) return "Ihr könnt nicht auf eigene Angebote bieten.";
            if (a.BidderId == p.Data.Id) return "Ihr seid bereits Höchstbietender.";
            if (p.IsBot) return "KI-Bewohner bieten nicht mit.";
            var c = new EconomyCommit();
            if (a.Buyout > 0 && amount >= a.Buyout)
            {
                if (!_w.Economy.Debit(p, a.Buyout, "auction_buyout", a.Id.ToString())) return "Ihr habt nicht genug Geld.";
                RefundBidder(a, c, "Ein anderer Käufer hat den Sofortkaufpreis bezahlt.");
                CompleteSale(a, p.Data.Id, p.Name, a.Buyout, c);
                _w.Economy.Commit(c, p);
                p.Send(p.BuildPlayerState());
                p.Notice($"Gekauft: {_w.Content.Items[a.ItemId].Name}. Die Ware kommt per Post.");
                return null;
            }
            if (amount < MinBid(a)) return $"Das Mindestgebot beträgt {LootSystem.Money(MinBid(a))}.";
            if (!_w.Economy.Debit(p, amount, "auction_bid", a.Id.ToString())) return "Ihr habt nicht genug Geld.";
            RefundBidder(a, c, "Ihr wurdet überboten.");
            a.CurrentBid = amount;
            a.BidderId = p.Data.Id;
            a.BidderName = p.Name;
            c.UpsertAuctions.Add(a);
            _w.Economy.Commit(c, p);
            p.Send(p.BuildPlayerState());
            p.Notice("Gebot abgegeben.");
            return null;
        }

        private void RefundBidder(AuctionData a, EconomyCommit c, string reason)
        {
            if (a.BidderId is not long bid || a.CurrentBid <= 0) return;
            c.UpsertMail.Add(_w.Mail.Create(bid, null, "Auktionshaus", $"Gebot erstattet: {_w.Content.Items[a.ItemId].Name}", reason, a.CurrentBid, Array.Empty<ItemStack>()));
            a.BidderId = null;
            a.BidderName = "";
            a.CurrentBid = 0;
        }

        /// <summary>Verkauf abschließen: Ware an den Käufer, Erlös abzüglich 5 % plus Einstellgebühr an den Verkäufer.</summary>
        private void CompleteSale(AuctionData a, long buyerId, string buyerName, long price, EconomyCommit c)
        {
            var def = _w.Content.Items[a.ItemId];
            long cut = (long)Math.Round(price * CutRate);
            c.UpsertMail.Add(_w.Mail.Create(buyerId, null, "Auktionshaus", $"Gewonnen: {def.Name}", $"Ihr habt {def.Name} für {LootSystem.Money(price)} erworben.", 0,
                new[] { new ItemStack { ItemId = a.ItemId, Count = a.Count, Durability = a.Durability, Enchant = a.Enchant } }));
            c.UpsertMail.Add(_w.Mail.Create(a.SellerId, null, "Auktionshaus", $"Verkauft: {def.Name}",
                $"Käufer: {buyerName}. Erlös {LootSystem.Money(price)}, Gebühr {LootSystem.Money(cut)}, Einstellgebühr zurück {LootSystem.Money(a.Deposit)}.",
                price - cut + a.Deposit, Array.Empty<ItemStack>()));
            _w.Economy.RecordSink("auction_cut", cut);
            _all.Remove(a.Id);
            c.DeleteAuctions.Add(a.Id);
        }

        public string? Cancel(Player p, long auctionId)
        {
            if (!_all.TryGetValue(auctionId, out var a) || a.SellerId != p.Data.Id) return "Angebot nicht gefunden.";
            var c = new EconomyCommit();
            if (a.CurrentBid > 0)
            {
                long fee = Math.Max(1, (long)Math.Round(a.CurrentBid * CutRate));
                if (!_w.Economy.Debit(p, fee, "auction_cancel_fee", a.Id.ToString())) return $"Zum Abbrechen sind {LootSystem.Money(fee)} fällig.";
                RefundBidder(a, c, "Der Verkäufer hat die Auktion abgebrochen.");
            }
            ReturnItem(a, c, "Auktion abgebrochen");
            _w.Economy.Commit(c, p);
            p.Send(p.BuildPlayerState());
            return null;
        }

        private void ReturnItem(AuctionData a, EconomyCommit c, string subject)
        {
            var def = _w.Content.Items[a.ItemId];
            c.UpsertMail.Add(_w.Mail.Create(a.SellerId, null, "Auktionshaus", $"{subject}: {def.Name}", "Die Einstellgebühr wird nicht erstattet.", 0,
                new[] { new ItemStack { ItemId = a.ItemId, Count = a.Count, Durability = a.Durability, Enchant = a.Enchant } }));
            _all.Remove(a.Id);
            c.DeleteAuctions.Add(a.Id);
        }

        public void Update(float dt)
        {
            _timer -= dt;
            if (_timer > 0) return;
            _timer = 5;
            var now = DateTime.UtcNow;
            var expired = _all.Values.Where(a => a.ExpiresAt <= now).ToList();
            if (expired.Count == 0) return;
            var c = new EconomyCommit();
            foreach (var a in expired)
            {
                if (a.BidderId is long buyer) CompleteSale(a, buyer, a.BidderName, a.CurrentBid, c);
                else ReturnItem(a, c, "Nicht verkauft");
            }
            _w.Economy.Commit(c);
        }

        // ------------------------------------------------------------ Suche

        private AuctionItemData ToNet(AuctionData a, Player viewer)
        {
            var left = a.ExpiresAt - DateTime.UtcNow;
            byte tl = left.TotalMinutes < 30 ? (byte)0 : left.TotalHours < 2 ? (byte)1 : left.TotalHours < 12 ? (byte)2 : (byte)3;
            return new AuctionItemData
            {
                Id = a.Id, ItemId = a.ItemId, Count = a.Count, Seller = a.SellerName, StartBid = a.StartBid, Buyout = a.Buyout, CurrentBid = a.CurrentBid,
                MinBid = MinBid(a), TimeLeft = tl, IsMine = a.SellerId == viewer.Data.Id, IsMyBid = a.BidderId == viewer.Data.Id, HasBids = a.BidderId != null,
            };
        }

        public S_AuctionList Search(Player p, C_AuctionSearch q)
        {
            var house = House(p);
            string query = (q.Query ?? "").Trim();
            var hits = _all.Values.Where(a => a.House == house && a.ExpiresAt > DateTime.UtcNow).Where(a =>
            {
                var d = _w.Content.Items[a.ItemId];
                if (query.Length > 0 && d.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) return false;
                if (q.Category != 255 && (byte)d.Category != q.Category) return false;
                if (q.MinLevel > 0 && d.RequiredLevel < q.MinLevel) return false;
                if (q.MaxLevel > 0 && d.RequiredLevel > q.MaxLevel) return false;
                if (q.MinQuality != 255 && (byte)d.Quality < q.MinQuality) return false;
                return true;
            }).OrderBy(a => _w.Content.Items[a.ItemId].Name, StringComparer.Ordinal)
              .ThenBy(a => (a.Buyout > 0 ? a.Buyout : MinBid(a)) / (double)a.Count).ToList();
            int page = Math.Max(0, q.Page);
            return new S_AuctionList { Kind = 0, Total = hits.Count, Page = page, Auctions = hits.Skip(page * PageSize).Take(PageSize).Select(a => ToNet(a, p)).ToList() };
        }

        public S_AuctionList Mine(Player p, bool bids)
        {
            var list = _all.Values.Where(a => bids ? a.BidderId == p.Data.Id : a.SellerId == p.Data.Id).OrderBy(a => a.ExpiresAt).ToList();
            return new S_AuctionList { Kind = bids ? (byte)2 : (byte)1, Total = list.Count, Auctions = list.Select(a => ToNet(a, p)).ToList() };
        }

        // ------------------------------------------------------------ KI-Angebot

        /// <summary>
        /// Ein Bewohner stellt gute Funde ein – begrenzt (höchstens 3 je Bot, 40 je Haus), zu einem nachvollziehbaren
        /// Preis um das Zwei- bis Dreieinhalbfache des Händlerwerts. Rückgabe: eingestellt.
        /// </summary>
        public bool TryBotPost(Player bot, int bagIndex)
        {
            if (bot.Data.Id <= 0 || bot.Data.Bag[bagIndex] is not { } s) return false;
            var house = House(bot);
            if (_all.Values.Count(a => a.House == house && a.SellerIsBot) >= MaxBotAuctionsPerHouse) return false;
            if (_all.Values.Count(a => a.SellerId == bot.Data.Id) >= MaxPerBot) return false;
            var def = _w.Content.Items[s.ItemId];
            if (def.SellPrice <= 0) return false;
            long value = (long)def.SellPrice * s.Count;
            long start = Math.Max(1, (long)(value * (2 + _w.Random.NextDouble() * 1.5)));
            long buyout = (long)(start * 1.35);
            return Post(bot, bagIndex, s.Count, start, buyout, _w.Random.Chance(0.5) ? 24 : 48) == null;
        }
    }
}
