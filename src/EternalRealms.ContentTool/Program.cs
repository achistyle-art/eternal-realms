using EternalRealms.ContentTool;

// Eternal Realms – Content-Werkzeug (Kapitel 65)
//   er-content [--content <ordner>] <befehl> …
//   pruefen                          Content prüfen (Exitcode 1 bei Fehlern) – auch in der CI
//   version                          Content-Version mit Prüfsumme
//   stats                            Überblick über Umfang und Regionen
//   verweise <id>                    Wo wird eine ID verwendet?
//   umbenennen <alt> <neu> [--probe] ID überall ändern, Generatoren anpassen, Content-Migration für Spielstände anlegen
//   neu item|creature|quest <id> "<Name>" [--level N] [--giver npc] [--target kreatur] [--zone region] [--file datei]
var argList = args.ToList();
string? contentDir = null;
int ci = argList.IndexOf("--content");
if (ci >= 0 && ci + 1 < argList.Count) { contentDir = argList[ci + 1]; argList.RemoveRange(ci, 2); }
contentDir ??= FindContent();
if (contentDir == null) { Console.Error.WriteLine("Content-Ordner nicht gefunden (--content <ordner>)."); return 2; }
if (argList.Count == 0) { Help(); return 2; }

var options = new Dictionary<string, string>();
var positional = new List<string>();
for (int i = 1; i < argList.Count; i++)
{
    if (argList[i].StartsWith("--"))
    {
        var key = argList[i][2..];
        if (i + 1 < argList.Count && !argList[i + 1].StartsWith("--")) options[key] = argList[++i];
        else options[key] = "true";
    }
    else positional.Add(argList[i]);
}

var editor = new ContentEditor(contentDir);
try
{
    switch (argList[0])
    {
        case "pruefen":
        case "validate":
        {
            var errors = editor.Validate();
            foreach (var e in errors) Console.WriteLine("FEHLER: " + e);
            Console.WriteLine(errors.Count == 0 ? $"Content gültig ({editor.Load().Version})." : $"{errors.Count} Fehler.");
            return errors.Count == 0 ? 0 : 1;
        }
        case "version":
            Console.WriteLine(editor.Load().Version);
            return 0;
        case "stats":
            Console.Write(editor.Stats());
            return 0;
        case "verweise":
        {
            if (positional.Count < 1) { Help(); return 2; }
            var refs = editor.FindReferences(positional[0]);
            foreach (var r in refs) Console.WriteLine($"{r.File}:{r.Line}: {r.Text}");
            var kinds = string.Join(", ", ContentEditor.KindsOf(editor.Load(), positional[0]));
            Console.WriteLine($"{refs.Count} Verweise. Art: {(kinds.Length > 0 ? kinds : "keine Definition")}");
            return 0;
        }
        case "umbenennen":
        {
            if (positional.Count < 2) { Help(); return 2; }
            var res = editor.Rename(positional[0], positional[1], options.ContainsKey("probe"));
            foreach (var f in res.ChangedFiles) Console.WriteLine("  " + f);
            foreach (var m in res.Messages) Console.WriteLine(m);
            return res.Success ? 0 : 1;
        }
        case "neu":
        {
            if (positional.Count < 3) { Help(); return 2; }
            var (ok, msg) = editor.New(positional[0], positional[1], positional[2], options);
            Console.WriteLine(msg);
            return ok ? 0 : 1;
        }
        default:
            Help();
            return 2;
    }
}
catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine("Fehler: " + ex.Message);
    return 1;
}

static void Help() => Console.WriteLine(@"Eternal Realms – Content-Werkzeug
  er-content [--content <ordner>] pruefen | version | stats
  er-content verweise <id>
  er-content umbenennen <alt> <neu> [--probe]
  er-content neu item <id> ""<Name>"" [--level N] [--quality Common] [--price Kupfer] [--file items/eigene.json]
  er-content neu creature <id> ""<Name>"" [--level N] [--family Beast] [--file creatures/eigene.json]
  er-content neu quest <id> ""<Titel>"" --giver <npc> --target <kreatur> [--count 6] [--level N] [--zone region]");

static string? FindContent()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "content", "races.json"))) dir = dir.Parent;
    return dir == null ? null : Path.Combine(dir.FullName, "content");
}
