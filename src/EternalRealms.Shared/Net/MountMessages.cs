using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    /// <summary>Aufsitzen (leer = zuletzt benutztes bzw. schnellstes Reittier).</summary>
    public sealed class C_Mount : Message
    {
        public override Opcode Op => Opcode.C_Mount;
        public string MountId = "";
        public override void Write(NetDataWriter w) => w.Put(MountId);
        public override void Read(NetDataReader r) => MountId = r.GetString();
    }

    public sealed class C_Dismount : Message
    {
        public override Opcode Op => Opcode.C_Dismount;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    /// <summary>Reiten lernen beim Stallmeister (nächste Stufe).</summary>
    public sealed class C_LearnRiding : Message
    {
        public override Opcode Op => Opcode.C_LearnRiding;
        public int NpcId;
        public override void Write(NetDataWriter w) => w.Put(NpcId);
        public override void Read(NetDataReader r) => NpcId = r.GetInt();
    }

    /// <summary>Reittier-Sammlung: Reitfertigkeit (0, 75, 150) und bekannte Reittiere.</summary>
    public sealed class S_MountJournal : Message
    {
        public override Opcode Op => Opcode.S_MountJournal;
        public int RidingSkill;
        public List<string> Mounts = new List<string>();
        public string Active = "";
        public override void Write(NetDataWriter w) { w.Put(RidingSkill); w.PutStrings(Mounts); w.Put(Active); }
        public override void Read(NetDataReader r) { RidingSkill = r.GetInt(); Mounts = r.GetStrings(); Active = r.GetString(); }
    }
}
