using System.Collections.Generic;

namespace EternalRealms.Shared.Content
{
    /// <summary>
    /// Ein Modifikator verändert Werte einer Einheit. Talente, Auren (Buffs/Debuffs) und Gegenstände
    /// verwenden alle dieselbe Struktur, damit der Server Werte an genau einer Stelle zusammenrechnet.
    /// </summary>
    public enum ModType : byte
    {
        StatFlat = 0,
        StatPercent,
        MaxHealthFlat,
        MaxHealthPercent,
        MaxPowerPercent,
        ArmorFlat,
        ArmorPercent,
        ResistFlat,
        AttackPowerFlat,
        SpellPowerFlat,
        HealingPowerFlat,
        /// <summary>Kritische Trefferchance in Prozentpunkten (5 = +5 %).</summary>
        CritChance,
        HitChance,
        DodgeChance,
        ParryChance,
        BlockChance,
        DamageDonePercent,
        DamageTakenPercent,
        HealingDonePercent,
        HealingTakenPercent,
        CostPercent,
        /// <summary>Zauberzeit in Sekunden (negativ = schneller).</summary>
        CastTimeFlat,
        CooldownFlat,
        ThreatPercent,
        MoveSpeedPercent,
        AttackSpeedPercent,
        PowerRegenPercent,
        RageGenerationPercent,
        CritDamagePercent,
        /// <summary>Wahrscheinlichkeit, beim Zaubern nicht zurückgeworfen zu werden, in Prozent.</summary>
        PushbackResistPercent,
    }

    public enum CritCategory : byte { All = 0, Melee, Ranged, Spell }

    public sealed class ModifierDef
    {
        public ModType Type { get; set; }
        public float Value { get; set; }
        public Stat? Stat { get; set; }
        public School? School { get; set; }
        /// <summary>Wirkt nur auf diese Fähigkeit (Basis-ID ohne Rang).</summary>
        public string? Ability { get; set; }
        public CritCategory Crit { get; set; } = CritCategory.All;

        public ModifierDef Scaled(float factor) => new ModifierDef
        {
            Type = Type, Value = Value * factor, Stat = Stat, School = School, Ability = Ability, Crit = Crit,
        };

        public override string ToString() => $"{Type} {Value:+0.##;-0.##}{(Stat.HasValue ? " " + Stat : "")}{(School.HasValue ? " " + School : "")}{(Ability != null ? " " + Ability : "")}";
    }

    /// <summary>Hilfsfunktionen zum Abfragen einer Modifikatorliste.</summary>
    public static class ModifierQuery
    {
        /// <summary>
        /// Summiert alle passenden Modifikatoren. Ein Modifikator ohne Schule/Fähigkeit/Wert gilt für alle;
        /// einer mit Filter nur, wenn der Filter passt.
        /// </summary>
        public static float Sum(IEnumerable<ModifierDef> mods, ModType type, School? school = null, string? ability = null,
            Stat? stat = null, CritCategory crit = CritCategory.All)
        {
            float total = 0;
            foreach (var m in mods)
            {
                if (m.Type != type) continue;
                if (m.Stat.HasValue && m.Stat != stat) continue;
                if (m.School.HasValue && m.School != school) continue;
                if (m.Ability != null && m.Ability != ability) continue;
                if (type == ModType.CritChance && m.Crit != CritCategory.All && m.Crit != crit) continue;
                total += m.Value;
            }
            return total;
        }
    }
}
