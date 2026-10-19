using System;
using System.Collections.Generic;
using EternalRealms.Shared.Net;

namespace EternalRealms.Shared.Client
{
    /// <summary>Was der Client über eine Einheit weiß (vom Server gemeldet, nie selbst berechnet).</summary>
    public sealed class ClientEntity
    {
        public int Id;
        public EntityKind Kind;
        public string TemplateId = "";
        public string Name = "";
        public string Title = "";
        public Race Race;
        public CharClass Class;
        public Gender Gender;
        public int Level;
        public Faction Faction;
        public NpcRole Roles;
        public PowerType PowerType;
        public QuestMarker Marker;
        public AppearanceData Appearance = new AppearanceData();
        public Vec3 Position;
        public Vec3 PreviousPosition;
        public float Facing;
        public int Health, MaxHealth, Power, MaxPower;
        public UnitFlags Flags;
        public int TargetId;
        public double LastUpdate;
        public List<AuraData> Auras = new List<AuraData>();
        public S_CastStart? Casting;
        public double CastStartedAt;

        public bool IsDead => (Flags & (UnitFlags.Dead | UnitFlags.Ghost)) != 0;
        public bool IsBot => (Flags & UnitFlags.Bot) != 0;
        public float HealthPct => MaxHealth <= 0 ? 0 : Health / (float)MaxHealth;

        public void Apply(EntitySnapshotData s, double now)
        {
            PreviousPosition = Position;
            Position = s.Position;
            Facing = s.Facing;
            Health = s.Health;
            MaxHealth = s.MaxHealth;
            Power = s.Power;
            MaxPower = s.MaxPower;
            Flags = (UnitFlags)s.Flags;
            TargetId = s.TargetId;
            LastUpdate = now;
        }
    }

    /// <summary>Spiegel der Welt auf Clientseite: sichtbare Einheiten, eigene Werte, Quests, Inventar.</summary>
    public sealed class ClientWorldState
    {
        public Dictionary<int, ClientEntity> Entities { get; } = new Dictionary<int, ClientEntity>();
        public int MyId;
        public S_PlayerState? Stats;
        public S_Inventory? Inventory;
        public S_QuestLog? QuestLog;
        public S_Talents? Talents;
        public S_WorldState? World;
        public DeathState DeathState;
        public Vec3 CorpsePosition;

        public ClientEntity? Get(int id) => Entities.TryGetValue(id, out var e) ? e : null;
        public ClientEntity? Me => Get(MyId);

        public void Clear()
        {
            Entities.Clear();
        }

        public void Spawn(S_EntitySpawn m, double now)
        {
            if (!Entities.TryGetValue(m.Id, out var e)) Entities[m.Id] = e = new ClientEntity { Id = m.Id };
            e.Kind = m.Kind; e.TemplateId = m.TemplateId; e.Name = m.Name; e.Title = m.Title; e.Race = m.Race; e.Class = m.Class; e.Gender = m.Gender;
            e.Level = m.Level; e.Faction = m.Faction; e.Roles = (NpcRole)m.NpcRoles; e.PowerType = m.PowerType; e.Marker = m.Marker; e.Appearance = m.Appearance;
            e.Apply(m.State, now);
            e.PreviousPosition = e.Position;
        }

        public void Snapshot(S_Snapshot s, double now)
        {
            foreach (var d in s.Entities)
                if (Entities.TryGetValue(d.Id, out var e)) e.Apply(d, now);
        }
    }
}
