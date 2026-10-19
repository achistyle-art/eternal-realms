using System;

namespace EternalRealms.Shared
{
    /// <summary>Zufallsquelle für Kampfwürfe. Austauschbar, damit Tests deterministisch laufen.</summary>
    public interface IRandom
    {
        /// <summary>Gleichverteilt in [0, 1).</summary>
        double NextDouble();
        /// <summary>Ganzzahl in [min, max] (beide inklusive).</summary>
        int Range(int min, int max);
    }

    public sealed class GameRandom : IRandom
    {
        private readonly Random _rng;
        public GameRandom(int? seed = null) { _rng = seed.HasValue ? new Random(seed.Value) : new Random(); }
        public double NextDouble() => _rng.NextDouble();
        public int Range(int min, int max) => max <= min ? min : _rng.Next(min, max + 1);
    }

    public static class RandomExtensions
    {
        public static bool Chance(this IRandom r, double probability) => r.NextDouble() < probability;

        public static float RangeF(this IRandom r, float min, float max) => min + (float)r.NextDouble() * (max - min);

        public static T Pick<T>(this IRandom r, System.Collections.Generic.IReadOnlyList<T> list) => list[r.Range(0, list.Count - 1)];
    }
}

namespace EternalRealms.Shared
{
    /// <summary>
    /// Stabiler Hash (FNV-1a). string.GetHashCode ist in .NET pro Prozess zufällig – für reproduzierbare
    /// Weltgenerierung (Spawnpositionen) braucht es einen Wert, der überall gleich ist.
    /// </summary>
    public static class StableHash
    {
        public static int Of(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return (int)h;
            }
        }
    }
}
