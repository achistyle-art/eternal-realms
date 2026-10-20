using System.Diagnostics;
using EternalRealms.Server.Net;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared.Content;

namespace EternalRealms.Server
{
    /// <summary>
    /// Ein Realm-Server: lädt Content, verbindet Datenbank und Netzwerk und betreibt die Spielschleife
    /// mit fester Tickrate (Standard 20 Ticks/s).
    /// </summary>
    public sealed class GameServer : IDisposable
    {
        public ServerConfig Config { get; }
        public ContentDatabase Content { get; }
        public GameWorld World { get; }
        public NetworkServer Network { get; }
        private readonly IAccountRepository _accounts;
        private readonly ICharacterRepository _chars;
        private readonly IDisposable? _store;
        private volatile bool _running;
        public double AverageTickMs { get; private set; }

        public GameServer(ServerConfig config, ContentDatabase content, IAccountRepository accounts, ICharacterRepository chars, IDisposable? store = null)
        {
            Config = config;
            Content = content;
            _accounts = accounts;
            _chars = chars;
            _store = store;
            World = new GameWorld(content, config, chars);
            Network = new NetworkServer(World, accounts, chars);
        }

        /// <summary>Erstellt einen Server aus der Konfiguration: Content laden und prüfen, Datenbank migrieren.</summary>
        public static async Task<GameServer> CreateAsync(ServerConfig config, string? migrationsDir = null, Action<string>? log = null)
        {
            log ??= Console.WriteLine;
            var content = ContentDatabase.LoadFromDirectory(config.ContentPath);
            var errors = content.Validate();
            if (errors.Count > 0) throw new InvalidDataException("Content fehlerhaft:\n" + string.Join("\n", errors));
            log($"Content geladen: {content.Races.Count} Völker, {content.Classes.Count} Klassen, {content.Abilities.Count} Fähigkeitsränge, " +
                $"{content.Talents.Count} Talente, {content.Items.Count} Gegenstände, {content.Creatures.Count} Kreaturen, {content.Quests.Count} Quests, " +
                $"{content.Zones.Count} Regionen.");
            IAccountRepository accounts;
            ICharacterRepository chars;
            IDisposable? store = null;
            if (!string.IsNullOrWhiteSpace(config.ConnectionString))
            {
                var pg = new PostgresStore(config.ConnectionString);
                if (migrationsDir != null) await pg.MigrateAsync(migrationsDir, log);
                accounts = pg; chars = pg; store = pg;
                log("Datenbank: PostgreSQL");
            }
            else
            {
                var mem = new InMemoryStore();
                accounts = mem; chars = mem;
                log("Datenbank: Arbeitsspeicher (Daten gehen beim Beenden verloren – ER_DATABASE setzen für PostgreSQL)");
            }
            var server = new GameServer(config, content, accounts, chars, store) { };
            server.World.Log = log;
            var bots = await chars.ListBotsAsync();
            server.World.Bots.LoadOffline(bots);
            if (chars is IEconomyRepository eco)
            {
                var mail = await eco.LoadMailAsync();
                server.World.Mail.Load(mail);
                var auctions = await eco.LoadAuctionsAsync();
                server.World.Auctions.Load(auctions);
                log($"Wirtschaft: {mail.Count} Briefe, {auctions.Count} Auktionen geladen");
            }
            if (chars is ISocialRepository social)
            {
                var guilds = await social.LoadGuildsAsync();
                server.World.Guilds.Load(guilds);
                log($"Gilden: {guilds.Count} geladen");
            }
            return server;
        }

        public bool Start()
        {
            if (!Network.Start(Config.Port)) return false;
            _running = true;
            return true;
        }

        /// <summary>Spielschleife mit fester Schrittweite. Bleibt der Server zurück, holt er bis zu 5 Ticks auf.</summary>
        public void Run(CancellationToken token)
        {
            double step = 1.0 / Config.TickRate;
            var sw = Stopwatch.StartNew();
            double next = sw.Elapsed.TotalSeconds;
            double lastStatus = 0;
            while (_running && !token.IsCancellationRequested)
            {
                double now = sw.Elapsed.TotalSeconds;
                if (now < next)
                {
                    Network.Poll();
                    Thread.Sleep(Math.Max(0, (int)((next - now) * 1000) - 1));
                    continue;
                }
                int catchUp = 0;
                while (now >= next && catchUp++ < 5)
                {
                    var t0 = sw.Elapsed.TotalMilliseconds;
                    try
                    {
                        Network.Poll();
                        World.Update((float)step);
                    }
                    catch (Exception ex)
                    {
                        // Ein Fehler in einem Tick darf nie den ganzen Realm anhalten: protokollieren und weiterlaufen.
                        World.Log("Fehler im Spieltakt: " + ex);
                    }
                    AverageTickMs = AverageTickMs * 0.98 + (sw.Elapsed.TotalMilliseconds - t0) * 0.02;
                    next += step;
                }
                if (now - next > 1) next = now; // nach langer Pause nicht ewig nachholen
                if (now - lastStatus > 300)
                {
                    lastStatus = now;
                    World.Log($"Status: {Network.ConnectedCount} Verbindungen, {World.AllPlayers.Count(p => !p.IsBot)} Spieler, " +
                              $"{World.AllPlayers.Count(p => p.IsBot)} KI-Mitspieler, Tick Ø {AverageTickMs:0.00} ms");
                }
            }
        }

        public void Stop()
        {
            _running = false;
            World.SaveAll();
            Network.Stop();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var pending = World.AllPlayers.ToList();
            foreach (var p in pending) World.Logout(p);
            Network.Stop();
            _running = false;
            World.Economy.Commit(new Persistence.EconomyCommit());
            if (World.Persistence != null && !World.Persistence.Flush(TimeSpan.FromSeconds(15)))
                World.Log($"Warnung: {World.Persistence.Pending} Speicheraufträge nicht abgeschlossen.");
            World.Persistence?.Dispose();
            _store?.Dispose();
        }

        private bool _disposed;
    }
}
