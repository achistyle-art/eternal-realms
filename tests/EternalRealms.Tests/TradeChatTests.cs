using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    /// <summary>Kapitel 31/32/46: direkter Handel (atomar, ohne Verdopplung), öffentliche Kanäle, Spamschutz, Gegenstandslinks.</summary>
    public class TradeChatTests
    {
        private static readonly Vec3 Village = new(400, 0, 400);

        private static (TestWorld t, Player a, Player b) Pair()
        {
            var t = new TestWorld();
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village, name: "Haendler");
            var b = t.CreatePlayer(Race.Elarii, CharClass.Mage, 5, Village + new Vec3(2, 0, 0), name: "Kunde");
            t.World.Economy.Credit(a, 5_000, "test");
            t.World.Economy.Credit(b, 5_000, "test");
            return (t, a, b);
        }

        private static int Version(TestWorld t, Player p) => t.Conn(p).Of<S_TradeState>().Last().Version;

        private static void Open(TestWorld t, Player a, Player b)
        {
            Assert.Null(t.World.Trades.Request(a, b.Id));
            Assert.Single(t.Conn(b).Of<S_TradeRequest>());
            Assert.Null(t.World.Trades.Respond(b, true));
            Assert.True(t.World.Trades.IsTrading(a));
        }

        [Fact]
        public void ItemsAndMoneyChangeHandsExactlyOnceAndAreLogged()
        {
            var (t, a, b) = Pair();
            a.AddItem("item_wolf_fang", 4);
            b.AddItem("item_minor_healing_potion", 2);
            Open(t, a, b);
            int fang = a.Data.Bag.FindIndex(s => s?.ItemId == "item_wolf_fang");
            int potion = b.Data.Bag.FindIndex(s => s?.ItemId == "item_minor_healing_potion");
            Assert.Null(t.World.Trades.SetItem(a, 0, fang));
            Assert.Null(t.World.Trades.SetMoney(a, 1200));
            Assert.Null(t.World.Trades.SetItem(b, 2, potion));
            long aMoney = a.Money, bMoney = b.Money, minted = t.World.Economy.Minted;
            Assert.Null(t.World.Trades.Accept(a, true, Version(t, a)));
            Assert.True(t.World.Trades.IsTrading(a)); // wartet auf die Gegenseite
            Assert.Null(t.World.Trades.Accept(b, true, Version(t, b)));
            Assert.False(t.World.Trades.IsTrading(a));
            Assert.Equal(0, a.CountItem("item_wolf_fang"));
            Assert.Equal(4, b.CountItem("item_wolf_fang"));
            Assert.Equal(2, a.CountItem("item_minor_healing_potion"));
            Assert.Equal(0, b.CountItem("item_minor_healing_potion"));
            Assert.Equal(aMoney - 1200, a.Money);
            Assert.Equal(bMoney + 1200, b.Money);
            Assert.Equal(0, t.World.Economy.FlowByReason["trade"]); // Abbuchung und Gutschrift heben sich auf
            Assert.Equal(minted, t.World.Economy.Minted); // Handel schöpft kein Geld
            Assert.True(t.Conn(a).Of<S_TradeClosed>().Single().Completed);

            t.World.Persistence!.Flush(TimeSpan.FromSeconds(5));
            Assert.Contains(t.Store.Ledger, l => l.Reason == "trade_item" && l.Reference.Contains("item_wolf_fang") && l.CharacterId == a.Data.Id);
            Assert.Equal(4, t.Store.GetAsync(b.Data.Id).Result!.Bag.Where(s => s?.ItemId == "item_wolf_fang").Sum(s => s!.Count));
            Assert.Equal(b.Money, t.Store.GetAsync(b.Data.Id).Result!.Money);
        }

        [Fact]
        public void AnyChangeResetsAcceptanceAndStaleConfirmationsAreRejected()
        {
            var (t, a, b) = Pair();
            a.AddItem("item_wolf_fang", 1);
            Open(t, a, b);
            int v = Version(t, b);
            Assert.Null(t.World.Trades.Accept(b, true, v));
            // A legt nach b's Bestätigung noch etwas hinein: b's Bestätigung verfällt
            Assert.Null(t.World.Trades.SetMoney(a, 10));
            Assert.False(t.Conn(a).Of<S_TradeState>().Last().TheirAccepted);
            Assert.Null(t.World.Trades.Accept(a, true, Version(t, a)));
            // b bestätigt mit dem alten Stand → abgelehnt, kein Tausch
            Assert.NotNull(t.World.Trades.Accept(b, true, v));
            Assert.True(t.World.Trades.IsTrading(a));
            Assert.Equal(1, a.CountItem("item_wolf_fang"));
        }

        [Fact]
        public void OfferedItemThatLeavesTheBagIsDroppedFromTheTrade()
        {
            var (t, a, b) = Pair();
            a.AddItem("item_wolf_fang", 3);
            Open(t, a, b);
            int idx = a.Data.Bag.FindIndex(s => s?.ItemId == "item_wolf_fang");
            Assert.Null(t.World.Trades.SetItem(a, 0, idx));
            Assert.Null(a.DestroyItem(idx)); // z. B. verkauft oder zerstört
            t.World.Update(0.05f);
            Assert.Equal("", t.Conn(b).Of<S_TradeState>().Last().TheirItems[0].ItemId);
            Assert.Null(t.World.Trades.Accept(a, true, Version(t, a)));
            Assert.Null(t.World.Trades.Accept(b, true, Version(t, b)));
            Assert.Equal(0, b.CountItem("item_wolf_fang")); // nichts entsteht aus dem Nichts
        }

        [Fact]
        public void BoundItemsFullBagsAndOtherFactionsAreRefused()
        {
            var (t, a, b) = Pair();
            a.AddItem("item_mount_dawn_drake", 1);
            a.AddItem("item_wolf_fang", 1);
            Open(t, a, b);
            Assert.NotNull(t.World.Trades.SetItem(a, 0, a.Data.Bag.FindIndex(s => s?.ItemId == "item_mount_dawn_drake")));
            Assert.Null(t.World.Trades.SetItem(a, 0, a.Data.Bag.FindIndex(s => s?.ItemId == "item_wolf_fang")));
            for (int i = 0; i < b.Data.Bag.Count; i++) b.Data.Bag[i] ??= new ItemStack { ItemId = "item_minor_healing_potion", Count = 1 };
            Assert.Null(t.World.Trades.Accept(a, true, Version(t, a)));
            Assert.Null(t.World.Trades.Accept(b, true, Version(t, b)));
            Assert.True(t.World.Trades.IsTrading(a)); // Ausführung abgelehnt, Handel bleibt offen
            Assert.Equal(1, a.CountItem("item_wolf_fang"));
            t.World.Trades.Cancel(b);
            Assert.False(t.World.Trades.IsTrading(a));

            var foe = t.CreatePlayer(Race.Grakar, CharClass.Warrior, 5, Village + new Vec3(1, 0, 1), name: "Fremder");
            Assert.NotNull(t.World.Trades.Request(a, foe.Id));
        }

        [Fact]
        public void WalkingAwayCancelsTheTrade()
        {
            var (t, a, b) = Pair();
            Open(t, a, b);
            b.Position = b.Position + new Vec3(40, 0, 0);
            t.World.Update(0.05f);
            Assert.False(t.World.Trades.IsTrading(a));
            Assert.False(t.Conn(b).Of<S_TradeClosed>().Single().Completed);
        }

        [Fact]
        public void GroupBotsAcceptGiftsButGiveNothingAway()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village);
            var bot = t.CreateBot(p, Role.Healer);
            Assert.NotNull(t.World.Trades.Request(p, bot.Id)); // nicht in der Gruppe
            t.World.Groups.Create(p);
            t.World.Groups.Join(p.Group!, bot);
            p.AddItem("item_minor_healing_potion", 3);
            Assert.Null(t.World.Trades.Request(p, bot.Id));
            Assert.Null(t.World.Trades.SetItem(p, 0, p.Data.Bag.FindIndex(s => s?.ItemId == "item_minor_healing_potion")));
            Assert.Null(t.World.Trades.Accept(p, true, Version(t, p)));
            Assert.False(t.World.Trades.IsTrading(p));
            Assert.Equal(3, bot.CountItem("item_minor_healing_potion"));
            Assert.Equal(0, p.CountItem("item_minor_healing_potion"));
        }

        // ------------------------------------------------------------ Chat

        [Fact]
        public void LinksAreSanitizedAndRenderedAsNames()
        {
            var db = TestContent.Db;
            var text = $"Verkaufe {ChatLinks.Item("item_wolf_fang")} und |item:gibt_es_nicht| sowie |kaputt";
            var clean = ChatLinks.Sanitize(text, db);
            Assert.Contains("|item:item_wolf_fang|", clean);
            Assert.DoesNotContain("gibt_es_nicht", clean);
            Assert.DoesNotContain("|kaputt", clean);
            Assert.Equal($"Verkaufe [{db.Items["item_wolf_fang"].Name}] und  sowie kaputt", ChatLinks.Plain(clean, db));
            var many = string.Concat(Enumerable.Repeat(ChatLinks.Item("item_wolf_fang"), 9));
            Assert.Equal(ChatLinks.MaxLinks, ChatLinks.Split(ChatLinks.Sanitize(many, db)).Count(x => x.link != null));
            Assert.Equal(2, ChatLinks.Split("a|quest:q_x|b").Count(x => x.link == null));
        }

        [Fact]
        public void TradeChannelOnlyInCitiesAndOnlyForTheOwnFaction()
        {
            var t = new TestWorld();
            var seller = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village, name: "Verkaeufer");
            var buyer = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 5, Village + new Vec3(5, 0, 0), name: "Kaeufer");
            var outside = t.CreatePlayer(Race.Aldorer, CharClass.Priest, 5, Village + new Vec3(300, 0, 300), name: "Wanderer");
            var foe = t.CreatePlayer(Race.Grakar, CharClass.Warrior, 5, new Vec3(420, 0, 420), zone: "zone_aschensteppe", name: "Gegner");
            foreach (var p in new[] { seller, buyer, outside, foe }) t.Conn(p).Clear();

            t.World.Chat.Handle(seller, ChatChannel.Trade, "Verkaufe " + ChatLinks.Item("item_wolf_fang"), "");
            Assert.Single(t.Conn(buyer).Of<S_Chat>(), c => c.Channel == ChatChannel.Trade);
            Assert.Empty(t.Conn(outside).Of<S_Chat>());
            Assert.Empty(t.Conn(foe).Of<S_Chat>()); // andere Fraktion, auch wenn in einer eigenen Stadt

            t.World.Chat.Handle(outside, ChatChannel.Trade, "Hallo?", "");
            Assert.Contains(t.Conn(outside).Of<S_Notice>(), n => n.IsError && n.Text.Contains("Städten"));

            t.World.Chat.Handle(outside, ChatChannel.LookingForGroup, "Suche Gruppe für die Krypta", "");
            Assert.Single(t.Conn(buyer).Of<S_Chat>(), c => c.Channel == ChatChannel.LookingForGroup);
            Assert.Empty(t.Conn(foe).Of<S_Chat>());
        }

        [Fact]
        public void LeavingAChannelStopsMessagesAndIsRemembered()
        {
            var t = new TestWorld();
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village);
            var b = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 5, Village + new Vec3(3, 0, 0));
            Assert.Equal(3, t.Conn(b).Of<S_ChatChannels>().Single().Joined.Count);
            t.World.Chat.SetChannel(b, ChatChannel.General, false);
            Assert.DoesNotContain((byte)ChatChannel.General, t.Conn(b).Of<S_ChatChannels>().Last().Joined);
            t.Conn(b).Clear();
            t.World.Chat.Handle(a, ChatChannel.General, "Hallo Gebiet", "");
            Assert.Empty(t.Conn(b).Of<S_Chat>());
            t.World.Chat.Handle(b, ChatChannel.General, "Ich bin nicht drin", "");
            Assert.Contains(t.Conn(b).Of<S_Notice>(), n => n.IsError);
            Assert.Contains((int)ChatChannel.General, b.Data.Clone().LeftChannels);
            t.Conn(b).Clear();
            t.World.Chat.SetChannel(b, ChatChannel.Party, false); // Gruppenkanal lässt sich nicht verlassen
            Assert.Contains(t.Conn(b).Of<S_Notice>(), n => n.IsError);
        }

        [Fact]
        public void SpamIsThrottledOnPublicChannelsButNotInTheGroup()
        {
            var t = new TestWorld();
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village);
            var b = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 5, Village + new Vec3(3, 0, 0));
            t.Conn(b).Clear();
            for (int i = 0; i < 10; i++) t.World.Chat.Handle(a, ChatChannel.Say, "Nachricht " + i, "");
            Assert.Equal(6, t.Conn(b).Of<S_Chat>().Count());
            t.World.Chat.Handle(b, ChatChannel.Say, "gleich", "");
            t.World.Chat.Handle(b, ChatChannel.Say, "gleich", "");
            Assert.Contains(t.Conn(b).Of<S_Notice>(), n => n.Text.Contains("schon"));
            t.World.Groups.Create(a);
            t.World.Groups.Join(a.Group!, b);
            t.Conn(b).Clear();
            for (int i = 0; i < 10; i++) t.World.Chat.Handle(a, ChatChannel.Party, "Gruppe " + i, "");
            Assert.Equal(10, t.Conn(b).Of<S_Chat>().Count());
        }
    }
}
