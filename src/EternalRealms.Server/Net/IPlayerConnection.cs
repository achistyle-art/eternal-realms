using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Net
{
    /// <summary>Verbindung zu einem Spieler-Client. Netzwerk (LiteNetLib) und Tests implementieren sie.</summary>
    public interface IPlayerConnection
    {
        /// <summary>Zuverlässig und in Reihenfolge.</summary>
        void Send(Message message);
        /// <summary>Unzuverlässig; nur der neueste Stand zählt (Positionsupdates).</summary>
        void SendUnreliable(Message message);
        /// <summary>Trennt die Verbindung; der Client erfährt den Grund und versucht keine automatische Wiederverbindung.</summary>
        void Disconnect(string reason);
        string RemoteAddress { get; }
        /// <summary>Nur die IP-Adresse (ohne Port) – für Begrenzungen von Anmeldeversuchen und das Prüfprotokoll.</summary>
        string RemoteIp => RemoteAddress;
    }

    /// <summary>Sammelt Nachrichten – für Tests und den eingebauten Testclient.</summary>
    public sealed class RecordingConnection : IPlayerConnection
    {
        public List<Message> Messages { get; } = new();
        public bool Disconnected { get; private set; }
        public string? DisconnectReason { get; private set; }
        public string RemoteAddress { get; set; } = "test";
        public void Send(Message message) { lock (Messages) Messages.Add(message); }
        public void SendUnreliable(Message message) { lock (Messages) Messages.Add(message); }
        public void Disconnect(string reason) { Disconnected = true; DisconnectReason = reason; }
        public IEnumerable<T> Of<T>() where T : Message { lock (Messages) return Messages.OfType<T>().ToList(); }
        public void Clear() { lock (Messages) Messages.Clear(); }
    }
}
