using System;

namespace EternalRealms.Shared.Rules
{
    /// <summary>
    /// Reine Kampfformeln ohne Zustand. Server berechnet damit Ergebnisse; der Client nutzt sie nur für Tooltips.
    /// </summary>
    public static class CombatMath
    {
        public const float MeleeRange = 5f;
        public const float GlobalCooldown = 1.5f;
        public const float EnergyGlobalCooldown = 1.0f;
        public const float LeaveCombatSeconds = 5f;

        /// <summary>Schadensreduktion durch Rüstung (0..0,75).</summary>
        public static float ArmorReduction(float armor, int attackerLevel)
        {
            if (armor <= 0) return 0;
            float dr = armor / (armor + 400f + 85f * attackerLevel);
            return Math.Min(dr, 0.75f);
        }

        /// <summary>Verfehlenchance im Nahkampf (0..1) für Angreifer- gegen Verteidigerstufe.</summary>
        public static float MeleeMissChance(int attackerLevel, int defenderLevel, float hitBonusPct, bool dualWield)
        {
            float miss = 0.05f + Math.Max(0, defenderLevel - attackerLevel) * 0.01f;
            if (defenderLevel - attackerLevel >= 3) miss += 0.01f; // Bosse sind schwerer zu treffen
            if (dualWield) miss += 0.19f;
            return Clamp01(miss - hitBonusPct / 100f);
        }

        /// <summary>Verfehlenchance für Zauber (0..1).</summary>
        public static float SpellMissChance(int attackerLevel, int defenderLevel, float hitBonusPct)
        {
            int diff = defenderLevel - attackerLevel;
            float miss = diff <= -3 ? 0.01f : diff <= 2 ? 0.04f + Math.Max(0, diff) * 0.01f : diff == 3 ? 0.17f : 0.17f + (diff - 3) * 0.11f;
            return Math.Max(0.01f, Clamp01(miss - hitBonusPct / 100f));
        }

        /// <summary>Durchschnittlicher Widerstandsanteil eines Zaubers (0..0,75).</summary>
        public static float AverageResist(float resistance, int attackerLevel)
        {
            if (resistance <= 0) return 0;
            return Math.Min(0.75f, resistance / (attackerLevel * 5f) * 0.75f);
        }

        /// <summary>Würfelt einen Teilwiderstand in 25 %-Stufen um den Mittelwert.</summary>
        public static float RollPartialResist(float average, IRandom rng)
        {
            if (average <= 0) return 0;
            double r = rng.NextDouble();
            float step = average >= 0.5f ? 0.5f : 0.25f;
            if (r < 0.2) return Math.Min(0.75f, average + step);
            if (r < 0.7) return (float)(Math.Round(average * 4) / 4.0);
            return Math.Max(0, average - step);
        }

        /// <summary>Wutgewinn aus verursachtem (Faktor 7,5) bzw. erlittenem (2,5) Schaden.</summary>
        public static float RageFromDamage(int damage, int level, bool dealt)
        {
            float c = 0.009f * level * level + 3.2f * level + 4f;
            return damage / c * (dealt ? 7.5f : 2.5f) / 2f;
        }

        /// <summary>Durchschnittlicher Abschlag bei Streiftreffern gegen höherstufige Gegner.</summary>
        public static float GlancingChance(int attackerLevel, int defenderLevel)
        {
            int diff = defenderLevel - attackerLevel;
            return diff < 0 ? 0 : 0.1f + diff * 0.05f;
        }

        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }

    /// <summary>Ergebnis eines Nahkampf-Angriffswurfs (eine einzige Tabelle, wie in klassischen MMORPGs).</summary>
    public static class AttackTable
    {
        public struct Chances
        {
            public float Miss, Dodge, Parry, Glancing, Block, Crit;
        }

        /// <summary>Würfelt einmal und liefert das Ergebnis der Angriffstabelle.</summary>
        public static CombatResult Roll(Chances c, IRandom rng, bool allowGlancing)
        {
            double r = rng.NextDouble();
            double acc = c.Miss;
            if (r < acc) return CombatResult.Miss;
            acc += c.Dodge;
            if (r < acc) return CombatResult.Dodge;
            acc += c.Parry;
            if (r < acc) return CombatResult.Parry;
            if (allowGlancing)
            {
                acc += c.Glancing;
                if (r < acc) return CombatResult.Glancing;
            }
            acc += c.Block;
            if (r < acc) return CombatResult.Block;
            acc += c.Crit;
            if (r < acc) return CombatResult.Crit;
            return CombatResult.Hit;
        }
    }
}
