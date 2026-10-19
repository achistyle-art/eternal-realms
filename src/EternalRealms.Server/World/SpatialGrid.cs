using EternalRealms.Shared;

namespace EternalRealms.Server.World
{
    /// <summary>
    /// Gleichmäßiges Raster für Umgebungsabfragen. Aggro, Sichtbarkeit und Flächenzauber fragen nur
    /// die Zellen in Reichweite ab statt aller Einheiten der Zone – so skaliert eine Zone auf Tausende Einheiten.
    /// </summary>
    public sealed class SpatialGrid
    {
        private readonly float _cell;
        private readonly Dictionary<long, List<Entity>> _cells = new();
        private readonly Dictionary<Entity, long> _where = new();

        public SpatialGrid(float cellSize = 32f) { _cell = cellSize; }

        private long Key(Vec3 p)
        {
            long x = (long)Math.Floor(p.X / _cell), z = (long)Math.Floor(p.Z / _cell);
            return (x << 32) ^ (z & 0xffffffffL);
        }

        public void Insert(Entity e)
        {
            var k = Key(e.Position);
            if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<Entity>();
            list.Add(e);
            _where[e] = k;
        }

        public void Remove(Entity e)
        {
            if (!_where.TryGetValue(e, out var k)) return;
            if (_cells.TryGetValue(k, out var list)) list.Remove(e);
            _where.Remove(e);
        }

        public void Moved(Entity e)
        {
            var k = Key(e.Position);
            if (_where.TryGetValue(e, out var old) && old == k) return;
            Remove(e);
            Insert(e);
        }

        public IEnumerable<Entity> Query(Vec3 center, float radius)
        {
            int minX = (int)Math.Floor((center.X - radius) / _cell), maxX = (int)Math.Floor((center.X + radius) / _cell);
            int minZ = (int)Math.Floor((center.Z - radius) / _cell), maxZ = (int)Math.Floor((center.Z + radius) / _cell);
            float r2 = radius * radius;
            for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    long k = ((long)x << 32) ^ (z & 0xffffffffL);
                    if (!_cells.TryGetValue(k, out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        float dx = e.Position.X - center.X, dz = e.Position.Z - center.Z;
                        if (dx * dx + dz * dz <= r2) yield return e;
                    }
                }
        }
    }
}
