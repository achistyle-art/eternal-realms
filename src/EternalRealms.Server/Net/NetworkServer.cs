using System.Net;
using System.Net.Sockets;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EternalRealms.Server.Net
{
    /// <summary>
    /// UDP-Netzwerk mit LiteNetLib: zuverlässig-geordnete Nachrichten für Spiellogik, unzuverlässig-sequenzierte
    /// für Positionsupdates. Wird im Spielthread gepollt – Nachrichten werden dort sofort verarbeitet.
    /// </summary>
    public sealed class NetworkServer : INetEventListener
    {
        private readonly GameWorld _w;
        private readonly IAccountRepository _accounts;
        private readonly ICharacterRepository _chars;
        private readonly NetManager _net;
        private readonly Dictionary<int, WorldSession> _byPeer = new();
        public SessionRegistry Registry { get; } = new();

        public NetworkServer(GameWorld world, IAccountRepository accounts, ICharacterRepository chars)
        {
            _w = world;
            _accounts = accounts;
            _chars = chars;
            _net = new NetManager(this) { AutoRecycle = true, DisconnectTimeout = 15000, UpdateTime = 5, ChannelsCount = 2, UnconnectedMessagesEnabled = false };
        }

        public int Port => _net.LocalPort;
        public int ConnectedCount => _byPeer.Count;

        public bool Start(int port) => _net.Start(port);

        public void Poll() => _net.PollEvents();

        public void Stop()
        {
            foreach (var s in _byPeer.Values.ToList()) s.Close();
            _net.Stop();
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (_byPeer.Count >= _w.Config.MaxConnections) { request.Reject(); return; }
            request.AcceptIfKey(ProtocolInfo.ConnectionKey);
        }

        public void OnPeerConnected(NetPeer peer)
        {
            var conn = new PeerConnection(peer);
            var session = new WorldSession(_w, _accounts, _chars, Registry, conn);
            _byPeer[peer.Id] = session;
            Registry.Add(session);
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (_byPeer.Remove(peer.Id, out var s)) s.Close();
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (!_byPeer.TryGetValue(peer.Id, out var s)) return;
            Message m;
            try { m = Packets.Read(reader); }
            catch (Exception) { peer.Disconnect(); return; } // fehlerhaftes Paket: Verbindung trennen
            try { s.Handle(m); }
            catch (Exception ex) { _w.Log($"Fehler bei {m.Op}: {ex}"); }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) => _w.Log($"Netzwerkfehler {endPoint}: {socketError}");
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        private sealed class PeerConnection : IPlayerConnection
        {
            private readonly NetPeer _peer;
            private readonly NetDataWriter _writer = new();
            public PeerConnection(NetPeer peer) { _peer = peer; }
            public string RemoteAddress => _peer.ToString();

            public void Send(Message message)
            {
                _writer.Reset();
                Packets.Write(_writer, message);
                _peer.Send(_writer, 0, DeliveryMethod.ReliableOrdered);
            }

            public void SendUnreliable(Message message)
            {
                _writer.Reset();
                Packets.Write(_writer, message);
                // Snapshots tragen ihre Tick-Nummer; der Client verwirft veraltete. Zu große Pakete (sollte nicht vorkommen)
                // gehen zuverlässig und damit fragmentiert raus, statt den Spieltakt mit einer Ausnahme abzubrechen.
                if (_writer.Length > _peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable)) _peer.Send(_writer, 0, DeliveryMethod.ReliableOrdered);
                else _peer.Send(_writer, 1, DeliveryMethod.Unreliable);
            }

            public void Disconnect(string reason) => _peer.Disconnect();
        }
    }
}
