using EternalRealms.Server.Persistence;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.World
{
    /// <summary>
    /// Taschen, Stapel und Bank (Kapitel 14 und 20). Die Taschenplätze liegen hintereinander in Data.Bag:
    /// zuerst der Rucksack (BagSize Plätze), danach die Plätze der angelegten Taschen 1–4 in dieser Reihenfolge.
    /// </summary>
    public sealed partial class Player
    {
        public const int ContainerSlots = 4;
        public const int BankSize = 28;

        public int BagCapacity => BagSize + Data.BagContainers.Values.Sum(c => Content.Items.TryGetValue(c.ItemId, out var d) ? d.BagSlots : 0);

        /// <summary>Erster Platz der Tasche in Taschenplatz <paramref name="container"/> und ihre Größe.</summary>
        private (int start, int size) ContainerRange(int container)
        {
            int start = BagSize;
            for (int i = 0; i < container; i++)
                if (Data.BagContainers.TryGetValue(i, out var c)) start += Content.Items[c.ItemId].BagSlots;
            int size = Data.BagContainers.TryGetValue(container, out var me) ? Content.Items[me.ItemId].BagSlots : 0;
            return (start, size);
        }

        private bool ValidBag(int i) => i >= 0 && i < Data.Bag.Count;

        /// <summary>Verschiebt einen Stapel: auf einen leeren Platz, tauscht mit einem anderen oder legt gleiche Gegenstände zusammen.</summary>
        public string? MoveItem(int from, int to)
        {
            EnsureBagSize();
            if (!ValidBag(from) || !ValidBag(to) || from == to) return "Ungültiger Platz.";
            var a = Data.Bag[from];
            if (a == null) return "Kein Gegenstand.";
            var b = Data.Bag[to];
            if (b != null && b.ItemId == a.ItemId)
            {
                int max = Content.Items[a.ItemId].MaxStack;
                if (b.Count < max)
                {
                    int put = Math.Min(a.Count, max - b.Count);
                    b.Count += put;
                    a.Count -= put;
                    if (a.Count <= 0) Data.Bag[from] = null;
                    return null;
                }
            }
            Data.Bag[from] = b;
            Data.Bag[to] = a;
            return null;
        }

        /// <summary>Teilt <paramref name="count"/> Exemplare auf den ersten freien Platz ab.</summary>
        public string? SplitStack(int from, int count)
        {
            EnsureBagSize();
            if (!ValidBag(from) || Data.Bag[from] is not { } s) return "Kein Gegenstand.";
            if (count <= 0 || count >= s.Count) return "Ungültige Anzahl.";
            int free = Data.Bag.IndexOf(null);
            if (free < 0) return "Eure Taschen sind voll.";
            s.Count -= count;
            Data.Bag[free] = new ItemStack { ItemId = s.ItemId, Count = count, Durability = s.Durability };
            return null;
        }

        /// <summary>Sortiert: Stapel zusammenlegen, dann nach Art, Qualität und Name ordnen.</summary>
        public void SortBags()
        {
            EnsureBagSize();
            var stacks = Data.Bag.Where(s => s != null).Select(s => s!).ToList();
            var merged = new List<ItemStack>();
            foreach (var g in stacks.GroupBy(s => (s.ItemId, s.Durability)))
            {
                int max = Content.Items[g.Key.ItemId].MaxStack;
                int total = g.Sum(s => s.Count);
                while (total > 0)
                {
                    int n = Math.Min(max, total);
                    merged.Add(new ItemStack { ItemId = g.Key.ItemId, Count = n, Durability = g.Key.Durability });
                    total -= n;
                }
            }
            int Order(ItemCategory c) => c switch
            {
                ItemCategory.Quest => 0, ItemCategory.Consumable => 1, ItemCategory.Weapon => 2, ItemCategory.Armor => 3,
                ItemCategory.Container => 4, ItemCategory.TradeGoods => 5, ItemCategory.Reagent => 6, _ => 7,
            };
            merged = merged.OrderBy(s => Order(Content.Items[s.ItemId].Category)).ThenByDescending(s => Content.Items[s.ItemId].Quality)
                .ThenBy(s => Content.Items[s.ItemId].Name, StringComparer.Ordinal).ThenByDescending(s => s.Count).ToList();
            for (int i = 0; i < Data.Bag.Count; i++) Data.Bag[i] = i < merged.Count ? merged[i] : null;
        }

        public string? DestroyItem(int bagIndex)
        {
            if (!ValidBag(bagIndex) || Data.Bag[bagIndex] == null) return "Kein Gegenstand.";
            Data.Bag[bagIndex] = null;
            return null;
        }

        /// <summary>Legt eine Tasche aus dem Inventar in den ersten freien Taschenplatz.</summary>
        public string? EquipBag(int bagIndex)
        {
            EnsureBagSize();
            if (!ValidBag(bagIndex) || Data.Bag[bagIndex] is not { } s) return "Kein Gegenstand.";
            var def = Content.Items[s.ItemId];
            if (def.Category != ItemCategory.Container || def.BagSlots <= 0) return "Das ist keine Tasche.";
            int slot = Enumerable.Range(0, ContainerSlots).FirstOrDefault(i => !Data.BagContainers.ContainsKey(i), -1);
            if (slot < 0) return "Alle Taschenplätze sind belegt.";
            Data.Bag[bagIndex] = null;
            var (start, _) = ContainerRange(slot);
            Data.BagContainers[slot] = s;
            Data.Bag.InsertRange(Math.Min(start, Data.Bag.Count), Enumerable.Repeat<ItemStack?>(null, def.BagSlots));
            return null;
        }

        /// <summary>Nimmt eine leere Tasche ab und legt sie in einen freien Platz außerhalb von ihr.</summary>
        public string? UnequipBag(int container)
        {
            EnsureBagSize();
            if (!Data.BagContainers.TryGetValue(container, out var bag)) return "Dort ist keine Tasche.";
            var (start, size) = ContainerRange(container);
            if (Data.Bag.Skip(start).Take(size).Any(s => s != null)) return "Die Tasche muss leer sein.";
            int free = Enumerable.Range(0, Data.Bag.Count).FirstOrDefault(i => (i < start || i >= start + size) && Data.Bag[i] == null, -1);
            if (free < 0) return "Kein Platz für die Tasche.";
            Data.Bag[free] = bag;
            Data.Bag.RemoveRange(start, size);
            Data.BagContainers.Remove(container);
            return null;
        }

        // ------------------------------------------------------------ Bank

        public void EnsureBankSize()
        {
            while (Data.Bank.Count < BankSize) Data.Bank.Add(null);
        }

        public string? BankDeposit(int bagIndex)
        {
            EnsureBankSize();
            if (!ValidBag(bagIndex) || Data.Bag[bagIndex] is not { } s) return "Kein Gegenstand.";
            var def = Content.Items[s.ItemId];
            if (def.Category == ItemCategory.Quest) return "Questgegenstände bleiben bei Euch.";
            int original = s.Count, left = s.Count;
            foreach (var b in Data.Bank)
                if (b != null && b.ItemId == s.ItemId && b.Durability == s.Durability && b.Count < def.MaxStack && left > 0)
                {
                    int put = Math.Min(left, def.MaxStack - b.Count);
                    b.Count += put;
                    left -= put;
                }
            if (left > 0)
            {
                int free = Data.Bank.IndexOf(null);
                if (free < 0)
                {
                    s.Count = left;
                    return left < original ? null : "Eure Bankfächer sind voll.";
                }
                Data.Bank[free] = new ItemStack { ItemId = s.ItemId, Count = left, Durability = s.Durability };
            }
            Data.Bag[bagIndex] = null;
            return null;
        }

        public string? BankWithdraw(int bankIndex)
        {
            EnsureBankSize();
            EnsureBagSize();
            if (bankIndex < 0 || bankIndex >= Data.Bank.Count || Data.Bank[bankIndex] is not { } s) return "Kein Gegenstand.";
            int free = Data.Bag.IndexOf(null);
            if (free < 0) return "Eure Taschen sind voll.";
            Data.Bag[free] = s;
            Data.Bank[bankIndex] = null;
            return null;
        }

        public S_Bank BuildBank(int npcId)
        {
            EnsureBankSize();
            return new S_Bank { NpcId = npcId, Slots = Data.Bank.Select(s => s?.ToNet() ?? new ItemStackData { ItemId = "", Count = 0 }).ToList() };
        }
    }
}
