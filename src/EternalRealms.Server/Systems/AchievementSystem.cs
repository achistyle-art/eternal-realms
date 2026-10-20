using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Erfolge und Titel (Kapitel 38). Bewusst ohne Spielvorteil: Belohnung sind Punkte und ein frei wählbarer Titel.
    /// Fortschritt kommt über <see cref="Report"/> aus allen Systemen; KI-Mitspieler sammeln ebenfalls Erfolge
    /// und gratulieren ihrer Gruppe.
    /// </summary>
    public sealed class AchievementSystem
    {
        private readonly GameWorld _w;
        /// <summary>Erfolge je Ereignistyp, damit eine Meldung nur die passenden Bedingungen prüft.</summary>
        private readonly Dictionary<ProgressEvent, List<AchievementDef>> _byEvent = new();

        public AchievementSystem(GameWorld world)
        {
            _w = world;
            foreach (var a in world.Content.Achievements.Values)
                foreach (var e in a.Criteria.Select(c => c.Event).Distinct())
                {
                    if (!_byEvent.TryGetValue(e, out var list)) _byEvent[e] = list = new List<AchievementDef>();
                    list.Add(a);
                }
        }

        public bool Has(Player p, string id) => p.Data.Achievements.TryGetValue(id, out var a) && a.CompletedAt != null;

        public int Points(Player p) => p.Data.Achievements.Where(kv => kv.Value.CompletedAt != null && _w.Content.Achievements.ContainsKey(kv.Key))
            .Sum(kv => _w.Content.Achievements[kv.Key].Points);

        public IEnumerable<string> Titles(Player p) => p.Data.Achievements.Where(kv => kv.Value.CompletedAt != null)
            .Select(kv => _w.Content.Achievements.TryGetValue(kv.Key, out var d) ? d.Title : null).Where(t => t != null).Select(t => t!);

        /// <summary>Fortschritt melden. amount = Anzahl bzw. bei Wert-Ereignissen der aktuelle Stand.</summary>
        public void Report(Player p, ProgressEvent ev, string target, int amount)
        {
            if (!_byEvent.TryGetValue(ev, out var list)) return;
            bool changed = false;
            foreach (var a in list)
            {
                if (a.Faction != null && a.Faction != p.Faction) continue;
                if (!p.Data.Achievements.TryGetValue(a.Id, out var prog)) p.Data.Achievements[a.Id] = prog = new AchievementProgress();
                if (prog.CompletedAt != null) continue;
                while (prog.Counts.Count < a.Criteria.Count) prog.Counts.Add(0);
                for (int i = 0; i < a.Criteria.Count; i++)
                {
                    var c = a.Criteria[i];
                    if (c.Event != ev || (c.Target.Length > 0 && c.Target != target)) continue;
                    int before = prog.Counts[i];
                    prog.Counts[i] = ProgressEvents.IsValue(ev) ? Math.Max(before, Math.Min(amount, c.Count)) : Math.Min(c.Count, before + amount);
                    changed |= prog.Counts[i] != before;
                }
                if (prog.Counts.Select((n, i) => n >= a.Criteria[i].Count).All(x => x)) Complete(p, a, prog);
            }
            if (changed && p.Connection != null && !p.IsBot) _dirty.Add(p);
        }

        private readonly HashSet<Player> _dirty = new();

        /// <summary>Gesammelt einmal je Takt senden, statt bei jedem Treffer.</summary>
        public void Flush()
        {
            foreach (var p in _dirty) if (!p.Removed) p.Send(Build(p));
            _dirty.Clear();
        }

        private void Complete(Player p, AchievementDef a, AchievementProgress prog)
        {
            prog.CompletedAt = DateTime.UtcNow;
            p.Send(new S_AchievementEarned { Id = a.Id, Name = a.Name, Points = a.Points, Title = a.Title ?? "" });
            p.Notice($"Erfolg errungen: {a.Name} ({a.Points} Punkte){(a.Title != null ? $" – neuer Titel: {a.Title}" : "")}");
            var text = new S_Chat { Channel = ChatChannel.System, Text = $"{p.Name} hat den Erfolg [{a.Name}] errungen!" };
            var told = new HashSet<Player> { p };
            if (p.Group != null)
                foreach (var m in p.Group.Members.Where(told.Add)) m.Send(text);
            if (_w.Guilds.GuildOf(p) is { } guild)
                foreach (var m in _w.AllPlayers.Where(o => _w.Guilds.GuildOf(o) == guild && told.Add(o))) m.Send(text);
            // KI-Mitspieler gratulieren – und freuen sich über eigene Erfolge
            if (p.Group != null)
                foreach (var b in p.Group.Bots.Where(b => b != p)) b.Bot?.Say("achievement_congrats", p, delay: 1.5f);
            p.Bot?.Gesture("cheer", chance: 0.8, delay: 1);
            _w.Log($"Erfolg: {p.Name} – {a.Name}");
        }

        public string? SetTitle(Player p, string title)
        {
            if (title.Length > 0 && !Titles(p).Contains(title)) return "Diesen Titel habt Ihr noch nicht.";
            p.Data.ActiveTitle = title;
            p.Zone?.BroadcastAll(p.BuildSpawn(p)); // Namensschild aktualisieren
            p.Send(Build(p));
            return null;
        }

        public S_Achievements Build(Player p) => new()
        {
            Progress = p.Data.Achievements.Where(kv => _w.Content.Achievements.ContainsKey(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new AchievementProgressData
            {
                Id = kv.Key, Counts = kv.Value.Counts.ToList(),
                CompletedAt = kv.Value.CompletedAt is DateTime d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0,
            }).ToList(),
            Titles = Titles(p).OrderBy(t => t, StringComparer.Ordinal).ToList(),
            ActiveTitle = p.Data.ActiveTitle,
            Points = Points(p),
        };
    }
}
