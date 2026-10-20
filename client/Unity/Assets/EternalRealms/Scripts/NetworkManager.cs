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

        private void Awake()
        {
            Instance = this;
            Client = new GameClient();
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

        public bool Connect() => Client.Connect(Host, Port);

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
