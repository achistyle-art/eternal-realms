using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    /// <summary>Emote ausführen (Kapitel 33); TargetId 0 = ohne Ziel.</summary>
    public sealed class C_Emote : Message
    {
        public override Opcode Op => Opcode.C_Emote;
        public string EmoteId = "";
        public int TargetId;
        public override void Write(NetDataWriter w) { w.Put(EmoteId); w.Put(TargetId); }
        public override void Read(NetDataReader r) { EmoteId = r.GetString(); TargetId = r.GetInt(); }
    }

    /// <summary>Jemand in der Nähe führt ein Emote aus – für Animation und Klang; der Text kommt als Emote-Chat.</summary>
    public sealed class S_Emote : Message
    {
        public override Opcode Op => Opcode.S_Emote;
        public int SourceId, TargetId;
        public string EmoteId = "";
        public override void Write(NetDataWriter w) { w.Put(SourceId); w.Put(TargetId); w.Put(EmoteId); }
        public override void Read(NetDataReader r) { SourceId = r.GetInt(); TargetId = r.GetInt(); EmoteId = r.GetString(); }
    }

    public enum WorldEventState : byte { Announced = 0, Running = 1, Success = 2, Failed = 3 }

    /// <summary>Stand eines Weltereignisses in der Region (Kapitel 37): für Tracker, Karte und Ansage.</summary>
    public sealed class S_WorldEvent : Message
    {
        public override Opcode Op => Opcode.S_WorldEvent;
        public string EventId = "";
        public string Name = "";
        public WorldEventState State;
        public string Text = "";
        public int Progress, Goal;
        public float SecondsLeft;
        public Vec3 Position;
        public float Radius;
        public int Participants;
        /// <summary>Gehört der Empfänger zu den Beteiligten (bekommt die Belohnung)?</summary>
        public bool Participating;
        public override void Write(NetDataWriter w)
        {
            w.Put(EventId); w.Put(Name); w.Put((byte)State); w.Put(Text); w.Put(Progress); w.Put(Goal); w.Put(SecondsLeft);
            w.Put(Position.X); w.Put(Position.Y); w.Put(Position.Z); w.Put(Radius); w.Put(Participants); w.Put(Participating);
        }
        public override void Read(NetDataReader r)
        {
            EventId = r.GetString(); Name = r.GetString(); State = (WorldEventState)r.GetByte(); Text = r.GetString(); Progress = r.GetInt(); Goal = r.GetInt(); SecondsLeft = r.GetFloat();
            Position = new Vec3(r.GetFloat(), r.GetFloat(), r.GetFloat()); Radius = r.GetFloat(); Participants = r.GetInt(); Participating = r.GetBool();
        }
    }
}
