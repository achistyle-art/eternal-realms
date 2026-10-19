using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.World
{
    public abstract class Entity
    {
        public int Id { get; internal set; }
        public string Name { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Facing { get; set; }
        public ZoneInstance? Zone { get; internal set; }
        public bool Removed { get; internal set; }
        public abstract EntityKind Kind { get; }

        public GameWorld World => Zone?.World ?? throw new InvalidOperationException($"{Name} ist in keiner Zone.");

        public float DistanceTo(Entity other) => Vec3.DistanceXZ(Position, other.Position);
        public float DistanceTo(Vec3 p) => Vec3.DistanceXZ(Position, p);

        public abstract S_EntitySpawn BuildSpawn(Player? viewer);
        public abstract EntitySnapshotData BuildSnapshot();

        public override string ToString() => $"{Name}#{Id}";
    }
}
