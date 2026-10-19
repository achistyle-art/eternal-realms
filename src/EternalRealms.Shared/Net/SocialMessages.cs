using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    /// <summary>Nachricht mit nur einem Text (Name, Nachricht des Tages …).</summary>
    public abstract class TextMessage : Message
    {
        public string Text = "";
        public override void Write(NetDataWriter w) => w.Put(Text);
        public override void Read(NetDataReader r) => Text = r.GetString();
    }

    /// <summary>Nachricht mit nur einer Charakter-ID.</summary>
    public abstract class IdMessage : Message
    {
        public long CharacterId;
        public override void Write(NetDataWriter w) => w.Put(CharacterId);
        public override void Read(NetDataReader r) => CharacterId = r.GetLong();
    }

    public sealed class C_GuildCreate : TextMessage { public override Opcode Op => Opcode.C_GuildCreate; }
    public sealed class C_GuildInvite : TextMessage { public override Opcode Op => Opcode.C_GuildInvite; }
    public sealed class C_GuildMotd : TextMessage { public override Opcode Op => Opcode.C_GuildMotd; }
    public sealed class C_GuildKick : IdMessage { public override Opcode Op => Opcode.C_GuildKick; }
    public sealed class C_FriendAdd : TextMessage { public override Opcode Op => Opcode.C_FriendAdd; }
    public sealed class C_FriendRemove : IdMessage { public override Opcode Op => Opcode.C_FriendRemove; }
    public sealed class C_IgnoreAdd : TextMessage { public override Opcode Op => Opcode.C_IgnoreAdd; }
    public sealed class C_IgnoreRemove : IdMessage { public override Opcode Op => Opcode.C_IgnoreRemove; }
    /// <summary>Gespeicherten KI-Begleiter (Favorit) in die Gruppe rufen.</summary>
    public sealed class C_InviteFavorite : IdMessage { public override Opcode Op => Opcode.C_InviteFavorite; }

    public sealed class C_GuildRespond : Message
    {
        public override Opcode Op => Opcode.C_GuildRespond;
        public bool Accept;
        public override void Write(NetDataWriter w) => w.Put(Accept);
        public override void Read(NetDataReader r) => Accept = r.GetBool();
    }

    public sealed class C_GuildLeave : Message
    {
        public override Opcode Op => Opcode.C_GuildLeave;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_GuildDisband : Message
    {
        public override Opcode Op => Opcode.C_GuildDisband;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_GuildRoster : Message
    {
        public override Opcode Op => Opcode.C_GuildRoster;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_FriendList : Message
    {
        public override Opcode Op => Opcode.C_FriendList;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    /// <summary>Rang setzen (0 = Gildenmeister übergeben).</summary>
    public sealed class C_GuildSetRank : Message
    {
        public override Opcode Op => Opcode.C_GuildSetRank;
        public long CharacterId;
        public int Rank;
        public override void Write(NetDataWriter w) { w.Put(CharacterId); w.Put(Rank); }
        public override void Read(NetDataReader r) { CharacterId = r.GetLong(); Rank = r.GetInt(); }
    }

    public sealed class C_ReportPlayer : Message
    {
        public override Opcode Op => Opcode.C_ReportPlayer;
        public string Name = "", Reason = "", Text = "";
        public override void Write(NetDataWriter w) { w.Put(Name); w.Put(Reason); w.Put(Text); }
        public override void Read(NetDataReader r) { Name = r.GetString(); Reason = r.GetString(); Text = r.GetString(); }
    }

    public sealed class C_ShareLocation : Message
    {
        public override Opcode Op => Opcode.C_ShareLocation;
        public bool Share;
        public override void Write(NetDataWriter w) => w.Put(Share);
        public override void Read(NetDataReader r) => Share = r.GetBool();
    }

    public sealed class S_GuildInvite : Message
    {
        public override Opcode Op => Opcode.S_GuildInvite;
        public string GuildName = "", From = "";
        public override void Write(NetDataWriter w) { w.Put(GuildName); w.Put(From); }
        public override void Read(NetDataReader r) { GuildName = r.GetString(); From = r.GetString(); }
    }

    public sealed class GuildMemberInfo
    {
        public long CharacterId;
        public string Name = "", Zone = "", Note = "";
        public int Level, Rank;
        public CharClass Class;
        public bool Online, IsBot;
        public void Write(NetDataWriter w) { w.Put(CharacterId); w.Put(Name); w.Put(Zone); w.Put(Note); w.Put(Level); w.Put(Rank); w.Put((byte)Class); w.Put(Online); w.Put(IsBot); }
        public static GuildMemberInfo Read(NetDataReader r) => new GuildMemberInfo
        {
            CharacterId = r.GetLong(), Name = r.GetString(), Zone = r.GetString(), Note = r.GetString(), Level = r.GetInt(), Rank = r.GetInt(),
            Class = (CharClass)r.GetByte(), Online = r.GetBool(), IsBot = r.GetBool(),
        };
    }

    /// <summary>Mitgliederliste der eigenen Gilde (leerer Name = keine Gilde).</summary>
    public sealed class S_GuildRoster : Message
    {
        public override Opcode Op => Opcode.S_GuildRoster;
        public long GuildId;
        public string Name = "", Motd = "";
        public List<string> RankNames = new List<string>();
        public List<GuildMemberInfo> Members = new List<GuildMemberInfo>();
        public override void Write(NetDataWriter w) { w.Put(GuildId); w.Put(Name); w.Put(Motd); w.PutStrings(RankNames); w.PutList(Members, (x, m) => m.Write(x)); }
        public override void Read(NetDataReader r) { GuildId = r.GetLong(); Name = r.GetString(); Motd = r.GetString(); RankNames = r.GetStrings(); Members = r.GetList(GuildMemberInfo.Read); }
    }

    /// <summary>Freund (Mensch oder gespeicherter KI-Begleiter).</summary>
    public sealed class FriendInfo
    {
        public long CharacterId;
        public string Name = "", Zone = "", Note = "";
        public int Level;
        public CharClass Class;
        public bool Online, IsBot;
        // Nur für KI-Begleiter (Kapitel 32):
        public Role Role;
        public int ItemLevel;
        public string Activity = "", Availability = "";
        public List<string> SharedAdventures = new List<string>();
        public void Write(NetDataWriter w)
        {
            w.Put(CharacterId); w.Put(Name); w.Put(Zone); w.Put(Note); w.Put(Level); w.Put((byte)Class); w.Put(Online); w.Put(IsBot);
            w.Put((byte)Role); w.Put(ItemLevel); w.Put(Activity); w.Put(Availability); w.PutStrings(SharedAdventures);
        }
        public static FriendInfo Read(NetDataReader r) => new FriendInfo
        {
            CharacterId = r.GetLong(), Name = r.GetString(), Zone = r.GetString(), Note = r.GetString(), Level = r.GetInt(), Class = (CharClass)r.GetByte(),
            Online = r.GetBool(), IsBot = r.GetBool(), Role = (Role)r.GetByte(), ItemLevel = r.GetInt(), Activity = r.GetString(), Availability = r.GetString(),
            SharedAdventures = r.GetStrings(),
        };
    }

    public sealed class S_FriendList : Message
    {
        public override Opcode Op => Opcode.S_FriendList;
        public List<FriendInfo> Friends = new List<FriendInfo>();
        public List<FriendInfo> Companions = new List<FriendInfo>();
        public List<string> Ignored = new List<string>();
        public List<long> IgnoredIds = new List<long>();
        public bool ShareLocation;
        public override void Write(NetDataWriter w)
        {
            w.PutList(Friends, (x, f) => f.Write(x)); w.PutList(Companions, (x, f) => f.Write(x)); w.PutStrings(Ignored); w.PutList(IgnoredIds, (x, i) => x.Put(i)); w.Put(ShareLocation);
        }
        public override void Read(NetDataReader r)
        {
            Friends = r.GetList(FriendInfo.Read); Companions = r.GetList(FriendInfo.Read); Ignored = r.GetStrings(); IgnoredIds = r.GetList(x => x.GetLong()); ShareLocation = r.GetBool();
        }
    }
}
