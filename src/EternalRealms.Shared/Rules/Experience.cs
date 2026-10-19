using System;

namespace EternalRealms.Shared.Rules
{
    /// <summary>
    /// Erfahrungsregeln für Stufe 1–60. Die Kurve ist bewusst steil: Stufe 2 dauert Minuten,
    /// die Stufen 50–60 jeweils mehrere Stunden. Gesamt ca. 4 Mio. EP – bei ~25.000 EP/Stunde
    /// im späten Spiel ergibt das eine klassische Levelphase von deutlich über 100 Spielstunden.
    /// </summary>
    public static class Experience
    {
        public const int MaxLevel = 60;
        public const int TalentStartLevel = 10;

        /// <summary>Zusatzaufwand ab Stufe 29, der die zweite Spielhälfte spürbar verlängert.</summary>
        private static int Difficulty(int level)
        {
            if (level <= 28) return 0;
            if (level == 29) return 1;
            if (level == 30) return 3;
            if (level == 31) return 6;
            return Math.Min(5 * (level - 30), 150);
        }

        /// <summary>EP, um von <paramref name="level"/> auf die nächste Stufe zu kommen.</summary>
        public static int XpToNextLevel(int level)
        {
            if (level >= MaxLevel) return 0;
            if (level < 1) level = 1;
            double raw = (8.0 * level + Difficulty(level)) * (45 + 5 * level);
            // Abschnittsweise Streckung, damit die hohen Stufen nicht nur durch die Formel, sondern
            // auch durch einen Faktor wertvoller werden.
            double factor = level < 10 ? 1.0 : level < 30 ? 1.05 : level < 50 ? 1.1 : 1.2;
            return (int)(Math.Round(raw * factor / 100.0) * 100);
        }

        public static long TotalXpToReach(int level)
        {
            long sum = 0;
            for (int l = 1; l < level && l < MaxLevel; l++) sum += XpToNextLevel(l);
            return sum;
        }

        /// <summary>Stufendifferenz, ab der ein Gegner "grau" ist und keine EP mehr gibt.</summary>
        public static int GrayLevel(int playerLevel)
        {
            if (playerLevel <= 5) return 0;
            if (playerLevel <= 39) return playerLevel - 5 - playerLevel / 10;
            if (playerLevel <= 59) return playerLevel - 1 - playerLevel / 5;
            return playerLevel - 9;
        }

        private static int ZeroDifference(int playerLevel)
        {
            if (playerLevel <= 7) return 5;
            if (playerLevel <= 9) return 6;
            if (playerLevel <= 11) return 7;
            if (playerLevel <= 15) return 8;
            if (playerLevel <= 19) return 9;
            if (playerLevel <= 29) return 11;
            if (playerLevel <= 39) return 12;
            if (playerLevel <= 44) return 13;
            if (playerLevel <= 49) return 14;
            if (playerLevel <= 54) return 15;
            if (playerLevel <= 59) return 16;
            return 17;
        }

        /// <summary>Basis-EP für das Besiegen eines Gegners (ohne Ruhebonus und Gruppenaufteilung).</summary>
        public static int KillXp(int playerLevel, int mobLevel, bool elite)
        {
            if (playerLevel >= MaxLevel) return 0;
            int baseXp = mobLevel * 5 + 45;
            double xp;
            if (mobLevel >= playerLevel)
            {
                int diff = Math.Min(mobLevel - playerLevel, 4);
                xp = baseXp * (1 + 0.05 * diff);
            }
            else
            {
                if (mobLevel <= GrayLevel(playerLevel)) return 0;
                xp = baseXp * (1 - (playerLevel - mobLevel) / (double)ZeroDifference(playerLevel));
            }
            if (elite) xp *= 2;
            return Math.Max(0, (int)Math.Round(xp));
        }

        /// <summary>Gruppenbonus: Größere Gruppen erhalten insgesamt etwas mehr EP.</summary>
        public static double GroupBonus(int members) => members switch
        {
            <= 2 => 1.0,
            3 => 1.166,
            4 => 1.3,
            _ => 1.4,
        };

        /// <summary>
        /// Quest-EP abhängig von der Stufendifferenz. Zu niedrige Quests geben stark verringerte EP.
        /// </summary>
        public static int QuestXp(int playerLevel, int questLevel, int baseXp)
        {
            if (playerLevel >= MaxLevel) return 0;
            int diff = playerLevel - questLevel;
            double f = diff <= 5 ? 1.0 : diff == 6 ? 0.8 : diff == 7 ? 0.6 : diff == 8 ? 0.4 : diff == 9 ? 0.2 : 0.1;
            return (int)(Math.Round(baseXp * f / 5.0) * 5);
        }

        /// <summary>EP für das erste Betreten eines Gebiets.</summary>
        public static int DiscoveryXp(int playerLevel, int areaXp) =>
            playerLevel >= MaxLevel ? 0 : areaXp > 0 ? areaXp : 20 + playerLevel * 10;

        public static int TalentPointsForLevel(int level) => Math.Max(0, level - (TalentStartLevel - 1));

        // ---------------------------------------------------------------- Ruhebonus

        /// <summary>Echtzeit-Sekunden für 5 % einer Stufe Ruhebonus (8 Stunden).</summary>
        public const double RestedSecondsPerBubble = 8 * 3600;

        /// <summary>Höchstmenge an Ruhe-EP: 1,5 Stufen.</summary>
        public static int MaxRestedXp(int level) => (int)(XpToNextLevel(level) * 1.5);

        /// <summary>Ruhe-EP, die in <paramref name="seconds"/> angesammelt werden. Im Gasthaus voll, sonst ein Viertel.</summary>
        public static int RestedGain(int level, double seconds, bool inInn)
        {
            double bubbles = seconds / RestedSecondsPerBubble * (inInn ? 1.0 : 0.25);
            return (int)(XpToNextLevel(level) * 0.05 * bubbles);
        }
    }
}
