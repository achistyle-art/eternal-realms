using EternalRealms.Server;

// Eternal Realms – Realm-Server
// Konfiguration über Umgebungsvariablen: ER_PORT, ER_DATABASE, ER_CONTENT, ER_DEV_COMMANDS, ER_AMBIENT_BOTS …
ServerConfig config;
try { config = ServerConfig.FromEnvironment(); }
catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
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
// Prüflauf für Updates (Kapitel 56): Content, Datenbankstand und gespeicherte Daten prüfen, ohne etwas zu ändern.
if (args.Contains("--check")) return await Check(config, Directory.Exists(migrations) ? migrations : null);
var statusFile = Environment.GetEnvironmentVariable("ER_STATUS_FILE");
GameServer server;
try
{
    server = await GameServer.CreateAsync(config, Directory.Exists(migrations) ? migrations : null);
}
catch (Exception ex)
{
    Console.Error.WriteLine("Start fehlgeschlagen: " + ex.Message);
    WriteStatus("failed " + ex.Message.Replace('\n', ' '));
    return 1;
}
if (!server.Start())
{
    Console.Error.WriteLine($"Port {config.Port} konnte nicht geöffnet werden.");
    WriteStatus($"failed Port {config.Port} belegt");
    return 1;
}
Console.WriteLine($"Server läuft auf UDP-Port {config.Port} mit {config.TickRate} Ticks/s, Betriebsart {ServerConfig.ModeLabel(config.Mode)}. Strg+C beendet.");
if (config.DevCommands) Console.WriteLine("WARNUNG: Entwicklerbefehle sind aktiv – diesen Realm nicht öffentlich betreiben.");
WriteStatus($"running {server.Content.Version} {Environment.ProcessId} {DateTime.UtcNow:O}");
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
WriteStatus($"stopped {DateTime.UtcNow:O}");
stopped.Set();
return 0;

void WriteStatus(string text)
{
    if (string.IsNullOrEmpty(statusFile)) return;
    try { File.WriteAllText(statusFile, text + "\n"); } catch (Exception ex) { Console.Error.WriteLine("Statusdatei: " + ex.Message); }
}

static async Task<int> Check(ServerConfig config, string? migrations)
{
    int problems = 0;
    void Bad(string text) { problems++; Console.WriteLine("FEHLER: " + text); }
    EternalRealms.Shared.Content.ContentDatabase content;
    try { content = EternalRealms.Shared.Content.ContentDatabase.LoadFromDirectory(config.ContentPath); }
    catch (Exception ex) { Bad("Content nicht lesbar: " + ex.Message); return 1; }
    var errors = content.Validate();
    foreach (var e in errors.Take(30)) Bad("Content: " + e);
    Console.WriteLine($"Content {content.Version}: {content.Items.Count} Gegenstände, {content.Quests.Count} Quests, {content.Migrations.Count} Content-Migrationen{(errors.Count == 0 ? ", gültig" : "")}");
    Console.WriteLine("Betriebsart: " + ServerConfig.ModeLabel(config.Mode));
    if (string.IsNullOrWhiteSpace(config.ConnectionString)) { Console.WriteLine("Datenbank: keine (Arbeitsspeicher)"); return problems == 0 ? 0 : 1; }
    if (migrations == null) { Bad("Migrationsordner fehlt (ER_MIGRATIONS)"); return 1; }
    try
    {
        using var store = new EternalRealms.Server.Persistence.PostgresStore(config.ConnectionString);
        var (applied, pending, unknown) = await store.MigrationStatusAsync(migrations);
        Console.WriteLine($"Datenbank: {applied.Count} Migrationen angewendet, ausstehend: {(pending.Count == 0 ? "keine" : string.Join(", ", pending))}");
        if (unknown.Count > 0) Bad("Datenbank ist neuer als dieser Server: " + string.Join(", ", unknown));
        if (pending.Count == 0 && unknown.Count == 0)
        {
            var (n, affected, lines) = await EternalRealms.Server.Persistence.ContentMigrator.InspectAsync(store, content, 30);
            Console.WriteLine($"Gespeicherte Daten: {n} Charaktere, {affected} würden beim nächsten Laden angepasst");
            foreach (var l in lines) Console.WriteLine("  " + l);
        }
        else Console.WriteLine("Gespeicherte Daten: Prüfung nach dem Anwenden der Migrationen (beim Start) möglich");
    }
    catch (Exception ex) { Bad("Datenbank: " + ex.Message); }
    Console.WriteLine(problems == 0 ? "Prüfung bestanden." : $"Prüfung fehlgeschlagen ({problems} Probleme).");
    return problems == 0 ? 0 : 1;
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
