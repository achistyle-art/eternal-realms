using EternalRealms.Shared.Content;

namespace EternalRealms.Shared.Client.Audio
{
    /// <summary>Lage des Spielers, aus der die Musik gewählt wird.</summary>
    public struct MusicSituation
    {
        public ZoneDef? Zone;
        public AreaDef? Area;
        public bool InCombat, BossFight, Ghost, Night;
    }

    /// <summary>
    /// Wählt Musik und Umgebungsgeräusch (Kapitel 34): Region, Gebiet (Stadt, Gasthaus), Kampf, Boss, Geisterwelt, Nacht.
    /// Kampfmusik setzt erst nach kurzer Zeit ein und klingt nach dem Kampf noch nach, damit sie nicht flackert.
    /// </summary>
    public sealed class MusicDirector
    {
        public const float CombatDelay = 2f, CombatLinger = 8f, Crossfade = 2.5f;

        private float _combatFor, _sinceCombat = 999;
        public string Current { get; private set; } = "";
        public string Ambience { get; private set; } = "";

        /// <summary>Rückgabe: true, wenn ein neues Stück beginnen soll (Überblendung über <see cref="Crossfade"/>).</summary>
        public bool Update(float dt, MusicSituation s)
        {
            if (s.InCombat || s.BossFight) { _combatFor += dt; _sinceCombat = 0; }
            else { _combatFor = 0; _sinceCombat += dt; }
            string next = Choose(s, _combatFor >= CombatDelay || (_sinceCombat < CombatLinger && Current.StartsWith("music_combat")) || s.BossFight);
            Ambience = AmbienceFor(s);
            if (next == Current) return false;
            Current = next;
            return true;
        }

        public static string Choose(MusicSituation s, bool combat)
        {
            if (s.Ghost) return "music_ghost";
            if (s.BossFight) return "music_boss";
            string biome = s.Zone?.Biome is { Length: > 0 } b ? b.ToLowerInvariant() : "wald";
            if (combat) return "music_combat_" + biome;
            if (s.Area != null && s.Area.Music.Length > 0) return s.Area.Music;
            if (s.Area != null && (s.Area.IsCity || s.Area.IsInn)) return "music_village";
            string zone = s.Zone?.Music is { Length: > 0 } m ? m : "music_" + biome;
            return s.Night && !zone.EndsWith("_night") ? zone.Replace("_day", "") + "_night" : zone;
        }

        public static string AmbienceFor(MusicSituation s)
        {
            if (s.Zone?.Ambience is { Length: > 0 } a) return s.Night ? a + "_night" : a;
            return s.Zone?.Biome switch { "Steppe" => "amb_steppe_wind", "Gruft" => "amb_crypt_drip", _ => s.Night ? "amb_forest_night" : "amb_forest_birds" };
        }
    }
}
