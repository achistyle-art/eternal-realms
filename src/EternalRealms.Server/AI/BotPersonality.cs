using EternalRealms.Server.Persistence;
using EternalRealms.Shared;

namespace EternalRealms.Server.AI
{
    /// <summary>
    /// Persönlichkeiten und Schwierigkeitsstufen der KI-Mitspieler. Persönlichkeiten prägen Dialoge und
    /// beeinflussen Entscheidungen nur geringfügig – die Gruppenrolle wird immer korrekt erfüllt.
    /// </summary>
    public static class BotPersonality
    {
        public const string Mutig = "mutig", Vorsichtig = "vorsichtig", Abenteuerlustig = "abenteuerlustig", Veteran = "veteran",
                            Freundlich = "freundlich", Witzig = "witzig", Wortkarg = "wortkarg";

        public static readonly string[] All = { Mutig, Vorsichtig, Abenteuerlustig, Veteran, Freundlich, Witzig, Wortkarg };

        public static string Label(string archetype) => archetype switch
        {
            Mutig => "mutig", Vorsichtig => "vorsichtig", Abenteuerlustig => "abenteuerlustig", Veteran => "erfahrener Veteran",
            Witzig => "witzig", Wortkarg => "wortkarg", _ => "freundlich",
        };

        public static string DifficultyLabel(BotDifficulty d) => d switch
        {
            BotDifficulty.Anfaenger => "Anfänger", BotDifficulty.Erfahren => "Erfahren", BotDifficulty.Veteran => "Veteran", _ => "Normal",
        };

        /// <summary>Wahrscheinlichkeit, dass ein Bot in einem Denkschritt einen Fehler macht (zögert, falsche Reihenfolge).</summary>
        public static float MistakeChance(BotDifficulty d) => d switch
        {
            BotDifficulty.Anfaenger => 0.15f, BotDifficulty.Normal => 0.07f, BotDifficulty.Erfahren => 0.03f, _ => 0.01f,
        };

        public static float SkillFor(BotDifficulty d, IRandom rng) => d switch
        {
            BotDifficulty.Anfaenger => 0.3f + (float)rng.NextDouble() * 0.15f,
            BotDifficulty.Normal => 0.5f + (float)rng.NextDouble() * 0.15f,
            BotDifficulty.Erfahren => 0.7f + (float)rng.NextDouble() * 0.15f,
            _ => 0.9f + (float)rng.NextDouble() * 0.1f,
        };

        /// <summary>Würfelt Persönlichkeitswerte passend zum Archetyp.</summary>
        public static void Apply(BotData d, string archetype, BotDifficulty difficulty, IRandom rng)
        {
            float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            d.Archetype = archetype;
            d.Difficulty = archetype == Veteran && difficulty < BotDifficulty.Erfahren ? BotDifficulty.Erfahren : difficulty;
            d.Skill = SkillFor(d.Difficulty, rng);
            d.Aggression = archetype == Mutig ? R(0.8f, 1f) : archetype == Vorsichtig ? R(0f, 0.3f) : R(0.3f, 0.7f);
            d.Caution = archetype == Vorsichtig ? R(0.8f, 1f) : archetype == Mutig ? R(0f, 0.3f) : archetype == Veteran ? R(0.5f, 0.8f) : R(0.2f, 0.7f);
            d.Chattiness = archetype == Wortkarg ? 0.1f : archetype is Witzig or Abenteuerlustig ? R(0.75f, 0.95f) : R(0.35f, 0.75f);
        }
    }
}
