using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Beute (Kapitel 13): Questgegenstände bekommt jedes Gruppenmitglied, das sie braucht, direkt. Geld wird
    /// gleichmäßig aufgeteilt. Gegenstände folgen der Beuteregel der Gruppe: Jeder für sich, Reihum, Gruppenbeute
    /// bzw. Bedarf vor Gier (ab der Schwelle wird gewürfelt) oder Plündermeister.
    /// </summary>
    public sealed class LootSystem
    {
        private readonly GameWorld _w;
        public LootSystem(GameWorld world) { _w = world; }

        public void Generate(Creature c, Player tapper)
        {
            c.Loot.Clear();
            var members = _w.Quests.CreditMembers(tapper, c.Position);
            foreach (var entry in c.Template.Loot)
            {
                if (entry.Quest != null)
                {
                    foreach (var m in members)
                    {
                        if (!_w.Quests.NeedsQuestItem(m, entry.Quest, entry.Item) || !_w.Random.Chance(entry.Chance)) continue;
                        int n = _w.Random.Range(entry.Min, entry.Max);
                        if (m.AddItem(entry.Item, n) > 0)
                        {
                            m.Send(new S_LootResult { CorpseId = c.Id, Items = { new ItemStackData { ItemId = entry.Item, Count = n } } });
                            m.Send(m.BuildInventory());
                            _w.Quests.OnItemsChanged(m);
                        }
                    }
                    continue;
                }
                if (_w.Random.Chance(entry.Chance)) c.Loot.Add(new LootItem { ItemId = entry.Item, Count = _w.Random.Range(entry.Min, entry.Max) });
            }
            if (c.Template.MoneyMax > 0) c.LootMoney = _w.Random.Range(c.Template.MoneyMin, c.Template.MoneyMax);
            else if (c.Template.Family == "Humanoid") c.LootMoney = _w.Random.Range(c.Level, c.Level * 4);
            c.Lootable = c.Loot.Count > 0 || c.LootMoney > 0;
        }

        public string? TryLoot(Player looter, Creature c)
        {
            if (!c.IsDead || !c.Lootable) return "Hier gibt es nichts zu holen.";
            if (looter.DistanceTo(c) > 7) return "Zu weit entfernt.";
            var tapper = c.TappedBy;
            if (tapper != null && tapper != looter && (looter.Group == null || looter.Group != tapper.Group)) return "Diese Beute gehört jemand anderem.";
            var members = looter.Group != null ? _w.Quests.CreditMembers(looter, c.Position) : new List<Player> { looter };
            var result = new S_LootResult { CorpseId = c.Id };

            if (c.LootMoney > 0)
            {
                int share = c.LootMoney / members.Count;
                int rest = c.LootMoney - share * members.Count;
                foreach (var m in members)
                {
                    int amount = share + (m == looter ? rest : 0);
                    _w.Economy.Credit(m, amount, "loot", c.Template.Id);
                    if (m != looter && amount > 0) m.Notice($"Euer Anteil: {Money(amount)}");
                    m.Send(m.BuildPlayerState());
                }
                result.Money = c.LootMoney;
                c.LootMoney = 0;
            }

            var group = looter.Group;
            foreach (var item in c.Loot.ToList())
            {
                var def = _w.Content.Items[item.ItemId];
                bool grouped = group != null && members.Count > 1;
                bool special = grouped && def.Quality >= group!.LootThreshold && group.LootMethod != LootMethod.FreeForAll;
                Player recipient = looter;
                if (special && group!.LootMethod == LootMethod.MasterLooter)
                {
                    // Bleibt auf der Leiche, bis der Plündermeister ihn zuteilt.
                    if (looter.Id != group.MasterLooterId) looter.Notice($"{def.Name} wird vom Plündermeister verteilt.");
                    else result.Items.Add(new ItemStackData { ItemId = item.ItemId, Count = item.Count });
                    continue;
                }
                if (special && group!.LootMethod is LootMethod.GroupLoot or LootMethod.NeedBeforeGreed)
                {
                    c.Loot.Remove(item);
                    _w.LootRolls.Start(group, members, item.ItemId, item.Count, c.Template.Id);
                    continue;
                }
                if (grouped && group!.LootMethod == LootMethod.RoundRobin)
                    recipient = members[group.LootRobin++ % members.Count];
                if (recipient.AddItem(item.ItemId, item.Count) < item.Count)
                {
                    if (recipient != looter && looter.AddItem(item.ItemId, item.Count) == item.Count) recipient = looter;
                    else { looter.Notice("Eure Taschen sind voll.", true); continue; }
                }
                c.Loot.Remove(item);
                if (recipient == looter) result.Items.Add(new ItemStackData { ItemId = item.ItemId, Count = item.Count });
                else
                {
                    group?.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"{recipient.Name} erhält {def.Name}." });
                    recipient.Send(new S_LootResult { CorpseId = c.Id, Items = { new ItemStackData { ItemId = item.ItemId, Count = item.Count } } });
                }
                recipient.Send(recipient.BuildInventory());
                _w.Quests.OnItemsChanged(recipient);
                if (recipient.IsBot) _w.Bots.OnBotReceivedItem(recipient, item.ItemId);
                if (def.Quality >= ItemQuality.Rare && group != null)
                    foreach (var b in group.Bots) b.Bot?.OnRareLoot(def);
            }
            looter.Send(result);
            looter.Send(looter.BuildInventory());
            if (c.Loot.Count == 0 && c.LootMoney == 0)
            {
                c.Lootable = false;
                c.CorpseTimer = Math.Min(c.CorpseTimer, 10);
            }
            return null;
        }

        /// <summary>Plündermeister teilt einen Gegenstand von einer Leiche einem Gruppenmitglied in der Nähe zu.</summary>
        public string? MasterLoot(Player master, int corpseId, string itemId, int targetId)
        {
            var g = master.Group;
            if (g == null || g.LootMethod != LootMethod.MasterLooter || g.MasterLooterId != master.Id) return "Ihr seid nicht der Plündermeister.";
            if (master.Zone?.Get(corpseId) is not Creature c || !c.IsDead || master.DistanceTo(c) > 7) return "Keine Leiche in der Nähe.";
            if (c.TappedBy != null && c.TappedBy.Group != g) return "Diese Beute gehört jemand anderem.";
            var item = c.Loot.FirstOrDefault(l => l.ItemId == itemId);
            if (item == null) return "Dieser Gegenstand liegt nicht mehr dort.";
            var target = g.Members.FirstOrDefault(m => m.Id == targetId);
            if (target == null || target.Zone != c.Zone || target.DistanceTo(c) > 100) return "Dieses Gruppenmitglied ist nicht in der Nähe.";
            if (target.AddItem(item.ItemId, item.Count) < item.Count) return $"Die Taschen von {target.Name} sind voll.";
            c.Loot.Remove(item);
            g.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"{target.Name} erhält {_w.Content.Items[item.ItemId].Name} (Plündermeister)." });
            target.Send(target.BuildInventory());
            _w.Quests.OnItemsChanged(target);
            if (target.IsBot) _w.Bots.OnBotReceivedItem(target, item.ItemId);
            if (c.Loot.Count == 0 && c.LootMoney == 0) { c.Lootable = false; c.CorpseTimer = Math.Min(c.CorpseTimer, 10); }
            return null;
        }

        public static string Money(long copper)
        {
            long g = copper / 10000, s = copper / 100 % 100, k = copper % 100;
            var parts = new List<string>();
            if (g > 0) parts.Add($"{g} Gold");
            if (s > 0) parts.Add($"{s} Silber");
            if (k > 0 || parts.Count == 0) parts.Add($"{k} Kupfer");
            return string.Join(" ", parts);
        }
    }
}
