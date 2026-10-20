using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    /// <summary>Wie der Gruppenfinder KI-Mitspieler einsetzt (Kapitel 9.5).</summary>
    public enum FinderAiMode : byte
    {
        /// <summary>Nur echte Spieler.</summary>
        HumansOnly = 0,
        /// <summary>Echte Spieler bevorzugt; nach kurzer Wartezeit füllen KI-Mitspieler auf.</summary>
        PreferHumans = 1,
        /// <summary>Sofort mit KI-Mitspielern auffüllen.</summary>
        FillWithAi = 2,
    }

    public sealed class FinderActivityData
    {
        public string Id = "", Name = "", Kind = "";
        public int MinLevel, MaxLevel, Size;
        public void Write(NetDataWriter w) { w.Put(Id); w.Put(Name); w.Put(Kind); w.Put(MinLevel); w.Put(MaxLevel); w.Put(Size); }
        public static FinderActivityData Read(NetDataReader r) => new FinderActivityData { Id = r.GetString(), Name = r.GetString(), Kind = r.GetString(), MinLevel = r.GetInt(), MaxLevel = r.GetInt(), Size = r.GetInt() };
    }

    public sealed class C_FinderActivities : Message
    {
        public override Opcode Op => Opcode.C_FinderActivities;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class S_FinderActivities : Message
    {
        public override Opcode Op => Opcode.S_FinderActivities;
        public List<FinderActivityData> Activities = new List<FinderActivityData>();
        public override void Write(NetDataWriter w) => w.PutList(Activities, (x, a) => a.Write(x));
        public override void Read(NetDataReader r) => Activities = r.GetList(FinderActivityData.Read);
    }

    /// <summary>Anmelden (allein oder als Gruppenanführer für die ganze Gruppe).</summary>
    public sealed class C_FinderJoin : Message
    {
        public override Opcode Op => Opcode.C_FinderJoin;
        public List<string> Activities = new List<string>();
        public List<byte> Roles = new List<byte>();
        public FinderAiMode Mode = FinderAiMode.PreferHumans;
        public override void Write(NetDataWriter w) { w.PutStrings(Activities); w.PutList(Roles, (x, b) => x.Put(b)); w.Put((byte)Mode); }
        public override void Read(NetDataReader r) { Activities = r.GetStrings(); Roles = r.GetList(x => x.GetByte()); Mode = (FinderAiMode)r.GetByte(); }
    }

    public sealed class C_FinderLeave : Message
    {
        public override Opcode Op => Opcode.C_FinderLeave;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class S_FinderStatus : Message
    {
        public override Opcode Op => Opcode.S_FinderStatus;
        public bool Queued;
        public List<string> Activities = new List<string>();
        public float WaitSeconds;
        public string Text = "";
        public override void Write(NetDataWriter w) { w.Put(Queued); w.PutStrings(Activities); w.Put(WaitSeconds); w.Put(Text); }
        public override void Read(NetDataReader r) { Queued = r.GetBool(); Activities = r.GetStrings(); WaitSeconds = r.GetFloat(); Text = r.GetString(); }
    }

    /// <summary>Gruppenvorschlag: Bestätigen innerhalb der Frist (Bereitschaft vor dem Start).</summary>
    public sealed class S_FinderProposal : Message
    {
        public override Opcode Op => Opcode.S_FinderProposal;
        public int ProposalId;
        public string Activity = "";
        public Role Role;
        public float Seconds;
        public List<string> Members = new List<string>();
        public bool IsReplacement;
        public override void Write(NetDataWriter w) { w.Put(ProposalId); w.Put(Activity); w.Put((byte)Role); w.Put(Seconds); w.PutStrings(Members); w.Put(IsReplacement); }
        public override void Read(NetDataReader r) { ProposalId = r.GetInt(); Activity = r.GetString(); Role = (Role)r.GetByte(); Seconds = r.GetFloat(); Members = r.GetStrings(); IsReplacement = r.GetBool(); }
    }

    public sealed class C_FinderAccept : Message
    {
        public override Opcode Op => Opcode.C_FinderAccept;
        public int ProposalId;
        public bool Accept;
        public override void Write(NetDataWriter w) { w.Put(ProposalId); w.Put(Accept); }
        public override void Read(NetDataReader r) { ProposalId = r.GetInt(); Accept = r.GetBool(); }
    }

    public sealed class C_ReadyCheck : Message
    {
        public override Opcode Op => Opcode.C_ReadyCheck;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class S_ReadyCheck : Message
    {
        public override Opcode Op => Opcode.S_ReadyCheck;
        public string From = "";
        public float Seconds;
        public override void Write(NetDataWriter w) { w.Put(From); w.Put(Seconds); }
        public override void Read(NetDataReader r) { From = r.GetString(); Seconds = r.GetFloat(); }
    }

    public sealed class C_ReadyResponse : Message
    {
        public override Opcode Op => Opcode.C_ReadyResponse;
        public bool Ready;
        public override void Write(NetDataWriter w) => w.Put(Ready);
        public override void Read(NetDataReader r) => Ready = r.GetBool();
    }

    public sealed class S_ReadyCheckResult : Message
    {
        public override Opcode Op => Opcode.S_ReadyCheckResult;
        public List<string> Ready = new List<string>(), NotReady = new List<string>();
        public override void Write(NetDataWriter w) { w.PutStrings(Ready); w.PutStrings(NotReady); }
        public override void Read(NetDataReader r) { Ready = r.GetStrings(); NotReady = r.GetStrings(); }
    }

    public sealed class C_VoteKick : Message
    {
        public override Opcode Op => Opcode.C_VoteKick;
        public int TargetId;
        public string Reason = "";
        public override void Write(NetDataWriter w) { w.Put(TargetId); w.Put(Reason); }
        public override void Read(NetDataReader r) { TargetId = r.GetInt(); Reason = r.GetString(); }
    }

    public sealed class S_VoteKick : Message
    {
        public override Opcode Op => Opcode.S_VoteKick;
        public int VoteId;
        public string TargetName = "", Reason = "";
        public float Seconds;
        public override void Write(NetDataWriter w) { w.Put(VoteId); w.Put(TargetName); w.Put(Reason); w.Put(Seconds); }
        public override void Read(NetDataReader r) { VoteId = r.GetInt(); TargetName = r.GetString(); Reason = r.GetString(); Seconds = r.GetFloat(); }
    }

    public sealed class C_VoteKickResponse : Message
    {
        public override Opcode Op => Opcode.C_VoteKickResponse;
        public int VoteId;
        public bool Yes;
        public override void Write(NetDataWriter w) { w.Put(VoteId); w.Put(Yes); }
        public override void Read(NetDataReader r) { VoteId = r.GetInt(); Yes = r.GetBool(); }
    }

    public sealed class C_SetRole : Message
    {
        public override Opcode Op => Opcode.C_SetRole;
        public Role Role;
        public override void Write(NetDataWriter w) => w.Put((byte)Role);
        public override void Read(NetDataReader r) => Role = (Role)r.GetByte();
    }

    public sealed class C_SetBotRole : Message
    {
        public override Opcode Op => Opcode.C_SetBotRole;
        public int BotEntityId;
        public Role Role;
        public override void Write(NetDataWriter w) { w.Put(BotEntityId); w.Put((byte)Role); }
        public override void Read(NetDataReader r) { BotEntityId = r.GetInt(); Role = (Role)r.GetByte(); }
    }

    public sealed class C_ConvertToRaid : Message
    {
        public override Opcode Op => Opcode.C_ConvertToRaid;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_SetSubgroup : Message
    {
        public override Opcode Op => Opcode.C_SetSubgroup;
        public int EntityId;
        public byte Subgroup;
        public override void Write(NetDataWriter w) { w.Put(EntityId); w.Put(Subgroup); }
        public override void Read(NetDataReader r) { EntityId = r.GetInt(); Subgroup = r.GetByte(); }
    }
}
