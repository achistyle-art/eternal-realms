using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using EternalRealms.Shared.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Client
{
    /// <summary>
    /// Netzwerkclient ohne Engine-Abhängigkeit. Unity (NetworkManager), der Konsolenclient und die Tests
    /// verwenden ihn gleichermaßen. <see cref="Poll"/> muss regelmäßig aufgerufen werden (z. B. in Update()).
    /// </summary>
    public sealed class GameClient : INetEventListener, IDisposable
    {
        private readonly NetManager _net;
        private readonly NetDataWriter _writer = new NetDataWriter();
        private NetPeer? _peer;
        private uint _moveSequence;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        public bool Connected => _peer != null && _peer.ConnectionState == ConnectionState.Connected;
        public bool LoggedIn { get; private set; }
        public bool InWorld { get; private set; }
        public string ZoneId { get; private set; } = "";
        public long CharacterId { get; private set; }
        public int Ping { get; private set; }
        /// <summary>Letzte Leistungswerte des Servers (kommen mit jedem Pong).</summary>
        public S_Pong ServerPerf { get; private set; } = new S_Pong();
        public ClientWorldState State { get; } = new ClientWorldState();
        public List<CharacterSummary> Characters { get; private set; } = new List<CharacterSummary>();
        public Dictionary<string, int> SpellBook { get; } = new Dictionary<string, int>();
        public Dictionary<string, float> Cooldowns { get; } = new Dictionary<string, float>();
        public List<BotCandidateData> BotCandidates { get; private set; } = new List<BotCandidateData>();
        public List<GroupMemberData> Group { get; private set; } = new List<GroupMemberData>();
        public int GroupLeaderId { get; private set; }
        /// <summary>Zielmarkierungen der Gruppe: Einheit → Symbol (1–8).</summary>
        public Dictionary<int, int> Markers { get; } = new Dictionary<int, int>();
        public List<S_Chat> ChatLog { get; } = new List<S_Chat>();
        public List<string> Notices { get; } = new List<string>();
        /// <summary>Kampfereignisse mit eigener Beteiligung (Kampflog im Chat), höchstens 300.</summary>
        public List<S_CombatEvent> CombatLog { get; } = new List<S_CombatEvent>();
        /// <summary>Zähler für neue Kampflog-Einträge (Benutzeroberflächen erkennen so Neues, auch wenn die Liste gekürzt wurde).</summary>
        public int CombatLogTotal { get; private set; }
        public S_NpcInteraction? LastInteraction { get; private set; }
        public string? PendingInviteFrom { get; private set; }

        // Wirtschaft
        public S_Bank? Bank { get; private set; }
        public List<MailSummaryData> Mail { get; private set; } = new List<MailSummaryData>();
        public int UnreadMail { get; private set; }
        public S_AuctionList? Auctions { get; private set; }
        /// <summary>Offene Bedarf/Gier-Würfe (RollId → Start), bis das Ergebnis kommt.</summary>
        public Dictionary<int, S_LootRollStart> OpenRolls { get; } = new Dictionary<int, S_LootRollStart>();
        public S_LootMethod LootMethod { get; private set; } = new S_LootMethod { Method = Net.LootMethod.GroupLoot, Threshold = ItemQuality.Uncommon };
        public S_TalentResetCost? TalentResetOffer { get; set; }
        public S_MountJournal MountJournal { get; private set; } = new S_MountJournal();
        public S_GuildRoster Guild { get; private set; } = new S_GuildRoster();
        public S_FriendList Friends { get; private set; } = new S_FriendList();
        public S_GuildInvite? PendingGuildInvite { get; set; }
        public List<FinderActivityData> FinderActivities { get; private set; } = new List<FinderActivityData>();
        public S_FinderStatus FinderStatus { get; private set; } = new S_FinderStatus();
        public S_FinderProposal? FinderProposal { get; set; }
        public S_ReadyCheck? ReadyCheck { get; set; }
        public S_VoteKick? VoteKick { get; set; }
        public S_InstanceInfo InstanceInfo { get; private set; } = new S_InstanceInfo();
        public List<ProfessionData> Professions { get; private set; } = new List<ProfessionData>();
        public S_ProfessionTrainer? ProfessionTrainer { get; set; }
        /// <summary>Am Charakter gespeicherte Einstellungen; bis S_Settings eintrifft die Standardwerte.</summary>
        public Ui.GameSettings Settings { get; private set; } = new Ui.GameSettings();
        public bool SettingsLoaded { get; private set; }
        /// <summary>Entdeckte Gebiete (Weltkarte, Nebel des Krieges).</summary>
        public HashSet<string> DiscoveredAreas { get; } = new HashSet<string>();
        public HashSet<string> KnownFlightNodes { get; } = new HashSet<string>();
        /// <summary>Offener Handel (null = keiner) und eine eingegangene Handelsanfrage.</summary>
        public S_TradeState? Trade { get; private set; }
        public S_TradeRequest? TradeRequest { get; set; }
        public S_TradeClosed? LastTradeResult { get; private set; }
        /// <summary>Betretene öffentliche Kanäle (Gebiet, Handel, Gruppensuche).</summary>
        public HashSet<ChatChannel> JoinedChannels { get; } = new HashSet<ChatChannel>();

        /// <summary>Jede empfangene Nachricht (nach der Verarbeitung) – für Benutzeroberflächen.</summary>
        public event Action<Message>? MessageReceived;
        public event Action<string>? Disconnected;

        public ClientEntity? Me => State.Me;
        public double Now => _clock.Elapsed.TotalSeconds;

        public GameClient()
        {
            _net = new NetManager(this) { AutoRecycle = true, DisconnectTimeout = 15000, ChannelsCount = 2 };
        }

        public bool Connect(string host, int port)
        {
            if (!_net.IsRunning && !_net.Start()) return false;
            _peer = _net.Connect(host, port, ProtocolInfo.ConnectionKey);
            return _peer != null;
        }

        public void Disconnect() { _peer?.Disconnect(); _net.Stop(); }
        public void Dispose() => Disconnect();

        public void Poll()
        {
            _net.PollEvents();
            if (Cooldowns.Count > 0)
            {
                var keys = new List<string>(Cooldowns.Keys);
                foreach (var k in keys) if (Cooldowns[k] < Now) Cooldowns.Remove(k);
            }
        }

        public void Send(Message m)
        {
            if (_peer == null) return;
            _writer.Reset();
            Packets.Write(_writer, m);
            _peer.Send(_writer, 0, DeliveryMethod.ReliableOrdered);
        }

        private void SendUnreliable(Message m)
        {
            if (_peer == null) return;
            _writer.Reset();
            Packets.Write(_writer, m);
            _peer.Send(_writer, 1, DeliveryMethod.Sequenced);
        }

        // ------------------------------------------------------------ Bequeme Aktionen

        public void Login(string account, string password, bool register = false) =>
            Send(new C_Login { Account = account, Password = password, Register = register });

        /// <summary>Meldet die gewünschte Position (ca. 10× pro Sekunde während der Bewegung).</summary>
        public void Move(Vec3 position, float facing, bool jumping = false)
        {
            if (Me != null) { Me.Position = position; Me.Facing = facing; }
            SendUnreliable(new C_Move { Position = position, Facing = facing, Jumping = jumping, Sequence = ++_moveSequence });
        }

        public void Target(int id) => Send(new C_SetTarget { TargetId = id });
        public void Cast(string abilityId, int targetId) => Send(new C_CastAbility { AbilityId = abilityId, TargetId = targetId });
        /// <summary>Befehl an KI-Mitspieler (botEntityId 0 = alle). Werte siehe C_BotCommand.</summary>
        public void BotCommand(byte command, int botEntityId = 0, int targetId = 0) =>
            Send(new C_BotCommand { Command = command, BotEntityId = botEntityId, TargetId = targetId });

        public void SetMarker(byte marker, int targetId) => Send(new C_SetMarker { Marker = marker, TargetId = targetId });

        public void Say(string text, ChatChannel ch = ChatChannel.Say) => Send(new C_Chat { Channel = ch, Text = text });

        /// <summary>Wählt mit der Tab-Taste das nächste feindliche Ziel vor dem Spieler.</summary>
        public ClientEntity? NextEnemyTarget(int currentId)
        {
            var me = Me;
            if (me == null) return null;
            var list = new List<ClientEntity>();
            foreach (var e in State.Entities.Values)
                if (e.Id != me.Id && !e.IsDead && (e.Faction == Faction.Hostile || e.Faction == Faction.Neutral || (e.Faction != me.Faction && e.Faction != Faction.Friendly && e.Kind != EntityKind.GameObject))
                    && Vec3.DistanceXZ(e.Position, me.Position) < 40 && e.Kind != EntityKind.GameObject)
                    list.Add(e);
            if (list.Count == 0) return null;
            list.Sort((a, b) => Vec3.DistanceXZ(a.Position, me.Position).CompareTo(Vec3.DistanceXZ(b.Position, me.Position)));
            int idx = list.FindIndex(e => e.Id == currentId);
            return list[(idx + 1) % list.Count];
        }

        // ------------------------------------------------------------ Empfang

        /// <summary>Nachricht ohne Netzwerk verarbeiten (Tests, Wiedergabe aufgezeichneter Sitzungen).</summary>
        public void Receive(Message m) => Handle(m);

        private void Handle(Message m)
        {
            double now = Now;
            switch (m)
            {
                case S_LoginResult r:
                    LoggedIn = r.Success;
                    Notices.Add(r.Message);
                    if (r.Success) Send(new C_CharacterList());
                    break;
                case S_CharacterList cl: Characters = cl.Characters; InWorld = false; break;
                case S_CreateCharacterResult cr: Notices.Add(cr.Message); break;
                case S_EnterWorld ew:
                    if (ew.ZoneId != ZoneId || !InWorld) State.Clear();
                    InWorld = true;
                    ZoneId = ew.ZoneId;
                    CharacterId = ew.CharacterId;
                    State.MyId = ew.EntityId;
                    if (!State.Entities.ContainsKey(ew.EntityId)) State.Entities[ew.EntityId] = new ClientEntity { Id = ew.EntityId, Kind = EntityKind.Player };
                    State.Entities[ew.EntityId].Position = ew.Position;
                    State.Entities[ew.EntityId].Facing = ew.Facing;
                    break;
                case S_EntitySpawn sp: State.Spawn(sp, now); break;
                case S_EntityDespawn ds: State.Entities.Remove(ds.Id); break;
                case S_Snapshot sn:
                    // Die eigene Position meldet der Client selbst; der Server korrigiert nur bei Abweichung.
                    var me = Me;
                    Vec3 mine = me?.Position ?? default;
                    State.Snapshot(sn, now);
                    if (me != null && Vec3.DistanceXZ(me.Position, mine) < 3f) me.Position = mine;
                    break;
                case S_PlayerState ps: State.Stats = ps; break;
                case S_SpellBook sb:
                    SpellBook.Clear();
                    for (int i = 0; i < sb.AbilityIds.Count; i++) SpellBook[sb.AbilityIds[i]] = sb.Ranks[i];
                    break;
                case S_Talents t: State.Talents = t; break;
                case S_Inventory inv: State.Inventory = inv; break;
                case S_QuestLog ql: State.QuestLog = ql; break;
                case S_QuestMarker qm: if (State.Get(qm.NpcId) is ClientEntity npc) npc.Marker = qm.Marker; break;
                case S_AuraUpdate au: if (State.Get(au.TargetId) is ClientEntity ae) ae.Auras = au.Auras; break;
                case S_CastStart cs: if (State.Get(cs.CasterId) is ClientEntity ce) { ce.Casting = cs; ce.CastStartedAt = now; } break;
                case S_CastEnd end: if (State.Get(end.CasterId) is ClientEntity ce2) ce2.Casting = null; break;
                case S_Cooldown cd:
                    if (cd.Seconds > 0) Cooldowns[cd.AbilityId] = (float)(now + cd.Seconds);
                    break;
                case S_CombatEvent cev:
                    CombatLog.Add(cev);
                    CombatLogTotal++;
                    if (CombatLog.Count > 300) CombatLog.RemoveRange(0, CombatLog.Count - 300);
                    break;
                case S_Chat chat:
                    ChatLog.Add(chat);
                    if (ChatLog.Count > 500) ChatLog.RemoveAt(0);
                    break;
                case S_Notice n:
                    Notices.Add(n.Text);
                    if (Notices.Count > 200) Notices.RemoveAt(0);
                    break;
                case S_BotCandidates bc: BotCandidates = bc.Candidates; break;
                case S_GroupUpdate gu: Group = gu.Members; GroupLeaderId = gu.LeaderId; if (gu.Members.Count == 0) Markers.Clear(); break;
                case S_GroupMarkers gm:
                    Markers.Clear();
                    for (int i = 0; i < gm.Markers.Count && i < gm.Targets.Count; i++) Markers[gm.Targets[i]] = gm.Markers[i];
                    break;
                case S_GroupInviteRequest gi: PendingInviteFrom = gi.FromName; break;
                case S_Bank bank: Bank = bank; break;
                case S_MailList ml: Mail = ml.Mails; UnreadMail = 0; break;
                case S_NewMail nm: UnreadMail = nm.Unread; break;
                case S_AuctionList al: Auctions = al; break;
                case S_LootRollStart rs: OpenRolls[rs.RollId] = rs; break;
                case S_LootRollResult rr: OpenRolls.Remove(rr.RollId); break;
                case S_LootMethod lm: LootMethod = lm; break;
                case S_TalentResetCost trc: TalentResetOffer = trc; break;
                case S_MountJournal mj: MountJournal = mj; break;
                case S_GuildRoster gro: Guild = gro; break;
                case S_FriendList fl: Friends = fl; break;
                case S_GuildInvite ginv: PendingGuildInvite = ginv; break;
                case S_FinderActivities fa: FinderActivities = fa.Activities; break;
                case S_FinderStatus fs: FinderStatus = fs; if (!fs.Queued) FinderProposal = null; break;
                case S_FinderProposal fp: FinderProposal = fp; break;
                case S_ReadyCheck rc: ReadyCheck = rc; break;
                case S_ReadyCheckResult: ReadyCheck = null; break;
                case S_VoteKick vk: VoteKick = vk; break;
                case S_InstanceInfo ii: InstanceInfo = ii; break;
                case S_Professions pf: Professions = pf.Professions; break;
                case S_TradeRequest treq: TradeRequest = treq; break;
                case S_TradeState tst: Trade = tst; TradeRequest = null; break;
                case S_TradeClosed tc: Trade = null; LastTradeResult = tc; break;
                case S_ChatChannels cc:
                    JoinedChannels.Clear();
                    foreach (var c in cc.Joined) JoinedChannels.Add((ChatChannel)c);
                    break;
                case S_Settings st: Settings = Ui.GameSettings.FromJson(st.Json); SettingsLoaded = true; break;
                case S_Exploration ex:
                    if (ex.Full) { DiscoveredAreas.Clear(); KnownFlightNodes.Clear(); }
                    foreach (var a in ex.Areas) DiscoveredAreas.Add(a);
                    foreach (var n in ex.FlightNodes) KnownFlightNodes.Add(n);
                    break;
                case S_ProfessionTrainer pt: ProfessionTrainer = pt; break;
                case S_NpcInteraction ni: LastInteraction = ni; break;
                case S_DeathState d: State.DeathState = d.State; State.CorpsePosition = d.CorpsePosition; break;
                case S_WorldState w: State.World = w; break;
                case S_Pong p: Ping = (int)((long)(now * 1000) - p.ClientTime); ServerPerf = p; break;
            }
            MessageReceived?.Invoke(m);
        }

        public void ClearInvite() => PendingInviteFrom = null;

        public void Roll(int rollId, RollChoice choice)
        {
            Send(new C_LootRoll { RollId = rollId, Choice = choice });
            OpenRolls.Remove(rollId);
        }

        /// <summary>Einstellungen ersetzen (z. B. ein neuer Charakter übernimmt Tasten und Optionen des zuletzt gespielten).</summary>
        public void ApplySettings(Ui.GameSettings settings)
        {
            settings.Normalize();
            Settings = settings;
        }

        /// <summary>Einstellungen bereinigen und am Charakter speichern (z. B. nach dem Schließen des Optionsfensters).</summary>
        public void SaveSettings()
        {
            Settings.Normalize();
            var json = Settings.ToJson();
            if (json.Length <= C_SaveSettings.MaxLength) Send(new C_SaveSettings { Json = json });
        }

        public void TradeAccept(bool accept) { if (Trade != null) Send(new C_TradeAccept { Accept = accept, Version = Trade.Version }); }

        public void SendPing() => Send(new C_Ping { ClientTime = (long)(Now * 1000) });

        // ------------------------------------------------------------ LiteNetLib

        void INetEventListener.OnPeerConnected(NetPeer peer) { _peer = peer; }
        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            LoggedIn = false;
            InWorld = false;
            Disconnected?.Invoke(info.Reason.ToString());
        }
        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) => Notices.Add("Netzwerkfehler: " + socketError);
        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            Message m;
            try { m = Packets.Read(reader); }
            catch (Exception ex) { Notices.Add("Unlesbares Paket: " + ex.Message); return; }
            Handle(m);
        }
        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { Ping = latency; }
        void INetEventListener.OnConnectionRequest(ConnectionRequest request) => request.Reject();
    }
}
