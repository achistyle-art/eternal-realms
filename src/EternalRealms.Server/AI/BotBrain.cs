using EternalRealms.Server.Persistence;
using EternalRealms.Server.Systems;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.AI
{
    public enum BotStance { Passive, Assist, Aggressive }

    public enum BotCommand { None, Follow, Stay, Attack, Dungeon, Rest, Passive, Assist, Aggressive, Status, Pull, Buff }

    /// <summary>
    /// Entscheidungslogik eines KI-Mitspielers. Ein Bot ist eine normale Spielfigur (Player); das Gehirn
    /// trifft nur Entscheidungen und nutzt dieselben Aktionen wie ein menschlicher Spieler:
    /// Ziel wählen, Fähigkeit wirken, laufen, Quest annehmen, chatten.
    ///
    /// Prioritäten pro Denkschritt: tot? → zaubert gerade? → Notfall → Heilen → Kampf → Wiederbeleben →
    /// Haltung/Begleiter/Buffs → Rasten → Folgen bzw. eigenständig jagen.
    /// </summary>
    public sealed class BotBrain
    {
        public Player Me { get; }
        public BotData Data { get; }
        public Role Role => Data.Role;
        public BotProfileDef? Profile { get; }
        public bool Following { get; set; } = true;
        public Vec3 StayPosition { get; set; }
        public BotStance Stance { get; set; } = BotStance.Assist;
        public bool DungeonMode { get; set; }
        public bool Resting { get; private set; }
        public string LastDecision { get; private set; } = "";

        private readonly GameWorld _w;
        private float _thinkTimer, _deadTimer, _buffTimer, _idleChatTimer = 120;
        private Vec3? _moveTarget;
        private float _moveTolerance = 1.5f;
        private Unit? _forcedTarget;
        private readonly Dictionary<Unit, double> _firstTankHit = new();
        private readonly Dictionary<string, double> _lastSaid = new();
        private bool _toldLowMana;
        // eigenständiges Spielen
        private enum AutoState { Hunt, Rest, ToTown, Town }
        private AutoState _auto = AutoState.Hunt;
        private Creature? _prey;
        private int _killsSinceTown;
        private float _townTimer, _preySearchTimer;

        public BotBrain(Player me, GameWorld world)
        {
            Me = me;
            _w = world;
            Data = me.Data.Bot ??= new BotData();
            Profile = world.Content.GetBotProfile(me.Class, Data.Role) ?? world.Content.BotProfiles.FirstOrDefault(p => p.Class == me.Class);
            _thinkTimer = (float)world.Random.NextDouble();
        }

        /// <summary>Reaktionszeit: geübte Bots reagieren schneller.</summary>
        private float ReactionTime => 0.25f + (1 - Data.Skill) * 0.45f;
        private Player? Leader => Me.Group != null && Me.Group.Leader != Me ? Me.Group.Leader : null;
        private bool InGroup => Me.Group != null && Me.Group.Members.Count > 1;
        private double Now => _w.Time;

        // ================================================================== Schleife

        public void Update(float dt)
        {
            if (Me.Zone == null || Me.IsFlying) return;
            _thinkTimer -= dt;
            if (_thinkTimer <= 0)
            {
                _thinkTimer = ReactionTime;
                try { Think(); }
                catch (Exception ex) { _w.Log($"Bot {Me.Name}: {ex.Message}"); }
            }
            StepMovement(dt);
        }

        private void Think()
        {
            if (Me.IsDead) { ThinkDead(); return; }
            _deadTimer = 0;
            if (Me.Cast != null)
            {
                // Überflüssige Heilung abbrechen (Ziel inzwischen voll), wie es ein aufmerksamer Spieler tun würde.
                var c = Me.Cast;
                if (c.Ability.Effects.Any(e => e.Type == EffectType.Heal) && c.Target != null && c.Target.HealthPct > 0.97f && c.Remaining > 0.4f)
                    _w.Combat.CancelCast(Me, "Nicht mehr nötig");
                return;
            }
            if (!Me.CanAct) return;
            if (InGroup && Leader != null && !SyncWithLeader()) return;

            var enemies = EngagedEnemies();
            if (Me.HealthPct < 0.25f && enemies.Count > 0 && TryEmergency()) return;
            if (TryHeal(enemies.Count > 0)) return;
            if (TryInterrupt(enemies)) return;
            if (enemies.Count > 0)
            {
                StopResting();
                Fight(enemies);
                return;
            }
            Me.AutoAttacking = false;
            if (TryResurrect()) return;
            if (MaintainSelf()) return;
            if (TryRest()) return;
            if (InGroup) FollowLeader();
            else ThinkAutonomous();
        }

        // ================================================================== Tod

        private void ThinkDead()
        {
            _deadTimer += ReactionTime;
            _moveTarget = null;
            if (Me.DeathState == DeathState.Dead)
            {
                // In der Gruppe auf Wiederbelebung warten, solange ein Heiler lebt und kämpft oder kommt.
                bool rezPossible = InGroup && Me.Group!.Members.Any(m => m != Me && m.IsAlive && m.Zone == Me.Zone && CanResurrect(m));
                bool groupFighting = InGroup && Me.Group!.Members.Any(m => m.IsAlive && m.InCombat);
                if ((rezPossible && _deadTimer < 60) || (groupFighting && _deadTimer < 30)) return;
                if (_deadTimer < 3) return;
                _w.Death.ReleaseSpirit(Me);
                return;
            }
            // Geist: zur Leiche laufen.
            if (Me.Zone!.Def.Id != Me.Data.CorpseZone && _w.Content.Zones.ContainsKey(Me.Data.CorpseZone)) { _w.Transfer(Me, Me.Data.CorpseZone, Me.Data.CorpsePosition); return; }
            if (Vec3.DistanceXZ(Me.Position, Me.Data.CorpsePosition) <= DeathSystem.ReclaimRange - 5) { _w.Death.ReclaimCorpse(Me); return; }
            MoveTo(Me.Data.CorpsePosition, 3);
        }

        private bool CanResurrect(Player p) =>
            p.Bot?.Profile?.Resurrect is string r && p.KnownRank(r) > 0 || (!p.IsBot && p.Class is CharClass.Priest or CharClass.Paladin or CharClass.Shaman or CharClass.Druid);

        // ================================================================== Gruppe

        /// <summary>Hält den Bot in der Nähe des Anführers (auch über Zonen und Instanzen hinweg).</summary>
        private bool SyncWithLeader()
        {
            var l = Leader!;
            if (l.Zone == null) return false;
            if (l.Zone != Me.Zone)
            {
                if (l.IsFlying) return false;
                _w.Transfer(Me, l.Zone.Def.Id, l.Position + new Vec3(1.5f, 0, -1.5f), l.Zone.InstanceKey);
                return false;
            }
            if (!Following) return true;
            if (Me.DistanceTo(l) > 90 && !Me.InCombat)
            {
                _w.MovePlayerWithinZone(Me, l.Position + new Vec3(1.5f, 0, -1.5f));
                return false;
            }
            return true;
        }

        private void FollowLeader()
        {
            var l = Leader;
            if (l == null) return;
            if (!Following) { if (Me.DistanceTo(StayPosition) > 1.5f) MoveTo(StayPosition, 1); return; }
            int idx = Me.Group!.Members.Where(m => m.IsBot).ToList().IndexOf(Me);
            float angle = l.Facing + (float)Math.PI + (idx - 1.5f) * 0.55f;
            float dist = 3f + (idx % 2) * 1.2f;
            var slot = l.Position + new Vec3((float)Math.Sin(angle) * dist, 0, (float)Math.Cos(angle) * dist);
            if (Me.DistanceTo(slot) > 2.5f) MoveTo(slot, 1.2f);
            else
            {
                _moveTarget = null;
                Me.Facing = l.Facing;
            }
            if (!Me.IsBot) return;
            if ((_idleChatTimer -= ReactionTime) <= 0)
            {
                _idleChatTimer = 240 + (float)_w.Random.NextDouble() * 360;
                if (_w.Random.Chance(Data.Chattiness * 0.5)) Say("idle");
            }
        }

        /// <summary>Gegner, die gerade mit dem Bot oder seiner Gruppe kämpfen.</summary>
        private List<Creature> EngagedEnemies()
        {
            var zone = Me.Zone!;
            var mates = InGroup ? Me.Group!.Members.Where(m => m.Zone == zone).Cast<Unit>().ToList() : new List<Unit> { Me };
            if (Me.Pet != null) mates.Add(Me.Pet);
            foreach (var m in Me.Group?.Members ?? new List<Player>()) if (m.Pet != null) mates.Add(m.Pet);
            var set = new HashSet<Creature>();
            foreach (var m in mates)
                foreach (var u in m.CombatWith)
                    if (u is Creature c && c.IsAlive && !c.IsEvading && Me.CanAttack(c) && c.Zone == zone && c.DistanceTo(Me) < 60) set.Add(c);
            if (_forcedTarget is Creature fc && fc.IsAlive && Me.CanAttack(fc)) set.Add(fc);
            if (Stance == BotStance.Aggressive && InGroup && Leader != null)
                foreach (var c in zone.UnitsInRange(Leader.Position, 12).OfType<Creature>())
                    if (c.IsAlive && Me.CanAttack(c) && c.Faction == Faction.Hostile && c.Level >= Experience.GrayLevel(Me.Level)) set.Add(c);
            if (Stance == BotStance.Passive) set.RemoveWhere(c => !mates.Any(m => c.Target == m));
            return set.ToList();
        }

        // ================================================================== Kampf

        private void Fight(List<Creature> enemies)
        {
            Unit? target = Role switch
            {
                Role.Tank => PickTankTarget(enemies),
                Role.Healer => PickAssistTarget(enemies),
                _ => PickAssistTarget(enemies),
            };
            if (target == null) { if (InGroup) FollowLeader(); return; }
            if (Me.Target != target) { Me.Target = target; Decide($"Ziel: {target.Name}"); }

            // Heiler halten Abstand – außer Paladine ohne Gruppe, die selbst in den Nahkampf gehen.
            bool ranged = Profile?.Ranged == true || (Role == Role.Healer && (InGroup || Me.Class != CharClass.Paladin));
            if (Role == Role.Healer && Me.PowerPct < 0.5f)
            {
                KeepDistance(target, 22);
                return;
            }
            if (Role != Role.Tank && DungeonMode && WaitForTank(target)) { KeepDistance(target, ranged ? 25 : 8); return; }

            if (ranged)
            {
                float d = Me.DistanceTo(target);
                // Kiten: Ist der Gegner festgehalten (Frostnova, Wurzeln) und tankt ihn niemand, Abstand gewinnen statt Treffer einzustecken.
                var tank = Me.Group?.MemberWithRole(Role.Tank);
                bool tanked = tank != null && tank != Me && target.Target == tank;
                if (!tanked && target.Target == Me && d < 9 && target.IsRooted)
                {
                    var away = (Me.Position - target.Position).NormalizedXZ();
                    MoveTo(Me.Position + away * 14, 1);
                    Decide("Abstand gewinnen");
                    return;
                }
                if (d > 28) MoveTo(target.Position, 26);
                else { _moveTarget = null; Me.Facing = Vec3.FacingTowards(Me.Position, target.Position); }
                if (Me.Class == CharClass.Hunter) Me.AutoAttacking = d >= 8 || Me.InMeleeRange(target);
            }
            else
            {
                Me.AutoAttacking = true;
                if (!Me.InMeleeRange(target))
                {
                    // Nahkämpfer stellen sich hinter das Ziel, wenn es nicht sie angreift (wie erfahrene Spieler).
                    Vec3 spot = target.Position;
                    if (target.Target != Me && Role != Role.Tank)
                        spot = target.Position - new Vec3((float)Math.Sin(target.Facing), 0, (float)Math.Cos(target.Facing)) * 1.5f;
                    MoveTo(spot, Me.MeleeReachTo(target) - 0.6f);
                }
                else { _moveTarget = null; Me.Facing = Vec3.FacingTowards(Me.Position, target.Position); }
            }
            RunRotation(target, enemies);
        }

        private Unit? PickTankTarget(List<Creature> enemies)
        {
            // 1. Lose Gegner, die Gruppenmitglieder angreifen, zurückholen (Spott).
            var loose = enemies.Where(e => e.Target != null && e.Target != Me && e.Target is Player tp && tp.Group == Me.Group)
                               .OrderBy(e => e.Target is Player p && p.Bot?.Role == Role.Healer ? 0 : 1).ThenBy(e => e.DistanceTo(Me)).FirstOrDefault();
            if (loose != null)
            {
                if (Profile?.Taunt != null && TryAbility(Profile.Taunt, loose)) { Say("taunt", loose); return loose; }
                return loose;
            }
            if (_forcedTarget != null && _forcedTarget.IsAlive && enemies.Contains(_forcedTarget)) return _forcedTarget;
            if (Me.Target is Creature cur && enemies.Contains(cur)) return cur;
            if (Leader?.Target is Creature lt && enemies.Contains(lt)) return lt;
            return enemies.OrderBy(e => e.DistanceTo(Me)).FirstOrDefault();
        }

        private Unit? PickAssistTarget(List<Creature> enemies)
        {
            var usable = enemies.Where(e => !e.Auras.Any(a => a.Def.BreaksOnDamage && a.Def.IsDebuff)).ToList();
            if (usable.Count == 0) return null;
            if (_forcedTarget is Creature f && usable.Contains(f)) return f;
            var tank = Me.Group?.MemberWithRole(Role.Tank);
            if (tank != null && tank != Me && tank.Target is Creature tt && usable.Contains(tt)) return tt;
            if (Leader?.Target is Creature lt && usable.Contains(lt)) return lt;
            var onHealer = usable.FirstOrDefault(e => e.Target is Player p && p.Bot?.Role == Role.Healer);
            if (onHealer != null) return onHealer;
            if (Me.Target is Creature cur && usable.Contains(cur)) return cur;
            return usable.OrderBy(e => e.HealthPct).ThenBy(e => e.DistanceTo(Me)).First();
        }

        /// <summary>Im Dungeon warten Schadensverursacher kurz, bis der Tank Bedrohung aufgebaut hat.</summary>
        private bool WaitForTank(Unit target)
        {
            var tank = Me.Group?.MemberWithRole(Role.Tank);
            if (tank == null || tank == Me || target is not Creature c) return false;
            if (c.Target != tank) return c.Target == null;
            if (!_firstTankHit.TryGetValue(c, out var t)) { _firstTankHit[c] = Now; return true; }
            float patience = 1.2f + Data.Caution * 1.5f;
            return Now - t < patience;
        }

        private void KeepDistance(Unit target, float dist)
        {
            float d = Me.DistanceTo(target);
            if (d < dist - 6)
            {
                var away = (Me.Position - target.Position).NormalizedXZ();
                MoveTo(target.Position + away * dist, 2);
            }
            else if (d > dist + 10) MoveTo(target.Position, dist);
        }

        private void RunRotation(Unit target, List<Creature> enemies)
        {
            if (Profile == null) return;
            if (Me.Gcd > 0) return;
            foreach (var rule in Profile.Rotation)
            {
                if (Me.KnownRank(rule.Ability) <= 0) continue;
                var ab = Ranked(rule.Ability);
                if (ab == null || !Condition(rule, ab, target, enemies)) continue;
                var t = ab.Target is TargetType.Self or TargetType.Party or TargetType.AreaAroundSelf ? Me : target;
                if (ab.Target == TargetType.AreaAroundSelf && ab.IsHarmful && enemies.Count(e => e.DistanceTo(Me) <= ab.Radius) == 0) continue;
                if (_w.Combat.TryCast(Me, ab, t) == null) { Decide($"{ab.Name} → {t.Name}"); return; }
            }
        }

        private bool Condition(BotRuleDef r, AbilityDef ab, Unit target, List<Creature> enemies)
        {
            switch (r.If)
            {
                case BotCondition.Always: return true;
                case BotCondition.TargetMissingAura: return r.Aura == null || !target.Auras.Any(a => a.CasterId == Me.Id && a.Def.Id.StartsWith(r.Aura, StringComparison.Ordinal));
                case BotCondition.SelfMissingAura: return r.Aura == null || !Me.HasAuraFamily(r.Aura);
                case BotCondition.TargetHealthBelow: return target.HealthPct < r.Value;
                case BotCondition.SelfHealthBelow: return Me.HealthPct < r.Value;
                case BotCondition.SelfPowerAbove: return r.Value > 1 ? Me.Power >= r.Value : Me.PowerPct >= r.Value;
                case BotCondition.SelfPowerBelow: return Me.PowerPct < r.Value;
                case BotCondition.ComboPointsAtLeast: return Me.ComboPoints >= r.Value && Me.ComboTargetId == target.Id;
                case BotCondition.TargetCasting: return target.Cast != null;
                case BotCondition.MultipleEnemies: return enemies.Count(e => e.DistanceTo(target) <= 8) >= r.Value;
                case BotCondition.NotInMeleeRange: return !Me.InMeleeRange(target);
                case BotCondition.InMeleeRange: return enemies.Any(e => Me.InMeleeRange(e));
                case BotCondition.TargetNotTargetingMe: return target.Target != Me;
            }
            return true;
        }

        private bool TryInterrupt(List<Creature> enemies)
        {
            var id = Profile?.Interrupt;
            if (id == null || Me.KnownRank(id) <= 0) return false;
            var caster = enemies.FirstOrDefault(e => e.Cast != null && e.Cast.Remaining > 0.3f && e.Cast.Ability.IsSpell);
            if (caster == null) return false;
            // Vorsichtige Bots reagieren zuverlässiger, ungeübte verpassen manche Unterbrechung.
            if (!_w.Random.Chance(0.35 + Data.Skill * 0.6)) return false;
            return TryAbility(id, caster);
        }

        private bool TryEmergency()
        {
            foreach (var potion in new[] { "item_healing_potion", "item_lesser_healing_potion", "item_minor_healing_potion" })
            {
                int idx = Me.Data.Bag.FindIndex(s => s?.ItemId == potion);
                if (idx >= 0 && _w.Interactions.UseItem(Me, idx, 0) == null) { Say("low_health"); return true; }
            }
            if (Me.KnownRank("racial_scale_regen") > 0 && TryAbility("racial_scale_regen", Me)) return true;
            if (Role != Role.Healer && _w.Random.Chance(0.3)) Say("low_health");
            return false;
        }

        // ================================================================== Heilen

        private bool TryHeal(bool inCombat)
        {
            if (Profile == null || Profile.Heals.Count == 0) return false;
            bool isHealer = Role == Role.Healer;
            var mates = new List<Unit> { Me };
            if (Me.Group != null) mates.AddRange(Me.Group.Members.Where(m => m != Me && m.Zone == Me.Zone && m.IsAlive && m.DistanceTo(Me) < 45));
            if (isHealer && Me.Group != null) mates.AddRange(Me.Group.Members.Where(m => m.Pet != null && m.Pet.IsAlive).Select(m => (Unit)m.Pet!));
            bool healerAlive = Me.Group?.Members.Any(m => m != Me && m.IsAlive && m.Bot?.Role == Role.Healer) == true;
            Unit? target = null;
            float worst = 2;
            foreach (var m in mates)
            {
                float threshold = !inCombat ? 0.7f : isHealer ? (IsTank(m) ? 0.88f : 0.75f) + Data.Caution * 0.05f : healerAlive ? 0.25f : 0.4f;
                // Allein heilt man sich sparsam, damit genug Mana für den Kampf bleibt.
                if (!InGroup) threshold = inCombat ? (isHealer ? 0.45f : 0.35f) + Data.Caution * 0.05f : 0.6f;
                if (!isHealer && m != Me && healerAlive) continue;
                if (!isHealer && !inCombat) threshold = 0.5f;
                if (m.HealthPct < threshold && m.HealthPct < worst) { worst = m.HealthPct; target = m; }
            }
            if (target == null) return false;
            if (!isHealer && Me.Form is Form.Bear or Form.Cat && inCombat && target == Me && Me.HealthPct > 0.3f) return false;
            if (Me.DistanceTo(target) > 36) { MoveTo(target.Position, 30); return true; }
            var spell = ChooseHeal(target, inCombat);
            if (spell == null) return false;
            if (_w.Combat.TryCast(Me, spell, target) == null)
            {
                _moveTarget = null;
                Decide($"Heile {target.Name} mit {spell.Name}");
                if (target.HealthPct < 0.3f && target != Me) Say("heal_incoming", target);
                return true;
            }
            return false;
        }

        private bool IsTank(Unit u) => u is Player p && (p.Bot?.Role == Role.Tank || p.Form is Form.DefensiveStance or Form.Bear);

        /// <summary>Wählt die passende Heilung: Schild/HoT vorbeugend, schnelle Heilung im Notfall, sonst die effizienteste.</summary>
        private AbilityDef? ChooseHeal(Unit target, bool inCombat)
        {
            float deficit = target.MaxHealth - target.Health;
            var options = new List<(AbilityDef ab, float heal, bool hot, bool shield)>();
            foreach (var id in Profile!.Heals)
            {
                if (Me.KnownRank(id) <= 0 || Me.CooldownLeft(id) > 0) continue;
                var ab = Ranked(id);
                if (ab == null || _w.Combat.BlockedByAura(ab, target)) continue;
                if (ab.Target == TargetType.Friendly && target == Me) continue;
                float heal = 0;
                bool hot = false, shield = false;
                foreach (var e in ab.Effects)
                {
                    if (e.Type == EffectType.Heal) heal += (e.Min + e.Max) / 2f + Me.HealingPower * e.Coefficient;
                    if (e.Type == EffectType.ApplyAura && e.Aura != null && _w.Content.Auras.TryGetValue(e.Aura, out var a))
                    {
                        if (a.Periodic == PeriodicType.Heal) { hot = true; heal += (a.TickMin + a.TickMax) / 2f * (a.Duration / a.TickInterval); }
                        if (a.Absorb > 0) { shield = true; heal += a.Absorb; }
                    }
                }
                if (heal <= 0) continue;
                if (_w.Combat.EffectiveCost(Me, ab) > Me.Power) continue;
                options.Add((ab, heal, hot, shield));
            }
            if (options.Count == 0) return null;
            float pct = target.HealthPct;
            if (inCombat)
            {
                var sh = options.FirstOrDefault(o => o.shield && (IsTank(target) || pct < 0.5f || !InGroup));
                if (sh.ab != null) return sh.ab;
            }
            if (pct > 0.5f)
            {
                var hot = options.FirstOrDefault(o => o.hot && !target.Auras.Any(a => a.CasterId == Me.Id && a.Def.Periodic == PeriodicType.Heal));
                if (hot.ab != null) return hot.ab;
            }
            var direct = options.Where(o => !o.hot && !o.shield).ToList();
            if (direct.Count == 0) return options[0].ab;
            if (!InGroup && inCombat) return direct.OrderByDescending(o => o.heal).First().ab;
            if (pct < 0.4f && inCombat)
                return direct.OrderBy(o => _w.Combat.EffectiveCastTime(Me, o.ab)).ThenByDescending(o => o.heal).First().ab;
            var fitting = direct.Where(o => o.heal <= deficit * 1.25f).OrderByDescending(o => o.heal).FirstOrDefault();
            return fitting.ab ?? direct.OrderBy(o => o.heal).First().ab;
        }

        private bool TryResurrect()
        {
            var res = Profile?.Resurrect;
            if (res == null || Me.KnownRank(res) <= 0 || Me.Group == null) return false;
            var dead = Me.Group.Members.FirstOrDefault(m => m != Me && m.IsDead && m.Data.CorpseZone == Me.Zone!.Def.Id);
            if (dead == null) return false;
            var ab = Ranked(res)!;
            if (Me.CooldownLeft(res) > 0 || _w.Combat.EffectiveCost(Me, ab) > Me.Power) return false;
            if (Vec3.DistanceXZ(Me.Position, dead.Data.CorpsePosition) > ab.MaxRange - 4) { MoveTo(dead.Data.CorpsePosition, ab.MaxRange - 8); return true; }
            if (_w.Combat.TryCast(Me, ab, dead) == null) { _moveTarget = null; Say("resurrect", dead); Decide($"Belebe {dead.Name} wieder"); return true; }
            return false;
        }

        // ================================================================== Pflege außerhalb des Kampfes

        private bool MaintainSelf()
        {
            if (Profile == null) return false;
            if (Profile.Form != Form.None && Me.Form != Profile.Form)
                foreach (var ab in KnownAbilities())
                    if (ab.Effects.Any(e => e.Type == EffectType.ApplyAura && e.Aura != null && _w.Content.Auras[e.Aura].Form == Profile.Form))
                        if (_w.Combat.TryCast(Me, ab, Me) == null) return true;
            if (Profile.Pet != null && Me.Pet == null && Me.KnownRank(Profile.Pet) > 0 && !Me.InCombat)
                if (_w.Combat.TryCast(Me, Ranked(Profile.Pet)!, Me) == null) { Decide("Ruft Begleiter"); return true; }
            _buffTimer -= ReactionTime;
            if (_buffTimer > 0) return false;
            _buffTimer = 3;
            foreach (var id in Profile.Buffs)
            {
                if (Me.KnownRank(id) <= 0) continue;
                var ab = Ranked(id)!;
                var auraId = ab.Effects.FirstOrDefault(e => e.Type == EffectType.ApplyAura)?.Aura;
                if (auraId == null) continue;
                var fam = Aura.Family(auraId);
                var mates = Me.Group?.Members.Where(m => m.Zone == Me.Zone && m.IsAlive && m.DistanceTo(Me) < 30).Cast<Unit>().ToList() ?? new List<Unit> { Me };
                if (ab.Target is TargetType.Self)
                {
                    if (!Me.HasAuraFamily(fam) && _w.Combat.TryCast(Me, ab, Me) == null) return true;
                    continue;
                }
                if (ab.Target == TargetType.Party)
                {
                    if (mates.Any(m => !m.HasAuraFamily(fam)) && _w.Combat.TryCast(Me, ab, Me) == null) return true;
                    continue;
                }
                var missing = mates.FirstOrDefault(m => !m.HasAuraFamily(fam));
                if (missing != null && _w.Combat.TryCast(Me, ab, missing) == null) { Decide($"{ab.Name} auf {missing.Name}"); return true; }
            }
            return false;
        }

        private bool TryRest()
        {
            bool usesMana = Me.PowerType == PowerType.Mana || Me.StoredMana > 0 && Me.ClassDef.ManaFactor > 0;
            float manaPct = Me.PowerType == PowerType.Mana ? Me.PowerPct : 1;
            bool leaderAway = Leader != null && Following && Me.DistanceTo(Leader) > 25;
            if (Resting)
            {
                bool done = Me.HealthPct > 0.95f && (!usesMana || manaPct > 0.9f);
                if (done || leaderAway || Me.InCombat) { StopResting(); if (done && InGroup && _w.Random.Chance(Data.Chattiness)) Say("ready"); return false; }
                if (!Me.Auras.Any(a => a.Def.BreaksOnMove)) StartRestAbilities(manaPct);
                return true;
            }
            float hpLimit = 0.55f + Data.Caution * 0.15f;
            float manaLimit = (Role == Role.Healer ? 0.5f : 0.35f) + Data.Caution * 0.1f;
            if (DungeonMode) { hpLimit += 0.15f; manaLimit += 0.2f; }
            if (leaderAway) return false;
            if (Me.HealthPct >= hpLimit && (!usesMana || manaPct >= manaLimit)) return false;
            if (Leader != null && Leader.InCombat) return false;
            Resting = true;
            _moveTarget = null;
            if (usesMana && manaPct < manaLimit) { Say(InGroup && Role == Role.Healer ? "low_mana" : "drink"); _toldLowMana = true; }
            else Say("eat");
            StartRestAbilities(manaPct);
            return true;
        }

        private void StartRestAbilities(float manaPct)
        {
            if (Me.HealthPct < 0.95f && !Me.Auras.Any(a => a.Def.Id == "food_regen")) _w.Combat.TryCast(Me, _w.Content.GetAbilityRank("item_eat", 1)!, Me);
            if (Me.PowerType == PowerType.Mana && manaPct < 0.9f && !Me.Auras.Any(a => a.Def.Id == "drink_regen")) _w.Combat.TryCast(Me, _w.Content.GetAbilityRank("item_drink", 1)!, Me);
        }

        private void StopResting()
        {
            if (!Resting) return;
            Resting = false;
            _toldLowMana = false;
            Me.RemoveAuras(a => a.Def.BreaksOnMove);
        }

        // ================================================================== Eigenständig spielen

        /// <summary>
        /// Ohne Gruppe leben Bots in der Welt: Sie jagen passende Gegner, rasten, kehren ins Dorf zurück,
        /// verkaufen Plunder, unterhalten sich im Zonenchat und steigen dabei selbst auf.
        /// </summary>
        private void ThinkAutonomous()
        {
            var zone = Me.Zone!;
            switch (_auto)
            {
                case AutoState.Hunt:
                    if (_killsSinceTown >= 12 || Me.FreeBagSlots < 2) { _auto = AutoState.ToTown; return; }
                    if (_prey != null && (_prey.Removed || _prey.IsDead))
                    {
                        if (_prey.Lootable && _prey.TappedBy == Me && Me.DistanceTo(_prey) < 6) { _w.Loot.TryLoot(Me, _prey); _prey = null; return; }
                        if (_prey.Lootable && _prey.TappedBy == Me) { MoveTo(_prey.Position, 3); return; }
                        _prey = null;
                    }
                    if (_prey == null || _prey.TappedBy != null && _prey.TappedBy != Me)
                    {
                        _preySearchTimer -= ReactionTime;
                        if (_preySearchTimer > 0 && _moveTarget != null) return;
                        _preySearchTimer = 2;
                        _prey = FindPrey(zone);
                        if (_prey == null) { MoveTo(HuntingGround(zone), 5); return; }
                    }
                    Me.Target = _prey;
                    var ab = OpeningAbility();
                    bool rangedHunter = Profile?.Ranged == true || (Role == Role.Healer && Me.Class != CharClass.Paladin);
                    float range = rangedHunter ? Math.Min(28, ab?.MaxRange ?? 25) : Me.MeleeReachTo(_prey) - 0.6f;
                    if (Me.DistanceTo(_prey) > range) { MoveTo(_prey.Position, range); return; }
                    _moveTarget = null;
                    Me.AutoAttacking = !(Profile?.Ranged == true) || Me.Class == CharClass.Hunter;
                    if (ab != null && _w.Combat.TryCast(Me, ab, _prey) == null) return;
                    return;
                case AutoState.ToTown:
                {
                    var town = zone.Def.Areas.FirstOrDefault(a => a.IsCity) ?? zone.Def.Areas.FirstOrDefault();
                    if (town == null) { _auto = AutoState.Hunt; return; }
                    if (Me.DistanceTo(town.Center) > 15) { MoveTo(town.Center, 10); return; }
                    SellJunk();
                    _auto = AutoState.Town;
                    _townTimer = 40 + (float)_w.Random.NextDouble() * 60;
                    if (_w.Random.Chance(Data.Chattiness * 0.6)) Say("general", channel: ChatChannel.General);
                    return;
                }
                case AutoState.Town:
                    _townTimer -= ReactionTime;
                    if (_townTimer <= 0) { _auto = AutoState.Hunt; _killsSinceTown = 0; }
                    return;
            }
        }

        private AbilityDef? OpeningAbility()
        {
            if (Profile == null) return null;
            foreach (var r in Profile.Rotation)
            {
                if (Me.KnownRank(r.Ability) <= 0) continue;
                var ab = Ranked(r.Ability);
                if (ab != null && ab.IsHarmful && ab.Target == TargetType.Enemy && !ab.RequiresComboPoints && ab.TargetHealthBelow <= 0) return ab;
            }
            return null;
        }

        private Creature? FindPrey(ZoneInstance zone)
        {
            int gray = Experience.GrayLevel(Me.Level);
            return zone.UnitsInRange(Me.Position, 60).OfType<Creature>()
                .Where(c => c.IsAlive && !c.InCombat && c.TappedBy == null && !c.Template.Elite && !c.Template.Rare && !c.IsSummoned && c.Owner == null && !c.IsPet && !c.Template.IsNpc
                            && (c.Faction == Faction.Hostile || c.Faction == Faction.Neutral) && c.Level > gray && c.Level <= Me.Level + 1)
                .OrderBy(c => c.DistanceTo(Me)).FirstOrDefault();
        }

        private Vec3 HuntingGround(ZoneInstance zone)
        {
            int gray = Experience.GrayLevel(Me.Level);
            var spots = zone.SpawnPoints.Where(s =>
            {
                var t = _w.Content.Creatures[s.Def.Creature];
                return !t.Elite && !t.Rare && t.MaxLevel > gray && t.MinLevel <= Me.Level + 1 && (t.Faction == Faction.Hostile || t.Faction == Faction.Neutral);
            }).ToList();
            if (spots.Count == 0) return zone.Def.Areas.FirstOrDefault(a => a.IsCity)?.Center ?? Me.Position;
            // Bevorzugt Gegner der eigenen Stufe, dann die Nähe.
            var best = spots.OrderBy(s => Math.Abs(_w.Content.Creatures[s.Def.Creature].MaxLevel - Me.Level) * 100 + Vec3.DistanceXZ(s.Position, Me.Position)).First();
            return best.Position;
        }

        private void SellJunk()
        {
            for (int i = 0; i < Me.Data.Bag.Count; i++)
            {
                var s = Me.Data.Bag[i];
                if (s == null) continue;
                var def = _w.Content.Items[s.ItemId];
                if (def.Category == ItemCategory.Quest || def.Category == ItemCategory.Consumable || def.Id == "item_hearthstone") continue;
                if (def.IsEquippable && _w.Bots.ConsiderEquip(Me, i)) continue;
                Me.Money += (long)def.SellPrice * s.Count;
                Me.Data.Bag[i] = null;
            }
        }

        // ================================================================== Bewegung

        private void MoveTo(Vec3 pos, float tolerance)
        {
            _moveTarget = pos;
            _moveTolerance = Math.Max(0.5f, tolerance);
        }

        private void StepMovement(float dt)
        {
            if (_moveTarget == null || !Me.CanMove || Me.Cast != null || Me.IsFlying) return;
            var target = _moveTarget.Value;
            float dist = Me.DistanceTo(target);
            if (dist <= _moveTolerance) { _moveTarget = null; return; }
            if (Resting) StopResting();
            var next = Vec3.MoveTowards(Me.Position, target, Math.Min(dist - _moveTolerance + 0.1f, Me.MoveSpeed * dt));
            Me.Facing = Vec3.FacingTowards(Me.Position, target);
            _w.MoveUnit(Me, next);
        }

        // ================================================================== Ereignisse

        public void OnPartyChat(Player from, string text)
        {
            var group = Me.Group;
            if (group == null) return;
            // Wird ein Bot namentlich angesprochen, reagieren nur die Genannten.
            var named = group.Bots.Where(b => text.Contains(b.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count > 0 && !named.Contains(Me)) return;
            var cmd = BotCommandParser.Parse(text);
            int order = group.Bots.ToList().IndexOf(Me);
            switch (cmd)
            {
                case BotCommand.Follow: Following = true; Say("follow", delay: order * 0.6f); break;
                case BotCommand.Stay: Following = false; StayPosition = Me.Position; _moveTarget = null; Say("stay", delay: order * 0.6f); break;
                case BotCommand.Attack:
                case BotCommand.Pull:
                    if (from.Target is Creature t && Me.CanAttack(t))
                    {
                        _forcedTarget = t;
                        if (cmd == BotCommand.Attack || Role == Role.Tank) { Me.Target = t; Say("attack", t, delay: order * 0.4f); }
                    }
                    break;
                case BotCommand.Dungeon:
                    DungeonMode = true;
                    // Der Tank antwortet zuerst, die anderen beziehen sich darauf (Bots reden miteinander).
                    float d = Role == Role.Tank ? 0.8f : 2.2f + order * 1.1f;
                    Say("dungeon", delay: d);
                    if (Role != Role.Tank && _w.Random.Chance(0.5)) Say("dungeon_reply", delay: d + 2.5f);
                    if (Role == Role.Healer && Me.PowerPct < 0.8f) { Resting = false; _buffTimer = 0; }
                    break;
                case BotCommand.Rest: if (!Me.InCombat) { Resting = true; StartRestAbilities(Me.PowerPct); Say("rest", delay: order * 0.5f); } break;
                case BotCommand.Passive: Stance = BotStance.Passive; Say("passive", delay: order * 0.5f); break;
                case BotCommand.Assist: Stance = BotStance.Assist; Say("follow", delay: order * 0.5f); break;
                case BotCommand.Aggressive: Stance = BotStance.Aggressive; Say("aggressive", delay: order * 0.5f); break;
                case BotCommand.Status: Say("status", delay: order * 0.7f, force: true); break;
                case BotCommand.Buff: _buffTimer = 0; break;
            }
        }

        public void OnWhisper(Player from, string text)
        {
            var lower = text.ToLowerInvariant();
            if (lower.Contains("inv") || lower.Contains("gruppe") || lower.Contains("mitkommen"))
            {
                if (Me.Group != null) { _w.Bots.Whisper(Me, from, "Sorry, ich bin schon in einer Gruppe."); return; }
                if (Math.Abs(from.Level - Me.Level) > 6) { _w.Bots.Whisper(Me, from, "Wir sind zu weit auseinander in der Stufe, sorry."); return; }
                _w.Bots.Whisper(Me, from, "Klar, lad mich ein!");
                return;
            }
            if (_w.Random.Chance(0.7)) _w.Bots.Whisper(Me, from, Data.Archetype == "wortkarg" ? "Hm?" : "Hallo! Brauchst du Hilfe? Lad mich einfach in deine Gruppe ein.");
        }

        public void OnDamaged(Unit source, int amount)
        {
            if (Resting) StopResting();
            if (!InGroup && source is Creature c && _prey == null) _prey = c;
        }

        public void OnTargetedBy(Creature c) { if (Resting) StopResting(); }

        public void OnKill(Creature c)
        {
            _firstTankHit.Remove(c);
            if (_forcedTarget == c) _forcedTarget = null;
            if (!InGroup) _killsSinceTown++;
            if (c.IsBoss) Say("boss_kill", delay: 0.5f + (float)_w.Random.NextDouble() * 2);
            else if (_w.Random.Chance(Data.Chattiness * 0.08)) Say("kill");
        }

        public void OnLevelUp()
        {
            _w.Progression.AutoTrain(Me);
            if (Profile != null) _w.Progression.AutoSpendTalents(Me, Profile.TalentTree);
            Me.MarkStatsDirty();
            Say("level_up", force: InGroup);
            if (Me.Group != null)
                foreach (var b in Me.Group.Bots.Where(b => b != Me))
                    if (_w.Random.Chance(b.Bot!.Data.Chattiness * 0.6)) b.Bot.Say("level_up_reply", Me, delay: 1.5f + (float)_w.Random.NextDouble() * 2);
        }

        public void OnDied() { Say("died", force: true); _moveTarget = null; Resting = false; }
        public void OnGroupMemberDied(Player p) { }
        public void OnQuestAccepted(QuestDef q) { }
        public void OnQuestReady(QuestDef q) { if (InGroup && _w.Random.Chance(Data.Chattiness * 0.4)) Say("quest_done"); }

        public void OnAbilityUsed(AbilityDef ab, Unit? target)
        {
            if (Role == Role.Tank && target is Creature c && !_firstTankHit.ContainsKey(c)) _firstTankHit[c] = Now;
            if (Me.Group != null && Role == Role.Tank && target is Creature tc)
                foreach (var b in Me.Group.Bots) b.Bot!._firstTankHit.TryAdd(tc, Now);
            if (Role == Role.Healer && InGroup && Me.PowerType == PowerType.Mana && Me.PowerPct < 0.2f && !_toldLowMana)
            {
                _toldLowMana = true;
                Say("low_mana", force: true);
                var tank = Me.Group!.Bots.FirstOrDefault(b => b.Bot!.Role == Role.Tank);
                tank?.Bot!.Say("low_mana_reply", Me, delay: 2f);
            }
        }

        // ================================================================== Sprechen

        /// <summary>Sagt eine zum Ereignis, zur Rolle und Persönlichkeit passende Zeile (mit Platzhaltern).</summary>
        public void Say(string ev, Unit? target = null, float delay = 0, bool force = false, ChatChannel? channel = null)
        {
            if (!force && _lastSaid.TryGetValue(ev, out var last) && Now - last < 8) return;
            if (!force && ev is not ("join" or "dungeon" or "dungeon_reply" or "follow" or "stay" or "attack" or "low_mana" or "status" or "dismiss" or "boss_kill" or "died" or "wipe")
                && !_w.Random.Chance(0.25 + Data.Chattiness * 0.75)) return;
            var line = _w.Bots.PickLine(ev, Role, Data.Archetype);
            if (line == null) return;
            _lastSaid[ev] = Now;
            var text = Fill(line, target);
            _w.Bots.ScheduleChat(Me, channel ?? (InGroup ? ChatChannel.Party : ChatChannel.Say), text, delay);
        }

        private string Fill(string line, Unit? target)
        {
            var g = Me.Group;
            string tank = g?.MemberWithRole(Role.Tank)?.Name ?? "Tank";
            string healer = g?.Members.FirstOrDefault(m => m.Bot?.Role == Role.Healer)?.Name ?? "Heiler";
            string powerName = Me.PowerType switch { PowerType.Rage => "Wut", PowerType.Energy => "Energie", _ => "Mana" };
            return line.Replace("{me}", Me.Name).Replace("{leader}", g?.Leader.Name ?? Me.Name).Replace("{tank}", tank).Replace("{healer}", healer)
                .Replace("{target}", target?.Name ?? "das Ziel").Replace("{zone}", Me.Zone?.Def.Name ?? "")
                .Replace("{level}", Me.Level.ToString()).Replace("{health}", ((int)(Me.HealthPct * 100)).ToString())
                .Replace("{power}", ((int)(Me.PowerPct * 100)).ToString()).Replace("{powername}", powerName).Replace("{item}", target?.Name ?? "das");
        }

        // ================================================================== Hilfen

        private AbilityDef? Ranked(string baseId) => _w.Content.GetAbilityRank(baseId, Me.KnownRank(baseId));

        private IEnumerable<AbilityDef> KnownAbilities()
        {
            foreach (var kv in Me.Data.Abilities)
            {
                var a = _w.Content.GetAbilityRank(kv.Key, kv.Value);
                if (a != null) yield return a;
            }
        }

        private bool TryAbility(string baseId, Unit target)
        {
            if (Me.KnownRank(baseId) <= 0) return false;
            var ab = Ranked(baseId);
            return ab != null && _w.Combat.TryCast(Me, ab, target) == null;
        }

        private void Decide(string what) => LastDecision = what;
    }

    /// <summary>Versteht einfache Befehle im Gruppenchat (deutsch, teils englisch).</summary>
    public static class BotCommandParser
    {
        private static readonly (BotCommand cmd, string[] words)[] Table =
        {
            (BotCommand.Dungeon, new[] { "dungeon", "instanz", "krypta", "verlies" }),
            (BotCommand.Stay, new[] { "wartet", "warte", "bleibt", "bleib", "stopp", "stop", "halt", "stay" }),
            (BotCommand.Follow, new[] { "folgt", "folge", "folg", "kommt", "follow", "mir nach" }),
            (BotCommand.Pull, new[] { "zieh", "pull", "pullen" }),
            (BotCommand.Attack, new[] { "angriff", "greift", "greif", "attack", "auf ihn", "attacke", "los!" }),
            (BotCommand.Rest, new[] { "pause", "rast", "trinkt", "esst", "ausruhen" }),
            (BotCommand.Passive, new[] { "passiv" }),
            (BotCommand.Aggressive, new[] { "aggressiv", "aggro" }),
            (BotCommand.Assist, new[] { "verteidig", "normal", "assist" }),
            (BotCommand.Status, new[] { "status", "wie geht", "bericht" }),
            (BotCommand.Buff, new[] { "buff" }),
        };

        public static BotCommand Parse(string text)
        {
            var t = " " + text.ToLowerInvariant() + " ";
            foreach (var (cmd, words) in Table)
                foreach (var w in words)
                    if (t.Contains(w)) return cmd;
            return BotCommand.None;
        }
    }
}
