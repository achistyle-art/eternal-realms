namespace EternalRealms.Shared
{
    /// <summary>Spielbare Fraktionen und KI-Gesinnungen.</summary>
    public enum Faction : byte
    {
        None = 0,
        Sonnenbuendnis = 1,
        Schattenpakt = 2,
        /// <summary>Greift jeden Spieler an.</summary>
        Hostile = 3,
        /// <summary>Greift erst an, wenn es selbst angegriffen wird.</summary>
        Neutral = 4,
        /// <summary>Freundlich zu beiden Fraktionen (z. B. Geisterheiler).</summary>
        Friendly = 5,
    }

    public enum Race : byte
    {
        // Sonnenbündnis
        Aldorer = 1,
        Felsgeborene = 2,
        Elarii = 3,
        Fennlinge = 4,
        // Schattenpakt
        Grakar = 5,
        Nachtgeborene = 6,
        Hornvolk = 7,
        Skarr = 8,
    }

    public enum CharClass : byte
    {
        None = 0,
        Warrior = 1,
        Paladin = 2,
        Hunter = 3,
        Rogue = 4,
        Priest = 5,
        Shaman = 6,
        Mage = 7,
        Warlock = 8,
        Druid = 9,
    }

    public enum Gender : byte { Male = 0, Female = 1 }

    public enum PowerType : byte { None = 0, Mana = 1, Rage = 2, Energy = 3 }

    public enum School : byte { Physical = 0, Holy = 1, Fire = 2, Nature = 3, Frost = 4, Shadow = 5, Arcane = 6 }

    public enum Stat : byte { Strength = 0, Agility = 1, Stamina = 2, Intellect = 3, Spirit = 4 }

    public enum ArmorType : byte { None = 0, Cloth = 1, Leather = 2, Mail = 3, Plate = 4 }

    public enum WeaponType : byte
    {
        None = 0, Sword1H, Sword2H, Axe1H, Axe2H, Mace1H, Mace2H, Dagger, Staff, Polearm, FistWeapon,
        Bow, Gun, Crossbow, Wand, Thrown, Shield, OffhandFrill,
    }

    public enum ItemQuality : byte { Poor = 0, Common = 1, Uncommon = 2, Rare = 3, Epic = 4, Legendary = 5 }

    public enum ItemCategory : byte { Misc = 0, Weapon, Armor, Consumable, Quest, TradeGoods, Container, Reagent }

    public enum EquipSlot : byte
    {
        None = 0, Head, Neck, Shoulders, Back, Chest, Wrist, Hands, Waist, Legs, Feet,
        Finger1, Finger2, Trinket1, Trinket2, MainHand, OffHand, Ranged,
    }

    /// <summary>Wo ein Gegenstand getragen werden kann (Finger passt in Finger1/Finger2 usw.).</summary>
    public enum InventoryType : byte
    {
        None = 0, Head, Neck, Shoulders, Back, Chest, Wrist, Hands, Waist, Legs, Feet, Finger, Trinket,
        OneHand, MainHandOnly, TwoHand, OffHand, Shield, Ranged,
    }

    public enum Role : byte { Tank = 0, Healer = 1, MeleeDps = 2, RangedDps = 3 }

    /// <summary>Haltungen und Gestalten (Krieger-Haltungen, Druiden-Formen).</summary>
    public enum Form : byte { None = 0, BattleStance, DefensiveStance, BerserkerStance, Bear, Cat, Travel }

    public enum EntityKind : byte { Player = 1, Creature = 2, Npc = 3, Pet = 4, GameObject = 5 }

    public enum DeathState : byte { Alive = 0, Dead = 1, Ghost = 2 }

    public enum TargetType : byte
    {
        Self = 0,
        Enemy,
        Friendly,
        FriendlyOrSelf,
        /// <summary>Flächenwirkung um den Zaubernden herum.</summary>
        AreaAroundSelf,
        /// <summary>Flächenwirkung um das Ziel herum.</summary>
        AreaAroundTarget,
        /// <summary>Totes freundliches Ziel (Wiederbelebung).</summary>
        DeadFriendly,
        /// <summary>Alle Mitglieder der eigenen Gruppe in Reichweite.</summary>
        Party,
    }

    public enum CombatResult : byte
    {
        Hit = 0, Crit, Miss, Dodge, Parry, Block, Glancing, Resist, PartialResist, Absorb, Immune, Heal, CritHeal, Evade,
    }

    public enum QuestState : byte { None = 0, Active = 1, Complete = 2, Failed = 3, Rewarded = 4 }

    /// <summary>Was ein Questgeber dem Spieler über dem Kopf anzeigt.</summary>
    public enum QuestMarker : byte { None = 0, Available = 1, AvailableLowLevel = 2, Incomplete = 3, ReadyToTurnIn = 4 }

    public enum QuestCategory : byte { Main = 0, Side, Class, Dungeon, Raid, Profession, Faction }

    public enum ObjectiveType : byte
    {
        Kill = 0,
        Collect,
        Explore,
        Interact,
        Escort,
        Deliver,
        Talk,
    }

    public enum ChatChannel : byte { Say = 0, Yell, Party, Guild, Whisper, General, System, Emote, Raid }

    public enum Weather : byte { Clear = 0, Rain, Snow, Fog, Storm }

    [System.Flags]
    public enum NpcRole : ushort
    {
        None = 0,
        QuestGiver = 1 << 0,
        Vendor = 1 << 1,
        ClassTrainer = 1 << 2,
        Innkeeper = 1 << 3,
        FlightMaster = 1 << 4,
        SpiritHealer = 1 << 5,
        Guard = 1 << 6,
        Banker = 1 << 7,
        Auctioneer = 1 << 8,
        Repair = 1 << 9,
        ProfessionTrainer = 1 << 10,
    }

    [System.Flags]
    public enum UnitFlags : uint
    {
        None = 0,
        InCombat = 1 << 0,
        Dead = 1 << 1,
        Ghost = 1 << 2,
        Elite = 1 << 3,
        Rare = 1 << 4,
        Boss = 1 << 5,
        Stealthed = 1 << 6,
        Stunned = 1 << 7,
        Rooted = 1 << 8,
        Silenced = 1 << 9,
        Feared = 1 << 10,
        Casting = 1 << 11,
        Bot = 1 << 12,
        Lootable = 1 << 13,
        Tapped = 1 << 14,
        Evading = 1 << 15,
        PvP = 1 << 16,
        Resting = 1 << 17,
        Mounted = 1 << 18,
    }
}
