using EternalRealms.Server;

// Eternal Realms – Realm-Server
// Konfiguration über Umgebungsvariablen: ER_PORT, ER_DATABASE, ER_CONTENT, ER_DEV_COMMANDS, ER_AMBIENT_BOTS …
var config = ServerConfig.FromEnvironment();
string root = FindRoot();
if (!Path.IsPathRooted(config.ContentPath)) config.ContentPath = Path.Combine(root, config.ContentPath);
// Migrationen: ER_MIGRATIONS, sonst neben dem Content-Ordner (…/content → …/db/migrations), sonst im Projektwurzelverzeichnis.
var migrations = Environment.GetEnvironmentVariable("ER_MIGRATIONS") is { Length: > 0 } envMig ? envMig
    : Directory.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config.ContentPath)) ?? root, "db", "migrations"))
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config.ContentPath))!, "db", "migrations")
        : Path.Combine(root, "db", "migrations");
if (!string.IsNullOrWhiteSpace(config.ConnectionString) && !Directory.Exists(migrations))
    Console.WriteLine($"Warnung: Migrationsordner {migrations} nicht gefunden – Datenbankschema wird nicht aktualisiert (ER_MIGRATIONS setzen).");

Console.WriteLine("Eternal Realms – Realm-Server");
GameServer server;
try
{
    server = await GameServer.CreateAsync(config, Directory.Exists(migrations) ? migrations : null);
}
catch (Exception ex)
{
    Console.Error.WriteLine("Start fehlgeschlagen: " + ex.Message);
    return 1;
}
if (!server.Start())
{
    Console.Error.WriteLine($"Port {config.Port} konnte nicht geöffnet werden.");
    return 1;
}
Console.WriteLine($"Server läuft auf UDP-Port {config.Port} mit {config.TickRate} Ticks/s. Strg+C beendet.");
using var cts = new CancellationTokenSource();
var stopped = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
// SIGTERM (systemd, Docker): erst sauber speichern, dann beenden.
AppDomain.CurrentDomain.ProcessExit += (_, _) => { cts.Cancel(); stopped.Wait(TimeSpan.FromSeconds(20)); };
if (config.Console)
{
    // Serverkonsole (ER_CONSOLE=true): Spielleiter-Befehle ohne Punkt, z. B. „rechte meinkonto 2“, „ansage Neustart in 5 Minuten“.
    Console.WriteLine("Konsole aktiv – „hilfe“ zeigt die Befehle.");
    var consoleThread = new Thread(() =>
    {
        string? line;
        while (!cts.IsCancellationRequested && (line = Console.ReadLine()) != null)
            server.ConsoleCommand(line, text => Console.WriteLine("> " + text));
    }) { IsBackground = true, Name = "Konsole" };
    consoleThread.Start();
}
server.Run(cts.Token);
Console.WriteLine("Speichere alle Charaktere …");
server.Dispose();
Console.WriteLine("Server beendet.");
stopped.Set();
return 0;

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
