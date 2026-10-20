using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.World
{
    public struct WeaponInfo
    {
        public int Min, Max;
        public float Speed;
        public School School;
        public bool Present;
        public float Average => (Min + Max) / 2f;
    }

    public sealed class CastState
    {
        public AbilityDef Ability = null!;
        public Unit? Target;
        public float Remaining;
        public float Total;
        public int Pushbacks;
        public Vec3 StartPosition;
        /// <summary>Statt einer Fähigkeit: Interaktion mit einem Objekt (Kiste öffnen, Kraut pflücken).</summary>
        public GameObjectEntity? Object;
        public string? ItemId;
        /// <summary>Statt einer Fähigkeit: Handwerk, Kürschnern, Entzaubern (wird bei Abschluss ausgeführt).</summary>
        public Action? OnComplete;
        public string Label = "";
    }

    /// <summary>
    /// Eine kämpfende Einheit: Spieler, KI-Mitspieler, Monster, NPC oder Begleiter.
    /// Enthält Zustand und abgeleitete Werte; Regeln stehen in den Systemen (CombatSystem usw.).
    /// </summary>
    public abstract class Unit : Entity
    {
        public int Level { get; set; } = 1;
        public Faction Faction { get; set; }
        public int Health { get; set; }
        public int MaxHealth { get; set; } = 1;
        public virtual PowerType PowerType { get; protected set; }
        public int Power { get; set; }
        public int MaxPower { get; set; }

        // ---------------------------------------------------------------- abgeleitete Werte
        public StatBlock Stats { get; } = new();
        public int Armor { get; set; }
        public readonly float[] Resist = new float[7];
        public int AttackPower { get; set; }
        public int RangedAttackPower { get; set; }
        public int SpellPower { get; set; }
        public int HealingPower { get; set; }
        /// <summary>Chancen in Prozent.</summary>
        public float MeleeCrit { get; set; } = 5;
        public float SpellCrit { get; set; } = 1.5f;
        public float Dodge { get; set; } = 5;
        public float Parry { get; set; }
        public float Block { get; set; }
        public float HitBonus { get; set; }
        public float BaseMoveSpeed { get; set; } = 7f;
        public float MoveSpeed { get; set; } = 7f;
        public float AttackSpeedMultiplier { get; set; } = 1f;
        public WeaponInfo MainHand;
        public WeaponInfo RangedWeapon;
        public bool HasShield { get; set; }
        public float CombatReach { get; set; } = 1.5f;

        /// <summary>Alle aktiven Modifikatoren (Auren, Talente, Gegenstände). Wird bei Änderungen neu aufgebaut.</summary>
        public List<ModifierDef> Modifiers { get; } = new();
        private bool _statsDirty = true;

        // ---------------------------------------------------------------- Zustand
        public List<Aura> Auras { get; } = new();
        public CastState? Cast { get; set; }
        public float Gcd { get; set; }
        public Dictionary<string, float> Cooldowns { get; } = new();
        public Dictionary<School, float> SchoolLockouts { get; } = new();
        public Unit? Target { get; set; }
        public bool AutoAttacking { get; set; }
        public float SwingTimer { get; set; }
        public float RangedSwingTimer { get; set; }
        public bool InCombat { get; set; }
        public float CombatTimer { get; set; }
        /// <summary>Kreaturen, die mit dieser Einheit im Kampf sind.</summary>
        public HashSet<Unit> CombatWith { get; } = new();
        public DeathState DeathState { get; set; }
        public Form Form { get; set; }
        public int ComboPoints { get; set; }
        public int ComboTargetId { get; set; }
        public float RegenTimer { get; set; } = 2f;
        public float RegenAccumulator { get; set; }
        public double LastManaSpendTime { get; set; } = -10;
        public float FearTimer { get; set; }
        public Vec3 FearDirection { get; set; }
        public bool AurasChanged { get; set; }

        public bool IsAlive => DeathState == DeathState.Alive;
        public bool IsDead => DeathState != DeathState.Alive;
        public float HealthPct => MaxHealth <= 0 ? 0 : Health / (float)MaxHealth;
        public float PowerPct => MaxPower <= 0 ? 1 : Power / (float)MaxPower;

        public bool IsStunned => Auras.Any(a => a.Def.Stun);
        public bool IsRooted => Auras.Any(a => a.Def.Root);
        public bool IsSilenced => Auras.Any(a => a.Def.Silence);
        public bool IsFeared => Auras.Any(a => a.Def.Fear);
        public bool IsStealthed => Auras.Any(a => a.Def.Stealth);
        public bool CanAct => IsAlive && !IsStunned && !IsFeared;
        public bool CanMove => IsAlive && !IsStunned && !IsRooted;

        public virtual bool IsPlayerControlled => false;
        public virtual Faction EffectiveFaction => Faction;
        public virtual bool IsEvading => false;
        public virtual bool IsPvPFlagged => false;

        // ---------------------------------------------------------------- Freund/Feind

        public bool IsHostileTo(Unit other)
        {
            if (other == this) return false;
            var a = EffectiveFaction;
            var b = other.EffectiveFaction;
            if (a == Faction.Friendly || b == Faction.Friendly) return false;
            if (a == b) return false;
            bool aWild = a == Faction.Hostile || a == Faction.Neutral;
            bool bWild = b == Faction.Hostile || b == Faction.Neutral;
            if (aWild && bWild) return false;
            if (aWild || bWild) return true;
            // Sonnenbündnis gegen Schattenpakt: NPCs immer, Spieler nur mit PvP-Markierung.
            if (IsPlayerControlled && other.IsPlayerControlled) return IsPvPFlagged && other.IsPvPFlagged;
            return true;
        }

        public bool IsFriendlyTo(Unit other) => other == this || (!IsHostileTo(other) && EffectiveFaction != Faction.Hostile && other.EffectiveFaction != Faction.Hostile
                                                                     && EffectiveFaction != Faction.Neutral && other.EffectiveFaction != Faction.Neutral);

        public bool CanAttack(Unit other) => other.IsAlive && !other.Removed && !other.IsEvading && IsHostileTo(other);

        public float MeleeReachTo(Unit other) => CombatMath.MeleeRange + CombatReach + other.CombatReach - 3f;
        public bool InMeleeRange(Unit other) => DistanceTo(other) <= MeleeReachTo(other) + 0.5f;

        // ---------------------------------------------------------------- Werte

        public void MarkStatsDirty() => _statsDirty = true;

        public void EnsureStats()
        {
            if (!_statsDirty) return;
            _statsDirty = false;
            Modifiers.Clear();
            CollectModifiers(Modifiers);
            RecalculateStats();
        }

        protected virtual void CollectModifiers(List<ModifierDef> into)
        {
            foreach (var a in Auras)
                foreach (var m in a.Def.Modifiers)
                    into.Add(a.Stacks > 1 ? m.Scaled(a.Stacks) : m);
        }

        protected abstract void RecalculateStats();

        public float Mod(ModType type, School? school = null, string? ability = null, Stat? stat = null, CritCategory crit = CritCategory.All)
        {
            EnsureStats();
            return ModifierQuery.Sum(Modifiers, type, school, ability, stat, crit);
        }

        protected void ClampPools()
        {
            if (Health > MaxHealth) Health = MaxHealth;
            if (Power > MaxPower) Power = MaxPower;
        }

        // ---------------------------------------------------------------- Auren

        public Aura? FindAura(string auraId) => Auras.FirstOrDefault(a => a.Def.Id == auraId);
        public bool HasAuraFamily(string family) => Auras.Any(a => a.FamilyId == family || a.Def.Id.StartsWith(family, StringComparison.Ordinal));

        public void RemoveAura(Aura aura)
        {
            if (!Auras.Remove(aura)) return;
            OnAuraRemoved(aura);
            AurasChanged = true;
            MarkStatsDirty();
        }

        public void RemoveAuras(Func<Aura, bool> predicate)
        {
            foreach (var a in Auras.Where(predicate).ToList()) RemoveAura(a);
        }

        protected virtual void OnAuraRemoved(Aura aura)
        {
            if (aura.Def.Form != Form.None && Form == aura.Def.Form) SetForm(Form.None);
        }

        public virtual void SetForm(Form form) => Form = form;

        // ---------------------------------------------------------------- Kampfstatus

        public void EnterCombatWith(Unit other)
        {
            if (other == this) return;
            InCombat = true;
            CombatTimer = CombatMath.LeaveCombatSeconds;
            if (other is Creature || this is Creature)
            {
                CombatWith.Add(other);
                other.CombatWith.Add(this);
            }
            other.InCombat = true;
            other.CombatTimer = CombatMath.LeaveCombatSeconds;
        }

        public void ClearCombat()
        {
            foreach (var u in CombatWith) u.CombatWith.Remove(this);
            CombatWith.Clear();
            InCombat = false;
            AutoAttacking = false;
        }

        public void SetCooldown(string abilityBaseId, float seconds)
        {
            if (seconds <= 0) return;
            Cooldowns[abilityBaseId] = seconds;
        }

        public float CooldownLeft(string abilityBaseId) => Cooldowns.TryGetValue(abilityBaseId, out var v) ? v : 0;

        /// <summary>Wird jeden Tick aufgerufen: Abklingzeiten, Kampftimer, Regeneration.</summary>
        public virtual void UpdateTimers(float dt, double now)
        {
            if (Gcd > 0) Gcd = Math.Max(0, Gcd - dt);
            if (Cooldowns.Count > 0)
                foreach (var k in Cooldowns.Keys.ToList())
                {
                    var v = Cooldowns[k] - dt;
                    if (v <= 0) Cooldowns.Remove(k); else Cooldowns[k] = v;
                }
            if (SchoolLockouts.Count > 0)
                foreach (var k in SchoolLockouts.Keys.ToList())
                {
                    var v = SchoolLockouts[k] - dt;
                    if (v <= 0) SchoolLockouts.Remove(k); else SchoolLockouts[k] = v;
                }
            if (InCombat)
            {
                // Weit entfernte Gegner (Teleport, Flucht) halten niemanden im Kampf.
                CombatWith.RemoveWhere(u => u.Removed || u.IsDead || u.Zone != Zone || Vec3.DistanceXZ(u.Position, Position) > CombatMath.CombatDropDistance);
                if (CombatWith.Count == 0)
                {
                    CombatTimer -= dt;
                    if (CombatTimer <= 0) { InCombat = false; OnLeaveCombat(); }
                }
                else CombatTimer = CombatMath.LeaveCombatSeconds;
            }
        }

        protected virtual void OnLeaveCombat() { }

        public virtual uint Flags
        {
            get
            {
                var f = UnitFlags.None;
                if (InCombat) f |= UnitFlags.InCombat;
                if (DeathState == DeathState.Dead) f |= UnitFlags.Dead;
                if (DeathState == DeathState.Ghost) f |= UnitFlags.Ghost;
                if (IsStealthed) f |= UnitFlags.Stealthed;
                if (IsStunned) f |= UnitFlags.Stunned;
                if (IsRooted) f |= UnitFlags.Rooted;
                if (IsSilenced) f |= UnitFlags.Silenced;
                if (IsFeared) f |= UnitFlags.Feared;
                if (Cast != null) f |= UnitFlags.Casting;
                if (IsEvading) f |= UnitFlags.Evading;
                if (IsPvPFlagged) f |= UnitFlags.PvP;
                if (Auras.Any(a => a.Def.Mounted)) f |= UnitFlags.Mounted;
                return (uint)f;
            }
        }

        public override EntitySnapshotData BuildSnapshot() => new()
        {
            Id = Id, Position = Position, Facing = Facing, Health = Health, MaxHealth = MaxHealth, Power = Power, MaxPower = MaxPower,
            Flags = Flags, TargetId = Target?.Id ?? 0,
        };
    }
}
