using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    // ============================================================ Client -> Server

    public sealed class C_Login : Message
    {
        public override Opcode Op => Opcode.C_Login;
        public string Account = "", Password = "";
        public bool Register;
        public int ProtocolVersion = ProtocolInfo.ProtocolVersion;
        /// <summary>Wiederverbindung: Token aus dem letzten <see cref="S_LoginResult"/> statt Passwort.</summary>
        public string Token = "";
        /// <summary>Code der Zwei-Faktor-Anmeldung (sechs Ziffern), falls das Konto sie verlangt.</summary>
        public string TotpCode = "";
        // Protokollversion zuerst: ein veralteter Client wird erkannt, auch wenn sich die übrigen Felder geändert haben.
        public override void Write(NetDataWriter w) { w.Put(ProtocolVersion); w.Put(Account); w.Put(Password); w.Put(Register); w.Put(Token); w.Put(TotpCode); }
        public override void Read(NetDataReader r)
        {
            ProtocolVersion = r.GetInt();
            if (ProtocolVersion != ProtocolInfo.ProtocolVersion) return;
            Account = r.GetString(); Password = r.GetString(); Register = r.GetBool(); Token = r.GetString(); TotpCode = r.GetString();
        }
    }

    public sealed class C_CharacterList : Message
    {
        public override Opcode Op => Opcode.C_CharacterList;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_CreateCharacter : Message
    {
        public override Opcode Op => Opcode.C_CreateCharacter;
        public string Name = "";
        public Race Race;
        public CharClass Class;
        public Gender Gender;
        public AppearanceData Appearance = new AppearanceData();
        public override void Write(NetDataWriter w) { w.Put(Name); w.Put((byte)Race); w.Put((byte)Class); w.Put((byte)Gender); Appearance.Write(w); }
        public override void Read(NetDataReader r) { Name = r.GetString(); Race = (Race)r.GetByte(); Class = (CharClass)r.GetByte(); Gender = (Gender)r.GetByte(); Appearance = AppearanceData.Read(r); }
    }

    public sealed class C_DeleteCharacter : Message
    {
        public override Opcode Op => Opcode.C_DeleteCharacter;
        public long CharacterId;
        public override void Write(NetDataWriter w) => w.Put(CharacterId);
        public override void Read(NetDataReader r) => CharacterId = r.GetLong();
    }

    public sealed class C_EnterWorld : Message
    {
        public override Opcode Op => Opcode.C_EnterWorld;
        public long CharacterId;
        public override void Write(NetDataWriter w) => w.Put(CharacterId);
        public override void Read(NetDataReader r) => CharacterId = r.GetLong();
    }

    public sealed class C_Logout : Message
    {
        public override Opcode Op => Opcode.C_Logout;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    /// <summary>Gewünschte Position. Der Server prüft Geschwindigkeit und Zustand (betäubt, festgehalten).</summary>
    public sealed class C_Move : Message
    {
        public override Opcode Op => Opcode.C_Move;
        public Vec3 Position;
        public float Facing;
        public bool Jumping;
        public uint Sequence;
        public override void Write(NetDataWriter w) { w.Put(Position); w.Put(Facing); w.Put(Jumping); w.Put(Sequence); }
        public override void Read(NetDataReader r) { Position = r.GetVec3(); Facing = r.GetFloat(); Jumping = r.GetBool(); Sequence = r.GetUInt(); }
    }

    public sealed class C_SetTarget : Message
    {
        public override Opcode Op => Opcode.C_SetTarget;
        public int TargetId;
        public override void Write(NetDataWriter w) => w.Put(TargetId);
        public override void Read(NetDataReader r) => TargetId = r.GetInt();
    }

    public sealed class C_CastAbility : Message
    {
        public override Opcode Op => Opcode.C_CastAbility;
        /// <summary>Basis-ID; der Server nutzt den höchsten erlernten Rang, falls Rank = 0.</summary>
        public string AbilityId = "";
        public int Rank;
        public int TargetId;
        public override void Write(NetDataWriter w) { w.Put(AbilityId); w.Put(Rank); w.Put(TargetId); }
        public override void Read(NetDataReader r) { AbilityId = r.GetString(); Rank = r.GetInt(); TargetId = r.GetInt(); }
    }

    public sealed class C_CancelCast : Message
    {
        public override Opcode Op => Opcode.C_CancelCast;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_AttackStart : Message
    {
        public override Opcode Op => Opcode.C_AttackStart;
        public int TargetId;
        public override void Write(NetDataWriter w) => w.Put(TargetId);
        public override void Read(NetDataReader r) => TargetId = r.GetInt();
    }

    public sealed class C_AttackStop : Message
    {
        public override Opcode Op => Opcode.C_AttackStop;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_Chat : Message
    {
        public override Opcode Op => Opcode.C_Chat;
        public ChatChannel Channel;
        public string Text = "";
        public string WhisperTo = "";
        public override void Write(NetDataWriter w) { w.Put((byte)Channel); w.Put(Text); w.Put(WhisperTo); }
        public override void Read(NetDataReader r) { Channel = (ChatChannel)r.GetByte(); Text = r.GetString(); WhisperTo = r.GetString(); }
    }

    /// <summary>Mit NPC sprechen oder Objekt benutzen.</summary>
    public sealed class C_Interact : Message
    {
        public override Opcode Op => Opcode.C_Interact;
        public int EntityId;
        public override void Write(NetDataWriter w) => w.Put(EntityId);
        public override void Read(NetDataReader r) => EntityId = r.GetInt();
    }

    public sealed class C_QuestAccept : Message
    {
        public override Opcode Op => Opcode.C_QuestAccept;
        public int NpcId;
        public string QuestId = "";
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(QuestId); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); QuestId = r.GetString(); }
    }

    public sealed class C_QuestTurnIn : Message
    {
        public override Opcode Op => Opcode.C_QuestTurnIn;
        public int NpcId;
        public string QuestId = "";
        public int ChoiceIndex = -1;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(QuestId); w.Put(ChoiceIndex); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); QuestId = r.GetString(); ChoiceIndex = r.GetInt(); }
    }

    public sealed class C_QuestAbandon : Message
    {
        public override Opcode Op => Opcode.C_QuestAbandon;
        public string QuestId = "";
        public override void Write(NetDataWriter w) => w.Put(QuestId);
        public override void Read(NetDataReader r) => QuestId = r.GetString();
    }

    public sealed class C_Loot : Message
    {
        public override Opcode Op => Opcode.C_Loot;
        public int CorpseId;
        public override void Write(NetDataWriter w) => w.Put(CorpseId);
        public override void Read(NetDataReader r) => CorpseId = r.GetInt();
    }

    public sealed class C_UseItem : Message
    {
        public override Opcode Op => Opcode.C_UseItem;
        public int BagIndex;
        public int TargetId;
        public override void Write(NetDataWriter w) { w.Put(BagIndex); w.Put(TargetId); }
        public override void Read(NetDataReader r) { BagIndex = r.GetInt(); TargetId = r.GetInt(); }
    }

    public sealed class C_EquipItem : Message
    {
        public override Opcode Op => Opcode.C_EquipItem;
        public int BagIndex;
        public override void Write(NetDataWriter w) => w.Put(BagIndex);
        public override void Read(NetDataReader r) => BagIndex = r.GetInt();
    }

    public sealed class C_UnequipItem : Message
    {
        public override Opcode Op => Opcode.C_UnequipItem;
        public EquipSlot Slot;
        public override void Write(NetDataWriter w) => w.Put((byte)Slot);
        public override void Read(NetDataReader r) => Slot = (EquipSlot)r.GetByte();
    }

    public sealed class C_BuyItem : Message
    {
        public override Opcode Op => Opcode.C_BuyItem;
        public int NpcId;
        public string ItemId = "";
        public int Count = 1;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(ItemId); w.Put(Count); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); ItemId = r.GetString(); Count = r.GetInt(); }
    }

    public sealed class C_SellItem : Message
    {
        public override Opcode Op => Opcode.C_SellItem;
        public int NpcId;
        public int BagIndex;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(BagIndex); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); BagIndex = r.GetInt(); }
    }

    public sealed class C_TrainAbility : Message
    {
        public override Opcode Op => Opcode.C_TrainAbility;
        public int NpcId;
        public string AbilityId = "";
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(AbilityId); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); AbilityId = r.GetString(); }
    }

    public sealed class C_LearnTalent : Message
    {
        public override Opcode Op => Opcode.C_LearnTalent;
        public string TalentId = "";
        public override void Write(NetDataWriter w) => w.Put(TalentId);
        public override void Read(NetDataReader r) => TalentId = r.GetString();
    }

    public sealed class C_ReleaseSpirit : Message
    {
        public override Opcode Op => Opcode.C_ReleaseSpirit;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_ReclaimCorpse : Message
    {
        public override Opcode Op => Opcode.C_ReclaimCorpse;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_SpiritHealerResurrect : Message
    {
        public override Opcode Op => Opcode.C_SpiritHealerResurrect;
        public int NpcId;
        public override void Write(NetDataWriter w) => w.Put(NpcId);
        public override void Read(NetDataReader r) => NpcId = r.GetInt();
    }

    public sealed class C_GroupInvite : Message
    {
        public override Opcode Op => Opcode.C_GroupInvite;
        public string Name = "";
        public override void Write(NetDataWriter w) => w.Put(Name);
        public override void Read(NetDataReader r) => Name = r.GetString();
    }

    public sealed class C_GroupAccept : Message
    {
        public override Opcode Op => Opcode.C_GroupAccept;
        public bool Accept = true;
        public override void Write(NetDataWriter w) => w.Put(Accept);
        public override void Read(NetDataReader r) => Accept = r.GetBool();
    }

    public sealed class C_GroupLeave : Message
    {
        public override Opcode Op => Opcode.C_GroupLeave;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    /// <summary>"KI-Mitspieler suchen" im Gruppenmenü.</summary>
    public sealed class C_BotSearch : Message
    {
        public override Opcode Op => Opcode.C_BotSearch;
        /// <summary>255 = alle Rollen.</summary>
        public byte Role = 255;
        /// <summary>255 = beliebige Schwierigkeit, sonst 0 Anfänger … 3 Veteran.</summary>
        public byte Difficulty = 255;
        public override void Write(NetDataWriter w) { w.Put(Role); w.Put(Difficulty); }
        public override void Read(NetDataReader r) { Role = r.GetByte(); Difficulty = r.GetByte(); }
    }

    public sealed class C_BotInvite : Message
    {
        public override Opcode Op => Opcode.C_BotInvite;
        public long CandidateId;
        public override void Write(NetDataWriter w) => w.Put(CandidateId);
        public override void Read(NetDataReader r) => CandidateId = r.GetLong();
    }

    public sealed class C_BotDismiss : Message
    {
        public override Opcode Op => Opcode.C_BotDismiss;
        public int EntityId;
        public override void Write(NetDataWriter w) => w.Put(EntityId);
        public override void Read(NetDataReader r) => EntityId = r.GetInt();
    }

    /// <summary>Bot als bevorzugten Begleiter speichern oder entfernen.</summary>
    public sealed class C_BotFavorite : Message
    {
        public override Opcode Op => Opcode.C_BotFavorite;
        public int EntityId;
        public bool Favorite = true;
        public override void Write(NetDataWriter w) { w.Put(EntityId); w.Put(Favorite); }
        public override void Read(NetDataReader r) { EntityId = r.GetInt(); Favorite = r.GetBool(); }
    }

    /// <summary>Befehl aus der Kommandooberfläche an einen Bot (0 = alle Bots der Gruppe).</summary>
    public sealed class C_BotCommand : Message
    {
        public override Opcode Op => Opcode.C_BotCommand;
        /// <summary>Wert der Server-Aufzählung BotCommand (Follow=1, Stay=2, Attack=3 …).</summary>
        public byte Command;
        public int BotEntityId;
        public int TargetId;
        /// <summary>0 = keine, 1 = links, 2 = rechts.</summary>
        public byte Side;
        public override void Write(NetDataWriter w) { w.Put(Command); w.Put(BotEntityId); w.Put(TargetId); w.Put(Side); }
        public override void Read(NetDataReader r) { Command = r.GetByte(); BotEntityId = r.GetInt(); TargetId = r.GetInt(); Side = r.GetByte(); }
    }

    /// <summary>Zielmarkierung setzen (1–8; 8 = Totenkopf, 7 = Kreuz, 5 = Mond). TargetId 0 entfernt sie.</summary>
    public sealed class C_SetMarker : Message
    {
        public override Opcode Op => Opcode.C_SetMarker;
        public byte Marker;
        public int TargetId;
        public override void Write(NetDataWriter w) { w.Put(Marker); w.Put(TargetId); }
        public override void Read(NetDataReader r) { Marker = r.GetByte(); TargetId = r.GetInt(); }
    }

    public sealed class C_Ping : Message
    {
        public override Opcode Op => Opcode.C_Ping;
        public long ClientTime;
        public override void Write(NetDataWriter w) => w.Put(ClientTime);
        public override void Read(NetDataReader r) => ClientTime = r.GetLong();
    }

    public sealed class C_CancelAura : Message
    {
        public override Opcode Op => Opcode.C_CancelAura;
        public string AuraId = "";
        public override void Write(NetDataWriter w) => w.Put(AuraId);
        public override void Read(NetDataReader r) => AuraId = r.GetString();
    }

    public sealed class C_TakeFlight : Message
    {
        public override Opcode Op => Opcode.C_TakeFlight;
        public int NpcId;
        public string Destination = "";
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(Destination); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); Destination = r.GetString(); }
    }

    // ============================================================ Server -> Client

    public sealed class S_LoginResult : Message
    {
        public override Opcode Op => Opcode.S_LoginResult;
        public bool Success;
        public string Message = "";
        /// <summary>Einmal gültiges Token für eine Wiederverbindung (nur im Speicher halten).</summary>
        public string Token = "";
        /// <summary>Passwort stimmte, das Konto verlangt aber einen Zwei-Faktor-Code.</summary>
        public bool NeedsTwoFactor;
        /// <summary>Wiederverbindung: der Charakter war noch in der Welt und wird direkt übernommen.</summary>
        public bool Resumed;
        public byte GmLevel;
        public override void Write(NetDataWriter w) { w.Put(Success); w.Put(Message); w.Put(Token); w.Put(NeedsTwoFactor); w.Put(Resumed); w.Put(GmLevel); }
        public override void Read(NetDataReader r) { Success = r.GetBool(); Message = r.GetString(); Token = r.GetString(); NeedsTwoFactor = r.GetBool(); Resumed = r.GetBool(); GmLevel = r.GetByte(); }
    }

    public sealed class S_CharacterList : Message
    {
        public override Opcode Op => Opcode.S_CharacterList;
        public List<CharacterSummary> Characters = new List<CharacterSummary>();
        public override void Write(NetDataWriter w) => w.PutList(Characters, (x, c) => c.Write(x));
        public override void Read(NetDataReader r) => Characters = r.GetList(CharacterSummary.Read);
    }

    public sealed class S_CreateCharacterResult : Message
    {
        public override Opcode Op => Opcode.S_CreateCharacterResult;
        public bool Success;
        public string Message = "";
        public long CharacterId;
        public override void Write(NetDataWriter w) { w.Put(Success); w.Put(Message); w.Put(CharacterId); }
        public override void Read(NetDataReader r) { Success = r.GetBool(); Message = r.GetString(); CharacterId = r.GetLong(); }
    }

    public sealed class S_EnterWorld : Message
    {
        public override Opcode Op => Opcode.S_EnterWorld;
        public int EntityId;
        public long CharacterId;
        public string ZoneId = "";
        public Vec3 Position;
        public float Facing;
        public override void Write(NetDataWriter w) { w.Put(EntityId); w.Put(CharacterId); w.Put(ZoneId); w.Put(Position); w.Put(Facing); }
        public override void Read(NetDataReader r) { EntityId = r.GetInt(); CharacterId = r.GetLong(); ZoneId = r.GetString(); Position = r.GetVec3(); Facing = r.GetFloat(); }
    }

    public sealed class S_PlayerState : Message
    {
        public override Opcode Op => Opcode.S_PlayerState;
        public int Level, Xp, XpToNext, RestedXp, TalentPointsFree;
        public long Money;
        public float Strength, Agility, Stamina, Intellect, Spirit;
        public int Armor, AttackPower, SpellPower;
        public float MeleeCrit, SpellCrit, Dodge;
        // Charakterfenster (Kapitel 28)
        public int RangedAttackPower, HealingPower;
        public float Parry, Block, Hit, MoveSpeed;
        /// <summary>Widerstände je Schule (Index = <see cref="School"/>).</summary>
        public List<int> Resist = new List<int>();
        public List<string> ReputationIds = new List<string>();
        public List<int> ReputationValues = new List<int>();
        public override void Write(NetDataWriter w)
        {
            w.Put(Level); w.Put(Xp); w.Put(XpToNext); w.Put(RestedXp); w.Put(Money); w.Put(TalentPointsFree);
            w.Put(Strength); w.Put(Agility); w.Put(Stamina); w.Put(Intellect); w.Put(Spirit);
            w.Put(Armor); w.Put(AttackPower); w.Put(SpellPower); w.Put(MeleeCrit); w.Put(SpellCrit); w.Put(Dodge);
            w.Put(RangedAttackPower); w.Put(HealingPower); w.Put(Parry); w.Put(Block); w.Put(Hit); w.Put(MoveSpeed);
            w.PutList(Resist, (x, v) => x.Put(v)); w.PutStrings(ReputationIds); w.PutList(ReputationValues, (x, v) => x.Put(v));
        }
        public override void Read(NetDataReader r)
        {
            Level = r.GetInt(); Xp = r.GetInt(); XpToNext = r.GetInt(); RestedXp = r.GetInt(); Money = r.GetLong(); TalentPointsFree = r.GetInt();
            Strength = r.GetFloat(); Agility = r.GetFloat(); Stamina = r.GetFloat(); Intellect = r.GetFloat(); Spirit = r.GetFloat();
            Armor = r.GetInt(); AttackPower = r.GetInt(); SpellPower = r.GetInt(); MeleeCrit = r.GetFloat(); SpellCrit = r.GetFloat(); Dodge = r.GetFloat();
            RangedAttackPower = r.GetInt(); HealingPower = r.GetInt(); Parry = r.GetFloat(); Block = r.GetFloat(); Hit = r.GetFloat(); MoveSpeed = r.GetFloat();
            Resist = r.GetList(x => x.GetInt()); ReputationIds = r.GetStrings(); ReputationValues = r.GetList(x => x.GetInt());
        }
    }

    /// <summary>Bekannte Fähigkeiten: Basis-ID -> höchster Rang.</summary>
    public sealed class S_SpellBook : Message
    {
        public override Opcode Op => Opcode.S_SpellBook;
        public List<string> AbilityIds = new List<string>();
        public List<int> Ranks = new List<int>();
        public override void Write(NetDataWriter w) { w.PutStrings(AbilityIds); w.PutList(Ranks, (x, v) => x.Put(v)); }
        public override void Read(NetDataReader r) { AbilityIds = r.GetStrings(); Ranks = r.GetList(x => x.GetInt()); }
    }

    public sealed class S_Talents : Message
    {
        public override Opcode Op => Opcode.S_Talents;
        public List<string> TalentIds = new List<string>();
        public List<int> Ranks = new List<int>();
        public int FreePoints;
        public override void Write(NetDataWriter w) { w.PutStrings(TalentIds); w.PutList(Ranks, (x, v) => x.Put(v)); w.Put(FreePoints); }
        public override void Read(NetDataReader r) { TalentIds = r.GetStrings(); Ranks = r.GetList(x => x.GetInt()); FreePoints = r.GetInt(); }
    }

    public sealed class S_Inventory : Message
    {
        public override Opcode Op => Opcode.S_Inventory;
        public long Money;
        public int BagSize;
        /// <summary>Taschenplätze; leere Plätze haben ItemId "". Reihenfolge: Rucksack, dann Taschen 1–4.</summary>
        public List<ItemStackData> Bag = new List<ItemStackData>();
        /// <summary>Index = (int)EquipSlot.</summary>
        public List<ItemStackData> Equipment = new List<ItemStackData>();
        /// <summary>Die vier angelegten Taschen (leer = ItemId "").</summary>
        public List<ItemStackData> Containers = new List<ItemStackData>();
        public override void Write(NetDataWriter w) { w.Put(Money); w.Put(BagSize); w.PutList(Bag, (x, s) => s.Write(x)); w.PutList(Equipment, (x, s) => s.Write(x)); w.PutList(Containers, (x, s) => s.Write(x)); }
        public override void Read(NetDataReader r) { Money = r.GetLong(); BagSize = r.GetInt(); Bag = r.GetList(ItemStackData.Read); Equipment = r.GetList(ItemStackData.Read); Containers = r.GetList(ItemStackData.Read); }
    }

    public sealed class S_QuestLog : Message
    {
        public override Opcode Op => Opcode.S_QuestLog;
        public List<QuestProgressData> Quests = new List<QuestProgressData>();
        public List<string> Completed = new List<string>();
        public override void Write(NetDataWriter w) { w.PutList(Quests, (x, q) => q.Write(x)); w.PutStrings(Completed); }
        public override void Read(NetDataReader r) { Quests = r.GetList(QuestProgressData.Read); Completed = r.GetStrings(); }
    }

    public sealed class S_EntitySpawn : Message
    {
        public override Opcode Op => Opcode.S_EntitySpawn;
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
        public ushort NpcRoles;
        public PowerType PowerType;
        public QuestMarker Marker;
        public AppearanceData Appearance = new AppearanceData();
        public EntitySnapshotData State = new EntitySnapshotData();
        /// <summary>Aktives Reittier (leer = zu Fuß).</summary>
        public string MountId = "";
        public override void Write(NetDataWriter w)
        {
            w.Put(Id); w.Put((byte)Kind); w.Put(TemplateId); w.Put(Name); w.Put(Title); w.Put((byte)Race); w.Put((byte)Class); w.Put((byte)Gender);
            w.Put(Level); w.Put((byte)Faction); w.Put(NpcRoles); w.Put((byte)PowerType); w.Put((byte)Marker); Appearance.Write(w); State.Write(w); w.Put(MountId);
        }
        public override void Read(NetDataReader r)
        {
            Id = r.GetInt(); Kind = (EntityKind)r.GetByte(); TemplateId = r.GetString(); Name = r.GetString(); Title = r.GetString();
            Race = (Race)r.GetByte(); Class = (CharClass)r.GetByte(); Gender = (Gender)r.GetByte(); Level = r.GetInt(); Faction = (Faction)r.GetByte();
            NpcRoles = r.GetUShort(); PowerType = (PowerType)r.GetByte(); Marker = (QuestMarker)r.GetByte(); Appearance = AppearanceData.Read(r); State = EntitySnapshotData.Read(r); MountId = r.GetString();
        }
    }

    public sealed class S_EntityDespawn : Message
    {
        public override Opcode Op => Opcode.S_EntityDespawn;
        public int Id;
        public override void Write(NetDataWriter w) => w.Put(Id);
        public override void Read(NetDataReader r) => Id = r.GetInt();
    }

    /// <summary>Periodischer Zustand aller sichtbaren Einheiten (unzuverlässig, sequenziert).</summary>
    public sealed class S_Snapshot : Message
    {
        public override Opcode Op => Opcode.S_Snapshot;
        public uint Tick;
        public uint LastProcessedMove;
        public List<EntitySnapshotData> Entities = new List<EntitySnapshotData>();
        public override void Write(NetDataWriter w) { w.Put(Tick); w.Put(LastProcessedMove); w.PutList(Entities, (x, e) => e.Write(x)); }
        public override void Read(NetDataReader r) { Tick = r.GetUInt(); LastProcessedMove = r.GetUInt(); Entities = r.GetList(EntitySnapshotData.Read); }
    }

    public sealed class S_CombatEvent : Message
    {
        public override Opcode Op => Opcode.S_CombatEvent;
        public int SourceId, TargetId;
        public string AbilityId = "";
        public CombatResult Result;
        public School School;
        public int Amount, Absorbed, Resisted, Overkill;
        public bool Periodic;
        public override void Write(NetDataWriter w)
        {
            w.Put(SourceId); w.Put(TargetId); w.Put(AbilityId); w.Put((byte)Result); w.Put((byte)School); w.Put(Amount); w.Put(Absorbed); w.Put(Resisted); w.Put(Overkill); w.Put(Periodic);
        }
        public override void Read(NetDataReader r)
        {
            SourceId = r.GetInt(); TargetId = r.GetInt(); AbilityId = r.GetString(); Result = (CombatResult)r.GetByte(); School = (School)r.GetByte();
            Amount = r.GetInt(); Absorbed = r.GetInt(); Resisted = r.GetInt(); Overkill = r.GetInt(); Periodic = r.GetBool();
        }
    }

    public sealed class S_CastStart : Message
    {
        public override Opcode Op => Opcode.S_CastStart;
        public int CasterId, TargetId;
        public string AbilityId = "";
        public float CastTime;
        public bool Channeled;
        public override void Write(NetDataWriter w) { w.Put(CasterId); w.Put(TargetId); w.Put(AbilityId); w.Put(CastTime); w.Put(Channeled); }
        public override void Read(NetDataReader r) { CasterId = r.GetInt(); TargetId = r.GetInt(); AbilityId = r.GetString(); CastTime = r.GetFloat(); Channeled = r.GetBool(); }
    }

    public sealed class S_CastEnd : Message
    {
        public override Opcode Op => Opcode.S_CastEnd;
        public int CasterId;
        public string AbilityId = "";
        public bool Success;
        public string Reason = "";
        public override void Write(NetDataWriter w) { w.Put(CasterId); w.Put(AbilityId); w.Put(Success); w.Put(Reason); }
        public override void Read(NetDataReader r) { CasterId = r.GetInt(); AbilityId = r.GetString(); Success = r.GetBool(); Reason = r.GetString(); }
    }

    public sealed class S_AuraUpdate : Message
    {
        public override Opcode Op => Opcode.S_AuraUpdate;
        public int TargetId;
        public List<AuraData> Auras = new List<AuraData>();
        public override void Write(NetDataWriter w) { w.Put(TargetId); w.PutList(Auras, (x, a) => a.Write(x)); }
        public override void Read(NetDataReader r) { TargetId = r.GetInt(); Auras = r.GetList(AuraData.Read); }
    }

    public sealed class S_UnitDied : Message
    {
        public override Opcode Op => Opcode.S_UnitDied;
        public int Id, KillerId;
        public override void Write(NetDataWriter w) { w.Put(Id); w.Put(KillerId); }
        public override void Read(NetDataReader r) { Id = r.GetInt(); KillerId = r.GetInt(); }
    }

    public sealed class S_XpGain : Message
    {
        public override Opcode Op => Opcode.S_XpGain;
        public int Amount, RestedBonus, SourceId;
        public string Reason = "";
        public override void Write(NetDataWriter w) { w.Put(Amount); w.Put(RestedBonus); w.Put(SourceId); w.Put(Reason); }
        public override void Read(NetDataReader r) { Amount = r.GetInt(); RestedBonus = r.GetInt(); SourceId = r.GetInt(); Reason = r.GetString(); }
    }

    public sealed class S_LevelUp : Message
    {
        public override Opcode Op => Opcode.S_LevelUp;
        public int EntityId, NewLevel, HealthGained, PowerGained, TalentPoints;
        public List<string> NewTrainableAbilities = new List<string>();
        public override void Write(NetDataWriter w) { w.Put(EntityId); w.Put(NewLevel); w.Put(HealthGained); w.Put(PowerGained); w.Put(TalentPoints); w.PutStrings(NewTrainableAbilities); }
        public override void Read(NetDataReader r) { EntityId = r.GetInt(); NewLevel = r.GetInt(); HealthGained = r.GetInt(); PowerGained = r.GetInt(); TalentPoints = r.GetInt(); NewTrainableAbilities = r.GetStrings(); }
    }

    /// <summary>Antwort auf das Ansprechen eines NPCs. Texte kommen aus dem Content des Clients.</summary>
    public sealed class S_NpcInteraction : Message
    {
        public override Opcode Op => Opcode.S_NpcInteraction;
        public int NpcId;
        public string TemplateId = "";
        public List<string> QuestsAvailable = new List<string>();
        public List<string> QuestsInProgress = new List<string>();
        public List<string> QuestsToTurnIn = new List<string>();
        public List<string> VendorItems = new List<string>();
        /// <summary>Lernbare Fähigkeiten als Rang-ID.</summary>
        public List<string> Trainable = new List<string>();
        public List<string> FlightDestinations = new List<string>();
        public bool CanResurrect;
        public bool IsInnkeeper;
        public override void Write(NetDataWriter w)
        {
            w.Put(NpcId); w.Put(TemplateId); w.PutStrings(QuestsAvailable); w.PutStrings(QuestsInProgress); w.PutStrings(QuestsToTurnIn);
            w.PutStrings(VendorItems); w.PutStrings(Trainable); w.PutStrings(FlightDestinations); w.Put(CanResurrect); w.Put(IsInnkeeper);
        }
        public override void Read(NetDataReader r)
        {
            NpcId = r.GetInt(); TemplateId = r.GetString(); QuestsAvailable = r.GetStrings(); QuestsInProgress = r.GetStrings(); QuestsToTurnIn = r.GetStrings();
            VendorItems = r.GetStrings(); Trainable = r.GetStrings(); FlightDestinations = r.GetStrings(); CanResurrect = r.GetBool(); IsInnkeeper = r.GetBool();
        }
    }

    public sealed class S_LootResult : Message
    {
        public override Opcode Op => Opcode.S_LootResult;
        public int CorpseId;
        public int Money;
        public List<ItemStackData> Items = new List<ItemStackData>();
        public override void Write(NetDataWriter w) { w.Put(CorpseId); w.Put(Money); w.PutList(Items, (x, i) => i.Write(x)); }
        public override void Read(NetDataReader r) { CorpseId = r.GetInt(); Money = r.GetInt(); Items = r.GetList(ItemStackData.Read); }
    }

    public sealed class S_Chat : Message
    {
        public override Opcode Op => Opcode.S_Chat;
        public ChatChannel Channel;
        public int SenderId;
        public string SenderName = "";
        public string Text = "";
        public bool SenderIsBot;
        public override void Write(NetDataWriter w) { w.Put((byte)Channel); w.Put(SenderId); w.Put(SenderName); w.Put(Text); w.Put(SenderIsBot); }
        public override void Read(NetDataReader r) { Channel = (ChatChannel)r.GetByte(); SenderId = r.GetInt(); SenderName = r.GetString(); Text = r.GetString(); SenderIsBot = r.GetBool(); }
    }

    /// <summary>Hinweis- oder Fehlermeldung ("Nicht genug Mana", "Ziel außer Reichweite").</summary>
    public sealed class S_Notice : Message
    {
        public override Opcode Op => Opcode.S_Notice;
        public bool IsError;
        public string Text = "";
        public override void Write(NetDataWriter w) { w.Put(IsError); w.Put(Text); }
        public override void Read(NetDataReader r) { IsError = r.GetBool(); Text = r.GetString(); }
    }

    public sealed class S_GroupUpdate : Message
    {
        public override Opcode Op => Opcode.S_GroupUpdate;
        public int LeaderId;
        public bool IsRaid;
        public List<GroupMemberData> Members = new List<GroupMemberData>();
        public override void Write(NetDataWriter w) { w.Put(LeaderId); w.Put(IsRaid); w.PutList(Members, (x, m) => m.Write(x)); }
        public override void Read(NetDataReader r) { LeaderId = r.GetInt(); IsRaid = r.GetBool(); Members = r.GetList(GroupMemberData.Read); }
    }

    public sealed class S_BotCandidates : Message
    {
        public override Opcode Op => Opcode.S_BotCandidates;
        public List<BotCandidateData> Candidates = new List<BotCandidateData>();
        public override void Write(NetDataWriter w) => w.PutList(Candidates, (x, c) => c.Write(x));
        public override void Read(NetDataReader r) => Candidates = r.GetList(BotCandidateData.Read);
    }

    public sealed class S_DeathState : Message
    {
        public override Opcode Op => Opcode.S_DeathState;
        public DeathState State;
        public Vec3 CorpsePosition;
        public float ReclaimDelay;
        public override void Write(NetDataWriter w) { w.Put((byte)State); w.Put(CorpsePosition); w.Put(ReclaimDelay); }
        public override void Read(NetDataReader r) { State = (DeathState)r.GetByte(); CorpsePosition = r.GetVec3(); ReclaimDelay = r.GetFloat(); }
    }

    public sealed class S_WorldState : Message
    {
        public override Opcode Op => Opcode.S_WorldState;
        /// <summary>Spielzeit in Stunden 0..24.</summary>
        public float GameHour;
        public Weather Weather;
        public float WeatherIntensity;
        public override void Write(NetDataWriter w) { w.Put(GameHour); w.Put((byte)Weather); w.Put(WeatherIntensity); }
        public override void Read(NetDataReader r) { GameHour = r.GetFloat(); Weather = (Weather)r.GetByte(); WeatherIntensity = r.GetFloat(); }
    }

    public sealed class S_Pong : Message
    {
        public override Opcode Op => Opcode.S_Pong;
        public long ClientTime;
        public long ServerTime;
        /// <summary>Leistung des Servers (Kapitel 52): mittlere und 99-%-Taktdauer in ms, Spieler und Einheiten in der Welt.</summary>
        public float TickMs, TickP99Ms;
        public int Players, Entities;
        public override void Write(NetDataWriter w) { w.Put(ClientTime); w.Put(ServerTime); w.Put(TickMs); w.Put(TickP99Ms); w.Put(Players); w.Put(Entities); }
        public override void Read(NetDataReader r) { ClientTime = r.GetLong(); ServerTime = r.GetLong(); TickMs = r.GetFloat(); TickP99Ms = r.GetFloat(); Players = r.GetInt(); Entities = r.GetInt(); }
    }

    public sealed class S_Cooldown : Message
    {
        public override Opcode Op => Opcode.S_Cooldown;
        public string AbilityId = "";
        public float Seconds;
        public float Gcd;
        public override void Write(NetDataWriter w) { w.Put(AbilityId); w.Put(Seconds); w.Put(Gcd); }
        public override void Read(NetDataReader r) { AbilityId = r.GetString(); Seconds = r.GetFloat(); Gcd = r.GetFloat(); }
    }

    public sealed class S_GroupInviteRequest : Message
    {
        public override Opcode Op => Opcode.S_GroupInviteRequest;
        public string FromName = "";
        public override void Write(NetDataWriter w) => w.Put(FromName);
        public override void Read(NetDataReader r) => FromName = r.GetString();
    }

    public sealed class S_GroupMarkers : Message
    {
        public override Opcode Op => Opcode.S_GroupMarkers;
        public List<int> Markers = new List<int>();
        public List<int> Targets = new List<int>();
        public override void Write(NetDataWriter w) { w.PutList(Markers, (x, v) => x.Put(v)); w.PutList(Targets, (x, v) => x.Put(v)); }
        public override void Read(NetDataReader r) { Markers = r.GetList(x => x.GetInt()); Targets = r.GetList(x => x.GetInt()); }
    }

    public sealed class S_QuestMarker : Message
    {
        public override Opcode Op => Opcode.S_QuestMarker;
        public int NpcId;
        public QuestMarker Marker;
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put((byte)Marker); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); Marker = (QuestMarker)r.GetByte(); }
    }
}
