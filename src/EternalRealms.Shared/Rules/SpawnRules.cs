using EternalRealms.Shared.Content;

namespace EternalRealms.Shared.Rules
{
    /// <summary>
    /// Wiederkehr von Kreaturen (Kapitel 47). Gewöhnliche Gegner kehren schneller zurück, wenn viele Spieler in der
    /// Nähe sind (keine leer gefegten Questgebiete); Seltene und Weltbosse haben feste, zufällige Fenster.
    /// </summary>
    public static class SpawnRules
    {
        public const float MinCrowdFactor = 0.35f;

        /// <summary>Faktor für die Wiederkehrzeit: 1 Spieler = 1, je weiterer Spieler 25 % schneller, höchstens auf 35 %.</summary>
        public static float CrowdFactor(int playersNearby) =>
            playersNearby <= 1 ? 1f : System.Math.Max(MinCrowdFactor, 1f / (1f + 0.25f * (playersNearby - 1)));

        /// <summary>Wiederkehrzeit in Sekunden. special = Seltener, Boss oder Weltboss (keine Beschleunigung).</summary>
        public static float RespawnDelay(SpawnDef def, IRandom rng, int playersNearby, bool special)
        {
            float min = def.RespawnSeconds;
            float max = System.Math.Max(min, def.RespawnMaxSeconds);
            float t = max > min ? min + (float)rng.NextDouble() * (max - min) : min;
            return special ? t : t * CrowdFactor(playersNearby);
        }

        /// <summary>Welche Kreatur erscheint? Der Seltene nur, wenn er nicht schon lebt.</summary>
        public static string PickCreature(SpawnDef def, IRandom rng, bool rareAlive)
        {
            if (def.RareCreature != null && !rareAlive && def.RareChance > 0 && rng.NextDouble() < def.RareChance) return def.RareCreature;
            return def.Creature;
        }
    }
}
