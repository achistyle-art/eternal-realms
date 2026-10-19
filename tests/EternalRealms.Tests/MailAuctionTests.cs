using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    public class MailTests
    {
        private static (TestWorld t, GameObjectEntity box, Player a, Player b) Setup()
        {
            var t = new TestWorld();
            var box = t.World.Zone("zone_morgentauwald")!.Entities.Values.OfType<GameObjectEntity>().First(o => o.Def.IsMailbox);
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, box.Position + new Vec3(1, 0, 0), name: "Absender");
            var b = t.CreatePlayer(Race.Elarii, CharClass.Mage, 5, box.Position + new Vec3(-1, 0, 0), name: "Empfaenger");
            t.World.Economy.Credit(a, 10_000, "test");
            return (t, box, a, b);
        }

        [Fact]
        public void MoneyAndItemsArriveAndAreTakenExactlyOnce()
        {
            var (t, box, a, b) = Setup();
            b.Data.AccountId = a.Data.AccountId; // eigene Charaktere: ohne Verzögerung
            a.AddItem("item_wolf_fang", 3);
            int idx = a.Data.Bag.FindIndex(s => s?.ItemId == "item_wolf_fang");
            long before = a.Money;
            t.World.Mail.Send(a, new C_MailSend { ObjectId = box.Id, To = "Empfaenger", Subject = "Hallo", Money = 1000, BagIndices = { idx } });
            Assert.Equal(0, a.CountItem("item_wolf_fang"));
            Assert.Equal(before - 1000 - 30, a.Money); // Geld + Porto
            var mail = Assert.Single(t.World.Mail.Inbox(b.Data.Id));
            long bMoney = b.Money;
            Assert.Null(t.World.Mail.Take(b, mail.Id, 255));
            Assert.Equal(3, b.CountItem("item_wolf_fang"));
            Assert.Equal(bMoney + 1000, b.Money);
            t.World.Mail.Take(b, mail.Id, 255); // zweites Entnehmen bringt nichts
            Assert.Equal(3, b.CountItem("item_wolf_fang"));
            Assert.Equal(bMoney + 1000, b.Money);
            Assert.Null(t.World.Mail.Delete(b, mail.Id));
            Assert.Empty(t.World.Mail.Inbox(b.Data.Id));
        }

        [Fact]
        public void ItemsToOtherAccountsAreDelayedAndPersistedAtomically()
        {
            var (t, box, a, b) = Setup();
            a.AddItem("item_wolf_fang", 2);
            int idx = a.Data.Bag.FindIndex(s => s?.ItemId == "item_wolf_fang");
            t.World.Mail.Send(a, new C_MailSend { ObjectId = box.Id, To = "Empfaenger", Subject = "Paket", BagIndices = { idx } });
            Assert.Empty(t.World.Mail.Inbox(b.Data.Id)); // eine Stunde unterwegs
            Assert.True(t.World.Persistence!.Flush(TimeSpan.FromSeconds(5)));
            var stored = t.Store.GetAsync(a.Data.Id).Result!;
            Assert.DoesNotContain(stored.Bag, s => s?.ItemId == "item_wolf_fang"); // Absender gespeichert ohne Gegenstand …
            var mails = t.Store.LoadMailAsync().Result;
            Assert.Contains(mails, m => m.RecipientId == b.Data.Id && m.Items.Any(i => i.ItemId == "item_wolf_fang" && i.Count == 2)); // … Brief mit Gegenstand
        }

        [Fact]
        public void OfflineRecipientsAreFoundAndBoundItemsRejected()
        {
            var (t, box, a, b) = Setup();
            long bId = b.Data.Id;
            t.World.Logout(b);
            t.Run(0.5);
            t.World.Mail.Send(a, new C_MailSend { ObjectId = box.Id, To = "Empfaenger", Subject = "Offline", Money = 50 });
            t.Run(1, () => t.World.Mail.Inbox(bId).Count > 0);
            Assert.Single(t.World.Mail.Inbox(bId));
            a.AddItem("item_hearthstone", 1); // gebunden/Questgegenstand? Ruhestein ist einzigartig, aber nicht gebunden
            t.World.Mail.Send(a, new C_MailSend { ObjectId = box.Id, To = "Niemand", Subject = "x" });
            t.Run(1);
            Assert.Contains(t.Conn(a).Of<S_Notice>(), n => n.Text.Contains("nicht gefunden"));
        }
    }

    public class AuctionTests
    {
        [Fact]
        public void BidOutbidBuyoutAndPayouts()
        {
            var t = new TestWorld();
            var zone = t.World.Zone("zone_morgentauwald")!;
            var npc = zone.Entities.Values.OfType<Creature>().First(c => c.Template.Id == "npc_mt_auctioneer");
            var seller = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, npc.Position + new Vec3(1, 0, 0), name: "Haendler");
            var b1 = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 10, npc.Position + new Vec3(2, 0, 0), name: "Bieterin");
            var b2 = t.CreatePlayer(Race.Aldorer, CharClass.Rogue, 10, npc.Position + new Vec3(3, 0, 0), name: "Kaeufer");
            foreach (var p in new[] { seller, b1, b2 }) t.World.Economy.Credit(p, 100_000, "test");
            seller.AddItem("item_bag_leather", 1);
            int idx = seller.Data.Bag.FindIndex(s => s?.ItemId == "item_bag_leather");
            long sellerStart = seller.Money;
            Assert.NotNull(t.World.Auctions.Post(seller, idx, 1, 1000, 500, 24)); // Sofortkauf < Mindestgebot
            Assert.Null(t.World.Auctions.Post(seller, idx, 1, 1000, 5000, 24));
            var a = Assert.Single(t.World.Auctions.All);
            long deposit = a.Deposit;
            Assert.Equal(sellerStart - deposit, seller.Money);
            Assert.Equal(0, seller.CountItem("item_bag_leather"));

            Assert.NotNull(t.World.Auctions.Bid(b1, a.Id, 500)); // unter Mindestgebot
            Assert.Null(t.World.Auctions.Bid(b1, a.Id, 1000));
            Assert.Equal(99_000, b1.Money);
            Assert.NotNull(t.World.Auctions.Bid(b2, a.Id, 1020)); // < +5 %
            Assert.Null(t.World.Auctions.Bid(b2, a.Id, 5000)); // Sofortkauf
            Assert.Empty(t.World.Auctions.All);
            Assert.Equal(95_000, b2.Money);

            var refund = Assert.Single(t.World.Mail.Inbox(b1.Data.Id));
            Assert.Equal(1000, refund.Money);
            var goods = Assert.Single(t.World.Mail.Inbox(b2.Data.Id));
            Assert.Equal("item_bag_leather", goods.Items.Single().ItemId);
            var payout = Assert.Single(t.World.Mail.Inbox(seller.Data.Id));
            Assert.Equal(5000 - 250 + deposit, payout.Money);
            Assert.Equal(-250, t.World.Economy.FlowByReason["auction_cut"]);
        }

        [Fact]
        public void ExpiredAuctionsReturnTheItemOrSellToTheHighestBidder()
        {
            var t = new TestWorld();
            var npc = t.World.Zone("zone_morgentauwald")!.Entities.Values.OfType<Creature>().First(c => c.Template.Id == "npc_mt_auctioneer");
            var seller = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, npc.Position, name: "Verkaeufer");
            var bidder = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 10, npc.Position, name: "Bieter");
            t.World.Economy.Credit(seller, 10_000, "test");
            t.World.Economy.Credit(bidder, 10_000, "test");
            seller.AddItem("item_bag_linen", 2);
            int idx = seller.Data.Bag.FindIndex(s => s?.ItemId == "item_bag_linen");
            Assert.Null(t.World.Auctions.Post(seller, idx, 1, 100, 0, 12));
            idx = seller.Data.Bag.FindIndex(s => s?.ItemId == "item_bag_linen");
            Assert.Null(t.World.Auctions.Post(seller, idx, 1, 100, 0, 12));
            var list = t.World.Auctions.All.ToList();
            Assert.Null(t.World.Auctions.Bid(bidder, list[0].Id, 150));
            foreach (var a in list) a.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            t.Run(6);
            Assert.Empty(t.World.Auctions.All);
            var sellerMail = t.World.Mail.Inbox(seller.Data.Id);
            Assert.Contains(sellerMail, m => m.Items.Any(i => i.ItemId == "item_bag_linen")); // unverkauft zurück
            Assert.Contains(sellerMail, m => m.Money > 0); // Erlös des verkauften
            Assert.Contains(t.World.Mail.Inbox(bidder.Data.Id), m => m.Items.Any(i => i.ItemId == "item_bag_linen"));
            var search = t.World.Auctions.Search(bidder, new C_AuctionSearch { Query = "beutel" });
            Assert.Equal(0, search.Total);
        }
    }
}
