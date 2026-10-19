using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    // ======================================================================= Inventar und Bank

    /// <summary>Verschiebt einen Stapel auf einen anderen Platz (tauschen oder zusammenlegen).</summary>
    public sealed class C_MoveItem : Message
    {
        public override Opcode Op => Opcode.C_MoveItem;
        public int From, To;
        public override void Write(NetDataWriter w) { w.Put(From); w.Put(To); }
        public override void Read(NetDataReader r) { From = r.GetInt(); To = r.GetInt(); }
    }

    /// <summary>Teilt Count Exemplare in einen freien Platz ab.</summary>
    public sealed class C_SplitStack : Message
    {
        public override Opcode Op => Opcode.C_SplitStack;
        public int From, Count;
        public override void Write(NetDataWriter w) { w.Put(From); w.Put(Count); }
        public override void Read(NetDataReader r) { From = r.GetInt(); Count = r.GetInt(); }
    }

    public sealed class C_SortBags : Message
    {
        public override Opcode Op => Opcode.C_SortBags;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_EquipBag : Message
    {
        public override Opcode Op => Opcode.C_EquipBag;
        public int BagIndex;
        public override void Write(NetDataWriter w) => w.Put(BagIndex);
        public override void Read(NetDataReader r) => BagIndex = r.GetInt();
    }

    public sealed class C_UnequipBag : Message
    {
        public override Opcode Op => Opcode.C_UnequipBag;
        public int ContainerSlot;
        public override void Write(NetDataWriter w) => w.Put(ContainerSlot);
        public override void Read(NetDataReader r) => ContainerSlot = r.GetInt();
    }

    public sealed class C_DestroyItem : Message
    {
        public override Opcode Op => Opcode.C_DestroyItem;
        public int BagIndex;
        public override void Write(NetDataWriter w) => w.Put(BagIndex);
        public override void Read(NetDataReader r) => BagIndex = r.GetInt();
    }

    public sealed class C_BankDeposit : Message
    {
        public override Opcode Op => Opcode.C_BankDeposit;
        public int NpcId, BagIndex;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(BagIndex); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); BagIndex = r.GetInt(); }
    }

    public sealed class C_BankWithdraw : Message
    {
        public override Opcode Op => Opcode.C_BankWithdraw;
        public int NpcId, BankIndex;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(BankIndex); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); BankIndex = r.GetInt(); }
    }

    public sealed class S_Bank : Message
    {
        public override Opcode Op => Opcode.S_Bank;
        public int NpcId;
        public List<ItemStackData> Slots = new List<ItemStackData>();
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.PutList(Slots, (x, s) => s.Write(x)); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); Slots = r.GetList(ItemStackData.Read); }
    }

    // ======================================================================= Beuteverteilung

    public enum LootMethod : byte { FreeForAll = 0, RoundRobin = 1, GroupLoot = 2, NeedBeforeGreed = 3, MasterLooter = 4 }

    public enum RollChoice : byte { Pass = 0, Need = 1, Greed = 2 }

    public sealed class C_SetLootMethod : Message
    {
        public override Opcode Op => Opcode.C_SetLootMethod;
        public LootMethod Method;
        public ItemQuality Threshold = ItemQuality.Uncommon;
        public int MasterLooterId;
        public override void Write(NetDataWriter w) { w.Put((byte)Method); w.Put((byte)Threshold); w.Put(MasterLooterId); }
        public override void Read(NetDataReader r) { Method = (LootMethod)r.GetByte(); Threshold = (ItemQuality)r.GetByte(); MasterLooterId = r.GetInt(); }
    }

    public sealed class S_LootMethod : Message
    {
        public override Opcode Op => Opcode.S_LootMethod;
        public LootMethod Method;
        public ItemQuality Threshold;
        public int MasterLooterId;
        public override void Write(NetDataWriter w) { w.Put((byte)Method); w.Put((byte)Threshold); w.Put(MasterLooterId); }
        public override void Read(NetDataReader r) { Method = (LootMethod)r.GetByte(); Threshold = (ItemQuality)r.GetByte(); MasterLooterId = r.GetInt(); }
    }

    public sealed class S_LootRollStart : Message
    {
        public override Opcode Op => Opcode.S_LootRollStart;
        public int RollId;
        public string ItemId = "";
        public int Count;
        public float Seconds;
        /// <summary>Darf dieser Spieler „Bedarf“ wählen (Gegenstand für seine Klasse nutzbar)?</summary>
        public bool CanNeed;
        public override void Write(NetDataWriter w) { w.Put(RollId); w.Put(ItemId); w.Put(Count); w.Put(Seconds); w.Put(CanNeed); }
        public override void Read(NetDataReader r) { RollId = r.GetInt(); ItemId = r.GetString(); Count = r.GetInt(); Seconds = r.GetFloat(); CanNeed = r.GetBool(); }
    }

    public sealed class C_LootRoll : Message
    {
        public override Opcode Op => Opcode.C_LootRoll;
        public int RollId;
        public RollChoice Choice;
        public override void Write(NetDataWriter w) { w.Put(RollId); w.Put((byte)Choice); }
        public override void Read(NetDataReader r) { RollId = r.GetInt(); Choice = (RollChoice)r.GetByte(); }
    }

    public sealed class LootRollEntryData
    {
        public string Name = "";
        public RollChoice Choice;
        public int Value;
        public bool IsBot;
        public void Write(NetDataWriter w) { w.Put(Name); w.Put((byte)Choice); w.Put(Value); w.Put(IsBot); }
        public static LootRollEntryData Read(NetDataReader r) => new LootRollEntryData { Name = r.GetString(), Choice = (RollChoice)r.GetByte(), Value = r.GetInt(), IsBot = r.GetBool() };
    }

    public sealed class S_LootRollResult : Message
    {
        public override Opcode Op => Opcode.S_LootRollResult;
        public int RollId;
        public string ItemId = "";
        public int WinnerId;
        public string WinnerName = "";
        public List<LootRollEntryData> Rolls = new List<LootRollEntryData>();
        public override void Write(NetDataWriter w) { w.Put(RollId); w.Put(ItemId); w.Put(WinnerId); w.Put(WinnerName); w.PutList(Rolls, (x, e) => e.Write(x)); }
        public override void Read(NetDataReader r) { RollId = r.GetInt(); ItemId = r.GetString(); WinnerId = r.GetInt(); WinnerName = r.GetString(); Rolls = r.GetList(LootRollEntryData.Read); }
    }

    /// <summary>Plündermeister verteilt einen Gegenstand einer Leiche an ein Gruppenmitglied.</summary>
    public sealed class C_MasterLoot : Message
    {
        public override Opcode Op => Opcode.C_MasterLoot;
        public int CorpseId;
        public string ItemId = "";
        public int TargetId;
        public override void Write(NetDataWriter w) { w.Put(CorpseId); w.Put(ItemId); w.Put(TargetId); }
        public override void Read(NetDataReader r) { CorpseId = r.GetInt(); ItemId = r.GetString(); TargetId = r.GetInt(); }
    }

    // ======================================================================= Post

    public sealed class MailSummaryData
    {
        public long Id;
        public string Sender = "", Subject = "", Body = "";
        public long Money, Cod;
        public List<ItemStackData> Items = new List<ItemStackData>();
        public float DaysLeft;
        public bool IsRead, Returned;
        public void Write(NetDataWriter w)
        {
            w.Put(Id); w.Put(Sender); w.Put(Subject); w.Put(Body); w.Put(Money); w.Put(Cod); w.PutList(Items, (x, s) => s.Write(x)); w.Put(DaysLeft); w.Put(IsRead); w.Put(Returned);
        }
        public static MailSummaryData Read(NetDataReader r) => new MailSummaryData
        {
            Id = r.GetLong(), Sender = r.GetString(), Subject = r.GetString(), Body = r.GetString(), Money = r.GetLong(), Cod = r.GetLong(),
            Items = r.GetList(ItemStackData.Read), DaysLeft = r.GetFloat(), IsRead = r.GetBool(), Returned = r.GetBool(),
        };
    }

    /// <summary>Postfach öffnen (ObjectId = Briefkasten in der Nähe).</summary>
    public sealed class C_MailList : Message
    {
        public override Opcode Op => Opcode.C_MailList;
        public int ObjectId;
        public override void Write(NetDataWriter w) => w.Put(ObjectId);
        public override void Read(NetDataReader r) => ObjectId = r.GetInt();
    }

    public sealed class S_MailList : Message
    {
        public override Opcode Op => Opcode.S_MailList;
        public List<MailSummaryData> Mails = new List<MailSummaryData>();
        public override void Write(NetDataWriter w) => w.PutList(Mails, (x, m) => m.Write(x));
        public override void Read(NetDataReader r) => Mails = r.GetList(MailSummaryData.Read);
    }

    public sealed class C_MailSend : Message
    {
        public override Opcode Op => Opcode.C_MailSend;
        public int ObjectId;
        public string To = "", Subject = "", Body = "";
        public long Money, Cod;
        public List<int> BagIndices = new List<int>();
        public override void Write(NetDataWriter w) { w.Put(ObjectId); w.Put(To); w.Put(Subject); w.Put(Body); w.Put(Money); w.Put(Cod); w.PutList(BagIndices, (x, i) => x.Put(i)); }
        public override void Read(NetDataReader r) { ObjectId = r.GetInt(); To = r.GetString(); Subject = r.GetString(); Body = r.GetString(); Money = r.GetLong(); Cod = r.GetLong(); BagIndices = r.GetList(x => x.GetInt()); }
    }

    /// <summary>Anhang entnehmen: Index −1 = Geld, 0..11 = Gegenstand, 255 = alles.</summary>
    public sealed class C_MailTake : Message
    {
        public override Opcode Op => Opcode.C_MailTake;
        public int ObjectId;
        public long MailId;
        public int Index = 255;
        public override void Write(NetDataWriter w) { w.Put(ObjectId); w.Put(MailId); w.Put(Index); }
        public override void Read(NetDataReader r) { ObjectId = r.GetInt(); MailId = r.GetLong(); Index = r.GetInt(); }
    }

    public sealed class C_MailDelete : Message
    {
        public override Opcode Op => Opcode.C_MailDelete;
        public int ObjectId;
        public long MailId;
        public override void Write(NetDataWriter w) { w.Put(ObjectId); w.Put(MailId); }
        public override void Read(NetDataReader r) { ObjectId = r.GetInt(); MailId = r.GetLong(); }
    }

    public sealed class C_MailReturn : Message
    {
        public override Opcode Op => Opcode.C_MailReturn;
        public int ObjectId;
        public long MailId;
        public override void Write(NetDataWriter w) { w.Put(ObjectId); w.Put(MailId); }
        public override void Read(NetDataReader r) { ObjectId = r.GetInt(); MailId = r.GetLong(); }
    }

    public sealed class S_NewMail : Message
    {
        public override Opcode Op => Opcode.S_NewMail;
        public int Unread;
        public override void Write(NetDataWriter w) => w.Put(Unread);
        public override void Read(NetDataReader r) => Unread = r.GetInt();
    }

    // ======================================================================= Auktionshaus

    public sealed class AuctionItemData
    {
        public long Id;
        public string ItemId = "";
        public int Count;
        public string Seller = "";
        public long StartBid, Buyout, CurrentBid, MinBid;
        /// <summary>0 = kurz (&lt; 30 min), 1 = mittel (&lt; 2 h), 2 = lang (&lt; 12 h), 3 = sehr lang.</summary>
        public byte TimeLeft;
        public bool IsMine, IsMyBid, HasBids;
        public void Write(NetDataWriter w)
        {
            w.Put(Id); w.Put(ItemId); w.Put(Count); w.Put(Seller); w.Put(StartBid); w.Put(Buyout); w.Put(CurrentBid); w.Put(MinBid); w.Put(TimeLeft); w.Put(IsMine); w.Put(IsMyBid); w.Put(HasBids);
        }
        public static AuctionItemData Read(NetDataReader r) => new AuctionItemData
        {
            Id = r.GetLong(), ItemId = r.GetString(), Count = r.GetInt(), Seller = r.GetString(), StartBid = r.GetLong(), Buyout = r.GetLong(), CurrentBid = r.GetLong(),
            MinBid = r.GetLong(), TimeLeft = r.GetByte(), IsMine = r.GetBool(), IsMyBid = r.GetBool(), HasBids = r.GetBool(),
        };
    }

    public sealed class C_AuctionSearch : Message
    {
        public override Opcode Op => Opcode.C_AuctionSearch;
        public int NpcId;
        public string Query = "";
        /// <summary>255 = alle Kategorien.</summary>
        public byte Category = 255;
        public int MinLevel, MaxLevel;
        /// <summary>Mindestqualität, 255 = alle.</summary>
        public byte MinQuality = 255;
        public int Page;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(Query); w.Put(Category); w.Put(MinLevel); w.Put(MaxLevel); w.Put(MinQuality); w.Put(Page); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); Query = r.GetString(); Category = r.GetByte(); MinLevel = r.GetInt(); MaxLevel = r.GetInt(); MinQuality = r.GetByte(); Page = r.GetInt(); }
    }

    public sealed class S_AuctionList : Message
    {
        public override Opcode Op => Opcode.S_AuctionList;
        /// <summary>0 = Suche, 1 = eigene Angebote, 2 = eigene Gebote.</summary>
        public byte Kind;
        public int Total, Page;
        public List<AuctionItemData> Auctions = new List<AuctionItemData>();
        public override void Write(NetDataWriter w) { w.Put(Kind); w.Put(Total); w.Put(Page); w.PutList(Auctions, (x, a) => a.Write(x)); }
        public override void Read(NetDataReader r) { Kind = r.GetByte(); Total = r.GetInt(); Page = r.GetInt(); Auctions = r.GetList(AuctionItemData.Read); }
    }

    public sealed class C_AuctionPost : Message
    {
        public override Opcode Op => Opcode.C_AuctionPost;
        public int NpcId, BagIndex, Count;
        public long StartBid, Buyout;
        /// <summary>12, 24 oder 48 Stunden.</summary>
        public int DurationHours = 24;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(BagIndex); w.Put(Count); w.Put(StartBid); w.Put(Buyout); w.Put(DurationHours); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); BagIndex = r.GetInt(); Count = r.GetInt(); StartBid = r.GetLong(); Buyout = r.GetLong(); DurationHours = r.GetInt(); }
    }

    /// <summary>Gebot; ein Betrag ≥ Sofortkaufpreis kauft sofort.</summary>
    public sealed class C_AuctionBid : Message
    {
        public override Opcode Op => Opcode.C_AuctionBid;
        public int NpcId;
        public long AuctionId, Amount;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(AuctionId); w.Put(Amount); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); AuctionId = r.GetLong(); Amount = r.GetLong(); }
    }

    public sealed class C_AuctionCancel : Message
    {
        public override Opcode Op => Opcode.C_AuctionCancel;
        public int NpcId;
        public long AuctionId;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(AuctionId); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); AuctionId = r.GetLong(); }
    }

    public sealed class C_AuctionMine : Message
    {
        public override Opcode Op => Opcode.C_AuctionMine;
        public int NpcId;
        /// <summary>false = eigene Angebote, true = eigene Gebote.</summary>
        public bool Bids;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(Bids); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); Bids = r.GetBool(); }
    }

    // ======================================================================= Talente zurücksetzen

    /// <summary>Beim Klassenlehrer: Confirm = false fragt nur den Preis ab.</summary>
    public sealed class C_ResetTalents : Message
    {
        public override Opcode Op => Opcode.C_ResetTalents;
        public int NpcId;
        public bool Confirm;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(Confirm); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); Confirm = r.GetBool(); }
    }

    public sealed class S_TalentResetCost : Message
    {
        public override Opcode Op => Opcode.S_TalentResetCost;
        public int NpcId;
        public long Cost;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(Cost); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); Cost = r.GetLong(); }
    }
}
