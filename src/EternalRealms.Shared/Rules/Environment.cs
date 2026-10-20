namespace EternalRealms.Shared.Rules
{
    /// <summary>
    /// Tageszeit und Wetter mit spielerischer Wirkung (Kapitel 36). Alle Werte sind bewusst klein, damit das Wetter
    /// spürbar, aber nie entscheidend ist. Bewegungstempo bleibt unverändert (die Clientvorhersage kennt es nicht).
    /// </summary>
    public static class EnvironmentRules
    {
        public const float DawnHour = 6f, DuskHour = 21f;

        public static bool IsNight(float hour) => hour < DawnHour || hour >= DuskHour;

        /// <summary>Tageszeit-Bedingung eines Spawns oder Ereignisses: "" = immer, "day", "night".</summary>
        public static bool TimeMatches(string condition, float hour) => condition switch
        {
            "day" => !IsNight(hour),
            "night" => IsNight(hour),
            _ => true,
        };

        /// <summary>Schadensfaktor einer Schule bei diesem Wetter (Intensität 0–1).</summary>
        public static float SchoolDamage(Weather w, float intensity, School school)
        {
            float k = 0.1f * System.Math.Max(0, System.Math.Min(1, intensity));
            switch (w)
            {
                case Weather.Rain: return school == School.Fire ? 1 - k : school == School.Nature || school == School.Frost ? 1 + k * 0.5f : 1;
                case Weather.Storm: return school == School.Nature ? 1 + k : school == School.Fire ? 1 - k * 0.5f : 1;
                case Weather.Snow: return school == School.Frost ? 1 + k : school == School.Fire ? 1 - k * 0.5f : 1;
                default: return 1;
            }
        }

        /// <summary>Wie weit Kreaturen Gegner bemerken: Nebel und Nacht verkürzen die Sicht.</summary>
        public static float SightFactor(Weather w, float intensity, float hour)
        {
            float f = 1;
            if (w == Weather.Fog) f -= 0.35f * intensity;
            else if (w == Weather.Storm || w == Weather.Snow) f -= 0.15f * intensity;
            if (IsNight(hour)) f -= 0.15f;
            return System.Math.Max(0.4f, f);
        }

        /// <summary>Nachtwesen (Untote, Werwölfe, Geister) sind nachts stärker.</summary>
        public static float NightCreatureDamage(string family, float hour) =>
            IsNight(hour) && (family == "Undead" || family == "Werwolf" || family == "Spirit") ? 1.1f : 1f;

        /// <summary>Angeln: Regen bringt mehr Fische an die Oberfläche.</summary>
        public static float FishingBonus(Weather w) => w == Weather.Rain ? 1.25f : w == Weather.Storm ? 0.8f : 1f;
    }
}
