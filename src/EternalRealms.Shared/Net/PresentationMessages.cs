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
}
