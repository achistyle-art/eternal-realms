using System;
using EternalRealms.Shared.Content;

namespace EternalRealms.Shared.Rules
{
    /// <summary>Wie aus Grundwerten Leben, Mana, Angriffskraft und Chancen werden.</summary>
    public static class StatFormulas
    {
        public static int BaseHealth(ClassDef cls, int level) =>
            (int)((18 + level * 14 + level * level * 0.25f) * cls.HealthFactor);

        public static int BaseMana(ClassDef cls, int level) =>
            cls.ManaFactor <= 0 ? 0 : (int)((20 + level * 18 + level * level * 0.25f) * cls.ManaFactor);

        public static float BaseStat(ClassDef cls, RaceDef race, Stat stat, int level) =>
            cls.BaseStats.Get(stat) + race.StatBonus.Get(stat) + cls.StatsPerLevel.Get(stat) * (level - 1);

        public static int HealthFromStamina(float stamina) => (int)(Math.Max(0, stamina - 20) * 10 + Math.Min(stamina, 20));
        public static int ManaFromIntellect(float intellect) => (int)(Math.Max(0, intellect - 20) * 15 + Math.Min(intellect, 20));

        /// <summary>Kritische Nahkampfchance in Prozent.</summary>
        public static float MeleeCrit(float agility, int level) => 5f + agility / (12f + level * 0.25f);
        public static float SpellCrit(float intellect, int level) => 1.5f + intellect / (35f + level * 0.5f);
        public static float Dodge(float agility, int level) => 3f + agility / (14f + level * 0.3f);

        /// <summary>Lebensregeneration pro 2 Sekunden außerhalb des Kampfes.</summary>
        public static float HealthRegenPerTick(float spirit, int level) => 2f + spirit * 0.2f + level * 0.3f;

        /// <summary>Manaregeneration pro 2 Sekunden, wenn 5 Sekunden lang kein Mana ausgegeben wurde.</summary>
        public static float ManaRegenPerTick(float spirit, float intellect) => 4f + spirit * 0.25f + intellect * 0.05f;

        // ------------------------------------------------------------- Kreaturen

        public static int CreatureHealth(int level, float multiplier, bool elite)
        {
            double hp = 40 + 13 * level + 1.05 * level * level;
            if (elite) hp *= 3;
            return Math.Max(1, (int)(hp * multiplier));
        }

        /// <summary>Durchschnittlicher Schaden pro Sekunde einer normalen Kreatur.</summary>
        public static float CreatureDps(int level, float multiplier, bool elite)
        {
            double dps = 0.8 + 0.55 * level + 0.012 * level * level;
            if (elite) dps *= 1.6;
            return (float)(dps * multiplier);
        }

        public static int CreatureArmor(int level, float multiplier) => (int)((20 + level * 50) * multiplier);

        public static int CreatureMana(int level, int factor) => factor <= 0 ? 0 : (40 + level * 30) * factor;
    }
}
