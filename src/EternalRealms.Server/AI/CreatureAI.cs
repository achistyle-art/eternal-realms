using EternalRealms.Server.World;
using EternalRealms.Shared.Rules;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.AI
{
    public enum CreatureState { Idle, Combat, Evade, Flee }

    /// <summary>
    /// Verhalten von Monstern, NPCs, Begleitern und Eskorten. Monster erkennen Spieler im Aggroradius,
    /// rufen Hilfe, nutzen Fähigkeiten mit Abklingzeiten, fliehen, kehren nach dem Kampf heim (Leine)
    /// und Bosse wechseln anhand ihres Lebens die Kampfphase.
    /// </summary>
    public sealed class CreatureAI
    {
        private readonly Creature _c;
        public CreatureState State { get; private set; } = CreatureState.Idle;
        public List<Vec3> Patrol { get; set; } = new();
        public float WanderRadius { get; set; }
        public int PhaseIndex { get; private set; } = -1;

        private int _patrolIndex;
        private Vec3? _wanderTarget;
        private float _wanderTimer, _aggroTimer, _pauseTimer, _fleeTimer, _abilityRetry;
        private float _barkTimer = 60;
        private float _despawnAfter = -1;
        private bool _fled;
        private readonly Dictionary<CreatureAbilityDef, float> _timers = new();

        public CreatureAI(Creature c) { _c = c; _barkTimer = 30 + (StableHash.Of(c.Template.Id) & 0x3f); }

        private GameWorld W => _c.World;
        private CreatureDef T => _c.Template;

        public void PauseFor(float seconds) => _pauseTimer = seconds;
        public void DespawnAfter(float seconds) => _despawnAfter = seconds;

        // ------------------------------------------------------------ Schleife

        public void Update(float dt)
        {
            if (_c.Zone == null) return;
            if (_c.IsDead)
            {
                _c.CorpseTimer -= dt;
                if (_c.CorpseTimer <= 0) Despawn();
                return;
            }
            if (_despawnAfter > 0 && (_despawnAfter -= dt) <= 0) { Despawn(); return; }
            if (_c.IsPet) { UpdatePet(dt); return; }
            if (_c.Escort != null && State == CreatureState.Idle) { W.Quests.UpdateEscort(_c, dt); if (_c.Removed) return; ScanForAggro(dt); return; }

            switch (State)
            {
                case CreatureState.Idle: UpdateIdle(dt); break;
                case CreatureState.Combat: UpdateCombat(dt); break;
                case CreatureState.Evade: UpdateEvade(dt); break;
                case CreatureState.Flee: UpdateFlee(dt); break;
            }
        }

        private void Despawn()
        {
            if (_c.Spawn != null && _c.Spawn.Current == _c && _c.Zone != null) _c.Zone.OnSpawnDied(_c.Spawn, _c);
            _c.Zone?.Remove(_c);
        }

        // ------------------------------------------------------------ Ruhe

        private void UpdateIdle(float dt)
        {
            if (_pauseTimer > 0) { _pauseTimer -= dt; return; }
            if (ScanForAggro(dt)) return;
            if (T.Barks.Count > 0 && (_barkTimer -= dt) <= 0)
            {
                _barkTimer = 90 + W.Random.Range(0, 120);
                if (_c.Zone!.PlayersInRange(_c.Position, 30).Any(p => !p.IsBot)) W.Chat.NpcSay(_c, T.Barks[W.Random.Range(0, T.Barks.Count - 1)]);
            }
            if (T.Schedule.Count > 0)
            {
                var entry = T.Schedule.Where(s => s.Hour <= (int)W.GameHour).OrderByDescending(s => s.Hour).FirstOrDefault() ?? T.Schedule.OrderByDescending(s => s.Hour).First();
                if (_c.DistanceTo(entry.Position) > 1f) MoveTowards(entry.Position, dt, 0.45f);
                return;
            }
            if (Patrol.Count > 0)
            {
                var wp = Patrol[_patrolIndex % Patrol.Count];
                if (_c.DistanceTo(wp) < 1f) _patrolIndex++;
                else MoveTowards(wp, dt, 0.5f);
                return;
            }
            if (WanderRadius > 0)
            {
                _wanderTimer -= dt;
                if (_wanderTarget == null && _wanderTimer <= 0)
                {
                    double ang = W.Random.NextDouble() * Math.PI * 2, d = W.Random.NextDouble() * WanderRadius;
                    _wanderTarget = _c.Home + new Vec3((float)(Math.Cos(ang) * d), 0, (float)(Math.Sin(ang) * d));
                }
                if (_wanderTarget != null)
                {
                    if (_c.DistanceTo(_wanderTarget.Value) < 0.8f) { _wanderTarget = null; _wanderTimer = 6 + (float)W.Random.NextDouble() * 8; }
                    else MoveTowards(_wanderTarget.Value, dt, 0.35f);
                }
                return;
            }
            if (_c.DistanceTo(_c.Home) > 1f) MoveTowards(_c.Home, dt, 0.6f);
            else _c.Facing = _c.HomeFacing;
        }

        /// <summary>Feindliche Kreaturen und Wachen suchen in ihrem Aggroradius nach Zielen.</summary>
        private bool ScanForAggro(float dt)
        {
            _aggroTimer -= dt;
            if (_aggroTimer > 0) return false;
            _aggroTimer = 0.5f;
            bool guard = T.Roles.HasFlag(NpcRole.Guard);
            if (T.Faction != Faction.Hostile && !guard && _c.Escort == null) return false;
            Unit? best = null;
            float bestDist = float.MaxValue;
            foreach (var u in _c.Zone!.UnitsInRange(_c.Position, 45))
            {
                if (!_c.CanAttack(u) || u.Removed) continue;
                if (u is Player pl && (pl.IsFlying || pl.DeathState != DeathState.Alive)) continue;
                // Wachen: Monster in der Nähe und – in Städten und Lagern – Spieler der feindlichen Fraktion (Kapitel 35)
                if (guard && !(u is Creature cu && cu.Faction == Faction.Hostile && (cu.InCombat || _c.DistanceTo(cu) < 12))
                          && !(u is Player enemy && !enemy.IsBot && enemy.Faction != T.Faction && _c.DistanceTo(enemy) < 20)) continue;
                if (_c.Escort != null && !(u is Creature ec && ec.InCombat)) continue;
                float radius = AggroRadiusFor(u);
                float d = _c.DistanceTo(u);
                if (u.IsStealthed) radius = Math.Min(radius, 3f);
                if (d <= radius && d < bestDist) { best = u; bestDist = d; }
            }
            if (best == null) return false;
            Engage(best);
            return true;
        }

        private float AggroRadiusFor(Unit u)
        {
            float r = T.AggroRadius + (_c.Level - u.Level) * 1f;
            if (u.Level - _c.Level >= 5) r *= 0.5f;
            // Nebel, Sturm und Nacht verkürzen die Sicht (Kapitel 36)
            if (_c.Zone != null) r *= EnvironmentRules.SightFactor(_c.Zone.Weather, _c.Zone.WeatherIntensity, _c.World.GameHour);
            return Math.Clamp(r, 4f, 40f);
        }

        // ------------------------------------------------------------ Kampf

        public void OnAttacked(Unit attacker)
        {
            if (_c.IsDead || _c.Evading) return;
            if (State == CreatureState.Idle) Engage(attacker);
        }

        public void Engage(Unit target)
        {
            if (_c.IsDead) return;
            if (State != CreatureState.Combat && State != CreatureState.Flee)
            {
                State = CreatureState.Combat;
                _timers.Clear();
                foreach (var a in AllAbilities()) _timers[a] = a.InitialDelay;
            }
            _c.Threat.Add(target, 1);
            _c.EnterCombatWith(target);
            _c.Target ??= target;
            _c.AutoAttacking = true;
            if (T.CallsForHelp) CallForHelp(target, 12f);
            if (_c.IsBoss && T.Phases.Count > 0 && PhaseIndex < 0) EnterPhase(0);
        }

        private void CallForHelp(Unit target, float radius)
        {
            foreach (var other in _c.Zone!.UnitsInRange(_c.Position, radius).OfType<Creature>())
                if (other != _c && other.IsAlive && other.AI.State == CreatureState.Idle && other.Template.Family == T.Family && other.Faction == _c.Faction && !other.IsPet)
                    other.AI.Engage(target);
        }

        private IEnumerable<CreatureAbilityDef> AllAbilities()
        {
            foreach (var a in T.Abilities) yield return a;
            if (PhaseIndex >= 0 && PhaseIndex < T.Phases.Count) foreach (var a in T.Phases[PhaseIndex].Abilities) yield return a;
        }

        private void UpdateCombat(float dt)
        {
            var target = _c.Threat.SelectTarget(_c, _c.Target);
            float leash = _c.IsBoss ? 120 : 70;
            if (_c.Escort != null) leash = 1000;
            if (target == null || _c.DistanceTo(_c.Home) > leash || target.Zone != _c.Zone)
            {
                if (_c.Escort != null || _c.IsSummoned && _c.Spawn == null && target == null) { LeaveCombatSoft(); return; }
                StartEvade();
                return;
            }
            if (_c.Target != target)
            {
                _c.Target = target;
                if (target is Player tp && tp.Bot != null) tp.Bot.OnTargetedBy(_c);
            }
            _c.AutoAttacking = true;
            UpdatePhase();
            if (T.FleeAtHealth > 0 && !_fled && _c.HealthPct < T.FleeAtHealth && !_c.IsRooted)
            {
                _fled = true;
                State = CreatureState.Flee;
                _fleeTimer = 4f;
                W.Chat.NpcSay(_c, $"{_c.Name} versucht zu fliehen!", false);
                CallForHelp(target, 20f);
                return;
            }
            if (_c.Cast != null || !_c.CanAct) return;
            if (TryAbilities(dt, target)) return;
            Chase(target, dt);
        }

        private void LeaveCombatSoft()
        {
            State = CreatureState.Idle;
            _c.Threat.Clear();
            _c.ClearCombat();
            _c.Target = null;
            if (_c.IsSummoned && _c.Spawn == null && _c.Escort == null && !_c.IsPet) DespawnAfter(5);
        }

        private void Chase(Unit target, float dt)
        {
            _c.Facing = Vec3.FacingTowards(_c.Position, target.Position);
            if (T.Ranged && T.PreferredRange > 0 && _c.Power > 0)
            {
                if (_c.DistanceTo(target) > T.PreferredRange) MoveTowards(target.Position, dt);
                return;
            }
            float reach = _c.MeleeReachTo(target) - 0.3f;
            if (_c.DistanceTo(target) > reach)
            {
                var dir = (_c.Position - target.Position).NormalizedXZ();
                MoveTowards(target.Position + dir * Math.Max(0.5f, reach - 0.5f), dt);
            }
        }

        private bool TryAbilities(float dt, Unit target)
        {
            if (_abilityRetry > 0) { _abilityRetry -= dt; }
            foreach (var a in AllAbilities().ToList())
            {
                if (!_timers.ContainsKey(a)) _timers[a] = a.InitialDelay;
                _timers[a] -= dt;
                if (_timers[a] > 0 || _abilityRetry > 0) continue;
                if (_c.HealthPct >= a.HealthBelow) continue;
                if (!W.Random.Chance(a.Chance)) { _timers[a] = a.Interval; continue; }
                var def = W.Content.GetAbilityRank(a.Ability, 1);
                if (def == null) continue;
                var t = PickTarget(a.TargetRule, target, def);
                if (t == null) { _timers[a] = 1; continue; }
                var err = W.Combat.TryCast(_c, def, t);
                if (err == null) { _timers[a] = a.Interval; return true; }
                _abilityRetry = 0.5f;
            }
            return false;
        }

        private Unit? PickTarget(AbilityTargetRule rule, Unit current, AbilityDef def)
        {
            switch (rule)
            {
                case AbilityTargetRule.Self: return _c;
                case AbilityTargetRule.Tank: return _c.Threat.Top() ?? current;
                case AbilityTargetRule.Random:
                {
                    var list = _c.Threat.Units.Where(u => u.IsAlive && u.Zone == _c.Zone).ToList();
                    return list.Count == 0 ? current : list[W.Random.Range(0, list.Count - 1)];
                }
                case AbilityTargetRule.Farthest:
                    return _c.Threat.Units.Where(u => u.IsAlive).OrderByDescending(u => _c.DistanceTo(u)).FirstOrDefault() ?? current;
                case AbilityTargetRule.LowestHealthAlly:
                    return _c.Zone!.UnitsInRange(_c.Position, def.MaxRange).OfType<Creature>()
                        .Where(o => o.IsAlive && !o.IsHostileTo(_c) && o.HealthPct < 0.6f).OrderBy(o => o.HealthPct).FirstOrDefault();
                default:
                    return current;
            }
        }

        private void UpdatePhase()
        {
            if (T.Phases.Count == 0) return;
            int idx = 0;
            for (int i = 0; i < T.Phases.Count; i++)
                if (_c.HealthPct < T.Phases[i].HealthBelow) idx = i;
            if (idx > PhaseIndex) EnterPhase(idx);
        }

        private void EnterPhase(int idx)
        {
            PhaseIndex = idx;
            var ph = T.Phases[idx];
            if (ph.Yell != null) W.Chat.NpcSay(_c, ph.Yell, true);
            _c.DamageMultiplier = ph.DamageMultiplier;
            _c.MarkStatsDirty();
            foreach (var a in ph.Abilities) _timers[a] = a.InitialDelay;
            foreach (var add in ph.SummonAdds)
                for (int i = 0; i < add.Count; i++)
                {
                    double ang = W.Random.NextDouble() * Math.PI * 2;
                    var pos = _c.Position + new Vec3((float)Math.Cos(ang) * 6, 0, (float)Math.Sin(ang) * 6);
                    var c = _c.Zone!.SpawnCreature(add.Creature, pos);
                    var victim = PickTarget(AbilityTargetRule.Random, _c.Target ?? _c, W.Content.Abilities.Values.First());
                    if (victim != null && victim != _c) c.AI.Engage(victim);
                }
        }

        private void StartEvade()
        {
            State = CreatureState.Evade;
            _c.Evading = true;
            _c.Threat.Clear();
            _c.ClearCombat();
            _c.Target = null;
            _c.AutoAttacking = false;
            if (_c.Cast != null) W.Combat.CancelCast(_c, "Rückzug");
        }

        private void UpdateEvade(float dt)
        {
            if (_c.DistanceTo(_c.Home) > 1f && !_c.IsSummoned) { MoveTowards(_c.Home, dt, 1.6f); return; }
            _c.Evading = false;
            _c.Health = _c.MaxHealth;
            _c.Power = _c.MaxPower;
            _c.RemoveAuras(a => a.Def.IsDebuff);
            _c.TappedBy = null;
            _c.DamageMultiplier = 1f;
            _c.MarkStatsDirty();
            PhaseIndex = -1;
            _fled = false;
            State = CreatureState.Idle;
            if (_c.IsSummoned && _c.Spawn == null && _c.Escort == null) DespawnAfter(1);
            // Beschworene Adds eines zurückgesetzten Bosses verschwinden.
            if (_c.IsBoss)
                foreach (var add in _c.Zone!.UnitsInRange(_c.Position, 80).OfType<Creature>().Where(a => a.IsSummoned && a.Spawn == null && a.Escort == null && !a.IsPet))
                    add.AI.DespawnAfter(0.5f);
        }

        private void UpdateFlee(float dt)
        {
            _fleeTimer -= dt;
            var from = _c.Target ?? _c.Threat.Top();
            if (_fleeTimer <= 0 || from == null) { State = CreatureState.Combat; return; }
            var dir = (_c.Position - from.Position).NormalizedXZ();
            MoveTowards(_c.Position + dir * 5, dt, 0.8f);
        }

        // ------------------------------------------------------------ Begleiter

        private void UpdatePet(float dt)
        {
            var owner = _c.Owner;
            if (owner == null || owner.Removed || owner.IsDead || owner.Zone != _c.Zone) { Despawn(); return; }
            if (_c.Level != owner.Level) { _c.Level = owner.Level; _c.MarkStatsDirty(); }
            Unit? target = null;
            if (owner.Target != null && owner.CanAttack(owner.Target) && (owner.AutoAttacking || owner.InCombat || owner.Cast?.Ability.IsHarmful == true)) target = owner.Target;
            target ??= _c.Threat.SelectTarget(_c, _c.Target);
            if (target == null)
                target = _c.Zone!.Entities.Values.OfType<Creature>().FirstOrDefault(m => m.IsAlive && m.Target == owner && _c.CanAttack(m));
            if (target != null && target.IsAlive && _c.DistanceTo(owner) < 50)
            {
                _c.Target = target;
                _c.AutoAttacking = true;
                if (_c.Cast != null || !_c.CanAct) return;
                if (State != CreatureState.Combat) { State = CreatureState.Combat; foreach (var a in T.Abilities) _timers[a] = a.InitialDelay; }
                if (TryAbilities(dt, target)) return;
                Chase(target, dt);
                return;
            }
            State = CreatureState.Idle;
            _c.Target = null;
            _c.AutoAttacking = false;
            if (_c.DistanceTo(owner) > 45) { _c.Zone!.SetPosition(_c, owner.Position); return; }
            var behind = owner.Position - new Vec3((float)Math.Sin(owner.Facing), 0, (float)Math.Cos(owner.Facing)) * 2.5f + new Vec3(1.2f, 0, 0);
            if (_c.DistanceTo(behind) > 1.5f) MoveTowards(behind, dt, _c.DistanceTo(behind) > 8 ? 1.2f : 1f);
        }

        // ------------------------------------------------------------ Bewegung

        /// <summary>
        /// Gerade Linie zum Ziel. Hindernisse kennt der Server noch nicht – im nächsten Meilenstein liefert
        /// ein aus Unity exportiertes Navmesh (Recast/Detour) die Wege.
        /// </summary>
        public void MoveTowards(Vec3 pos, float dt, float speedFactor = 1f)
        {
            if (!_c.CanMove) return;
            if (_c.Cast != null) return;
            var next = Vec3.MoveTowards(_c.Position, pos, _c.MoveSpeed * speedFactor * dt);
            if (Vec3.DistanceXZ(next, _c.Position) > 0.001f) _c.Facing = Vec3.FacingTowards(_c.Position, pos);
            _c.Zone!.SetPosition(_c, next);
        }
    }
}
