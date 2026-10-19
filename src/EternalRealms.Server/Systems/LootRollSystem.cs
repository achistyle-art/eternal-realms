using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Bedarf/Gier-Würfe (Kapitel 13). Der Gegenstand verlässt beim Start die Leiche und gehört dem Wurf, bis er genau
    /// einem Gewinner zugestellt ist (Tasche, sonst Post) – er kann weder doppelt noch gar nicht vergeben werden.
    /// KI-Mitspieler würfeln fair: „Bedarf“ nur bei einer echten Verbesserung und nur, wenn kein Mensch Bedarf hat;
    /// „Gier“ nur, wenn kein Mensch den Gegenstand haben will. Sie entscheiden erst, nachdem die Menschen gewählt haben.
    /// </summary>
    public sealed class LootRollSystem
    {
        public const float RollSeconds = 60;

        private sealed class Roll
        {
            public int Id;
            public Group Group = null!;
            public string ItemId = "";
            public int Count;
            public string Source = "";
            public double Deadline;
            public readonly Dictionary<Player, RollChoice?> Choices = new();
            public readonly HashSet<Player> CanNeed = new();
        }

        private readonly GameWorld _w;
        private readonly Dictionary<int, Roll> _rolls = new();
        private int _nextId = 1;

        public LootRollSystem(GameWorld world) { _w = world; }

        public int Active => _rolls.Count;

        public void Start(Group group, List<Player> eligible, string itemId, int count, string source)
        {
            var def = _w.Content.Items[itemId];
            var r = new Roll { Id = _nextId++, Group = group, ItemId = itemId, Count = count, Source = source, Deadline = _w.Time + RollSeconds };
            foreach (var m in eligible)
            {
                r.Choices[m] = null;
                bool usable = !def.IsEquippable || m.CanEquip(def) == null;
                if (group.LootMethod == LootMethod.GroupLoot || usable) r.CanNeed.Add(m);
            }
            _rolls[r.Id] = r;
            foreach (var m in eligible)
                m.Send(new S_LootRollStart { RollId = r.Id, ItemId = itemId, Count = count, Seconds = RollSeconds, CanNeed = r.CanNeed.Contains(m) });
        }

        public string? Choose(Player p, int rollId, RollChoice choice)
        {
            if (!_rolls.TryGetValue(rollId, out var r) || !r.Choices.ContainsKey(p)) return "Dieser Wurf ist vorbei.";
            if (r.Choices[p] != null) return "Ihr habt bereits gewählt.";
            if (choice == RollChoice.Need && !r.CanNeed.Contains(p)) return "Ihr könnt diesen Gegenstand nicht benutzen.";
            r.Choices[p] = choice;
            return null;
        }

        public void Update()
        {
            foreach (var r in _rolls.Values.ToList())
            {
                // Wer die Gruppe verlässt oder die Welt, verzichtet.
                foreach (var m in r.Choices.Keys.ToList())
                    if (r.Choices[m] == null && (m.Removed || m.Group != r.Group)) r.Choices[m] = RollChoice.Pass;
                bool humansDone = r.Choices.Where(kv => !kv.Key.IsBot).All(kv => kv.Value != null);
                if (humansDone || _w.Time >= r.Deadline - 5) DecideBots(r);
                if (r.Choices.Values.All(v => v != null) || _w.Time >= r.Deadline) Resolve(r);
            }
        }

        private void DecideBots(Roll r)
        {
            var def = _w.Content.Items[r.ItemId];
            bool humanNeed = r.Choices.Any(kv => !kv.Key.IsBot && kv.Value == RollChoice.Need);
            bool humanWants = r.Choices.Any(kv => !kv.Key.IsBot && kv.Value is RollChoice.Need or RollChoice.Greed);
            foreach (var bot in r.Choices.Keys.Where(b => b.IsBot && r.Choices[b] == null).ToList())
            {
                RollChoice c = RollChoice.Pass;
                if (!humanNeed && r.CanNeed.Contains(bot) && _w.Bots.IsUpgrade(bot, def)) c = RollChoice.Need;
                else if (!humanWants) c = RollChoice.Greed;
                r.Choices[bot] = c;
            }
        }

        private void Resolve(Roll r)
        {
            _rolls.Remove(r.Id);
            var def = _w.Content.Items[r.ItemId];
            var entries = new List<(Player p, RollChoice c, int v)>();
            foreach (var (p, c) in r.Choices)
            {
                var choice = c ?? RollChoice.Pass;
                entries.Add((p, choice, choice == RollChoice.Pass ? 0 : _w.Random.Range(1, 100)));
            }
            var best = entries.Where(e => e.c == RollChoice.Need).ToList();
            if (best.Count == 0) best = entries.Where(e => e.c == RollChoice.Greed).ToList();
            Player? winner = best.OrderByDescending(e => e.v).Select(e => e.p).FirstOrDefault();
            // Alle verzichten: Der Gegenstand geht reihum an ein Mitglied, damit nichts verloren geht.
            winner ??= r.Choices.Keys.Where(p => !p.Removed && p.Group == r.Group).ElementAtOrDefault(r.Group.LootRobin++ % Math.Max(1, r.Choices.Count))
                       ?? r.Choices.Keys.FirstOrDefault(p => !p.Removed);
            var result = new S_LootRollResult
            {
                RollId = r.Id, ItemId = r.ItemId, WinnerId = winner?.Id ?? 0, WinnerName = winner?.Name ?? "",
                Rolls = entries.Select(e => new LootRollEntryData { Name = e.p.Name, Choice = e.c, Value = e.v, IsBot = e.p.IsBot }).ToList(),
            };
            r.Group.Broadcast(result);
            if (winner == null) return; // niemand mehr in der Welt – sehr selten; Gegenstand verfällt wie ein ungeplünderter Leichnam
            string how = best.Count == 0 ? "(alle verzichtet)" : $"({(best[0].c == RollChoice.Need ? "Bedarf" : "Gier")} {best.Max(e => e.v)})";
            r.Group.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"{winner.Name} gewinnt {def.Name} {how}." });
            if (winner.AddItem(r.ItemId, r.Count) == r.Count)
            {
                winner.Send(winner.BuildInventory());
                _w.Quests.OnItemsChanged(winner);
                if (winner.IsBot) _w.Bots.OnBotReceivedItem(winner, r.ItemId);
                if (def.Quality >= ItemQuality.Rare) foreach (var b in r.Group.Bots) b.Bot?.OnRareLoot(def);
            }
            else
            {
                _w.Mail.SendSystem(winner.Data.Id, "Beute", $"Gewonnen: {def.Name}", "Eure Taschen waren voll – hier ist Eure Beute.", 0,
                    new[] { new ItemStack { ItemId = r.ItemId, Count = r.Count, Durability = def.MaxDurability } }, winner);
                winner.Notice($"Taschen voll – {def.Name} kommt per Post.");
            }
        }
    }
}
