using System;

namespace EternalRealms.Shared
{
    /// <summary>
    /// Engine-unabhängiger Vektor. Unity-Achsen: X = Osten, Y = Höhe, Z = Norden.
    /// Spiellogik (Reichweiten, Aggro) rechnet auf der XZ-Ebene, damit kleine Höhenunterschiede
    /// im Gelände keine Rolle spielen.
    /// </summary>
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public float LengthXZ => (float)Math.Sqrt(X * X + Z * Z);

        public Vec3 NormalizedXZ()
        {
            float l = LengthXZ;
            return l < 1e-5f ? Zero : new Vec3(X / l, 0, Z / l);
        }

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Length;

        /// <summary>Distanz auf der Bodenebene.</summary>
        public static float DistanceXZ(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Bewegt <paramref name="from"/> höchstens <paramref name="maxStep"/> Meter in Richtung <paramref name="to"/>.</summary>
        public static Vec3 MoveTowards(Vec3 from, Vec3 to, float maxStep)
        {
            var d = to - from;
            float len = d.Length;
            if (len <= maxStep || len < 1e-5f) return to;
            return from + d * (maxStep / len);
        }

        /// <summary>Blickrichtung in Radiant (0 = +Z/Norden, im Uhrzeigersinn) von a nach b.</summary>
        public static float FacingTowards(Vec3 a, Vec3 b) => (float)Math.Atan2(b.X - a.X, b.Z - a.Z);

        public bool Equals(Vec3 o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object? obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:0.0}, {Y:0.0}, {Z:0.0})";
    }
}
