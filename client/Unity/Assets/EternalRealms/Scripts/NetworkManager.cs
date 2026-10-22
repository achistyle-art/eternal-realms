using System.IO;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Content;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Verbindet Unity mit dem Realm-Server. Hält den gemeinsamen <see cref="GameClient"/> und den Content
    /// (aus StreamingAssets/content – dieselben JSON-Dateien wie der Server).
    /// </summary>
    public sealed class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; } = null!;

        public string Host = "127.0.0.1";
        public int Port = 7777;

        public GameClient Client { get; private set; } = null!;
        public ContentDatabase Content { get; private set; } = null!;
        public string? LoadError { get; private set; }
        private float _pingTimer;

        private const string LastServerPref = "EternalRealms.LastServer";

        private void Awake()
        {
            Instance = this;
            Client = new GameClient();
            // Voreinstellung des Alpha-Builds (StreamingAssets/server.txt), danach die zuletzt benutzte Adresse.
            try
            {
                var preset = Path.Combine(Application.streamingAssetsPath, "server.txt");
                if (File.Exists(preset)) ApplyServer(File.ReadAllText(preset));
            }
            catch (System.Exception) { }
            ApplyServer(PlayerPrefs.GetString(LastServerPref, ""));
            try
            {
                Content = ContentDatabase.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "content"));
            }
            catch (System.Exception ex)
            {
                LoadError = "Content nicht gefunden. Menü 'Eternal Realms > Shared-DLL und Content kopieren' ausführen. (" + ex.Message + ")";
                Content = new ContentDatabase();
            }
        }

        public bool Connect()
        {
            PlayerPrefs.SetString(LastServerPref, $"{Host}:{Port}");
            return Client.Connect(Host, Port);
        }

        /// <summary>„host“ oder „host:port“ übernehmen; Leeres und Ungültiges wird ignoriert.</summary>
        private void ApplyServer(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out var port) && port > 0 && port < 65536) { Host = text.Substring(0, colon); Port = port; }
            else if (colon < 0) Host = text;
        }

        private void Update()
        {
            Client.Poll();
            if (Client.Connected && (_pingTimer -= Time.deltaTime) <= 0)
            {
                _pingTimer = 5;
                Client.SendPing();
            }
        }

        private void OnApplicationQuit()
        {
            if (Client.InWorld) { Client.SaveSettings(); Client.Send(new Shared.Net.C_Logout()); }
            Client.Poll();
            Client.Disconnect();
        }
    }
}
