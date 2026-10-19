using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Beute: Questgegenstände bekommt jedes Gruppenmitglied, das sie braucht, direkt. Normale Beute liegt auf
    /// der Leiche; Gegenstände ab "Ungewöhnlich" werden reihum an Mitglieder verteilt, die sie nutzen können.
    /// Geld wird gleichmäßig aufgeteilt.
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
                    m.Money += amount;
                    if (m != looter && amount > 0) m.Notice($"Euer Anteil: {Money(amount)}");
                    m.Send(m.BuildPlayerState());
                }
                result.Money = c.LootMoney;
                c.LootMoney = 0;
            }

            foreach (var item in c.Loot.ToList())
            {
                var def = _w.Content.Items[item.ItemId];
                var recipient = looter;
                if (def.Quality >= ItemQuality.Uncommon && members.Count > 1)
                {
                    var eligible = members.Where(m => m.CanEquip(def) == null || !def.IsEquippable).ToList();
                    if (eligible.Count == 0) eligible = members;
                    var g = looter.Group!;
                    recipient = eligible[g.LootRobin++ % eligible.Count];
                }
                if (recipient.AddItem(item.ItemId, item.Count) < item.Count)
                {
                    if (recipient != looter && looter.AddItem(item.ItemId, item.Count) == item.Count) recipient = looter;
                    else { looter.Notice("Eure Taschen sind voll.", true); continue; }
                }
                c.Loot.Remove(item);
                if (recipient == looter) result.Items.Add(new ItemStackData { ItemId = item.ItemId, Count = item.Count });
                else
                {
                    looter.Group?.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"{recipient.Name} erhält {def.Name}." });
                    recipient.Send(new S_LootResult { CorpseId = c.Id, Items = { new ItemStackData { ItemId = item.ItemId, Count = item.Count } } });
                }
                recipient.Send(recipient.BuildInventory());
                _w.Quests.OnItemsChanged(recipient);
                if (recipient.IsBot) _w.Bots.OnBotReceivedItem(recipient, item.ItemId);
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
