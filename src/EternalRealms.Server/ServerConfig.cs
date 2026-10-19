namespace EternalRealms.Server
{
    /// <summary>Servereinstellungen. Werte kommen aus Umgebungsvariablen (siehe README).</summary>
    public sealed class ServerConfig
    {
        public int Port { get; set; } = 7777;
        public int MaxConnections { get; set; } = 2000;
        public int TickRate { get; set; } = 20;
        public string ContentPath { get; set; } = "content";
        /// <summary>Leer = Speicher im Arbeitsspeicher (nur für Tests und Ausprobieren).</summary>
        public string? ConnectionString { get; set; }
        public bool AllowRegistration { get; set; } = true;
        /// <summary>Chatbefehle mit # (Stufe setzen, Gegenstände erzeugen …). Nur für Entwicklung!</summary>
        public bool DevCommands { get; set; }
        public bool AmbientBots { get; set; } = true;
        /// <summary>Detailstufen für Zonen ohne menschliche Spieler (reduziert, ruhend).</summary>
        public bool ZoneLod { get; set; } = true;
        public double AutosaveSeconds { get; set; } = 60;
        /// <summary>Spielzeit läuft so viel schneller als Echtzeit (6 = ein Spieltag dauert 4 Stunden).</summary>
        public double TimeScale { get; set; } = 6;
        public int MaxCharactersPerAccount { get; set; } = 10;
        public int MaxMessagesPerSecond { get; set; } = 80;

        public static ServerConfig FromEnvironment()
        {
            var c = new ServerConfig();
            string? Env(string k) => Environment.GetEnvironmentVariable(k);
            if (int.TryParse(Env("ER_PORT"), out var port)) c.Port = port;
            if (int.TryParse(Env("ER_MAX_CONNECTIONS"), out var mc)) c.MaxConnections = mc;
            if (int.TryParse(Env("ER_TICK_RATE"), out var tr)) c.TickRate = Math.Clamp(tr, 5, 60);
            c.ContentPath = Env("ER_CONTENT") ?? c.ContentPath;
            c.ConnectionString = Env("ER_DATABASE");
            if (bool.TryParse(Env("ER_ALLOW_REGISTRATION"), out var reg)) c.AllowRegistration = reg;
            if (bool.TryParse(Env("ER_DEV_COMMANDS"), out var dev)) c.DevCommands = dev;
            if (bool.TryParse(Env("ER_AMBIENT_BOTS"), out var ab)) c.AmbientBots = ab;
            if (bool.TryParse(Env("ER_ZONE_LOD"), out var lod)) c.ZoneLod = lod;
            if (double.TryParse(Env("ER_AUTOSAVE_SECONDS"), out var ass)) c.AutosaveSeconds = Math.Max(5, ass);
            return c;
        }
    }
}
