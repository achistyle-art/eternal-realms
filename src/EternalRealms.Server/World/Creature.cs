using EternalRealms.Server.AI;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.World
{
    /// <summary>Bedrohungsliste einer Kreatur: bestimmt, wen sie angreift.</summary>
    public sealed class ThreatTable
    {
        private readonly Dictionary<Unit, float> _threat = new();
        public Unit? Forced { get; private set; }
        public float ForcedTimer { get; private set; }

        public IReadOnlyDictionary<Unit, float> Entries => _threat;
        public int Count => _threat.Count;

        public void Add(Unit u, float amount)
        {
            if (amount < 0 && !_threat.ContainsKey(u)) return;
            _threat.TryGetValue(u, out var cur);
            _threat[u] = Math.Max(0, cur + amount);
        }

        public float Get(Unit u) => _threat.TryGetValue(u, out var v) ? v : 0;
        public void Set(Unit u, float v) => _threat[u] = Math.Max(0, v);
        public void Remove(Unit u) { _threat.Remove(u); if (Forced == u) Forced = null; }
        public void Clear() { _threat.Clear(); Forced = null; }
        public bool Contains(Unit u) => _threat.ContainsKey(u);

        public void Force(Unit u, float seconds) { Forced = u; ForcedTimer = seconds; }

        public void Update(float dt)
        {
            if (Forced != null && (ForcedTimer -= dt) <= 0) Forced = null;
            foreach (var u in _threat.Keys.Where(u => u.Removed || u.IsDead).ToList()) _threat.Remove(u);
        }

        public Unit? Top()
        {
            Unit? best = null;
            float bv = -1;
            foreach (var kv in _threat)
                if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>
        /// Klassische Zielwechselregel: Ein neues Ziel muss im Nahkampf 110 %, auf Distanz 130 %
        /// der Bedrohung des aktuellen Ziels haben. Verhindert ständiges Hin- und Herspringen.
        /// </summary>
        public Unit? SelectTarget(Creature owner, Unit? current)
        {
            if (Forced != null && Forced.IsAlive && !Forced.Removed) return Forced;
            var top = Top();
            if (top == null) return null;
            if (current == null || current.IsDead || current.Removed || !_threat.ContainsKey(current)) return top;
            if (top == current) return current;
            float curThreat = _threat[current];
            float factor = owner.InMeleeRange(top) ? 1.1f : 1.3f;
            return _threat[top] > curThreat * factor ? top : current;
        }

        public IEnumerable<Unit> Units => _threat.Keys;
    }

    public sealed class Creature : Unit
    {
        public CreatureDef Template { get; }
        public Vec3 Home { get; set; }
        public float HomeFacing { get; set; }
        public SpawnPoint? Spawn { get; set; }
        public CreatureAI AI { get; set; }
        public ThreatTable Threat { get; } = new();
        /// <summary>Wer zuerst Schaden gemacht hat, bekommt Beute und Erfahrung (Spieler oder dessen Gruppe).</summary>
        public Player? TappedBy { get; set; }
        public List<LootItem> Loot { get; } = new();
        public int LootMoney { get; set; }
        public bool Lootable { get; set; }
        /// <summary>Bereits gehäutet (Kürschnerei).</summary>
        public bool Skinned { get; set; }
        public float CorpseTimer { get; set; }
        /// <summary>Besitzer bei Begleitern und Eskorte-NPCs.</summary>
        public Player? Owner { get; set; }
        public bool IsPet { get; set; }
        public EscortRun? Escort { get; set; }
        public float DamageMultiplier { get; set; } = 1f;
        public bool Evading { get; set; }
        /// <summary>Beschworene Zusatzgegner verschwinden nach dem Kampf.</summary>
        public bool IsSummoned { get; set; }
        /// <summary>Gehört zu einem laufenden Weltereignis (Kapitel 37).</summary>
        public string? EventId { get; set; }
        /// <summary>KI-Erklärerin des Tutorials: folgt diesem Spieler und kämpft nie (Kapitel 39).</summary>
        public Player? GuideFor { get; set; }
        /// <summary>Spielzeit, zu der die Kreatur still verschwindet (0 = nie).</summary>
        public double DespawnAt { get; set; }
        /// <summary>Vorrangiges Ziel eines Bosskampfs (Kapitel 44): KI-Mitspieler wechseln darauf.</summary>
        public bool PriorityTarget { get; set; }

        public override EntityKind Kind => IsPet ? EntityKind.Pet : Template.IsNpc && Faction != Faction.Hostile && Faction != Faction.Neutral ? EntityKind.Npc : EntityKind.Creature;
        public override bool IsEvading => Evading;
        public override Faction EffectiveFaction => IsPet && Owner != null ? Owner.EffectiveFaction : Faction;
        public override bool IsPlayerControlled => IsPet;
        public override bool IsPvPFlagged => IsPet && Owner != null && Owner.IsPvPFlagged;
        public bool IsBoss => Template.Boss || Template.WorldBoss;

        public Creature(CreatureDef template, int level)
        {
            Template = template;
            Name = template.Name;
            Level = level;
            Faction = template.Faction;
            PowerType = template.PowerType;
            BaseMoveSpeed = template.MoveSpeed;
            CombatReach = 1.5f * Math.Max(0.6f, template.Scale);
            AI = new CreatureAI(this);
            MarkStatsDirty();
            EnsureStats();
            Health = MaxHealth;
            Power = MaxPower;
        }

        protected override void RecalculateStats()
        {
            var t = Template;
            int hpBase = StatFormulas.CreatureHealth(Level, t.HealthMultiplier, t.Elite);
            if (t.IsNpc && t.Roles.HasFlag(NpcRole.Guard)) hpBase = StatFormulas.CreatureHealth(Level, t.HealthMultiplier, true);
            MaxHealth = (int)(hpBase * (1 + Mod(ModType.MaxHealthPercent) / 100f) + Mod(ModType.MaxHealthFlat));
            if (IsPet && Owner != null) MaxHealth = (int)(MaxHealth * 0.8f + Owner.Stats.Stamina * 3);
            MaxPower = PowerType == PowerType.Mana ? StatFormulas.CreatureMana(Level, Math.Max(1, t.ManaFactor)) : PowerType == PowerType.None ? 0 : 100;
            float dps = StatFormulas.CreatureDps(Level, t.DamageMultiplier * DamageMultiplier, t.Elite);
            float avg = dps * t.AttackSpeed;
            MainHand = new WeaponInfo { Min = Math.Max(1, (int)(avg * 0.8f)), Max = Math.Max(2, (int)(avg * 1.2f)), Speed = t.AttackSpeed, School = t.MeleeSchool, Present = true };
            Armor = (int)((StatFormulas.CreatureArmor(Level, t.ArmorMultiplier) + Mod(ModType.ArmorFlat)) * (1 + Mod(ModType.ArmorPercent) / 100f));
            if (Armor < 0) Armor = 0;
            for (int i = 0; i < Resist.Length; i++) Resist[i] = Mod(ModType.ResistFlat, (School)i);
            AttackPower = (int)Mod(ModType.AttackPowerFlat);
            SpellPower = (int)(Level * 1.5f + Mod(ModType.SpellPowerFlat));
            HealingPower = SpellPower;
            MeleeCrit = 5 + Mod(ModType.CritChance, crit: CritCategory.Melee);
            SpellCrit = 5 + Mod(ModType.CritChance, crit: CritCategory.Spell);
            Dodge = 5 + Mod(ModType.DodgeChance);
            Parry = t.Family == "Humanoid" || t.Boss ? 5 + Mod(ModType.ParryChance) : 0;
            Block = 0;
            HitBonus = Mod(ModType.HitChance);
            MoveSpeed = Math.Max(0.5f, BaseMoveSpeed * (1 + Mod(ModType.MoveSpeedPercent) / 100f));
            AttackSpeedMultiplier = Math.Max(0.2f, 1 + Mod(ModType.AttackSpeedPercent) / 100f);
            ClampPools();
        }

        public override uint Flags
        {
            get
            {
                var f = (UnitFlags)base.Flags;
                if (Template.Elite) f |= UnitFlags.Elite;
                if (Template.Rare) f |= UnitFlags.Rare;
                if (IsBoss) f |= UnitFlags.Boss;
                if (Lootable) f |= UnitFlags.Lootable;
                if (TappedBy != null) f |= UnitFlags.Tapped;
                return (uint)f;
            }
        }

        public override S_EntitySpawn BuildSpawn(Player? viewer) => new()
        {
            Id = Id, Kind = Kind, TemplateId = Template.Id, Name = Name, Title = Template.Title, Level = Level, Faction = EffectiveFaction,
            NpcRoles = (ushort)Template.Roles, PowerType = PowerType,
            Marker = viewer != null && Template.Roles.HasFlag(NpcRole.QuestGiver) ? viewer.World.Quests.MarkerFor(viewer, this) : QuestMarker.None,
            State = BuildSnapshot(),
        };
    }

    /// <summary>Ein Beutestapel auf einer Leiche.</summary>
    public sealed class LootItem
    {
        public string ItemId = "";
        public int Count = 1;
    }

    /// <summary>Wiederkehrender Spawnpunkt einer Kreatur.</summary>
    public sealed class SpawnPoint
    {
        public SpawnDef Def { get; }
        public Vec3 Position { get; }
        public float Facing { get; }
        public Creature? Current { get; set; }
        public float RespawnTimer { get; set; }
        public int Index { get; }

        public SpawnPoint(SpawnDef def, Vec3 pos, float facing, int index, string zoneId = "") { Def = def; Position = pos; Facing = facing; Index = index; Key = $"{zoneId}:{index}:{def.Creature}"; }

        /// <summary>Schlüssel für gespeicherte Wiederkehrzeiten.</summary>
        public string Key { get; }
    }

    /// <summary>Laufende Eskorte (Eskortquest).</summary>
    public sealed class EscortRun
    {
        public QuestDef Quest = null!;
        public Player Owner = null!;
        public int Waypoint;
        public float DefendTimer;
        public bool Started;
        public bool Finished;
        public HashSet<int> TriggeredAmbushes = new();
    }
}
