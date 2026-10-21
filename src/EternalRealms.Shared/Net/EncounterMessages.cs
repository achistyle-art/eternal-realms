using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    /// <summary>
    /// Bodeneffekt eines Bosses (Kapitel 43/44): erst Vorwarnung (<see cref="Armed"/> = false), dann wirksam.
    /// <see cref="Removed"/> entfernt ihn. Der Client zeichnet den Kreis und warnt, wenn man darin steht.
    /// </summary>
    public sealed class S_GroundEffect : Message
    {
        public override Opcode Op => Opcode.S_GroundEffect;
        public int Id;
        public int SourceId;
        public string Name = "";
        public Vec3 Position;
        public float Radius;
        /// <summary>Sekunden bis zur Wirkung (Vorwarnung) bzw. bis die Fläche verschwindet.</summary>
        public float Remaining;
        public bool Armed, Removed;
        public override void Write(NetDataWriter w)
        {
            w.Put(Id); w.Put(SourceId); w.Put(Name); w.Put(Position.X); w.Put(Position.Y); w.Put(Position.Z); w.Put(Radius); w.Put(Remaining); w.Put(Armed); w.Put(Removed);
        }
        public override void Read(NetDataReader r)
        {
            Id = r.GetInt(); SourceId = r.GetInt(); Name = r.GetString(); Position = new Vec3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            Radius = r.GetFloat(); Remaining = r.GetFloat(); Armed = r.GetBool(); Removed = r.GetBool();
        }
    }

    public enum EncounterState : byte { Running = 0, Won, Wiped }

    /// <summary>Bosskampf-Anzeige: Phase, Kampfzeit und Zeit bis zur ungezügelten Wut.</summary>
    public sealed class S_Encounter : Message
    {
        public override Opcode Op => Opcode.S_Encounter;
        public int BossId;
        public string Name = "", Phase = "";
        public EncounterState State;
        public float Elapsed;
        /// <summary>Sekunden bis zur Wut (negativ = keine; 0 = bereits wütend).</summary>
        public float EnrageIn = -1;
        public byte Deaths;
        public override void Write(NetDataWriter w) { w.Put(BossId); w.Put(Name); w.Put(Phase); w.Put((byte)State); w.Put(Elapsed); w.Put(EnrageIn); w.Put(Deaths); }
        public override void Read(NetDataReader r)
        {
            BossId = r.GetInt(); Name = r.GetString(); Phase = r.GetString(); State = (EncounterState)r.GetByte(); Elapsed = r.GetFloat(); EnrageIn = r.GetFloat(); Deaths = r.GetByte();
        }
    }
}
