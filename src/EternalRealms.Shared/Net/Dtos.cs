using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    /// <summary>Gewähltes Aussehen aus dem Charaktereditor.</summary>
    public sealed class AppearanceData
    {
        public byte SkinColor, Face, HairStyle, HairColor, Beard, EyeColor, Scar, Tattoo;
        public float Height = 1f;

        public void Write(NetDataWriter w)
        {
            w.Put(SkinColor); w.Put(Face); w.Put(HairStyle); w.Put(HairColor); w.Put(Beard); w.Put(EyeColor); w.Put(Scar); w.Put(Tattoo); w.Put(Height);
        }

        public static AppearanceData Read(NetDataReader r) => new AppearanceData
        {
            SkinColor = r.GetByte(), Face = r.GetByte(), HairStyle = r.GetByte(), HairColor = r.GetByte(), Beard = r.GetByte(),
            EyeColor = r.GetByte(), Scar = r.GetByte(), Tattoo = r.GetByte(), Height = r.GetFloat(),
        };

        public AppearanceData Clone() => (AppearanceData)MemberwiseClone();
    }

    public sealed class CharacterSummary
    {
        public long Id;
        public string Name = "";
        public Race Race;
        public CharClass Class;
        public Gender Gender;
        public int Level;
        public string ZoneId = "";
        public AppearanceData Appearance = new AppearanceData();

        public void Write(NetDataWriter w)
        {
            w.Put(Id); w.Put(Name); w.Put((byte)Race); w.Put((byte)Class); w.Put((byte)Gender); w.Put(Level); w.Put(ZoneId); Appearance.Write(w);
        }

        public static CharacterSummary Read(NetDataReader r) => new CharacterSummary
        {
            Id = r.GetLong(), Name = r.GetString(), Race = (Race)r.GetByte(), Class = (CharClass)r.GetByte(), Gender = (Gender)r.GetByte(),
            Level = r.GetInt(), ZoneId = r.GetString(), Appearance = AppearanceData.Read(r),
        };
    }

    public sealed class ItemStackData
    {
        public string ItemId = "";
        public int Count = 1;
        public int Durability;

        public void Write(NetDataWriter w) { w.Put(ItemId); w.Put(Count); w.Put(Durability); }
        public static ItemStackData Read(NetDataReader r) => new ItemStackData { ItemId = r.GetString(), Count = r.GetInt(), Durability = r.GetInt() };
    }

    public sealed class QuestProgressData
    {
        public string QuestId = "";
        public QuestState State;
        public List<int> Counts = new List<int>();

        public void Write(NetDataWriter w) { w.Put(QuestId); w.Put((byte)State); w.PutList(Counts, (x, c) => x.Put(c)); }
        public static QuestProgressData Read(NetDataReader r) => new QuestProgressData
        {
            QuestId = r.GetString(), State = (QuestState)r.GetByte(), Counts = r.GetList(x => x.GetInt()),
        };
    }

    public sealed class GroupMemberData
    {
        public int EntityId;
        public string Name = "";
        public CharClass Class;
        public int Level;
        public int Health, MaxHealth, Power, MaxPower;
        public PowerType PowerType;
        public bool IsBot;
        public Role Role;
        public bool Online = true;

        public void Write(NetDataWriter w)
        {
            w.Put(EntityId); w.Put(Name); w.Put((byte)Class); w.Put(Level); w.Put(Health); w.Put(MaxHealth); w.Put(Power); w.Put(MaxPower);
            w.Put((byte)PowerType); w.Put(IsBot); w.Put((byte)Role); w.Put(Online);
        }

        public static GroupMemberData Read(NetDataReader r) => new GroupMemberData
        {
            EntityId = r.GetInt(), Name = r.GetString(), Class = (CharClass)r.GetByte(), Level = r.GetInt(), Health = r.GetInt(), MaxHealth = r.GetInt(),
            Power = r.GetInt(), MaxPower = r.GetInt(), PowerType = (PowerType)r.GetByte(), IsBot = r.GetBool(), Role = (Role)r.GetByte(), Online = r.GetBool(),
        };
    }

    public sealed class BotCandidateData
    {
        public long CandidateId;
        public string Name = "";
        public Race Race;
        public CharClass Class;
        public int Level;
        public Role Role;
        public string Personality = "";
        public string Greeting = "";

        public void Write(NetDataWriter w)
        {
            w.Put(CandidateId); w.Put(Name); w.Put((byte)Race); w.Put((byte)Class); w.Put(Level); w.Put((byte)Role); w.Put(Personality); w.Put(Greeting);
        }

        public static BotCandidateData Read(NetDataReader r) => new BotCandidateData
        {
            CandidateId = r.GetLong(), Name = r.GetString(), Race = (Race)r.GetByte(), Class = (CharClass)r.GetByte(), Level = r.GetInt(),
            Role = (Role)r.GetByte(), Personality = r.GetString(), Greeting = r.GetString(),
        };
    }

    public sealed class EntitySnapshotData
    {
        public int Id;
        public Vec3 Position;
        public float Facing;
        public int Health, MaxHealth, Power, MaxPower;
        public uint Flags;
        public int TargetId;

        public void Write(NetDataWriter w)
        {
            w.Put(Id); w.Put(Position); w.Put(Facing); w.Put(Health); w.Put(MaxHealth); w.Put(Power); w.Put(MaxPower); w.Put(Flags); w.Put(TargetId);
        }

        public static EntitySnapshotData Read(NetDataReader r) => new EntitySnapshotData
        {
            Id = r.GetInt(), Position = r.GetVec3(), Facing = r.GetFloat(), Health = r.GetInt(), MaxHealth = r.GetInt(),
            Power = r.GetInt(), MaxPower = r.GetInt(), Flags = r.GetUInt(), TargetId = r.GetInt(),
        };
    }

    public sealed class AuraData
    {
        public string AuraId = "";
        public int Stacks;
        public float Remaining;
        public int CasterId;

        public void Write(NetDataWriter w) { w.Put(AuraId); w.Put(Stacks); w.Put(Remaining); w.Put(CasterId); }
        public static AuraData Read(NetDataReader r) => new AuraData { AuraId = r.GetString(), Stacks = r.GetInt(), Remaining = r.GetFloat(), CasterId = r.GetInt() };
    }
}
