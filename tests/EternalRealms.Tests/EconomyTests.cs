using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;

namespace EternalRealms.Tests
{
    /// <summary>Kapitel 14, 15 und 20: Taschen, Stapel, Bank und protokollierte Geldbewegungen.</summary>
    public class InventoryTests
    {
        [Fact]
        public void BagsAddSlotsAndMustBeEmptyToRemove()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5);
            int baseCap = p.Data.Bag.Count;
            p.AddItem("item_bag_leather", 1);
            int idx = p.Data.Bag.FindIndex(s => s?.ItemId == "item_bag_leather");
            Assert.Null(p.EquipBag(idx));
            Assert.Equal(baseCap + 8, p.Data.Bag.Count);
            Assert.Equal(baseCap + 8, p.BagCapacity);
            // Gegenstand in die neue Tasche legen → Abnehmen verboten
            p.AddItem("item_wolf_fang", 1);
            int fang = p.Data.Bag.FindIndex(s => s?.ItemId == "item_wolf_fang");
            Assert.Null(p.MoveItem(fang, baseCap + 3));
            Assert.NotNull(p.UnequipBag(0));
            Assert.Null(p.MoveItem(baseCap + 3, fang));
            Assert.Null(p.UnequipBag(0));
            Assert.Equal(baseCap, p.Data.Bag.Count);
            Assert.Equal(1, p.CountItem("item_bag_leather"));
        }

        [Fact]
        public void StacksSplitMergeAndSort()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5);
            for (int i = 0; i < p.Data.Bag.Count; i++) p.Data.Bag[i] = null;
            p.AddItem("item_tough_jerky", 15);
            Assert.Null(p.SplitStack(0, 5));
            Assert.Equal(2, p.Data.Bag.Count(s => s?.ItemId == "item_tough_jerky"));
            Assert.NotNull(p.SplitStack(0, 10)); // nicht den ganzen Stapel
            int second = p.Data.Bag.FindIndex(1, s => s?.ItemId == "item_tough_jerky");
            Assert.Null(p.MoveItem(second, 0)); // zusammenlegen
            Assert.Equal(15, p.Data.Bag[0]!.Count);
            p.AddItem("item_wolf_fang", 3);
            p.AddItem("item_minor_healing_potion", 2);
            p.Data.Bag[10] = p.Data.Bag[0]; p.Data.Bag[0] = null;
            p.SortBags();
            Assert.Equal(15, p.CountItem("item_tough_jerky"));
            Assert.Equal(3, p.CountItem("item_wolf_fang"));
            Assert.Equal(ItemCategory.Consumable, TestContent.Db.Items[p.Data.Bag[0]!.ItemId].Category);
            Assert.True(p.Data.Bag.Take(3).All(s => s != null), "Sortiert lückenlos von vorne");
        }

        [Fact]
        public void BankRequiresBankerAndKeepsItems()
        {
            var t = new TestWorld();
            var zone = t.World.Zone("zone_morgentauwald")!;
            var banker = zone.Entities.Values.OfType<Creature>().First(c => c.Template.Id == "npc_mt_banker");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, banker.Position + new Vec3(2, 0, 0));
            p.AddItem("item_wolf_fang", 4);
            int idx = p.Data.Bag.FindIndex(s => s?.ItemId == "item_wolf_fang");
            Assert.NotNull(t.World.Interactions.NpcWithRole(p, banker.Id, NpcRole.Banker));
            Assert.Null(p.BankDeposit(idx));
            Assert.Equal(0, p.CountItem("item_wolf_fang"));
            Assert.Equal(4, p.Data.Bank.Where(s => s != null).Sum(s => s!.Count));
            Assert.Null(p.BankWithdraw(p.Data.Bank.FindIndex(s => s != null)));
            Assert.Equal(4, p.CountItem("item_wolf_fang"));
            t.World.MovePlayerWithinZone(p, banker.Position + new Vec3(60, 0, 0));
            Assert.Null(t.World.Interactions.NpcWithRole(p, banker.Id, NpcRole.Banker));
        }
    }

    public class LedgerTests
    {
        [Fact]
        public void EveryMoneyChangeIsLoggedAndPersistedTogether()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5);
            long start = p.Money;
            t.World.Economy.Credit(p, 500, "test", "a");
            Assert.False(t.World.Economy.Debit(p, start + 10_000, "test")); // nicht genug Geld
            Assert.True(t.World.Economy.Debit(p, 200, "test", "b"));
            Assert.Equal(start + 300, p.Money);
            t.Run(6); // Protokoll wird alle 5 s geschrieben
            Assert.True(t.World.Persistence!.Flush(TimeSpan.FromSeconds(5)));
            var mine = t.Store.Ledger.Where(l => l.CharacterId == p.Data.Id && l.Reason == "test").ToList();
            Assert.Equal(new long[] { 500, -200 }, mine.Select(l => l.Delta));
            Assert.Equal(start + 300, mine[^1].Balance);
            Assert.Equal(300, t.World.Economy.FlowByReason["test"]);
        }

        [Fact]
        public void SavesAreAppliedInOrder()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5);
            for (int i = 0; i < 50; i++) { p.Data.Xp = i; t.World.Save(p); }
            Assert.True(t.World.Persistence!.Flush(TimeSpan.FromSeconds(5)));
            Assert.Equal(49, t.Store.GetAsync(p.Data.Id).Result!.Xp);
        }
    }
}
