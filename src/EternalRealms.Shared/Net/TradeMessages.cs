using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    // Direkter Spielerhandel (Kapitel 46). Der Server führt den Tausch erst aus, wenn beide denselben Stand bestätigt haben;
    // jede Änderung hebt beide Bestätigungen auf.

    public sealed class C_TradeRequest : Message
    {
        public override Opcode Op => Opcode.C_TradeRequest;
        public int TargetId;
        public override void Write(NetDataWriter w) => w.Put(TargetId);
        public override void Read(NetDataReader r) => TargetId = r.GetInt();
    }

    public sealed class C_TradeRespond : Message
    {
        public override Opcode Op => Opcode.C_TradeRespond;
        public bool Accept;
        public override void Write(NetDataWriter w) => w.Put(Accept);
        public override void Read(NetDataReader r) => Accept = r.GetBool();
    }

    /// <summary>Handelsplatz (0–5) mit einem Taschenplatz belegen; BagIndex −1 leert ihn.</summary>
    public sealed class C_TradeSetItem : Message
    {
        public override Opcode Op => Opcode.C_TradeSetItem;
        public int Slot, BagIndex;
        public override void Write(NetDataWriter w) { w.Put(Slot); w.Put(BagIndex); }
        public override void Read(NetDataReader r) { Slot = r.GetInt(); BagIndex = r.GetInt(); }
    }

    public sealed class C_TradeSetMoney : Message
    {
        public override Opcode Op => Opcode.C_TradeSetMoney;
        public long Money;
        public override void Write(NetDataWriter w) => w.Put(Money);
        public override void Read(NetDataReader r) => Money = r.GetLong();
    }

    /// <summary>Handel bestätigen (true) oder Bestätigung zurücknehmen (false). Version muss dem zuletzt gesehenen Stand entsprechen.</summary>
    public sealed class C_TradeAccept : Message
    {
        public override Opcode Op => Opcode.C_TradeAccept;
        public bool Accept;
        public int Version;
        public override void Write(NetDataWriter w) { w.Put(Accept); w.Put(Version); }
        public override void Read(NetDataReader r) { Accept = r.GetBool(); Version = r.GetInt(); }
    }

    public sealed class C_TradeCancel : Message
    {
        public override Opcode Op => Opcode.C_TradeCancel;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class S_TradeRequest : Message
    {
        public override Opcode Op => Opcode.S_TradeRequest;
        public int FromId;
        public string FromName = "";
        public override void Write(NetDataWriter w) { w.Put(FromId); w.Put(FromName); }
        public override void Read(NetDataReader r) { FromId = r.GetInt(); FromName = r.GetString(); }
    }

    public sealed class S_TradeState : Message
    {
        public const int Slots = 6;
        public override Opcode Op => Opcode.S_TradeState;
        /// <summary>Steigt mit jeder Änderung; eine Bestätigung gilt nur für genau diesen Stand.</summary>
        public int Version;
        public int PartnerId;
        public string PartnerName = "";
        public bool PartnerIsBot;
        public List<ItemStackData> MyItems = new List<ItemStackData>();
        /// <summary>Taschenplatz je eigenem Handelsplatz (−1 = leer), damit die Oberfläche die Gegenstände markieren kann.</summary>
        public List<int> MyBagIndices = new List<int>();
        public List<ItemStackData> TheirItems = new List<ItemStackData>();
        public long MyMoney, TheirMoney;
        public bool MyAccepted, TheirAccepted;
        public override void Write(NetDataWriter w)
        {
            w.Put(Version); w.Put(PartnerId); w.Put(PartnerName); w.Put(PartnerIsBot);
            w.PutList(MyItems, (x, s) => s.Write(x)); w.PutList(MyBagIndices, (x, v) => x.Put(v)); w.PutList(TheirItems, (x, s) => s.Write(x));
            w.Put(MyMoney); w.Put(TheirMoney); w.Put(MyAccepted); w.Put(TheirAccepted);
        }
        public override void Read(NetDataReader r)
        {
            Version = r.GetInt(); PartnerId = r.GetInt(); PartnerName = r.GetString(); PartnerIsBot = r.GetBool();
            MyItems = r.GetList(ItemStackData.Read); MyBagIndices = r.GetList(x => x.GetInt()); TheirItems = r.GetList(ItemStackData.Read);
            MyMoney = r.GetLong(); TheirMoney = r.GetLong(); MyAccepted = r.GetBool(); TheirAccepted = r.GetBool();
        }
    }

    public sealed class S_TradeClosed : Message
    {
        public override Opcode Op => Opcode.S_TradeClosed;
        public bool Completed;
        public string Reason = "";
        public override void Write(NetDataWriter w) { w.Put(Completed); w.Put(Reason); }
        public override void Read(NetDataReader r) { Completed = r.GetBool(); Reason = r.GetString(); }
    }

    /// <summary>Öffentlichen Kanal betreten oder verlassen (Gebiet, Handel, Gruppensuche).</summary>
    public sealed class C_ChatJoin : Message
    {
        public override Opcode Op => Opcode.C_ChatJoin;
        public ChatChannel Channel;
        public bool Join;
        public override void Write(NetDataWriter w) { w.Put((byte)Channel); w.Put(Join); }
        public override void Read(NetDataReader r) { Channel = (ChatChannel)r.GetByte(); Join = r.GetBool(); }
    }

    public sealed class S_ChatChannels : Message
    {
        public override Opcode Op => Opcode.S_ChatChannels;
        public List<byte> Joined = new List<byte>();
        public override void Write(NetDataWriter w) => w.PutList(Joined, (x, b) => x.Put(b));
        public override void Read(NetDataReader r) => Joined = r.GetList(x => x.GetByte());
    }
}
