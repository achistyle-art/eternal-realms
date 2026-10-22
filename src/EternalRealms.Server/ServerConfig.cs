namespace EternalRealms.Server
{
    /// <summary>
    /// Betriebsart eines Realms (Kapitel 60). Sie setzt nur Voreinstellungen; einzelne Umgebungsvariablen überschreiben sie.
    /// </summary>
    public enum ServerMode
    {
        /// <summary>Klassisches MMORPG nur mit Menschen: keine KI-Mitspieler, keine KI-Bewohner, Gruppensuche nur mit Menschen.</summary>
        Klassisch,
        /// <summary>Standard: Menschen und gekennzeichnete KI-Mitspieler – jeder Inhalt ist auch allein spielbar.</summary>
        KiUnterstuetzt,
        /// <summary>Entwicklung: Entwicklerbefehle und Serverkonsole an. Nie öffentlich betreiben!</summary>
        Entwicklung,
    }

    /// <summary>Servereinstellungen. Werte kommen aus Umgebungsvariablen (siehe README).</summary>
    public sealed class ServerConfig
    {
        public ServerMode Mode { get; set; } = ServerMode.KiUnterstuetzt;
        /// <summary>KI-Mitspieler dürfen in Gruppen eingeladen und von der Gruppensuche ergänzt werden.</summary>
        public bool BotCompanions { get; set; } = true;
        public int Port { get; set; } = 7777;
        public int MaxConnections { get; set; } = 2000;
        public int TickRate { get; set; } = 20;
        public string ContentPath { get; set; } = "content";
        /// <summary>Leer = Speicher im Arbeitsspeicher (nur für Tests und Ausprobieren).</summary>
        public string? ConnectionString { get; set; }
        public bool AllowRegistration { get; set; } = true;
        /// <summary>Geschlossene Alpha: neue Konten nur mit einem gültigen Einladungscode (Spielleitung: .einladung).</summary>
        public bool InviteOnly { get; set; }
        /// <summary>Begrüßung nach dem Betreten der Welt (z. B. Hinweise für Alpha-Tester); leer = keine.</summary>
        public string Motd { get; set; } = "";
        /// <summary>Alpha-Statistik: alle n Minuten eine JSON-Zeile in diese Datei (leer = aus).</summary>
        public string? MetricsFile { get; set; }
        public double MetricsMinutes { get; set; } = 5;
        /// <summary>Chatbefehle mit # (Stufe setzen, Gegenstände erzeugen …). Nur für Entwicklung!</summary>
        public bool DevCommands { get; set; }
        public bool AmbientBots { get; set; } = true;
        /// <summary>Detailstufen für Zonen ohne menschliche Spieler (reduziert, ruhend).</summary>
        public bool ZoneLod { get; set; } = true;
        public double AutosaveSeconds { get; set; } = 60;
        /// <summary>Spielzeit läuft so viel schneller als Echtzeit (6 = ein Spieltag dauert 4 Stunden).</summary>
        public double TimeScale { get; set; } = 6;
        /// <summary>Tutorial mit KI-Erklärerin für neue Charaktere (Kapitel 39).</summary>
        public bool Tutorial { get; set; } = true;
        public int MaxCharactersPerAccount { get; set; } = 10;
        public int MaxMessagesPerSecond { get; set; } = 80;
        /// <summary>Spieler gleichzeitig in der Welt; weitere warten in der Anmeldeschlange (Spielleiter ausgenommen).</summary>
        public int MaxPlayers { get; set; } = 1000;
        /// <summary>So lange bleibt ein Charakter nach einem Verbindungsabbruch in der Welt (0 = sofort abmelden).</summary>
        public double LinkdeadSeconds { get; set; } = 60;
        /// <summary>Gültigkeit eines Wiederverbindungstokens.</summary>
        public double SessionTokenHours { get; set; } = 12;
        /// <summary>Wartungsmodus beim Start: nur Spielleiter können sich anmelden.</summary>
        public bool Maintenance { get; set; }
        /// <summary>Befehle über die Serverkonsole (stdin) annehmen.</summary>
        public bool Console { get; set; }
        /// <summary>Anti-Cheat: ab dieser Punktzahl Meldung an Spielleiter, ab der zweiten Schwelle Trennung (nie automatische Sperre).</summary>
        public int CheatFlagScore { get; set; } = 50;
        public int CheatKickScore { get; set; } = 150;
        /// <summary>Gold pro Stunde (in Kupfer) je Stufe, ab dem der Geldzufluss eines Charakters auffällt.</summary>
        public long GoldPerHourPerLevel { get; set; } = 400_00;

        public static bool TryParseMode(string text, out ServerMode mode)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "klassisch": case "classic": mode = ServerMode.Klassisch; return true;
                case "ki": case "ki-unterstuetzt": case "ki-unterstützt": case "assisted": mode = ServerMode.KiUnterstuetzt; return true;
                case "entwicklung": case "dev": case "development": mode = ServerMode.Entwicklung; return true;
                default: mode = ServerMode.KiUnterstuetzt; return false;
            }
        }

        /// <summary>Voreinstellungen der Betriebsart übernehmen (vor den einzelnen Umgebungsvariablen).</summary>
        public ServerConfig ApplyMode(ServerMode mode)
        {
            Mode = mode;
            switch (mode)
            {
                case ServerMode.Klassisch:
                    AmbientBots = false; BotCompanions = false; DevCommands = false; Console = false;
                    break;
                case ServerMode.KiUnterstuetzt:
                    AmbientBots = true; BotCompanions = true; DevCommands = false;
                    break;
                case ServerMode.Entwicklung:
                    AmbientBots = true; BotCompanions = true; DevCommands = true; Console = true; AutosaveSeconds = 30;
                    break;
            }
            return this;
        }

        public static string ModeLabel(ServerMode m) => m switch
        {
            ServerMode.Klassisch => "klassisch (nur Menschen)", ServerMode.Entwicklung => "Entwicklung", _ => "KI-unterstützt",
        };

        public static ServerConfig FromEnvironment()
        {
            var c = new ServerConfig();
            string? Env(string k) => Environment.GetEnvironmentVariable(k);
            if (Env("ER_MODE") is { Length: > 0 } mode)
            {
                if (!TryParseMode(mode, out var m)) throw new ArgumentException($"ER_MODE '{mode}' unbekannt (klassisch, ki, entwicklung).");
                c.ApplyMode(m);
            }
            if (bool.TryParse(Env("ER_BOT_COMPANIONS"), out var bc)) c.BotCompanions = bc;
            if (int.TryParse(Env("ER_PORT"), out var port)) c.Port = port;
            if (int.TryParse(Env("ER_MAX_CONNECTIONS"), out var mc)) c.MaxConnections = mc;
            if (int.TryParse(Env("ER_TICK_RATE"), out var tr)) c.TickRate = Math.Clamp(tr, 5, 60);
            c.ContentPath = Env("ER_CONTENT") ?? c.ContentPath;
            c.ConnectionString = Env("ER_DATABASE");
            if (bool.TryParse(Env("ER_ALLOW_REGISTRATION"), out var reg)) c.AllowRegistration = reg;
            if (bool.TryParse(Env("ER_INVITE_ONLY"), out var inv)) c.InviteOnly = inv;
            c.Motd = Env("ER_MOTD") ?? c.Motd;
            c.MetricsFile = Env("ER_METRICS_FILE") is { Length: > 0 } mf ? mf : c.MetricsFile;
            if (double.TryParse(Env("ER_METRICS_MINUTES"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mm)) c.MetricsMinutes = Math.Clamp(mm, 0.1, 1440);
            if (bool.TryParse(Env("ER_DEV_COMMANDS"), out var dev)) c.DevCommands = dev;
            if (bool.TryParse(Env("ER_AMBIENT_BOTS"), out var ab)) c.AmbientBots = ab;
            if (bool.TryParse(Env("ER_ZONE_LOD"), out var lod)) c.ZoneLod = lod;
            if (bool.TryParse(Env("ER_TUTORIAL"), out var tut)) c.Tutorial = tut;
            if (double.TryParse(Env("ER_AUTOSAVE_SECONDS"), out var ass)) c.AutosaveSeconds = Math.Max(5, ass);
            if (int.TryParse(Env("ER_MAX_PLAYERS"), out var mp)) c.MaxPlayers = Math.Max(1, mp);
            if (double.TryParse(Env("ER_LINKDEAD_SECONDS"), out var ld)) c.LinkdeadSeconds = Math.Clamp(ld, 0, 600);
            if (double.TryParse(Env("ER_SESSION_TOKEN_HOURS"), out var th)) c.SessionTokenHours = Math.Clamp(th, 0.1, 168);
            if (bool.TryParse(Env("ER_MAINTENANCE"), out var mt)) c.Maintenance = mt;
            if (bool.TryParse(Env("ER_CONSOLE"), out var con)) c.Console = con;
            if (int.TryParse(Env("ER_CHEAT_FLAG_SCORE"), out var cf)) c.CheatFlagScore = Math.Max(1, cf);
            if (int.TryParse(Env("ER_CHEAT_KICK_SCORE"), out var ck)) c.CheatKickScore = Math.Max(1, ck);
            return c;
        }
    }
}
