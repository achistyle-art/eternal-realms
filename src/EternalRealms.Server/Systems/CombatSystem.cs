using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Serverseitige Kampfregeln. Jeder Schaden, jede Heilung und jede Aura wird hier berechnet –
    /// Clients senden nur Absichten ("wirke Feuerball auf Ziel 42").
    /// </summary>
    public sealed class CombatSystem
    {
        private readonly GameWorld _w;

        public CombatSystem(GameWorld world) { _w = world; }

        private IRandom Rng => _w.Random;

        // ================================================================== Zaubern

        /// <summary>Versucht eine Fähigkeit zu wirken. Rückgabe: Fehlermeldung oder null bei Erfolg.</summary>
        public string? TryCast(Unit caster, AbilityDef ab, Unit? explicitTarget, string? itemId = null)
        {
            if (caster.IsDead) return "Ihr seid tot.";
            if (caster is Player pl && pl.IsFlying) return "Nicht während des Fluges.";
            if (caster.IsStunned || caster.IsFeared) return "Ihr könnt das gerade nicht tun.";
            if (caster.IsSilenced && ab.IsSpell) return "Ihr seid zum Schweigen gebracht.";
            if (caster.SchoolLockouts.ContainsKey(ab.School) && ab.IsSpell) return "Diese Zauberschule ist gesperrt.";
            if (caster.Cast != null) return "Ihr zaubert bereits.";
            if (ab.TriggersGcd && caster.Gcd > 0) return "Die Fähigkeit ist noch nicht bereit.";
            if (caster.CooldownLeft(ab.Id) > 0) return "Die Fähigkeit ist noch nicht bereit.";
            if (ab.OutOfCombatOnly && caster.InCombat) return "Nicht im Kampf möglich.";
            if (ab.RequiresStealth && !caster.IsStealthed) return "Nur aus der Verstohlenheit möglich.";
            if (ab.RequiredForms.Count > 0 && !ab.RequiredForms.Contains(caster.Form)) return "Ihr seid nicht in der richtigen Haltung oder Gestalt.";
            if (caster is Player p)
            {
                if (ab.RequiresShield && !p.HasShield) return "Benötigt einen Schild.";
                if (ab.RequiresRangedWeapon && !p.RangedWeapon.Present) return "Benötigt eine Fernkampfwaffe.";
                var formAura = ab.Effects.FirstOrDefault(e => e.Type == EffectType.ApplyAura && e.Aura != null && _w.Content.Auras[e.Aura].Form != Form.None);
                if (formAura != null && caster.Form == _w.Content.Auras[formAura.Aura!].Form) return "Bereits aktiv.";
                // Druiden verlassen ihre Gestalt automatisch, um Manazauber zu wirken.
                if (p.Class == CharClass.Druid && (p.Form == Form.Bear || p.Form == Form.Cat) && ab.PowerType == PowerType.Mana && ab.Cost > 0 && formAura == null)
                    p.RemoveAuras(a => a.Def.Form != Form.None);
            }
            if (ab.RequiresComboPoints && caster.ComboPoints <= 0) return "Keine Kombopunkte.";

            var (target, err) = ResolveTarget(caster, ab, explicitTarget);
            if (err != null) return err;
            if (ab.RequiresComboPoints && target != null && target != caster && caster.ComboTargetId != target.Id) return "Keine Kombopunkte auf diesem Ziel.";
            if (target != null && target != caster)
            {
                var rangeErr = CheckRange(caster, ab, target);
                if (rangeErr != null) return rangeErr;
                if (ab.TargetHealthBelow > 0 && target.HealthPct >= ab.TargetHealthBelow) return "Das Ziel ist noch zu stark.";
                if (BlockedByAura(ab, target)) return "Das Ziel kann davon gerade nicht profitieren.";
            }
            int cost = EffectiveCost(caster, ab);
            if (cost > 0 && caster.Power < cost)
                return caster.PowerType switch { PowerType.Rage => "Nicht genug Wut.", PowerType.Energy => "Nicht genug Energie.", _ => "Nicht genug Mana." };

            float castTime = EffectiveCastTime(caster, ab);
            if (ab.TriggersGcd) caster.Gcd = caster.PowerType == PowerType.Energy ? CombatMath.EnergyGlobalCooldown : CombatMath.GlobalCooldown;
            if (target != null && target != caster && ab.IsHarmful) caster.Facing = Vec3.FacingTowards(caster.Position, target.Position);

            if (castTime > 0)
            {
                caster.Cast = new CastState { Ability = ab, Target = target, Remaining = castTime, Total = castTime, StartPosition = caster.Position, ItemId = itemId };
                caster.Zone?.BroadcastFrom(caster, new S_CastStart { CasterId = caster.Id, TargetId = target?.Id ?? 0, AbilityId = ab.RankedId, CastTime = castTime, Channeled = ab.Channeled });
                return null;
            }
            Execute(caster, ab, target, cost, itemId);
            return null;
        }

        public (Unit? target, string? error) ResolveTarget(Unit caster, AbilityDef ab, Unit? explicitTarget)
        {
            switch (ab.Target)
            {
                case TargetType.Self:
                case TargetType.Party:
                case TargetType.AreaAroundSelf:
                    if (ab.Target == TargetType.Self && ab.Effects.Any(e => e.Type == EffectType.Heal) && explicitTarget is Creature pet && pet.Owner == caster)
                        return (pet, null);
                    return (caster, null);
                case TargetType.Enemy:
                case TargetType.AreaAroundTarget:
                {
                    var t = explicitTarget ?? caster.Target;
                    if (t == null) return (null, "Ihr habt kein Ziel.");
                    if (!caster.CanAttack(t)) return (null, "Ungültiges Ziel.");
                    return (t, null);
                }
                case TargetType.Friendly:
                case TargetType.FriendlyOrSelf:
                {
                    var t = explicitTarget ?? caster.Target;
                    if (t != null && t.IsAlive && caster.IsFriendlyTo(t)) return (t, null);
                    if (ab.Target == TargetType.FriendlyOrSelf) return (caster, null);
                    return (null, "Ungültiges Ziel.");
                }
                case TargetType.DeadFriendly:
                {
                    var t = explicitTarget ?? caster.Target;
                    if (t is Player dead && dead.IsDead && !caster.IsHostileTo(dead)) return (dead, null);
                    return (null, "Das Ziel muss ein toter Verbündeter sein.");
                }
            }
            return (caster, null);
        }

        /// <summary>
        /// Schutzschilde legen einen Debuff an, der ein erneutes Schild verhindert (z. B. Geschwächte Seele).
        /// Wirkt eine Fähigkeit genau so einen Debuff, darf sie nicht auf ein Ziel mit diesem Debuff gewirkt werden.
        /// </summary>
        public bool BlockedByAura(AbilityDef ab, Unit target) =>
            !ab.IsHarmful && ab.Effects.Any(e => e.Type == EffectType.ApplyAura && e.Aura != null && _w.Content.Auras.TryGetValue(e.Aura, out var a) && a.IsDebuff && target.FindAura(a.Id) != null);

        public string? CheckRange(Unit caster, AbilityDef ab, Unit target)
        {
            float dist = caster.DistanceTo(target);
            if (target is Player tp && tp.DeathState == DeathState.Ghost) dist = Vec3.DistanceXZ(caster.Position, tp.Data.CorpsePosition);
            if (ab.MaxRange <= CombatMath.MeleeRange + 0.01f)
            {
                if (!caster.InMeleeRange(target)) return "Das Ziel ist außer Reichweite.";
                return null;
            }
            if (dist > ab.MaxRange + target.CombatReach) return "Das Ziel ist außer Reichweite.";
            if (ab.MinRange > 0 && dist < ab.MinRange) return "Das Ziel ist zu nah.";
            return null;
        }

        public int EffectiveCost(Unit caster, AbilityDef ab)
        {
            if (ab.Cost <= 0) return 0;
            float pct = caster.Mod(ModType.CostPercent, ab.School, ab.Id);
            return Math.Max(0, (int)Math.Round(ab.Cost * (1 + pct / 100f)));
        }

        public float EffectiveCastTime(Unit caster, AbilityDef ab)
        {
            if (ab.CastTime <= 0) return 0;
            return Math.Max(0.5f, ab.CastTime + caster.Mod(ModType.CastTimeFlat, ab.School, ab.Id));
        }

        public void CancelCast(Unit caster, string reason)
        {
            if (caster.Cast == null) return;
            var ab = caster.Cast.Ability;
            caster.Cast = null;
            caster.Zone?.BroadcastFrom(caster, new S_CastEnd { CasterId = caster.Id, AbilityId = ab?.RankedId ?? "", Success = false, Reason = reason });
        }

        private void CompleteCast(Unit caster)
        {
            var cast = caster.Cast!;
            caster.Cast = null;
            if (cast.Object != null && caster is Player op)
            {
                _w.Interactions.CompleteObjectInteraction(op, cast.Object);
                op.Zone?.BroadcastFrom(op, new S_CastEnd { CasterId = op.Id, AbilityId = "interact", Success = true });
                return;
            }
            var ab = cast.Ability;
            var target = cast.Target;
            if (target != null && target != caster)
            {
                bool valid = ab.Target == TargetType.DeadFriendly ? target.IsDead && !target.Removed : !target.Removed && (ab.IsHarmful ? caster.CanAttack(target) : target.IsAlive);
                if (!valid || target.Zone != caster.Zone) { CancelCast(caster, "Ungültiges Ziel."); caster.Cast = null; return; }
                float slack = ab.MaxRange + target.CombatReach + 5f;
                if (caster.DistanceTo(target) > slack && !(target is Player g && g.DeathState == DeathState.Ghost)) { BroadcastFail(caster, ab, "Das Ziel ist außer Reichweite."); return; }
            }
            int cost = EffectiveCost(caster, ab);
            if (caster.Power < cost) { BroadcastFail(caster, ab, "Nicht genug Mana."); return; }
            Execute(caster, ab, target, cost, cast.ItemId);
        }

        private void BroadcastFail(Unit caster, AbilityDef ab, string reason)
        {
            caster.Zone?.BroadcastFrom(caster, new S_CastEnd { CasterId = caster.Id, AbilityId = ab.RankedId, Success = false, Reason = reason });
            if (caster is Player p) p.Notice(reason, true);
        }

        // ================================================================== Ausführen

        private void Execute(Unit caster, AbilityDef ab, Unit? target, int cost, string? itemId)
        {
            if (cost > 0)
            {
                caster.Power -= cost;
                if (caster.PowerType == PowerType.Mana) caster.LastManaSpendTime = _w.Time;
            }
            float cd = ab.Cooldown + caster.Mod(ModType.CooldownFlat, ab.School, ab.Id);
            caster.SetCooldown(ab.Id, cd);
            if (caster is Player cp)
            {
                cp.Send(new S_Cooldown { AbilityId = ab.Id, Seconds = cd, Gcd = ab.TriggersGcd ? caster.Gcd : 0 });
                if (itemId != null) cp.RemoveItem(itemId, 1);
                if (itemId != null) cp.Send(cp.BuildInventory());
            }
            bool grantsStealth = ab.Effects.Any(e => e.Aura != null && _w.Content.Auras.TryGetValue(e.Aura, out var sa) && sa.Stealth);
            if (!grantsStealth && (ab.IsHarmful || ab.RequiresStealth)) BreakStealth(caster);

            var targets = CollectTargets(caster, ab, target);
            bool harmful = ab.IsHarmful;
            caster.Zone?.BroadcastFrom(caster, new S_CastEnd { CasterId = caster.Id, AbilityId = ab.RankedId, Success = true });

            foreach (var t in targets)
            {
                if (harmful && t != caster)
                {
                    if (!caster.CanAttack(t)) continue;
                    caster.EnterCombatWith(t);
                    if (t is Creature tc) tc.AI.OnAttacked(caster);
                    var hit = RollAbilityHit(caster, t, ab);
                    if (hit != CombatResult.Hit)
                    {
                        Event(caster, t, ab.RankedId, hit, ab.School, 0);
                        if (t is Creature mc) mc.Threat.Add(caster, 1);
                        continue;
                    }
                }
                ApplyEffects(caster, ab, t);
            }
            if (ab.RequiresComboPoints) { caster.ComboPoints = 0; caster.ComboTargetId = 0; }
            if (caster is Player bp && bp.Bot != null) bp.Bot.OnAbilityUsed(ab, target);
        }

        private List<Unit> CollectTargets(Unit caster, AbilityDef ab, Unit? target)
        {
            var zone = caster.Zone;
            if (zone == null) return new List<Unit>();
            switch (ab.Target)
            {
                case TargetType.AreaAroundSelf:
                case TargetType.AreaAroundTarget:
                {
                    var center = ab.Target == TargetType.AreaAroundSelf ? caster.Position : (target ?? caster).Position;
                    bool harmful = ab.IsHarmful;
                    var list = zone.UnitsInRange(center, ab.Radius)
                        .Where(u => harmful ? caster.CanAttack(u) : (u.IsAlive && caster.IsFriendlyTo(u) && IsGroupMate(caster, u)))
                        .OrderBy(u => Vec3.DistanceXZ(u.Position, center)).Take(ab.MaxTargets).ToList();
                    if (!harmful && !list.Contains(caster)) list.Insert(0, caster);
                    return list;
                }
                case TargetType.Party:
                {
                    var list = new List<Unit> { caster };
                    if (caster is Player p && p.Group != null)
                        foreach (var m in p.Group.Members)
                            if (m != p && m.Zone == zone && m.IsAlive && m.DistanceTo(p) <= Math.Max(ab.MaxRange, 30)) list.Add(m);
                    if (caster is Player op && op.Pet != null && op.Pet.IsAlive) list.Add(op.Pet);
                    return list;
                }
                default:
                    return target == null ? new List<Unit>() : new List<Unit> { target };
            }
        }

        private static bool IsGroupMate(Unit caster, Unit u)
        {
            if (u == caster) return true;
            if (caster is Player p && u is Player q) return p.Group != null && p.Group == q.Group;
            if (caster is Player o && u is Creature pet) return pet.Owner == o;
            return caster is Creature && u is Creature;
        }

        private CombatResult RollAbilityHit(Unit caster, Unit target, AbilityDef ab)
        {
            if (target.IsEvading) return CombatResult.Evade;
            bool physical = !ab.IsSpell;
            if (physical)
            {
                var c = new AttackTable.Chances
                {
                    Miss = CombatMath.MeleeMissChance(caster.Level, target.Level, caster.HitBonus, false),
                    Dodge = target.Dodge / 100f,
                    Parry = ab.MaxRange <= CombatMath.MeleeRange + 0.01f ? target.Parry / 100f : 0,
                };
                var r = AttackTable.Roll(c, Rng, false);
                return r == CombatResult.Miss || r == CombatResult.Dodge || r == CombatResult.Parry ? r : CombatResult.Hit;
            }
            float miss = CombatMath.SpellMissChance(caster.Level, target.Level, caster.Mod(ModType.HitChance, ab.School));
            return Rng.Chance(miss) ? CombatResult.Resist : CombatResult.Hit;
        }

        private void ApplyEffects(Unit caster, AbilityDef ab, Unit t)
        {
            foreach (var e in ab.Effects)
            {
                var victim = e.OnSelf ? caster : t;
                switch (e.Type)
                {
                    case EffectType.SchoolDamage:
                    {
                        float amount = Rng.Range(e.Min, e.Max);
                        if (ab.RequiresComboPoints) amount *= Math.Max(1, caster.ComboPoints);
                        if (e.School == School.Physical) amount += caster.AttackPower * (ab.RequiresComboPoints ? 0.03f * caster.ComboPoints : 0.1f);
                        else amount += caster.SpellPower * e.Coefficient;
                        if (e.OnSelf)
                        {
                            int self = Math.Min((int)amount, caster.Health - 1);
                            if (self > 0) { caster.Health -= self; Event(caster, caster, ab.RankedId, CombatResult.Hit, e.School, self); }
                            break;
                        }
                        bool crit = RollCrit(caster, ab, e.School);
                        DealDamage(caster, victim, amount, e.School, ab.Id, ab.RankedId, crit, false, e.ThreatMultiplier, e.Threat);
                        break;
                    }
                    case EffectType.WeaponDamage:
                    {
                        bool ranged = ab.RequiresRangedWeapon && caster.RangedWeapon.Present;
                        var w = ranged ? caster.RangedWeapon : caster.MainHand;
                        float ap = ranged ? caster.RangedAttackPower : caster.AttackPower;
                        float amount = (Rng.Range(w.Min, w.Max) + ap / 14f * w.Speed) * e.WeaponPercent + Rng.Range(e.Min, e.Max);
                        bool crit = RollCrit(caster, ab, School.Physical);
                        DealDamage(caster, victim, amount, School.Physical, ab.Id, ab.RankedId, crit, false, e.ThreatMultiplier, e.Threat);
                        break;
                    }
                    case EffectType.Heal:
                    {
                        float amount = Rng.Range(e.Min, e.Max) + caster.HealingPower * e.Coefficient;
                        bool crit = Rng.Chance((caster.SpellCrit + caster.Mod(ModType.CritChance, ab.School, ab.Id, crit: CritCategory.Spell) - caster.Mod(ModType.CritChance, crit: CritCategory.Spell)) / 100f);
                        if (crit) amount *= 1.5f;
                        Heal(caster, victim, amount, ab.RankedId, crit, false);
                        break;
                    }
                    case EffectType.ApplyAura:
                        if (e.Aura != null && _w.Content.Auras.TryGetValue(e.Aura, out var aura))
                        {
                            ApplyAura(caster, victim, aura, ab.RankedId);
                            if (e.Threat > 0 && victim is Creature tc2) tc2.Threat.Add(caster, e.Threat * ThreatMod(caster, ab));
                        }
                        break;
                    case EffectType.Interrupt:
                        if (victim.Cast != null && victim.Cast.Object == null)
                        {
                            var school = victim.Cast.Ability.School;
                            CancelCast(victim, "Unterbrochen");
                            if (e.Duration > 0) victim.SchoolLockouts[school] = e.Duration;
                            Event(caster, victim, ab.RankedId, CombatResult.Hit, school, 0);
                            if (caster is Player ip && ip.Bot != null) ip.Bot.Say("interrupt", victim);
                        }
                        break;
                    case EffectType.Taunt:
                        if (victim is Creature tc)
                        {
                            var top = tc.Threat.Top();
                            float topThreat = top != null ? tc.Threat.Get(top) : 0;
                            tc.Threat.Set(caster, Math.Max(tc.Threat.Get(caster), topThreat + 1));
                            tc.Threat.Force(caster, 3f);
                            caster.EnterCombatWith(tc);
                            tc.AI.OnAttacked(caster);
                        }
                        break;
                    case EffectType.Resurrect:
                        if (victim is Player dp && dp.IsDead) _w.Death.Resurrect(dp, e.Min / 100f, caster);
                        break;
                    case EffectType.GainPower:
                    {
                        int amount = Rng.Range(e.Min, e.Max);
                        victim.Power = Math.Min(victim.MaxPower, victim.Power + amount);
                        break;
                    }
                    case EffectType.Charge:
                        if (caster.Zone != null && victim != caster)
                        {
                            var dir = (caster.Position - victim.Position).NormalizedXZ();
                            caster.Zone.SetPosition(caster, victim.Position + dir * (victim.CombatReach + 1.5f));
                            caster.Facing = Vec3.FacingTowards(caster.Position, victim.Position);
                            if (caster is Player chp) chp.LastMoveTime = _w.Time;
                        }
                        break;
                    case EffectType.Dispel:
                    {
                        int n = Math.Max(1, e.Min);
                        foreach (var a in victim.Auras.Where(a => a.Def.IsDebuff && a.Def.Dispellable).Take(n).ToList()) victim.RemoveAura(a);
                        break;
                    }
                    case EffectType.SummonPet:
                        if (caster is Player sp && e.Pet != null) _w.SummonPet(sp, e.Pet);
                        break;
                    case EffectType.Teleport:
                        if (caster is Player tp && e.Zone != null) _w.TeleportSpecial(tp, e.Zone, e.Position);
                        break;
                    case EffectType.RestorePercent:
                        if (e.Aura != null && _w.Content.Auras.TryGetValue(e.Aura, out var regen))
                        {
                            var applied = ApplyAura(caster, victim, regen, ab.RankedId);
                            if (applied != null)
                            {
                                int ticks = Math.Max(1, (int)(regen.Duration / regen.TickInterval));
                                int pool = regen.Periodic == PeriodicType.Power ? victim.MaxPower : victim.MaxHealth;
                                applied.TickOverride = Math.Max(1, pool * e.Min / 100 / ticks);
                            }
                        }
                        break;
                    case EffectType.ComboPoints:
                        if (caster.ComboTargetId != victim.Id) { caster.ComboPoints = 0; caster.ComboTargetId = victim.Id; }
                        caster.ComboPoints = Math.Min(5, caster.ComboPoints + Math.Max(1, e.Min));
                        break;
                    case EffectType.ReduceThreat:
                        if (caster.Zone != null)
                            foreach (var c in caster.Zone.Entities.Values.OfType<Creature>().Where(c => c.Threat.Contains(caster)).ToList())
                            {
                                if (e.Min >= 100) { c.Threat.Remove(caster); caster.CombatWith.Remove(c); c.CombatWith.Remove(caster); }
                                else c.Threat.Set(caster, c.Threat.Get(caster) * (1 - e.Min / 100f));
                            }
                        break;
                }
            }
        }

        private bool RollCrit(Unit caster, AbilityDef ab, School school)
        {
            float chance;
            if (school == School.Physical) chance = caster.MeleeCrit + caster.Mod(ModType.CritChance, school, ab.Id, crit: CritCategory.Melee) - caster.Mod(ModType.CritChance, crit: CritCategory.Melee);
            else chance = caster.SpellCrit + caster.Mod(ModType.CritChance, school, ab.Id, crit: CritCategory.Spell) - caster.Mod(ModType.CritChance, crit: CritCategory.Spell);
            return Rng.Chance(chance / 100f);
        }

        private float ThreatMod(Unit caster, AbilityDef? ab, School? school = null) =>
            1 + caster.Mod(ModType.ThreatPercent, school ?? ab?.School, ab?.Id) / 100f;

        // ================================================================== Schaden & Heilung

        public int DealDamage(Unit? source, Unit target, float amount, School school, string? abilityBase, string eventId, bool crit, bool periodic,
            float threatMultiplier = 1f, int flatThreat = 0, bool glancing = false, bool blocked = false)
        {
            if (target.IsDead || target.Removed) return 0;
            if (target.IsEvading) { Event(source, target, eventId, CombatResult.Evade, school, 0); return 0; }
            if (source != null)
            {
                amount *= 1 + source.Mod(ModType.DamageDonePercent, school, abilityBase) / 100f;
                if (source is Creature sc) amount *= sc.DamageMultiplier;
            }
            if (crit) amount *= (school == School.Physical ? 2f : 1.5f) + (source?.Mod(ModType.CritDamagePercent, school, abilityBase) ?? 0) / 100f;
            if (glancing) amount *= 0.75f;
            if (blocked) amount *= 0.7f;
            amount *= 1 + target.Mod(ModType.DamageTakenPercent, school) / 100f;
            if (school == School.Physical) amount *= 1 - CombatMath.ArmorReduction(target.Armor, source?.Level ?? target.Level);
            int resisted = 0;
            if (school != School.Physical && school != School.Holy)
            {
                float avg = CombatMath.AverageResist(target.Resist[(int)school], source?.Level ?? target.Level);
                float part = CombatMath.RollPartialResist(avg, Rng);
                resisted = (int)(amount * part);
                amount -= resisted;
            }
            int dmg = Math.Max(amount > 0 ? 1 : 0, (int)Math.Round(amount));
            int absorbed = 0;
            foreach (var a in target.Auras.Where(a => a.AbsorbLeft > 0).ToList())
            {
                int take = Math.Min(a.AbsorbLeft, dmg - absorbed);
                a.AbsorbLeft -= take;
                absorbed += take;
                if (a.AbsorbLeft <= 0) target.RemoveAura(a);
                if (absorbed >= dmg) break;
            }
            int final = dmg - absorbed;
            int overkill = Math.Max(0, final - target.Health);
            target.Health -= final;

            if (source != null)
            {
                source.EnterCombatWith(target);
                if (target is Creature tc)
                {
                    // Mod(…, school, ability) enthält allgemeine, schulgebundene und fähigkeitsgebundene Modifikatoren.
                    float threat = (final + absorbed) * threatMultiplier * (1 + source.Mod(ModType.ThreatPercent, school, abilityBase) / 100f) + flatThreat;
                    tc.Threat.Add(source, Math.Max(1, threat));
                    var owner = source as Player ?? (source as Creature)?.Owner;
                    if (tc.TappedBy == null && owner != null && !tc.IsPet && tc.Owner == null) tc.TappedBy = owner;
                    tc.AI.OnAttacked(source);
                }
                if (target.PowerType == PowerType.Rage && final > 0)
                    target.Power = Math.Min(target.MaxPower, target.Power + (int)Math.Ceiling(CombatMath.RageFromDamage(final, target.Level, false)));
            }
            if (target.Cast != null && final > 0 && target.Cast.Object == null && !target.Cast.Ability.Channeled && target.Cast.Pushbacks < 2)
            {
                float resist = target.Mod(ModType.PushbackResistPercent, target.Cast.Ability.School, target.Cast.Ability.Id);
                if (!Rng.Chance(resist / 100f)) { target.Cast.Remaining += 0.5f; target.Cast.Pushbacks++; }
            }
            else if (target.Cast?.Object != null && final > 0) CancelCast(target, "Unterbrochen");
            if (final > 0 || absorbed > 0)
            {
                target.RemoveAuras(a => a.Def.BreaksOnDamage || (a.Def.BreaksOnMove && !a.Def.IsDebuff));
                BreakStealth(target);
                if (source != null) BreakStealth(source);
            }
            var result = absorbed > 0 && final == 0 ? CombatResult.Absorb : crit ? CombatResult.Crit : glancing ? CombatResult.Glancing : blocked ? CombatResult.Block : resisted > 0 ? CombatResult.PartialResist : CombatResult.Hit;
            target.Zone?.BroadcastFrom(target, new S_CombatEvent
            {
                SourceId = source?.Id ?? 0, TargetId = target.Id, AbilityId = eventId, Result = result, School = school, Amount = final, Absorbed = absorbed,
                Resisted = resisted, Overkill = overkill, Periodic = periodic,
            });
            if (target is Player tp && tp.Bot != null && source != null) tp.Bot.OnDamaged(source, final);
            if (target.Health <= 0) Kill(source, target);
            return final;
        }

        public int Heal(Unit? source, Unit target, float amount, string eventId, bool crit, bool periodic)
        {
            if (target.IsDead || target.Removed) return 0;
            if (source != null) amount *= 1 + source.Mod(ModType.HealingDonePercent) / 100f;
            amount *= 1 + target.Mod(ModType.HealingTakenPercent) / 100f;
            int heal = Math.Max(0, (int)Math.Round(amount));
            int effective = Math.Min(heal, target.MaxHealth - target.Health);
            target.Health += effective;
            if (source != null && effective > 0 && target.Zone != null)
            {
                // Heilbedrohung: Hälfte der effektiven Heilung, verteilt auf alle Gegner, die das Ziel bekämpfen.
                var enemies = target.Zone.Entities.Values.OfType<Creature>().Where(c => c.IsAlive && (c.Threat.Contains(target) || target.CombatWith.Contains(c))).ToList();
                if (enemies.Count > 0)
                {
                    float each = effective * 0.5f * ThreatMod(source, null) / enemies.Count;
                    foreach (var c in enemies) { c.Threat.Add(source, each); source.EnterCombatWith(c); }
                }
            }
            target.Zone?.BroadcastFrom(target, new S_CombatEvent
            {
                SourceId = source?.Id ?? 0, TargetId = target.Id, AbilityId = eventId, Result = crit ? CombatResult.CritHeal : CombatResult.Heal,
                Amount = effective, Overkill = heal - effective, Periodic = periodic,
            });
            return effective;
        }

        public void Event(Unit? source, Unit target, string abilityId, CombatResult result, School school, int amount)
        {
            target.Zone?.BroadcastFrom(target, new S_CombatEvent { SourceId = source?.Id ?? 0, TargetId = target.Id, AbilityId = abilityId, Result = result, School = school, Amount = amount });
        }

        public void BreakStealth(Unit u)
        {
            if (u.IsStealthed) u.RemoveAuras(a => a.Def.Stealth);
        }

        // ================================================================== Auren

        public Aura? ApplyAura(Unit caster, Unit target, AuraDef def, string sourceAbility)
        {
            if (target.Removed) return null;
            if (target.IsDead && !def.PersistsThroughDeath) return null;
            if (def.IsDebuff && target.IsEvading) return null;
            if (def.IsDebuff && target is Creature boss && boss.IsBoss && (def.Stun || def.Fear)) { Event(caster, target, sourceAbility, CombatResult.Immune, def.School, 0); return null; }
            string family = Aura.Family(def.Id);
            var existing = target.Auras.FirstOrDefault(a => a.FamilyId == family && (!def.IsDebuff || a.CasterId == caster.Id));
            if (existing != null)
            {
                if (existing.Def == def)
                {
                    existing.Remaining = def.Permanent ? float.MaxValue : def.Duration;
                    if (existing.Stacks < def.MaxStacks) existing.Stacks++;
                    existing.AbsorbLeft = def.Absorb > 0 ? def.Absorb + (int)(def.AbsorbCoefficient * caster.SpellPower) : existing.AbsorbLeft;
                    target.MarkStatsDirty();
                    target.AurasChanged = true;
                    return existing;
                }
                target.RemoveAura(existing);
            }
            if (def.Form != Form.None) target.RemoveAuras(a => a.Def.Form != Form.None);
            var aura = new Aura(def, caster, sourceAbility)
            {
                AbsorbLeft = def.Absorb > 0 ? def.Absorb + (int)(def.AbsorbCoefficient * caster.SpellPower) : 0,
                CasterPower = def.Periodic == PeriodicType.Heal ? caster.HealingPower : caster.SpellPower,
            };
            target.Auras.Add(aura);
            target.AurasChanged = true;
            target.MarkStatsDirty();
            if (def.Form != Form.None) target.SetForm(def.Form);
            if ((def.Stun || def.Fear) && target.Cast != null) CancelCast(target, "Unterbrochen");
            if (def.Silence && target.Cast != null && target.Cast.Ability.IsSpell) CancelCast(target, "Unterbrochen");
            if (def.Fear)
            {
                target.FearTimer = 0;
                target.FearDirection = (target.Position - caster.Position).NormalizedXZ();
            }
            if (def.Stun || def.Fear || def.Root) { target.AutoAttacking = target.AutoAttacking && !def.Stun; }
            if (def.IsDebuff && caster != target && caster.IsHostileTo(target))
            {
                caster.EnterCombatWith(target);
                if (target is Creature c) { c.Threat.Add(caster, 5 * ThreatMod(caster, null)); c.AI.OnAttacked(caster); if (c.TappedBy == null && caster is Player pp) c.TappedBy = pp; }
            }
            if (def.PartyAura && caster is Player owner && owner.Group != null && target == caster)
                foreach (var m in owner.Group.Members.Where(m => m != owner && m.Zone == owner.Zone && m.DistanceTo(owner) <= 30))
                    ApplyAura(caster, m, def, sourceAbility);
            return aura;
        }

        // ================================================================== Pro Tick

        public void UpdateUnit(Unit u, float dt)
        {
            if (u.Removed) return;
            u.EnsureStats();
            UpdateAuras(u, dt);
            if (u.Removed) return;
            if (u.Cast != null && u.IsAlive)
            {
                u.Cast.Remaining -= dt;
                if (u.Cast.Remaining <= 0) CompleteCast(u);
            }
            if (u.IsAlive) UpdateAutoAttack(u, dt);
            UpdateRegen(u, dt);
            if (u.IsFeared && u.IsAlive && u.Zone != null) UpdateFear(u, dt);
        }

        private void UpdateAuras(Unit u, float dt)
        {
            if (u.Auras.Count == 0) return;
            foreach (var a in u.Auras.ToList())
            {
                if (!u.Auras.Contains(a)) continue;
                if (!a.Def.Permanent) a.Remaining -= dt;
                if (a.Def.Periodic != PeriodicType.None && u.IsAlive)
                {
                    a.TickTimer -= dt;
                    while (a.TickTimer <= 0 && u.IsAlive && u.Auras.Contains(a))
                    {
                        a.TickTimer += a.Def.TickInterval;
                        int amount = a.TickOverride ?? (Rng.Range(a.Def.TickMin, a.Def.TickMax) + (int)(a.CasterPower * a.Def.TickCoefficient));
                        amount *= a.Stacks;
                        switch (a.Def.Periodic)
                        {
                            case PeriodicType.Damage:
                                var src = a.Caster != null && !a.Caster.Removed ? a.Caster : null;
                                DealDamage(src, u, amount, a.Def.School, Aura.Family(a.Def.Id), a.Def.Id, false, true);
                                break;
                            case PeriodicType.Heal:
                                Heal(a.Caster != null && !a.Caster.Removed ? a.Caster : null, u, amount, a.Def.Id, false, true);
                                break;
                            case PeriodicType.Power:
                                if (u.PowerType == PowerType.Mana) u.Power = Math.Min(u.MaxPower, u.Power + amount);
                                break;
                        }
                    }
                }
                if (a.Expired && u.Auras.Contains(a)) u.RemoveAura(a);
            }
        }

        private void UpdateAutoAttack(Unit u, float dt)
        {
            if (!u.AutoAttacking || u.Target == null)
            {
                if (u.SwingTimer > 0) u.SwingTimer = Math.Max(0, u.SwingTimer - dt);
                return;
            }
            var t = u.Target;
            if (!u.CanAttack(t) || t.Zone != u.Zone) { u.AutoAttacking = false; return; }
            if (!u.CanAct) return;
            bool hunterRanged = u is Player hp && hp.Class == CharClass.Hunter && hp.RangedWeapon.Present;
            float dist = u.DistanceTo(t);
            if (hunterRanged && dist >= 8 && dist <= 35 + t.CombatReach)
            {
                u.RangedSwingTimer -= dt;
                if (u.Cast != null) return;
                if (u.RangedSwingTimer <= 0)
                {
                    u.RangedSwingTimer = u.RangedWeapon.Speed / u.AttackSpeedMultiplier;
                    WeaponSwing(u, t, u.RangedWeapon, true);
                }
                return;
            }
            u.SwingTimer = Math.Max(0, u.SwingTimer - dt);
            if (u.Cast != null || !u.InMeleeRange(t)) return;
            if (u.SwingTimer <= 0)
            {
                u.SwingTimer = u.MainHand.Speed / u.AttackSpeedMultiplier;
                u.Facing = Vec3.FacingTowards(u.Position, t.Position);
                WeaponSwing(u, t, u.MainHand, false);
            }
        }

        public void WeaponSwing(Unit attacker, Unit target, WeaponInfo w, bool ranged)
        {
            if (target.IsEvading) { Event(attacker, target, "melee", CombatResult.Evade, w.School, 0); return; }
            BreakStealth(attacker);
            bool attackerIsPlayer = attacker.IsPlayerControlled;
            int lvlDiff = target.Level - attacker.Level;
            var c = new AttackTable.Chances
            {
                Miss = CombatMath.MeleeMissChance(attacker.Level, target.Level, attacker.HitBonus, false),
                Dodge = target.Dodge / 100f,
                Parry = ranged ? 0 : target.Parry / 100f,
                Glancing = attackerIsPlayer && target is Creature && !ranged ? CombatMath.GlancingChance(attacker.Level, target.Level) : 0,
                Block = target.Block / 100f,
                Crit = Math.Max(0, attacker.MeleeCrit / 100f - Math.Max(0, lvlDiff) * 0.01f),
            };
            var result = AttackTable.Roll(c, Rng, true);
            attacker.EnterCombatWith(target);
            if (target is Creature tc) tc.AI.OnAttacked(attacker);
            if (result == CombatResult.Miss || result == CombatResult.Dodge || result == CombatResult.Parry)
            {
                Event(attacker, target, ranged ? "shoot" : "melee", result, w.School, 0);
                if (target is Creature mc) mc.Threat.Add(attacker, 1);
                return;
            }
            float ap = ranged ? attacker.RangedAttackPower : attacker.AttackPower;
            float dmg = Rng.Range(w.Min, w.Max) + ap / 14f * w.Speed;
            int dealt = DealDamage(attacker, target, dmg, w.School, null, ranged ? "shoot" : "melee", result == CombatResult.Crit, false, 1f, 0,
                result == CombatResult.Glancing, result == CombatResult.Block);
            if (attacker.PowerType == PowerType.Rage && dealt > 0)
            {
                float rage = CombatMath.RageFromDamage(dealt, attacker.Level, true) * (1 + attacker.Mod(ModType.RageGenerationPercent) / 100f);
                attacker.Power = Math.Min(attacker.MaxPower, attacker.Power + (int)Math.Ceiling(rage));
            }
            if (dealt > 0 && !target.IsDead && !ranged)
                foreach (var a in attacker.Auras.Where(a => a.Def.ProcMax > 0).ToList())
                {
                    float proc = Rng.Range(a.Def.ProcMin, a.Def.ProcMax) + attacker.SpellPower * a.Def.ProcCoefficient;
                    DealDamage(attacker, target, proc, a.Def.ProcSchool, Aura.Family(a.Def.Id), a.Def.Id, false, false);
                    if (target.IsDead) break;
                }
        }

        private void UpdateRegen(Unit u, float dt)
        {
            if (u.PowerType == PowerType.Energy && u.IsAlive)
            {
                // Energie fließt kontinuierlich: 10 pro Sekunde.
                u.RegenAccumulator += 10f * dt * (1 + u.Mod(ModType.PowerRegenPercent) / 100f);
                if (u.RegenAccumulator >= 1) { int add = (int)u.RegenAccumulator; u.RegenAccumulator -= add; u.Power = Math.Min(u.MaxPower, u.Power + add); }
            }
            u.RegenTimer -= dt;
            if (u.RegenTimer > 0) return;
            u.RegenTimer += 2f;
            if (!u.IsAlive) return;
            if (u is Creature c)
            {
                if (!c.InCombat && !c.Evading && c.Health < c.MaxHealth) c.Health = Math.Min(c.MaxHealth, c.Health + Math.Max(1, c.MaxHealth / 8));
                if (c.PowerType == PowerType.Mana && _w.Time - c.LastManaSpendTime > 5) c.Power = Math.Min(c.MaxPower, c.Power + Math.Max(1, c.MaxPower / 20));
                return;
            }
            if (!u.InCombat && u.Health < u.MaxHealth)
                u.Health = Math.Min(u.MaxHealth, u.Health + (int)Math.Ceiling(StatFormulas.HealthRegenPerTick(u.Stats.Spirit, u.Level) * (u is Player rp && rp.CurrentArea?.IsInn == true ? 2 : 1)));
            switch (u.PowerType)
            {
                case PowerType.Mana:
                    if (_w.Time - u.LastManaSpendTime >= 5)
                        u.Power = Math.Min(u.MaxPower, u.Power + (int)Math.Ceiling(StatFormulas.ManaRegenPerTick(u.Stats.Spirit, u.Stats.Intellect) * (1 + u.Mod(ModType.PowerRegenPercent) / 100f)));
                    break;
                case PowerType.Rage:
                    if (!u.InCombat && u.Power > 0) u.Power = Math.Max(0, u.Power - 3);
                    break;
            }
        }

        private void UpdateFear(Unit u, float dt)
        {
            u.FearTimer -= dt;
            if (u.FearTimer <= 0)
            {
                u.FearTimer = 1.5f;
                double ang = Math.Atan2(u.FearDirection.X, u.FearDirection.Z) + (Rng.NextDouble() - 0.5) * 1.2;
                u.FearDirection = new Vec3((float)Math.Sin(ang), 0, (float)Math.Cos(ang));
            }
            if (u.IsRooted) return;
            var next = u.Position + u.FearDirection * (u.MoveSpeed * 0.6f * dt);
            u.Zone!.SetPosition(u, next);
            u.Facing = (float)Math.Atan2(u.FearDirection.X, u.FearDirection.Z);
        }

        // ================================================================== Tod

        public void Kill(Unit? killer, Unit victim)
        {
            if (victim.IsDead) return;
            victim.Health = 0;
            victim.DeathState = DeathState.Dead;
            if (victim.Cast != null) CancelCast(victim, "Tot");
            victim.AutoAttacking = false;
            victim.Target = null;
            victim.RemoveAuras(a => !a.Def.PersistsThroughDeath);
            victim.Zone?.BroadcastFrom(victim, new S_UnitDied { Id = victim.Id, KillerId = killer?.Id ?? 0 });
            var killerPlayer = killer as Player ?? (killer as Creature)?.Owner;

            if (victim is Creature c)
            {
                var attackers = c.CombatWith.ToList();
                c.Threat.Clear();
                c.ClearCombat();
                if (c.IsPet)
                {
                    if (c.Owner != null && c.Owner.Pet == c) c.Owner.Pet = null;
                    c.CorpseTimer = 3;
                    return;
                }
                if (c.Escort != null)
                {
                    _w.Quests.EscortFailed(c);
                    c.CorpseTimer = 5;
                    return;
                }
                var tapper = c.TappedBy ?? killerPlayer;
                if (tapper != null && !tapper.Removed)
                {
                    _w.Progression.AwardKillXp(tapper, c);
                    _w.Quests.OnCreatureKilled(tapper, c);
                    _w.Loot.Generate(c, tapper);
                    foreach (var p in attackers.OfType<Player>().Where(p => p.Bot != null)) p.Bot!.OnKill(c);
                    if (tapper.Group != null) foreach (var m in tapper.Group.Bots.Where(b => !attackers.Contains(b))) m.Bot!.OnKill(c);
                }
                c.CorpseTimer = c.IsSummoned ? 15 : c.Lootable ? 180 : 45;
                return;
            }
            if (victim is Player vp) _w.Death.OnPlayerDied(vp, killer);
        }
    }
}
