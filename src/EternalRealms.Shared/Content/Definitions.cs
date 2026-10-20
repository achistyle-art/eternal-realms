using System.Collections.Generic;

namespace EternalRealms.Shared.Content
{
    // =====================================================================================
    // Alle Inhalte des Spiels sind Daten (content/*.json). Diese Klassen sind ihr Schema.
    // Server und Client laden dieselben Dateien; der Server ist für alle Ergebnisse maßgeblich.
    // =====================================================================================

    public sealed class StatBlock
    {
        public float Strength { get; set; }
        public float Agility { get; set; }
        public float Stamina { get; set; }
        public float Intellect { get; set; }
        public float Spirit { get; set; }

        public float Get(Stat s) => s switch
        {
            Stat.Strength => Strength,
            Stat.Agility => Agility,
            Stat.Stamina => Stamina,
            Stat.Intellect => Intellect,
            _ => Spirit,
        };

        public void Set(Stat s, float v)
        {
            switch (s)
            {
                case Stat.Strength: Strength = v; break;
                case Stat.Agility: Agility = v; break;
                case Stat.Stamina: Stamina = v; break;
                case Stat.Intellect: Intellect = v; break;
                default: Spirit = v; break;
            }
        }
    }

    public sealed class FactionDef
    {
        public Faction Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Capital { get; set; } = "";
    }

    public sealed class RaceDef
    {
        public Race Id { get; set; }
        public string Name { get; set; } = "";
        public Faction Faction { get; set; }
        public string Lore { get; set; } = "";
        public string StartZone { get; set; } = "";
        public Vec3 StartPosition { get; set; }
        public float StartFacing { get; set; }
        public StatBlock StatBonus { get; set; } = new StatBlock();
        public List<CharClass> Classes { get; set; } = new List<CharClass>();
        public List<RacialTraitDef> Traits { get; set; } = new List<RacialTraitDef>();
        /// <summary>Erlaubter Bereich für die Körpergröße im Editor (Faktor auf das Basismodell).</summary>
        public float MinHeight { get; set; } = 0.95f;
        public float MaxHeight { get; set; } = 1.05f;
        public AppearanceOptions Appearance { get; set; } = new AppearanceOptions();
        /// <summary>Silben für den Namensgenerator (KI-Mitspieler, Namensvorschläge).</summary>
        public List<string> NameStarts { get; set; } = new List<string>();
        public List<string> NameEnds { get; set; } = new List<string>();
    }

    public sealed class AppearanceOptions
    {
        public int SkinColors { get; set; } = 8;
        public int Faces { get; set; } = 6;
        public int HairStyles { get; set; } = 10;
        public int HairColors { get; set; } = 10;
        public int Beards { get; set; } = 6;
        public int EyeColors { get; set; } = 6;
        public int Scars { get; set; } = 4;
        public int Tattoos { get; set; } = 5;
        /// <summary>Volksspezifische Animationsgruppe (Gang, Leerlauf, Emotes).</summary>
        public string AnimationSet { get; set; } = "";
    }

    public sealed class RacialTraitDef
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<ModifierDef> Modifiers { get; set; } = new List<ModifierDef>();
        /// <summary>Optionale aktive Volksfähigkeit.</summary>
        public string? Ability { get; set; }
    }

    public sealed class ClassDef
    {
        public CharClass Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public PowerType PowerType { get; set; }
        public ArmorType MaxArmor { get; set; }
        public List<WeaponType> Weapons { get; set; } = new List<WeaponType>();
        public List<Role> Roles { get; set; } = new List<Role>();
        public StatBlock BaseStats { get; set; } = new StatBlock();
        public StatBlock StatsPerLevel { get; set; } = new StatBlock();
        public float HealthFactor { get; set; } = 1f;
        public float ManaFactor { get; set; } = 0f;
        /// <summary>Angriffskraft pro Punkt Stärke / Beweglichkeit im Nahkampf.</summary>
        public float ApPerStrength { get; set; } = 2f;
        public float ApPerAgility { get; set; } = 0f;
        public float RangedApPerAgility { get; set; } = 1f;
        /// <summary>Fähigkeiten, die ab Stufe 1 bekannt sind.</summary>
        public List<string> StartingAbilities { get; set; } = new List<string>();
        public List<string> StartingItems { get; set; } = new List<string>();
        public List<string> TalentTrees { get; set; } = new List<string>();
        /// <summary>Kurzvorschau im Charaktereditor.</summary>
        public List<string> PreviewAbilities { get; set; } = new List<string>();
    }

    public enum EffectType : byte
    {
        SchoolDamage = 0,
        WeaponDamage,
        Heal,
        ApplyAura,
        Interrupt,
        Taunt,
        Resurrect,
        GainPower,
        Charge,
        Dispel,
        SummonPet,
        Teleport,
        /// <summary>Stellt X % Leben/Mana über die Dauer der Aura wieder her (Essen/Trinken).</summary>
        RestorePercent,
        /// <summary>Generiert Kombopunkte am Ziel.</summary>
        ComboPoints,
        /// <summary>Gibt den aktuellen Bedrohungswert am Ziel zum Teil ab.</summary>
        ReduceThreat,
    }

    public sealed class EffectDef
    {
        public EffectType Type { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public School School { get; set; }
        /// <summary>Anteil der Zauber-/Heilkraft, der aufaddiert wird.</summary>
        public float Coefficient { get; set; }
        /// <summary>Für Waffenschaden: Anteil des Waffenschadens (1 = 100 %).</summary>
        public float WeaponPercent { get; set; } = 1f;
        /// <summary>Zusatzschaden pro Kombopunkt (Finishing-Moves).</summary>
        public int PerComboPoint { get; set; }
        public string? Aura { get; set; }
        /// <summary>Zusätzliche feste Bedrohung.</summary>
        public int Threat { get; set; }
        public float ThreatMultiplier { get; set; } = 1f;
        /// <summary>Für Unterbrechungen: Sperrzeit der Schule in Sekunden.</summary>
        public float Duration { get; set; }
        public string? Pet { get; set; }
        public string? Zone { get; set; }
        public Vec3 Position { get; set; }
        /// <summary>Wirkt auf das Ziel statt auf den Zaubernden (für Effekte, die beides können).</summary>
        public bool OnSelf { get; set; }
    }

    public sealed class AbilityRankDef
    {
        public int Level { get; set; }
        public int Cost { get; set; }
        public int TrainingCost { get; set; }
        /// <summary>[min, max] je Effekt in der Reihenfolge der Effektliste; null = unverändert.</summary>
        public List<int[]?> Values { get; set; } = new List<int[]?>();
        /// <summary>Optional andere Aura je Rang (z. B. stärkerer DoT).</summary>
        public string? Aura { get; set; }
    }

    public sealed class AbilityDef
    {
        /// <summary>Basis-ID ohne Rang, z. B. "mage_fireball".</summary>
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public CharClass Class { get; set; }
        public int Rank { get; set; } = 1;
        public int Level { get; set; } = 1;
        public PowerType PowerType { get; set; }
        public int Cost { get; set; }
        public float CastTime { get; set; }
        public float Cooldown { get; set; }
        public bool TriggersGcd { get; set; } = true;
        public float MinRange { get; set; }
        public float MaxRange { get; set; } = 5f;
        public float Radius { get; set; }
        public int MaxTargets { get; set; } = 10;
        public TargetType Target { get; set; } = TargetType.Enemy;
        public School School { get; set; }
        public bool Channeled { get; set; }
        /// <summary>Darf nur aus der Tarnung genutzt werden.</summary>
        public bool RequiresStealth { get; set; }
        /// <summary>Nur außerhalb des Kampfes nutzbar.</summary>
        public bool OutOfCombatOnly { get; set; }
        public bool RequiresComboPoints { get; set; }
        /// <summary>Ziel muss unter diesem Lebensanteil sein (z. B. Hinrichten).</summary>
        public float TargetHealthBelow { get; set; }
        public List<Form> RequiredForms { get; set; } = new List<Form>();
        public bool RequiresRangedWeapon { get; set; }
        public bool RequiresShield { get; set; }
        public int TrainingCost { get; set; }
        /// <summary>Wird nicht beim Trainer gelernt (Talentfähigkeit, Volksfähigkeit, Kreaturen).</summary>
        public bool NotTrainable { get; set; }
        public string Icon { get; set; } = "";
        public string Animation { get; set; } = "cast";
        public List<EffectDef> Effects { get; set; } = new List<EffectDef>();
        public List<AbilityRankDef> Ranks { get; set; } = new List<AbilityRankDef>();

        /// <summary>Eindeutige ID inklusive Rang, z. B. "mage_fireball#3".</summary>
        public string RankedId => Rank <= 1 && Ranks.Count == 0 ? Id : Id + "#" + Rank;

        public bool IsHarmful => Target == TargetType.Enemy || Target == TargetType.AreaAroundTarget ||
                                  (Target == TargetType.AreaAroundSelf && Effects.Exists(e => e.Type == EffectType.SchoolDamage || e.Type == EffectType.WeaponDamage || e.Type == EffectType.Taunt));
        public bool IsSpell => School != School.Physical;
    }

    public enum PeriodicType : byte { None = 0, Damage, Heal, Power }

    public sealed class AuraDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsDebuff { get; set; }
        public float Duration { get; set; }
        public int MaxStacks { get; set; } = 1;
        public School School { get; set; }
        /// <summary>Magische Effekte können gebannt werden.</summary>
        public bool Dispellable { get; set; } = true;
        public List<ModifierDef> Modifiers { get; set; } = new List<ModifierDef>();
        public PeriodicType Periodic { get; set; }
        public float TickInterval { get; set; } = 3f;
        public int TickMin { get; set; }
        public int TickMax { get; set; }
        public float TickCoefficient { get; set; }
        public bool Stun { get; set; }
        public bool Root { get; set; }
        public bool Silence { get; set; }
        public bool Fear { get; set; }
        public bool Stealth { get; set; }
        public bool Mounted { get; set; }
        public Form Form { get; set; }
        /// <summary>Schaden, der absorbiert wird (Schilde).</summary>
        public int Absorb { get; set; }
        public float AbsorbCoefficient { get; set; }
        /// <summary>Endet bei Schaden (z. B. Kopfnuss, Furcht nach genug Schaden).</summary>
        public bool BreaksOnDamage { get; set; }
        /// <summary>Endet bei Bewegung (Essen/Trinken).</summary>
        public bool BreaksOnMove { get; set; }
        /// <summary>Bleibt nach dem Tod bestehen (z. B. Wiederbelebungsschwäche).</summary>
        public bool PersistsThroughDeath { get; set; }
        /// <summary>Dauerhaft bis zum Abbruch (Haltungen, Formen, Begleiter-Buffs).</summary>
        public bool Permanent { get; set; }
        /// <summary>Zusatzschaden bei jedem Nahkampftreffer des Trägers (Siegel, Waffenverzauberungen).</summary>
        public int ProcMin { get; set; }
        public int ProcMax { get; set; }
        public School ProcSchool { get; set; }
        public float ProcCoefficient { get; set; }
        /// <summary>Gruppen-Buff: gilt für alle Gruppenmitglieder in 30 m (Auren des Paladins).</summary>
        public bool PartyAura { get; set; }
        public string Icon { get; set; } = "";
    }

    public sealed class TalentTreeDef
    {
        public string Id { get; set; } = "";
        public CharClass Class { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public Role? PrimaryRole { get; set; }
        public List<TalentDef> Talents { get; set; } = new List<TalentDef>();
    }

    public sealed class TalentDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>Reihe 0..6. Reihe n verlangt 5·n investierte Punkte im selben Baum.</summary>
        public int Tier { get; set; }
        public int Column { get; set; }
        public int MaxRank { get; set; } = 5;
        public string? Requires { get; set; }
        /// <summary>Werte gelten pro Rang.</summary>
        public List<ModifierDef> Modifiers { get; set; } = new List<ModifierDef>();
        /// <summary>Talent schaltet eine Fähigkeit frei.</summary>
        public string? GrantsAbility { get; set; }
    }

    public sealed class ItemDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public ItemQuality Quality { get; set; } = ItemQuality.Common;
        public ItemCategory Category { get; set; }
        public InventoryType InventoryType { get; set; }
        public ArmorType ArmorType { get; set; }
        public WeaponType WeaponType { get; set; }
        public int ItemLevel { get; set; } = 1;
        public int RequiredLevel { get; set; }
        public List<CharClass> Classes { get; set; } = new List<CharClass>();
        public int MinDamage { get; set; }
        public int MaxDamage { get; set; }
        public float Speed { get; set; }
        /// <summary>Schadensart der Waffe (Zauberstäbe verursachen z. B. Arkanschaden).</summary>
        public School DamageSchool { get; set; }
        public int Armor { get; set; }
        public List<ModifierDef> Modifiers { get; set; } = new List<ModifierDef>();
        public int MaxStack { get; set; } = 1;
        public int MaxDurability { get; set; }
        public int BuyPrice { get; set; }
        public int SellPrice { get; set; }
        /// <summary>Beim Benutzen ausgelöste Fähigkeit (Essen, Trank, Ruhestein).</summary>
        public string? UseAbility { get; set; }
        public string? StartsQuest { get; set; }
        public bool Unique { get; set; }
        public bool BindOnPickup { get; set; }
        public int BagSlots { get; set; }
        /// <summary>Benutzen fügt dieses Reittier der Sammlung hinzu.</summary>
        public string? TeachesMount { get; set; }
        public string Icon { get; set; } = "";
        public string Model { get; set; } = "";

        public bool IsEquippable => InventoryType != InventoryType.None;
        public bool IsWeapon => Category == ItemCategory.Weapon;
        public float Dps => Speed > 0 ? (MinDamage + MaxDamage) / 2f / Speed : 0;
    }

    public sealed class LootEntryDef
    {
        public string Item { get; set; } = "";
        /// <summary>Wahrscheinlichkeit 0..1.</summary>
        public float Chance { get; set; } = 1f;
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 1;
        /// <summary>Fällt nur, solange diese Quest aktiv und das Ziel unerfüllt ist.</summary>
        public string? Quest { get; set; }
    }

    public enum AbilityTargetRule : byte { Current = 0, Self, Random, Farthest, LowestHealthAlly, Tank }

    public sealed class CreatureAbilityDef
    {
        public string Ability { get; set; } = "";
        public float Interval { get; set; } = 8f;
        public float InitialDelay { get; set; } = 3f;
        public float Chance { get; set; } = 1f;
        public AbilityTargetRule TargetRule { get; set; }
        public float HealthBelow { get; set; } = 1.01f;
    }

    public sealed class BossPhaseDef
    {
        /// <summary>Phase beginnt, sobald das Leben unter diesen Anteil fällt.</summary>
        public float HealthBelow { get; set; } = 1.01f;
        public string Name { get; set; } = "";
        public string? Yell { get; set; }
        public List<CreatureAbilityDef> Abilities { get; set; } = new List<CreatureAbilityDef>();
        public List<SpawnAddDef> SummonAdds { get; set; } = new List<SpawnAddDef>();
        public float DamageMultiplier { get; set; } = 1f;
    }

    public sealed class SpawnAddDef
    {
        public string Creature { get; set; } = "";
        public int Count { get; set; } = 1;
    }

    /// <summary>Vorlage für Monster, NPCs und Begleiter.</summary>
    public sealed class CreatureDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Family { get; set; } = "";
        public int MinLevel { get; set; } = 1;
        public int MaxLevel { get; set; } = 1;
        public Faction Faction { get; set; } = Faction.Hostile;
        public NpcRole Roles { get; set; }
        public bool Elite { get; set; }
        public bool Rare { get; set; }
        public bool Boss { get; set; }
        public bool WorldBoss { get; set; }
        public float HealthMultiplier { get; set; } = 1f;
        public float DamageMultiplier { get; set; } = 1f;
        public float ArmorMultiplier { get; set; } = 1f;
        public float AttackSpeed { get; set; } = 2f;
        public School MeleeSchool { get; set; }
        public float AggroRadius { get; set; } = 12f;
        public float MoveSpeed { get; set; } = 6.5f;
        public bool CallsForHelp { get; set; }
        public float FleeAtHealth { get; set; }
        public bool Ranged { get; set; }
        public float PreferredRange { get; set; } = 0f;
        public PowerType PowerType { get; set; }
        public int ManaFactor { get; set; }
        public List<CreatureAbilityDef> Abilities { get; set; } = new List<CreatureAbilityDef>();
        public List<BossPhaseDef> Phases { get; set; } = new List<BossPhaseDef>();
        public List<LootEntryDef> Loot { get; set; } = new List<LootEntryDef>();
        public int MoneyMin { get; set; }
        public int MoneyMax { get; set; }
        public string Gossip { get; set; } = "";
        public List<string> VendorItems { get; set; } = new List<string>();
        public CharClass TrainerClass { get; set; }
        /// <summary>Berufslehrer: welcher Beruf (Kapitel 17).</summary>
        public string? TrainerProfession { get; set; }
        /// <summary>Sätze, die NPCs gelegentlich sagen (Ambiente).</summary>
        public List<string> Barks { get; set; } = new List<string>();
        /// <summary>Tagesablauf: Stunde (0-23) -> Ort.</summary>
        public List<ScheduleEntryDef> Schedule { get; set; } = new List<ScheduleEntryDef>();
        public string Model { get; set; } = "";
        public float Scale { get; set; } = 1f;
        public int Reputation { get; set; }
        public string? ReputationFaction { get; set; }

        public bool IsNpc => Roles != NpcRole.None || Faction == Faction.Sonnenbuendnis || Faction == Faction.Schattenpakt || Faction == Faction.Friendly;
    }

    public sealed class ScheduleEntryDef
    {
        public int Hour { get; set; }
        public Vec3 Position { get; set; }
        public string Activity { get; set; } = "";
    }

    public enum ProfessionKind : byte { Gathering = 0, Crafting = 1, Secondary = 2 }

    public sealed class ProfessionRankDef
    {
        public string Name { get; set; } = "";
        public int MaxSkill { get; set; }
        public int RequiredLevel { get; set; }
        public int Cost { get; set; }
    }

    /// <summary>Beruf (Kapitel 17): höchstens zwei Hauptberufe (Sammeln/Herstellen), beliebig viele Nebenberufe.</summary>
    public sealed class ProfessionDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ProfessionKind Kind { get; set; }
        public string Description { get; set; } = "";
        public List<ProfessionRankDef> Ranks { get; set; } = new List<ProfessionRankDef>();
        /// <summary>Werkzeug, das in der Tasche sein muss (Hacke, Messer, Angelrute).</summary>
        public string? Tool { get; set; }
        public bool IsPrimary => Kind != ProfessionKind.Secondary;
    }

    public sealed class RecipeEnchantDef
    {
        /// <summary>Ausrüstungsplatz (EquipSlot), der verzaubert wird.</summary>
        public EquipSlot Slot { get; set; }
        public string Name { get; set; } = "";
        public List<ModifierDef> Modifiers { get; set; } = new List<ModifierDef>();
    }

    public sealed class RecipeDef
    {
        public string Id { get; set; } = "";
        public string Profession { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Ab dieser Fertigkeit herstellbar (orange); gelb/grün/grau: weniger Fortschritt.</summary>
        public int SkillRequired { get; set; }
        public int Yellow { get; set; }
        public int Green { get; set; }
        public int Gray { get; set; }
        public List<RewardItemDef> Reagents { get; set; } = new List<RewardItemDef>();
        public RewardItemDef? Creates { get; set; }
        public RecipeEnchantDef? Enchant { get; set; }
        /// <summary>Erforderliche Werkbank in der Nähe: anvil, forge, fire.</summary>
        public string? Station { get; set; }
        public string? Tool { get; set; }
        public float CastTime { get; set; } = 3f;
        public int TrainerCost { get; set; }
    }

    /// <summary>Reittier (Kapitel 18): Tempo +60 % ab Stufe 40 oder +100 % ab Stufe 60, Seltenheit über die Qualität.</summary>
    public sealed class MountDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>None = beide Fraktionen.</summary>
        public Faction Faction { get; set; }
        public int Speed { get; set; } = 60;
        public int RequiredLevel { get; set; } = 40;
        public ItemQuality Quality { get; set; } = ItemQuality.Uncommon;
        /// <summary>Herkunft: vendor, drop, raid, reputation, achievement.</summary>
        public string Source { get; set; } = "vendor";
        public string Model { get; set; } = "";
        /// <summary>Gegenstand (Zügel), der das Reittier lehrt.</summary>
        public string Item { get; set; } = "";
        public int Price { get; set; }
        public bool BindOnPickup { get; set; }
    }

    public sealed class ReputationDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Faction Faction { get; set; }
        public string Description { get; set; } = "";
    }

    public sealed class AreaDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Vec3 Center { get; set; }
        public float Radius { get; set; } = 30f;
        public bool IsInn { get; set; }
        public bool IsCity { get; set; }
        public bool IsSecret { get; set; }
        public bool IsViewpoint { get; set; }
        public int DiscoveryXp { get; set; }
        public string Music { get; set; } = "";
    }

    public sealed class SpawnDef
    {
        public string Creature { get; set; } = "";
        public Vec3 Position { get; set; }
        public int Count { get; set; } = 1;
        public float Radius { get; set; }
        public float RespawnSeconds { get; set; } = 60f;
        public float Facing { get; set; }
        public List<Vec3> Patrol { get; set; } = new List<Vec3>();
        /// <summary>Wandert zufällig im Umkreis um den Spawnpunkt.</summary>
        public float WanderRadius { get; set; }
    }

    public sealed class ObjectSpawnDef
    {
        public string Object { get; set; } = "";
        public Vec3 Position { get; set; }
        public int Count { get; set; } = 1;
        public float Radius { get; set; }
        public float RespawnSeconds { get; set; } = 45f;
    }

    /// <summary>Interaktive Objekte: Kisten, Kräuter, Questgegenstände, Portale, Reisepunkte.</summary>
    public sealed class GameObjectDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Model { get; set; } = "";
        public List<LootEntryDef> Loot { get; set; } = new List<LootEntryDef>();
        /// <summary>Nur bedienbar mit aktiver Quest.</summary>
        public string? Quest { get; set; }
        /// <summary>Teleportiert in eine Zone (Dungeon-Eingang, Schiff).</summary>
        public string? TeleportZone { get; set; }
        public Vec3 TeleportPosition { get; set; }
        public float InteractTime { get; set; } = 1.5f;
        public string? Profession { get; set; }
        public int SkillRequired { get; set; }
        /// <summary>Briefkasten: öffnet das Postfach statt Beute.</summary>
        public bool IsMailbox { get; set; }
        /// <summary>Werkbank für Berufe: anvil, forge, fire.</summary>
        public string? Station { get; set; }
    }

    public sealed class FlightPathDef
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public int Cost { get; set; }
        public float DurationSeconds { get; set; } = 60f;
    }

    public sealed class TravelNodeDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Zone { get; set; } = "";
        public Vec3 Position { get; set; }
        public Faction Faction { get; set; }
        /// <summary>Flugmeister oder Hafen.</summary>
        public bool IsHarbor { get; set; }
    }

    public sealed class NpcSpawnDef
    {
        public string Npc { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Facing { get; set; }
        public List<Vec3> Patrol { get; set; } = new List<Vec3>();
    }

    public sealed class ZoneDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Continent { get; set; } = "";
        public int MinLevel { get; set; } = 1;
        public int MaxLevel { get; set; } = 1;
        public Faction Faction { get; set; }
        public string Description { get; set; } = "";
        public string Biome { get; set; } = "";
        public string Music { get; set; } = "";
        public string Ambience { get; set; } = "";
        /// <summary>Mögliches Wetter dieser Region.</summary>
        public List<Weather> Weather { get; set; } = new List<Weather>();
        public bool IsInstance { get; set; }
        public int MaxPlayers { get; set; }
        /// <summary>Schlachtzugsperre in Tagen (7 = wöchentlich, 3 = alle drei Tage, 0 = keine Sperre wie bei Dungeons).</summary>
        public int LockoutDays { get; set; }
        public bool IsRaid => IsInstance && MaxPlayers > 5;
        public Vec3 InstanceEntry { get; set; }
        /// <summary>Wohin man beim Verlassen/Tod im Dungeon gebracht wird.</summary>
        public string? ExitZone { get; set; }
        public Vec3 ExitPosition { get; set; }
        /// <summary>Weltgröße (Kantenlänge in Metern), für Interessensbereiche.</summary>
        public float Size { get; set; } = 1024f;
        /// <summary>Lage auf der Kontinentkarte: [x, y, Breite, Höhe] in Kartenpunkten (0–1000, y nach Süden).</summary>
        public List<float> MapRect { get; set; } = new List<float>();
        /// <summary>False = Region existiert in der Weltkarte, hat aber noch keine Inhalte.</summary>
        public bool Playable { get; set; }
        public List<AreaDef> Areas { get; set; } = new List<AreaDef>();
        public List<Vec3> Graveyards { get; set; } = new List<Vec3>();
        public List<SpawnDef> Spawns { get; set; } = new List<SpawnDef>();
        public List<NpcSpawnDef> Npcs { get; set; } = new List<NpcSpawnDef>();
        public List<ObjectSpawnDef> Objects { get; set; } = new List<ObjectSpawnDef>();
        /// <summary>Anzahl KI-Mitspieler, die hier unabhängig unterwegs sind.</summary>
        public int AmbientBots { get; set; }
    }

    public sealed class ObjectiveDef
    {
        public ObjectiveType Type { get; set; }
        /// <summary>Kreatur-, Gegenstands-, Gebiets-, Objekt- oder NPC-ID.</summary>
        public string Target { get; set; } = "";
        public int Count { get; set; } = 1;
        public string Description { get; set; } = "";
    }

    public sealed class RewardItemDef
    {
        public string Item { get; set; } = "";
        public int Count { get; set; } = 1;
    }

    public sealed class ReputationRewardDef
    {
        public string Faction { get; set; } = "";
        public int Amount { get; set; }
    }

    public sealed class EscortDef
    {
        public string Npc { get; set; } = "";
        public List<Vec3> Waypoints { get; set; } = new List<Vec3>();
        public List<EscortAmbushDef> Ambushes { get; set; } = new List<EscortAmbushDef>();
        /// <summary>Am Ziel so lange verteidigen (Beschützen-Quests).</summary>
        public float DefendSeconds { get; set; }
        public string? StartText { get; set; }
        public string? EndText { get; set; }
    }

    public sealed class EscortAmbushDef
    {
        public int AtWaypoint { get; set; }
        public string Creature { get; set; } = "";
        public int Count { get; set; } = 1;
        public string? Text { get; set; }
    }

    public sealed class QuestDef
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public QuestCategory Category { get; set; }
        public string Zone { get; set; } = "";
        public int Level { get; set; } = 1;
        public int MinLevel { get; set; } = 1;
        public Faction Faction { get; set; }
        public CharClass Class { get; set; }
        public Race? Race { get; set; }
        public string Giver { get; set; } = "";
        public string TurnIn { get; set; } = "";
        /// <summary>Vorgängerquest(s) der Kette.</summary>
        public List<string> Requires { get; set; } = new List<string>();
        public string Text { get; set; } = "";
        public string ObjectivesText { get; set; } = "";
        public string ProgressText { get; set; } = "";
        public string CompletionText { get; set; } = "";
        public List<ObjectiveDef> Objectives { get; set; } = new List<ObjectiveDef>();
        /// <summary>Gegenstände, die bei Annahme gegeben werden (Lieferquests).</summary>
        public List<RewardItemDef> ProvidedItems { get; set; } = new List<RewardItemDef>();
        public int RewardXp { get; set; }
        public int RewardMoney { get; set; }
        public List<RewardItemDef> RewardItems { get; set; } = new List<RewardItemDef>();
        /// <summary>Spieler wählt genau einen dieser Gegenstände.</summary>
        public List<RewardItemDef> RewardChoices { get; set; } = new List<RewardItemDef>();
        public List<ReputationRewardDef> RewardReputation { get; set; } = new List<ReputationRewardDef>();
        /// <summary>Wird bei Abschluss gelehrt (Klassenquests).</summary>
        public string? RewardAbility { get; set; }
        public EscortDef? Escort { get; set; }
        public int SuggestedPlayers { get; set; } = 1;
        public float TimeLimitSeconds { get; set; }
        /// <summary>Entscheidungsquests: schließt diese Quests aus, sobald angenommen.</summary>
        public List<string> ExclusiveWith { get; set; } = new List<string>();
    }

    /// <summary>
    /// Emote (Kapitel 33): Chatbefehle, Texte und die zugehörige Animation. Platzhalter: {me} {target}.
    /// Ohne Ziel gilt TextNoTarget, mit Ziel TextTarget.
    /// </summary>
    public sealed class EmoteDef
    {
        public string Id { get; set; } = "";
        public List<string> Commands { get; set; } = new List<string>();
        public string TextNoTarget { get; set; } = "";
        public string TextTarget { get; set; } = "";
        /// <summary>Animationsname (siehe Client.Anim.AnimClip); leer = nur Text.</summary>
        public string Animation { get; set; } = "";
        /// <summary>Wiederholt sich, bis sich die Figur bewegt (Tanzen, Sitzen, Schlafen).</summary>
        public bool Loop { get; set; }
        /// <summary>Dauer einer einmaligen Animation in Sekunden.</summary>
        public float Duration { get; set; } = 2f;
    }

    /// <summary>Sätze der KI-Mitspieler für Ereignisse. Platzhalter: {me} {leader} {tank} {healer} {target} {zone}.</summary>
    public sealed class BotDialogDef
    {
        public string Event { get; set; } = "";
        public Role? Role { get; set; }
        public string? Personality { get; set; }
        public List<string> Lines { get; set; } = new List<string>();
    }

    /// <summary>Wie ein KI-Mitspieler einer Klasse/Rolle kämpft (Prioritätsliste).</summary>
    public sealed class BotProfileDef
    {
        public CharClass Class { get; set; }
        public Role Role { get; set; }
        public string TalentTree { get; set; } = "";
        public Form Form { get; set; }
        /// <summary>Buffs, die außerhalb des Kampfes auf Gruppe/selbst gehalten werden.</summary>
        public List<string> Buffs { get; set; } = new List<string>();
        public List<BotRuleDef> Rotation { get; set; } = new List<BotRuleDef>();
        public List<string> Heals { get; set; } = new List<string>();
        public string? Resurrect { get; set; }
        public string? Taunt { get; set; }
        public string? Interrupt { get; set; }
        public string? Pet { get; set; }
        public bool Ranged { get; set; }
    }

    public enum BotCondition : byte
    {
        Always = 0,
        TargetMissingAura,
        SelfMissingAura,
        TargetHealthBelow,
        SelfHealthBelow,
        SelfPowerAbove,
        ComboPointsAtLeast,
        TargetCasting,
        MultipleEnemies,
        NotInMeleeRange,
        TargetNotTargetingMe,
        SelfPowerBelow,
        InMeleeRange,
    }

    public sealed class BotRuleDef
    {
        public string Ability { get; set; } = "";
        public BotCondition If { get; set; }
        public float Value { get; set; }
        public string? Aura { get; set; }
    }
}
