using System;
using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    public static class ProtocolInfo
    {
        /// <summary>Wird beim Verbinden geprüft; Client und Server müssen übereinstimmen.</summary>
        public const string ConnectionKey = "EternalRealms/1";
        public const int ProtocolVersion = 3;
        public const int DefaultPort = 7777;
    }

    public enum Opcode : ushort
    {
        // Client -> Server (1–999)
        C_Login = 1, C_CharacterList, C_CreateCharacter, C_DeleteCharacter, C_EnterWorld, C_Logout,
        C_Move, C_SetTarget, C_CastAbility, C_CancelCast, C_AttackStart, C_AttackStop, C_Chat,
        C_Interact, C_QuestAccept, C_QuestTurnIn, C_QuestAbandon, C_Loot, C_UseItem, C_EquipItem, C_UnequipItem,
        C_BuyItem, C_SellItem, C_TrainAbility, C_LearnTalent, C_ReleaseSpirit, C_ReclaimCorpse, C_SpiritHealerResurrect,
        C_GroupInvite, C_GroupAccept, C_GroupLeave, C_BotSearch, C_BotInvite, C_BotDismiss, C_Ping, C_CancelAura, C_TakeFlight,
        C_BotFavorite, C_BotCommand, C_SetMarker,
        C_MoveItem, C_SplitStack, C_SortBags, C_EquipBag, C_UnequipBag, C_DestroyItem, C_BankDeposit, C_BankWithdraw,
        C_SetLootMethod, C_LootRoll, C_MasterLoot, C_MailList, C_MailSend, C_MailTake, C_MailDelete, C_MailReturn,
        C_AuctionSearch, C_AuctionPost, C_AuctionBid, C_AuctionCancel, C_AuctionMine, C_ResetTalents,
        C_Mount, C_Dismount, C_LearnRiding,
        C_GuildCreate, C_GuildInvite, C_GuildRespond, C_GuildLeave, C_GuildKick, C_GuildSetRank, C_GuildMotd, C_GuildRoster, C_GuildDisband,
        C_FriendAdd, C_FriendRemove, C_IgnoreAdd, C_IgnoreRemove, C_FriendList, C_ReportPlayer, C_ShareLocation, C_InviteFavorite,
        C_ResetInstances, C_InstanceInfo,
        C_FinderActivities, C_FinderJoin, C_FinderLeave, C_FinderAccept, C_ReadyCheck, C_ReadyResponse, C_VoteKick, C_VoteKickResponse,
        C_SetRole, C_SetBotRole, C_ConvertToRaid, C_SetSubgroup,
        C_LearnProfession, C_TrainProfessionRank, C_LearnRecipe, C_UnlearnProfession, C_Craft, C_Skin, C_Disenchant, C_ProfessionInfo,
        C_SaveSettings,
        C_TradeRequest, C_TradeRespond, C_TradeSetItem, C_TradeSetMoney, C_TradeAccept, C_TradeCancel, C_ChatJoin,
        C_Emote, C_SetTitle, C_TutorialSkip, C_TutorialRestart,
        C_AccountSecurity,

        // Server -> Client (ab 1000)
        S_LoginResult = 1000, S_CharacterList, S_CreateCharacterResult, S_EnterWorld, S_PlayerState, S_SpellBook,
        S_Talents, S_Inventory, S_QuestLog, S_EntitySpawn, S_EntityDespawn, S_Snapshot, S_CombatEvent, S_CastStart,
        S_CastEnd, S_AuraUpdate, S_UnitDied, S_XpGain, S_LevelUp, S_NpcInteraction, S_LootResult, S_Chat, S_Notice,
        S_GroupUpdate, S_BotCandidates, S_DeathState, S_WorldState, S_Pong, S_Cooldown, S_GroupInviteRequest, S_QuestMarker,
        S_GroupMarkers,
        S_Bank, S_LootMethod, S_LootRollStart, S_LootRollResult, S_MailList, S_NewMail, S_AuctionList, S_TalentResetCost,
        S_MountJournal, S_GuildRoster, S_GuildInvite, S_FriendList, S_InstanceInfo,
        S_FinderActivities, S_FinderStatus, S_FinderProposal, S_ReadyCheck, S_ReadyCheckResult, S_VoteKick,
        S_Professions, S_ProfessionTrainer,
        S_Settings, S_Exploration,
        S_TradeRequest, S_TradeState, S_TradeClosed, S_ChatChannels,
        S_Emote, S_WorldEvent, S_Achievements, S_AchievementEarned, S_Tutorial,
        S_AccountSecurity, S_QueueStatus, S_Goodbye,
        S_GroundEffect, S_Encounter,
    }

    public abstract class Message
    {
        public abstract Opcode Op { get; }
        public abstract void Write(NetDataWriter w);
        public abstract void Read(NetDataReader r);
    }

    /// <summary>Kodiert/dekodiert Nachrichten: [ushort Opcode][Nutzdaten].</summary>
    public static class Packets
    {
        private static readonly Dictionary<Opcode, Func<Message>> Factory = new Dictionary<Opcode, Func<Message>>();

        static Packets()
        {
            foreach (var t in typeof(Message).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(Message).IsAssignableFrom(t)) continue;
                var ctor = t.GetConstructor(Type.EmptyTypes);
                if (ctor == null) continue;
                var probe = (Message)ctor.Invoke(null);
                if (Factory.ContainsKey(probe.Op)) throw new InvalidOperationException("Opcode doppelt vergeben: " + probe.Op);
                Factory[probe.Op] = () => (Message)ctor.Invoke(null);
            }
        }

        public static void Write(NetDataWriter w, Message m)
        {
            w.Put((ushort)m.Op);
            m.Write(w);
        }

        public static byte[] Serialize(Message m)
        {
            var w = new NetDataWriter();
            Write(w, m);
            return w.CopyData();
        }

        public static Message Read(NetDataReader r)
        {
            var op = (Opcode)r.GetUShort();
            if (!Factory.TryGetValue(op, out var create)) throw new InvalidOperationException("Unbekannter Opcode: " + op);
            var m = create();
            m.Read(r);
            return m;
        }

        public static Message Deserialize(byte[] data)
        {
            var r = new NetDataReader(data);
            return Read(r);
        }
    }

    public static class NetExtensions
    {
        public static void Put(this NetDataWriter w, Vec3 v) { w.Put(v.X); w.Put(v.Y); w.Put(v.Z); }
        public static Vec3 GetVec3(this NetDataReader r) => new Vec3(r.GetFloat(), r.GetFloat(), r.GetFloat());

        public static void PutList<T>(this NetDataWriter w, IReadOnlyList<T> list, Action<NetDataWriter, T> put)
        {
            w.Put((ushort)list.Count);
            for (int i = 0; i < list.Count; i++) put(w, list[i]);
        }

        public static List<T> GetList<T>(this NetDataReader r, Func<NetDataReader, T> get)
        {
            int n = r.GetUShort();
            var list = new List<T>(n);
            for (int i = 0; i < n; i++) list.Add(get(r));
            return list;
        }

        public static void PutStrings(this NetDataWriter w, IReadOnlyList<string> list) => w.PutList(list, (x, s) => x.Put(s));
        public static List<string> GetStrings(this NetDataReader r) => r.GetList(x => x.GetString());
    }
}
