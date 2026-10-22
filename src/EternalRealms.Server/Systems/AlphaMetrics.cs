using System.Text.Json;
using EternalRealms.Server.World;
using EternalRealms.Shared.Content;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Alpha-Statistik für die Abstimmung: was menschliche Spieler seit dem Serverstart tun – Stufen, Tode, Quests, Bosskämpfe.
    /// Nur Zählwerte ohne Chat oder Kontodaten. Abrufbar mit „.statistik“; mit ER_METRICS_FILE schreibt der Server
    /// alle ER_METRICS_MINUTES Minuten eine JSON-Zeile in die Datei (Auswertung z. B. mit jq oder einer Tabellenkalkulation).
    /// </summary>
    public sealed class AlphaMetrics
    {
        public sealed class BossStats
        {
            public int Wins { get; set; }
            public int Losses { get; set; }
            public int Deaths { get; set; }
            public double TotalWinSeconds { get; set; }
            public double AverageWinSeconds => Wins > 0 ? TotalWinSeconds / Wins : 0;
        }

        public sealed class Snapshot
        {
            public DateTime At { get; set; }
            public string Content { get; set; } = "";
            public double UptimeMinutes { get; set; }
            public int OnlineHumans { get; set; }
            public int OnlineBots { get; set; }
            public Dictionary<string, int> HumansByZone { get; set; } = new();
            /// <summary>Stufen der angemeldeten Menschen in Fünferschritten („1–5“, „6–10“ …).</summary>
            public Dictionary<string, int> LevelBands { get; set; } = new();
            public int LevelUps { get; set; }
            public int QuestsCompleted { get; set; }
            public Dictionary<string, int> TopQuests { get; set; } = new();
            public Dictionary<string, int> DeathsByZone { get; set; } = new();
            public Dictionary<string, BossStats> Bosses { get; set; } = new();
            public double TickAverageMs { get; set; }
            public int TickOverruns { get; set; }
        }

        private readonly GameWorld _w;
        private readonly DateTime _started = DateTime.UtcNow;
        private int _levelUps, _quests;
        private readonly Dictionary<string, int> _questCounts = new(), _deaths = new();
        private readonly Dictionary<string, BossStats> _bosses = new();
        private double _nextWrite;

        public AlphaMetrics(GameWorld world) { _w = world; }

        /// <summary>Aus GameWorld.ReportProgress: zählt nur Menschen.</summary>
        public void OnProgress(Player p, ProgressEvent ev, string target)
        {
            if (p.IsBot) return;
            switch (ev)
            {
                case ProgressEvent.ReachLevel: _levelUps++; break;
                case ProgressEvent.CompleteQuest:
                    _quests++;
                    _questCounts[target] = _questCounts.GetValueOrDefault(target) + 1;
                    break;
                case ProgressEvent.Die:
                    string zone = p.Zone?.Def.Id ?? "?";
                    _deaths[zone] = _deaths.GetValueOrDefault(zone) + 1;
                    break;
            }
        }

        /// <summary>Aus dem Encounter-Manager: jeder Bosskampf mit Ausgang, Dauer und Toden.</summary>
        public void OnEncounter(EncounterSystem.Result r)
        {
            var s = _bosses.TryGetValue(r.BossId, out var b) ? b : _bosses[r.BossId] = new BossStats();
            if (r.Won) { s.Wins++; s.TotalWinSeconds += r.Duration; } else s.Losses++;
            s.Deaths += r.Deaths;
        }

        public Snapshot Take()
        {
            var humans = _w.AllPlayers.Where(p => !p.IsBot && !p.Removed).ToList();
            return new Snapshot
            {
                At = DateTime.UtcNow, Content = _w.Content.Version, UptimeMinutes = Math.Round((DateTime.UtcNow - _started).TotalMinutes, 1),
                OnlineHumans = humans.Count, OnlineBots = _w.AllPlayers.Count(p => p.IsBot && !p.Removed),
                HumansByZone = humans.GroupBy(p => p.Zone?.Def.Id ?? "?").OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                LevelBands = humans.GroupBy(p => (p.Level - 1) / 5).OrderBy(g => g.Key).ToDictionary(g => $"{g.Key * 5 + 1}–{g.Key * 5 + 5}", g => g.Count()),
                LevelUps = _levelUps, QuestsCompleted = _quests,
                TopQuests = _questCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(10).ToDictionary(kv => kv.Key, kv => kv.Value),
                DeathsByZone = _deaths.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
                Bosses = _bosses.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
                TickAverageMs = Math.Round(_w.TickStats.Average, 2), TickOverruns = (int)_w.TickOverruns,
            };
        }

        // Lesbare Zeilen (Umlaute, „+“ der Content-Version, Gedankenstriche) – die Datei wird nie in HTML eingebettet.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string ToJson(Snapshot s) => JsonSerializer.Serialize(s, JsonOptions);

        /// <summary>Kurzfassung für die Spielleitung (.statistik).</summary>
        public static IEnumerable<string> Describe(Snapshot s, ContentDatabase content)
        {
            yield return $"Seit {s.UptimeMinutes:0} min: {s.OnlineHumans} Spieler und {s.OnlineBots} KI-Mitspieler online, {s.LevelUps} Stufenaufstiege, {s.QuestsCompleted} Quests.";
            if (s.LevelBands.Count > 0) yield return "Stufen: " + string.Join(", ", s.LevelBands.Select(kv => $"{kv.Key}: {kv.Value}"));
            if (s.DeathsByZone.Count > 0) yield return "Tode: " + string.Join(", ", s.DeathsByZone.Select(kv => $"{Name(content, kv.Key)} {kv.Value}"));
            foreach (var (id, b) in s.Bosses)
                yield return $"{(content.Creatures.TryGetValue(id, out var c) ? c.Name : id)}: {b.Wins} Siege ({b.AverageWinSeconds:0} s), {b.Losses} Niederlagen, {b.Deaths} Tode";
            yield return $"Spielthread: {s.TickAverageMs:0.0} ms je Takt, {s.TickOverruns} Überläufe.";
        }

        private static string Name(ContentDatabase content, string zoneId) => content.Zones.TryGetValue(zoneId, out var z) ? z.Name : zoneId;

        /// <summary>Im Spielthread aufgerufen; das Schreiben selbst läuft im Hintergrund.</summary>
        public void Update()
        {
            var file = _w.Config.MetricsFile;
            if (string.IsNullOrEmpty(file) || _w.Time < _nextWrite) return;
            _nextWrite = _w.Time + _w.Config.MetricsMinutes * 60;
            string line = ToJson(Take());
            Task.Run(() =>
            {
                try { File.AppendAllText(file, line + "\n"); }
                catch (Exception ex) { _w.Post(() => _w.Log("Statistik nicht geschrieben: " + ex.Message)); }
            });
        }
    }
}
