using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    public enum CheatKind { Speed, Flood, InvalidAction, GoldRate }

    /// <summary>
    /// Erkennung von Unregelmäßigkeiten (Kapitel 49). Der Server setzt die Regeln ohnehin selbst durch (Bewegung wird
    /// begrenzt, unmögliche Aktionen abgelehnt); hier wird nur bewertet, wie auffällig ein Spieler ist. Jede Auffälligkeit
    /// bringt Punkte, die mit der Zeit abklingen – einzelne Ausreißer durch Verzögerungen fallen so nicht ins Gewicht.
    /// Ab <see cref="ServerConfig.CheatFlagScore"/> wird der Fall für Spielleiter markiert und protokolliert, ab
    /// <see cref="ServerConfig.CheatKickScore"/> die Verbindung getrennt. Gesperrt wird nie automatisch – das entscheidet ein Mensch.
    /// </summary>
    public sealed class AntiCheatSystem
    {
        /// <summary>Punkte, die pro Sekunde abklingen.</summary>
        public const double DecayPerSecond = 1.0;
        public const int MaxFlags = 200;

        public sealed class Record
        {
            public double Score;
            public double LastUpdate;
            public bool Flagged;
            public readonly Dictionary<CheatKind, int> Counts = new();
            public readonly Queue<(double Time, long Amount)> Income = new();
            public double LastGoldReport = -1e9;
        }

        public sealed class Flag
        {
            public DateTime At = DateTime.UtcNow;
            public string Name = "";
            public long CharacterId;
            public CheatKind Kind;
            public string Detail = "";
            public int Score;
            public bool Kicked;
        }

        private readonly GameWorld _w;
        private readonly Dictionary<long, Record> _records = new();
        private readonly List<Flag> _flags = new();

        /// <summary>Geld aus diesen Quellen entsteht neu; Übertragungen zwischen Spielern (Handel, Post) zählen nicht.</summary>
        private static readonly HashSet<string> GeneratedIncome = new() { "loot", "loot_background", "quest", "vendor_sell", "world_event" };

        public AntiCheatSystem(GameWorld world) { _w = world; }

        public IReadOnlyList<Flag> Flags => _flags;

        public static int DefaultPoints(CheatKind kind) => kind switch
        {
            CheatKind.Speed => 4,
            CheatKind.Flood => 15,
            CheatKind.InvalidAction => 3,
            CheatKind.GoldRate => 40,
            _ => 1,
        };

        public double ScoreOf(Player p) => _records.TryGetValue(p.Data.Id, out var r) ? Decayed(r) : 0;

        private double Decayed(Record r) => Math.Max(0, r.Score - (_w.Time - r.LastUpdate) * DecayPerSecond);

        public void Report(Player p, CheatKind kind, string detail, int? points = null)
        {
            if (p.IsBot || p.Data.Id <= 0) return;
            if (!_records.TryGetValue(p.Data.Id, out var r)) _records[p.Data.Id] = r = new Record { LastUpdate = _w.Time };
            r.Score = Decayed(r) + (points ?? DefaultPoints(kind));
            r.LastUpdate = _w.Time;
            r.Counts[kind] = r.Counts.GetValueOrDefault(kind) + 1;
            if (r.Score >= _w.Config.CheatKickScore)
            {
                AddFlag(p, kind, detail, r, kicked: true);
                r.Score = _w.Config.CheatFlagScore; // bleibt markiert, beginnt aber nicht sofort wieder an der Schwelle
                // Erst zu Beginn des nächsten Takts trennen: die gerade laufende Aktion (z. B. eine Buchung) läuft sauber zu Ende.
                _w.Post(() =>
                {
                    if (p.Zone == null && !p.IsLinkdead) return;
                    var session = _w.Sessions.ForPlayer(p);
                    if (session != null) session.Kick("Verbindung wegen auffälliger Spieldaten getrennt.");
                    else _w.Logout(p);
                });
            }
            else if (r.Score >= _w.Config.CheatFlagScore && !r.Flagged)
            {
                r.Flagged = true;
                AddFlag(p, kind, detail, r, kicked: false);
            }
        }

        private void AddFlag(Player p, CheatKind kind, string detail, Record r, bool kicked)
        {
            var counts = string.Join(", ", r.Counts.Select(kv => $"{kv.Key}×{kv.Value}"));
            var flag = new Flag { Name = p.Name, CharacterId = p.Data.Id, Kind = kind, Detail = detail, Score = (int)r.Score, Kicked = kicked };
            _flags.Add(flag);
            if (_flags.Count > MaxFlags) _flags.RemoveAt(0);
            string text = $"{p.Name}: {kind} ({detail}) – Punkte {(int)r.Score}, bisher {counts}{(kicked ? " – getrennt" : "")}";
            _w.Log("Anti-Cheat: " + text);
            _w.Audit(new AuditEntry { Actor = "Anti-Cheat", Action = kicked ? "anticheat.kick" : "anticheat.flag", Target = p.Name, Detail = text, Ip = p.Connection?.RemoteIp ?? "" });
            foreach (var gm in _w.AllPlayers.Where(x => x.IsGm && x.Connection != null))
                gm.Send(new S_Chat { Channel = ChatChannel.System, Text = "[Anti-Cheat] " + text });
        }

        /// <summary>Geldeingang (aus <see cref="EconomySystem"/>): neu entstandenes Geld pro Stunde mit der Stufe vergleichen.</summary>
        public void OnIncome(Player p, long amount, string reason)
        {
            if (p.IsBot || amount <= 0 || !GeneratedIncome.Contains(reason)) return;
            if (!_records.TryGetValue(p.Data.Id, out var r)) _records[p.Data.Id] = r = new Record { LastUpdate = _w.Time };
            r.Income.Enqueue((_w.Time, amount));
            while (r.Income.Count > 0 && _w.Time - r.Income.Peek().Time > 3600) r.Income.Dequeue();
            long hour = r.Income.Sum(i => i.Amount);
            long limit = _w.Config.GoldPerHourPerLevel * Math.Max(1, p.Level);
            if (hour > limit && _w.Time - r.LastGoldReport > 600)
            {
                r.LastGoldReport = _w.Time;
                Report(p, CheatKind.GoldRate, $"{hour / 10000.0:0.##} Gold in der letzten Stunde (Grenze {limit / 10000.0:0.##})");
            }
        }

        /// <summary>Abmelden setzt nichts zurück; nur abgeklungene, unauffällige Einträge werden entfernt.</summary>
        public void Cleanup()
        {
            foreach (var r in _records.Values)
                while (r.Income.Count > 0 && _w.Time - r.Income.Peek().Time > 3600) r.Income.Dequeue();
            foreach (var id in _records.Where(kv => Decayed(kv.Value) <= 0 && kv.Value.Income.Count == 0 && !kv.Value.Flagged).Select(kv => kv.Key).ToList())
                _records.Remove(id);
        }
    }
}
