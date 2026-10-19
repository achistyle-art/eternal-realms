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
        void Disconnect(string reason);
        string RemoteAddress { get; }
    }

    /// <summary>Sammelt Nachrichten – für Tests und den eingebauten Testclient.</summary>
    public sealed class RecordingConnection : IPlayerConnection
    {
        public List<Message> Messages { get; } = new();
        public bool Disconnected { get; private set; }
        public string RemoteAddress => "test";
        public void Send(Message message) { lock (Messages) Messages.Add(message); }
        public void SendUnreliable(Message message) { lock (Messages) Messages.Add(message); }
        public void Disconnect(string reason) => Disconnected = true;
        public IEnumerable<T> Of<T>() where T : Message { lock (Messages) return Messages.OfType<T>().ToList(); }
        public void Clear() { lock (Messages) Messages.Clear(); }
    }
}
