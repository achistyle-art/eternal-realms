using EternalRealms.Shared;
using EternalRealms.Shared.Content;

namespace EternalRealms.Server.AI
{
    /// <summary>Erzeugt volksgerechte Namen aus Silben (für KI-Mitspieler und Namensvorschläge im Editor).</summary>
    public static class NameGenerator
    {
        public static string Generate(RaceDef race, IRandom rng, Func<string, bool> taken)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                var name = race.NameStarts[rng.Range(0, race.NameStarts.Count - 1)] + race.NameEnds[rng.Range(0, race.NameEnds.Count - 1)];
                if (attempt > 20) name += race.NameEnds[rng.Range(0, race.NameEnds.Count - 1)];
                name = Normalize(name);
                if (name.Length >= 3 && name.Length <= 12 && !taken(name)) return name;
            }
            return Normalize(race.NameStarts[0] + rng.Range(100, 999));
        }

        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var lower = name.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        /// <summary>Gültige Charakternamen: 2–12 Buchstaben (inkl. Umlaute), keine Ziffern oder Leerzeichen.</summary>
        public static bool IsValid(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2 || name.Length > 12) return false;
            foreach (var ch in name) if (!char.IsLetter(ch)) return false;
            return true;
        }
    }
}
