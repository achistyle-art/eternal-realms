using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.World
{
    /// <summary>Interaktives Objekt in der Welt (Erzader, Kiste, Portal).</summary>
    public sealed class GameObjectEntity : Entity
    {
        public GameObjectDef Def { get; }
        public bool Available { get; set; } = true;
        public float RespawnTimer { get; set; }
        public float RespawnSeconds { get; set; }

        public GameObjectEntity(GameObjectDef def) { Def = def; Name = def.Name; }

        public override EntityKind Kind => EntityKind.GameObject;

        public override S_EntitySpawn BuildSpawn(Player? viewer) => new()
        {
            Id = Id, Kind = EntityKind.GameObject, TemplateId = Def.Id, Name = Def.Name, Faction = Faction.Friendly,
            State = BuildSnapshot(),
        };

        public override EntitySnapshotData BuildSnapshot() => new()
        {
            Id = Id, Position = Position, Facing = Facing, Health = Available ? 1 : 0, MaxHealth = 1, Flags = Available ? 0u : (uint)UnitFlags.Dead,
        };
    }
}
