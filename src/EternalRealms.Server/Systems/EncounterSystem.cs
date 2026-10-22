using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Bosskämpfe in Dungeons (Kapitel 43–45, zunächst für Gruppen bis 5): Beginn, Sieg und Niederlage, ungezügelte Wut nach
    /// Ablauf der Kampfzeit, Bodeneffekte mit Vorwarnung, Pflicht-Unterbrechungen mit Ansage und eine Auswertung je Kampf
    /// (Dauer, Tode, Treffer durch Bodeneffekte, Unterbrechungen) – Grundlage für die Abstimmung der KI-Mitspieler (Kapitel 48).
    /// </summary>
    public sealed class EncounterSystem
    {
        public const int MaxResults = 50;
        public const float BroadcastRange = 120f;

        public sealed class GroundZone
        {
            public int Id;
            public Creature Boss = null!;
            public ZoneInstance Zone = null!;
            public GroundZoneDef Def = null!;
            public AbilityDef Payload = null!;
            public Vec3 Position;
            public double ArmAt, EndAt, NextTick;
            public bool Armed;

            public bool Contains(Vec3 pos, float margin = 0) => Vec3.DistanceXZ(pos, Position) <= Def.Radius + margin;
            /// <summary>Sekunden bis zur Wirkung (≤ 0 = wirkt bereits).</summary>
            public double TimeToImpact(double now) => Armed ? 0 : ArmAt - now;
        }

        public sealed class Encounter
        {
            public Creature Boss = null!;
            public double Start;
            public int Deaths;
            public bool Enraged;
            public string Phase = "";
            public readonly HashSet<Player> Participants = new();
            public readonly List<GroundZone> Zones = new();
            public readonly Dictionary<string, int> GroundHits = new();
            public int Interrupts, MissedInterrupts;
            public string? PendingCast;
            public double LastBroadcast = -1e9;
        }

        /// <summary>Auswertung eines beendeten Kampfs.</summary>
        public sealed class Result
        {
            public string BossId = "", BossName = "";
            public bool Won;
            public float Duration;
            public int Deaths, Interrupts, MissedInterrupts;
            public Dictionary<string, int> GroundHits = new();
            public int TotalGroundHits => GroundHits.Values.Sum();
            public override string ToString() =>
                $"{BossName}: {(Won ? "Sieg" : "Niederlage")} nach {Duration:0} s, {Deaths} Tod(e), {TotalGroundHits} Treffer durch Bodeneffekte, " +
                $"Unterbrechungen {Interrupts}/{Interrupts + MissedInterrupts}";
        }

        private readonly GameWorld _w;
        private readonly Dictionary<Creature, Encounter> _active = new();
        private readonly List<Result> _results = new();

        public EncounterSystem(GameWorld world) { _w = world; }

        public IReadOnlyList<Result> Results => _results;
        public Encounter? Get(Creature boss) => _active.TryGetValue(boss, out var e) ? e : null;
        public IEnumerable<Encounter> Active => _active.Values;

        /// <summary>Alle Bodeneffekte einer Region (für KI-Mitspieler und Tests).</summary>
        public IEnumerable<GroundZone> ZonesIn(ZoneInstance? zone) =>
            zone == null ? Enumerable.Empty<GroundZone>() : _active.Values.SelectMany(e => e.Zones).Where(z => z.Zone == zone);

        /// <summary>Gefährlichste Fläche an einer Stelle (die zuerst wirkt), oder null.</summary>
        public GroundZone? DangerAt(ZoneInstance? zone, Vec3 pos, float margin = 0.5f) =>
            ZonesIn(zone).Where(z => z.Contains(pos, margin)).OrderBy(z => z.TimeToImpact(_w.Time)).FirstOrDefault();

        public bool IsSafe(ZoneInstance? zone, Vec3 pos, float margin = 1f) => DangerAt(zone, pos, margin) == null;

        /// <summary>Gilt der laufende Zauber dieses Gegners als Pflicht-Unterbrechung?</summary>
        public bool IsMustInterrupt(Unit caster) =>
            caster is Creature c && caster.Cast != null && _active.TryGetValue(c, out var e) && e.PendingCast == caster.Cast.Ability.Id;

        // ------------------------------------------------------------ Ablauf

        public void Start(Creature boss)
        {
            if (!boss.IsBoss || boss.Zone == null || _active.ContainsKey(boss)) return;
            int phase = boss.AI.PhaseIndex;
            var e = new Encounter { Boss = boss, Start = _w.Time, Phase = phase >= 0 && phase < boss.Template.Phases.Count ? boss.Template.Phases[phase].Name : "" };
            _active[boss] = e;
            CollectParticipants(e);
            _w.Log($"Bosskampf beginnt: {boss.Name} ({boss.Zone.Def.Name}) mit {string.Join(", ", e.Participants.Select(p => p.Name))}");
            Broadcast(e, EncounterState.Running, force: true);
        }

        public void OnPhase(Creature boss, string phaseName)
        {
            if (!_active.TryGetValue(boss, out var e)) return;
            e.Phase = phaseName;
            Broadcast(e, EncounterState.Running, force: true);
        }

        /// <summary>Boss setzt sich zurück, ohne zu sterben: die Gruppe ist gescheitert.</summary>
        public void OnEvade(Creature boss)
        {
            if (_active.TryGetValue(boss, out var e)) End(e, won: false);
        }

        public void OnUnitDied(Unit u)
        {
            if (u is Creature c && _active.TryGetValue(c, out var won)) { End(won, won: true); return; }
            if (u is Player p)
                foreach (var e in _active.Values.Where(e => e.Participants.Contains(p))) e.Deaths++;
        }

        public void OnInterrupted(Unit victim, Unit by)
        {
            if (victim is not Creature c || !_active.TryGetValue(c, out var e) || e.PendingCast == null) return;
            if (victim.Cast?.Ability.Id != e.PendingCast) return;
            e.PendingCast = null;
            e.Interrupts++;
        }

        /// <summary>Boss beginnt einen Zauber, der unterbrochen werden muss.</summary>
        public void OnMustInterruptCast(Creature boss, CreatureAbilityDef a, AbilityDef def)
        {
            if (!_active.TryGetValue(boss, out var e)) return;
            e.PendingCast = def.Id;
            Announce(e, (a.Announce ?? "{boss} wirkt {ability} – unterbrechen!").Replace("{boss}", boss.Name).Replace("{ability}", def.Name));
        }

        /// <summary>Bodeneffekt(e) einer Bossfähigkeit platzieren.</summary>
        public void SpawnGround(Creature boss, CreatureAbilityDef a, Unit current)
        {
            var g = a.Ground!;
            var payload = _w.Content.GetAbilityRank(a.Ability, 1);
            if (payload == null || boss.Zone == null) return;
            if (!_active.TryGetValue(boss, out var e)) { Start(boss); if (!_active.TryGetValue(boss, out e)) return; }
            CollectParticipants(e);
            var alive = e.Participants.Where(p => p.IsAlive && p.Zone == boss.Zone).ToList();
            var spots = new List<Vec3>();
            switch (g.Placement)
            {
                case GroundPlacement.Self: spots.Add(boss.Position); break;
                case GroundPlacement.Tank: spots.Add((boss.Threat.Top() ?? current).Position); break;
                case GroundPlacement.Farthest:
                    spots.Add((alive.OrderByDescending(p => boss.DistanceTo(p)).FirstOrDefault() ?? current).Position);
                    break;
                case GroundPlacement.EachPlayer: spots.AddRange(alive.Select(p => p.Position)); break;
                default:
                {
                    var pool = alive.ToList();
                    for (int i = 0; i < g.Count && pool.Count > 0; i++)
                    {
                        var victim = pool[_w.Random.Range(0, pool.Count - 1)];
                        pool.Remove(victim);
                        spots.Add(victim.Position);
                    }
                    if (spots.Count == 0) spots.Add(current.Position);
                    break;
                }
            }
            if (g.Placement is GroundPlacement.Self or GroundPlacement.Tank or GroundPlacement.Farthest)
                while (spots.Count < g.Count) spots.Add(spots[0]);
            foreach (var pos in spots)
            {
                var z = new GroundZone
                {
                    Id = _w.NextEntityId(), Boss = boss, Zone = boss.Zone, Def = g, Payload = payload, Position = pos,
                    ArmAt = _w.Time + g.Warning, EndAt = _w.Time + g.Warning + g.Duration,
                };
                z.NextTick = z.ArmAt;
                e.Zones.Add(z);
                SendZone(z);
            }
            if (a.Announce != null) Announce(e, a.Announce.Replace("{boss}", boss.Name).Replace("{ability}", payload.Name));
        }

        public void Update(float dt)
        {
            if (_active.Count == 0) return;
            foreach (var e in _active.Values.ToList())
            {
                var boss = e.Boss;
                if (boss.Removed || boss.Zone == null) { End(e, won: false, silent: true); continue; }
                CollectParticipants(e);
                // Der Pflicht-Zauber ist durchgekommen (nicht unterbrochen)
                if (e.PendingCast != null && boss.Cast?.Ability.Id != e.PendingCast)
                {
                    e.PendingCast = null;
                    e.MissedInterrupts++;
                }
                float limit = boss.Template.EnrageAfter;
                if (limit > 0 && !e.Enraged && _w.Time - e.Start >= limit)
                {
                    e.Enraged = true;
                    if (_w.Content.GetAbilityRank(boss.Template.EnrageAbility, 1) is AbilityDef enrage) _w.Combat.ApplyAbilityAt(boss, enrage, boss);
                    _w.Chat.NpcSay(boss, $"{boss.Name} gerät in ungezügelte Wut!", true);
                    Announce(e, $"{boss.Name} gerät in ungezügelte Wut!");
                }
                UpdateZones(e);
                Broadcast(e, EncounterState.Running);
            }
        }

        private void UpdateZones(Encounter e)
        {
            foreach (var z in e.Zones.ToList())
            {
                if (!z.Armed && _w.Time >= z.ArmAt)
                {
                    z.Armed = true;
                    if (z.Def.Duration > 0) SendZone(z);
                }
                if (z.Armed && _w.Time >= z.NextTick && _w.Time <= z.EndAt + 1e-6)
                {
                    z.NextTick = _w.Time + z.Def.TickInterval;
                    Hit(e, z);
                }
                if (z.Armed && _w.Time >= z.EndAt)
                {
                    e.Zones.Remove(z);
                    SendZone(z, removed: true);
                }
            }
        }

        private void Hit(Encounter e, GroundZone z)
        {
            foreach (var u in z.Zone.UnitsInRange(z.Position, z.Def.Radius + 0.01f).ToList())
            {
                if (!z.Contains(u.Position) || !e.Boss.CanAttack(u)) continue;
                _w.Combat.ApplyAbilityAt(e.Boss, z.Payload, u);
                if (u is Player p) e.GroundHits[p.Name] = e.GroundHits.GetValueOrDefault(p.Name) + 1;
            }
        }

        private void End(Encounter e, bool won, bool silent = false)
        {
            _active.Remove(e.Boss);
            foreach (var z in e.Zones) SendZone(z, removed: true);
            e.Zones.Clear();
            var r = new Result
            {
                BossId = e.Boss.Template.Id, BossName = e.Boss.Name, Won = won, Duration = (float)(_w.Time - e.Start), Deaths = e.Deaths,
                Interrupts = e.Interrupts, MissedInterrupts = e.MissedInterrupts, GroundHits = new Dictionary<string, int>(e.GroundHits),
            };
            _results.Add(r);
            _w.Metrics.OnEncounter(r);
            if (_results.Count > MaxResults) _results.RemoveAt(0);
            if (silent) return;
            _w.Log("Bosskampf beendet – " + r);
            Broadcast(e, won ? EncounterState.Won : EncounterState.Wiped, force: true);
            if (!won) Announce(e, $"{e.Boss.Name} hat Eure Gruppe besiegt. Erholt Euch und versucht es erneut.");
        }

        // ------------------------------------------------------------ Hilfen

        private void CollectParticipants(Encounter e)
        {
            foreach (var u in e.Boss.CombatWith)
            {
                if (u is Player p) e.Participants.Add(p);
                else if (u is Creature { Owner: Player owner }) e.Participants.Add(owner);
            }
            // Gruppenmitglieder in der Nähe gehören dazu, auch wenn der Boss sie noch nicht angegriffen hat (z. B. Heiler)
            foreach (var p in e.Participants.ToList())
                if (p.Group != null)
                    foreach (var m in p.Group.Members)
                        if (m.Zone == e.Boss.Zone && m.DistanceTo(e.Boss) < BroadcastRange) e.Participants.Add(m);
        }

        private IEnumerable<Player> Audience(Encounter e) =>
            e.Participants.Where(p => !p.IsBot && p.Connection != null)
                .Concat(e.Boss.Zone?.PlayersInRange(e.Boss.Position, BroadcastRange).Where(p => !p.IsBot) ?? Enumerable.Empty<Player>())
                .Distinct();

        private void Broadcast(Encounter e, EncounterState state, bool force = false)
        {
            if (!force && _w.Time - e.LastBroadcast < 1) return;
            e.LastBroadcast = _w.Time;
            float limit = e.Boss.Template.EnrageAfter;
            var m = new S_Encounter
            {
                BossId = e.Boss.Id, Name = e.Boss.Name, Phase = e.Phase, State = state, Elapsed = (float)(_w.Time - e.Start), Deaths = (byte)Math.Min(255, e.Deaths),
                EnrageIn = limit <= 0 ? -1 : (float)Math.Max(0, limit - (_w.Time - e.Start)),
            };
            foreach (var p in Audience(e)) p.Send(m);
        }

        private void Announce(Encounter e, string text)
        {
            foreach (var p in Audience(e))
            {
                p.Send(new S_Chat { Channel = ChatChannel.System, Text = "[Boss] " + text });
                p.Notice(text, true);
            }
        }

        private void SendZone(GroundZone z, bool removed = false)
        {
            var m = new S_GroundEffect
            {
                Id = z.Id, SourceId = z.Boss.Id, Name = z.Payload.Name, Position = z.Position, Radius = z.Def.Radius, Armed = z.Armed, Removed = removed,
                Remaining = (float)Math.Max(0, (z.Armed ? z.EndAt : z.ArmAt) - _w.Time),
            };
            z.Zone.BroadcastInRange(z.Position, ZoneInstance.ViewRange, m);
        }
    }
}
