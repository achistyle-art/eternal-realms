using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Dynamische Weltereignisse (Kapitel 37): Überfälle, Bosse, Verteidigungen. Jedes Ereignis kehrt nach einem
    /// zufälligen Intervall wieder (optional nur tags oder nachts), wird regionsweit angekündigt und belohnt alle,
    /// die mitgekämpft haben – auch KI-Mitspieler, die von sich aus hingehen. Ohne Spieler in der Region startet nichts.
    /// </summary>
    public sealed class WorldEventSystem
    {
        public const float ParticipationRange = 1.5f, BroadcastInterval = 2f;

        public sealed class Running
        {
            public WorldEventDef Def = null!;
            public ZoneInstance Zone = null!;
            public WorldEventState State = WorldEventState.Announced;
            public int Phase;
            public int Killed;
            public float Elapsed, PhaseElapsed, WaveTimer, Warmup;
            public readonly List<Creature> Spawned = new();
            public Creature? Defended;
            /// <summary>Beteiligte und ihr Beitrag (Treffer, Heilung, Anwesenheit im Kampf).</summary>
            public readonly Dictionary<Player, int> Contribution = new();
            public WorldEventPhaseDef CurrentPhase => Def.Phases[Math.Min(Phase, Def.Phases.Count - 1)];
        }

        private readonly GameWorld _w;
        private readonly Dictionary<string, double> _nextStart = new();
        private readonly Dictionary<string, Running> _active = new();
        private float _broadcastTimer, _presenceTimer;

        public WorldEventSystem(GameWorld world) { _w = world; }

        public IEnumerable<Running> Active => _active.Values;
        public Running? Get(string id) => _active.TryGetValue(id, out var r) ? r : null;

        private double Interval(WorldEventDef d) =>
            60.0 * (d.IntervalMinutes[0] + _w.Random.NextDouble() * (d.IntervalMinutes[1] - d.IntervalMinutes[0]));

        /// <summary>Ereignis sofort ankündigen (GM-Befehl, Tests). Rückgabe: Fehler oder null.</summary>
        public string? Start(string id, bool skipWarmup = false)
        {
            if (!_w.Content.WorldEvents.TryGetValue(id, out var def)) return "Unbekanntes Ereignis.";
            if (_active.ContainsKey(id)) return "Läuft bereits.";
            var zone = _w.Zone(def.Zone);
            if (zone == null) return "Region nicht geöffnet.";
            var run = new Running { Def = def, Zone = zone, Warmup = skipWarmup ? 0 : def.Warmup };
            _active[id] = run;
            if (def.Announce.Length > 0) zone.BroadcastAll(new S_Chat { Channel = ChatChannel.System, Text = def.Announce });
            Broadcast(run);
            return null;
        }

        public void Update(float dt)
        {
            foreach (var def in _w.Content.WorldEvents.Values)
            {
                if (_active.ContainsKey(def.Id)) continue;
                if (!_nextStart.TryGetValue(def.Id, out var at)) { _nextStart[def.Id] = _w.Time + Interval(def) * 0.5; continue; }
                if (_w.Time < at) continue;
                var zone = _w.Zone(def.Zone);
                // Nur wenn jemand da ist und die Tageszeit passt; sonst kurz darauf erneut prüfen
                if (zone == null || !zone.Players.Any(p => p.IsAlive) || !EnvironmentRules.TimeMatches(def.TimeOfDay, _w.GameHour)) { _nextStart[def.Id] = _w.Time + 60; continue; }
                Start(def.Id);
            }
            _presenceTimer -= dt;
            bool presence = _presenceTimer <= 0;
            if (presence) _presenceTimer = 2;
            foreach (var run in _active.Values.ToList()) Step(run, dt, presence);
            _broadcastTimer -= dt;
            if (_broadcastTimer <= 0)
            {
                _broadcastTimer = BroadcastInterval;
                foreach (var run in _active.Values) Broadcast(run);
            }
        }

        private void Step(Running run, float dt, bool presence)
        {
            if (run.State == WorldEventState.Announced)
            {
                run.Warmup -= dt;
                if (run.Warmup > 0) return;
                run.State = WorldEventState.Running;
                BeginPhase(run);
            }
            run.Elapsed += dt;
            run.PhaseElapsed += dt;
            if (presence) CountPresence(run);
            var ph = run.CurrentPhase;
            switch (ph.Type)
            {
                case WorldEventPhaseType.Kill:
                    run.WaveTimer -= dt;
                    int alive = run.Spawned.Count(c => c.IsAlive && !c.Removed);
                    if (run.WaveTimer <= 0 && run.Killed + alive < ph.Count) { SpawnWave(run, ph, Math.Min(ph.PerWave, ph.Count - run.Killed - alive)); run.WaveTimer = ph.WaveSeconds; }
                    if (run.Killed >= ph.Count) NextPhase(run);
                    break;
                case WorldEventPhaseType.Boss:
                    if (run.Killed >= 1) NextPhase(run);
                    break;
                case WorldEventPhaseType.Defend:
                    if (run.Defended == null || run.Defended.IsDead || run.Defended.Removed) { Finish(run, false); return; }
                    run.WaveTimer -= dt;
                    if (run.WaveTimer <= 0) { SpawnWave(run, ph, ph.PerWave); run.WaveTimer = ph.WaveSeconds; }
                    if (run.PhaseElapsed >= ph.Seconds) NextPhase(run);
                    break;
            }
            if (run.State == WorldEventState.Running && run.Elapsed > run.Def.Duration) Finish(run, false);
        }

        private Vec3 RandomPoint(Running run, float minFrac)
        {
            double ang = _w.Random.NextDouble() * Math.PI * 2, dist = run.Def.Radius * (minFrac + (1 - minFrac) * _w.Random.NextDouble());
            return new Vec3(run.Def.Position.X + (float)(Math.Cos(ang) * dist), run.Def.Position.Y, run.Def.Position.Z + (float)(Math.Sin(ang) * dist));
        }

        private void BeginPhase(Running run)
        {
            var ph = run.CurrentPhase;
            run.Killed = 0;
            run.PhaseElapsed = 0;
            run.WaveTimer = 0;
            if (ph.Type == WorldEventPhaseType.Boss)
            {
                var boss = run.Zone.SpawnCreature(ph.Creature, RandomPoint(run, 0.2f));
                Track(run, boss);
                if (ph.Yell.Length > 0) _w.Chat.NpcSay(boss, ph.Yell, yell: true);
            }
            else if (ph.Yell.Length > 0) run.Zone.BroadcastAll(new S_Chat { Channel = ChatChannel.System, Text = ph.Yell });
            if (ph.Type == WorldEventPhaseType.Defend && run.Defended == null)
            {
                run.Defended = run.Zone.SpawnCreature(ph.Defend, run.Def.Position);
                run.Defended.EventId = run.Def.Id;
            }
            Broadcast(run);
        }

        private void SpawnWave(Running run, WorldEventPhaseDef ph, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var c = run.Zone.SpawnCreature(ph.Creature, RandomPoint(run, 0.7f));
                Track(run, c);
                // Angreifer stürmen auf den zu schützenden NPC bzw. die Mitte des Gebiets
                if (run.Defended != null && !run.Defended.IsDead) c.AI.Engage(run.Defended);
                else c.AI.Patrol = new List<Vec3> { c.Position, run.Def.Position };
            }
        }

        private static void Track(Running run, Creature c)
        {
            c.EventId = run.Def.Id;
            run.Spawned.Add(c);
        }

        private void NextPhase(Running run)
        {
            run.Phase++;
            if (run.Phase >= run.Def.Phases.Count) { Finish(run, true); return; }
            BeginPhase(run);
        }

        /// <summary>Spieler im Ereignisgebiet, die gerade kämpfen, zählen als beteiligt.</summary>
        private static void CountPresence(Running run)
        {
            foreach (var p in run.Zone.PlayersInRange(run.Def.Position, run.Def.Radius * ParticipationRange))
                if (p.IsAlive && p.InCombat) run.Contribution[p] = run.Contribution.GetValueOrDefault(p) + 1;
        }

        /// <summary>Aus dem Kampfsystem: eine Kreatur ist gestorben.</summary>
        public void OnCreatureDied(Creature c, IEnumerable<Unit> attackers)
        {
            if (c.EventId == null || !_active.TryGetValue(c.EventId, out var run)) return;
            if (c == run.Defended) { Finish(run, false); return; }
            if (!run.Spawned.Contains(c)) return;
            run.Killed++;
            foreach (var p in attackers.OfType<Player>()) run.Contribution[p] = run.Contribution.GetValueOrDefault(p) + 3;
            if (c.TappedBy is { } tp) run.Contribution[tp] = run.Contribution.GetValueOrDefault(tp) + 3;
        }

        private void Finish(Running run, bool success)
        {
            run.State = success ? WorldEventState.Success : WorldEventState.Failed;
            _active.Remove(run.Def.Id);
            _nextStart[run.Def.Id] = _w.Time + Interval(run.Def);
            foreach (var c in run.Spawned.Where(c => c.IsAlive && !c.Removed)) run.Zone.Remove(c);
            if (run.Defended != null && !run.Defended.Removed) run.Zone.Remove(run.Defended);
            string text = success ? run.Def.SuccessText : run.Def.FailureText;
            if (text.Length > 0) run.Zone.BroadcastAll(new S_Chat { Channel = ChatChannel.System, Text = text });
            if (success) Reward(run);
            Broadcast(run);
            _w.Log($"Weltereignis {run.Def.Name}: {(success ? "geschafft" : "gescheitert")}, {run.Contribution.Count} Beteiligte.");
        }

        private void Reward(Running run)
        {
            var r = run.Def.Rewards;
            foreach (var (p, score) in run.Contribution)
            {
                if (score < 1 || p.Removed || p.Zone != run.Zone) continue;
                if (r.Xp > 0) _w.Progression.GiveXp(p, Experience.QuestXp(p.Level, run.Def.MaxLevel, r.Xp), "world_event");
                if (r.Money > 0) _w.Economy.Credit(p, r.Money, "world_event", run.Def.Id);
                foreach (var rep in r.Reputation) _w.Progression.AddReputation(p, rep.Faction, rep.Amount);
                var overflow = new List<ItemStack>();
                foreach (var it in r.Items)
                {
                    if (_w.Random.NextDouble() >= it.Chance) continue;
                    int n = _w.Random.Range(it.Min, it.Max);
                    int added = p.AddItem(it.Item, n);
                    if (added < n) overflow.Add(new ItemStack { ItemId = it.Item, Count = n - added, Durability = _w.Content.Items[it.Item].MaxDurability });
                }
                if (overflow.Count > 0 && p.Data.Id > 0) _w.Mail.SendSystem(p.Data.Id, run.Def.Name, "Belohnung", "Eure Taschen waren voll – hier ist Eure Belohnung.", 0, overflow, p);
                p.Notice($"Belohnung für „{run.Def.Name}“ erhalten.");
                p.Send(p.BuildInventory());
                p.Send(p.BuildPlayerState());
                p.Bot?.Gesture("cheer", chance: 0.7, delay: 1);
            }
        }

        private void Broadcast(Running run)
        {
            var ph = run.CurrentPhase;
            int goal = ph.Type == WorldEventPhaseType.Kill ? ph.Count : ph.Type == WorldEventPhaseType.Boss ? 1 : (int)ph.Seconds;
            int progress = ph.Type == WorldEventPhaseType.Defend ? (int)run.PhaseElapsed : run.Killed;
            float left = run.State == WorldEventState.Announced ? run.Warmup : Math.Max(0, run.Def.Duration - run.Elapsed);
            string text = run.State switch
            {
                WorldEventState.Announced => $"Beginnt in {left:0} s",
                WorldEventState.Running => ph.Text,
                WorldEventState.Success => run.Def.SuccessText,
                _ => run.Def.FailureText,
            };
            int participants = run.Contribution.Count(c => c.Value > 0);
            foreach (var p in run.Zone.Players)
            {
                if (p.Connection == null) continue;
                p.Send(new S_WorldEvent
                {
                    EventId = run.Def.Id, Name = run.Def.Name, State = run.State, Text = text,
                    Progress = Math.Min(progress, goal), Goal = goal, SecondsLeft = left, Position = run.Def.Position, Radius = run.Def.Radius,
                    Participants = participants, Participating = run.Contribution.GetValueOrDefault(p) > 0,
                });
            }
        }

        /// <summary>Für KI-Bewohner: laufendes Ereignis in ihrer Region, das zur Stufe passt.</summary>
        public Running? ActiveFor(ZoneInstance zone, Player p) =>
            _active.Values.FirstOrDefault(r => r.Zone == zone && r.State == WorldEventState.Running && p.Level >= r.Def.MinLevel - 2 && p.Level <= r.Def.MaxLevel + 5);
    }
}
