using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EternalRealms.Shared.Content;

namespace EternalRealms.ContentTool
{
    /// <summary>
    /// Content-Editor (Kapitel 65) für die Kommandozeile: prüfen, auswerten, Verweise finden, IDs sicher umbenennen
    /// (alle Dateien, Generatoren und eine Content-Migration für gespeicherte Daten) und neue Einträge aus Vorlagen anlegen.
    /// Jede Änderung wird anschließend geprüft; ist der Content danach ungültig, werden alle Dateien zurückgesetzt.
    /// </summary>
    public sealed class ContentEditor
    {
        public string Root { get; }
        /// <summary>Ordner der Python-Generatoren (tools/gen_*.py), deren Zeichenketten beim Umbenennen mitgeändert werden.</summary>
        public string? ToolsDir { get; }

        public ContentEditor(string contentRoot, string? toolsDir = null)
        {
            Root = Path.GetFullPath(contentRoot);
            ToolsDir = toolsDir ?? (Directory.Exists(Path.Combine(Root, "..", "tools")) ? Path.GetFullPath(Path.Combine(Root, "..", "tools")) : null);
        }

        public ContentDatabase Load() => ContentDatabase.LoadFromDirectory(Root);

        public List<string> Validate() => Load().Validate();

        private IEnumerable<string> ContentFiles() => Directory.GetFiles(Root, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal);

        private IEnumerable<string> GeneratorFiles() =>
            ToolsDir == null ? Enumerable.Empty<string>() : Directory.GetFiles(ToolsDir, "gen_*.py").OrderBy(f => f, StringComparer.Ordinal);

        /// <summary>Die ID als ganze Zeichenkette ("id" oder 'id') oder als Rang-ID ("id#2").</summary>
        private static Regex TokenPattern(string id) => new("([\"'])" + Regex.Escape(id) + "(?=[\"'#])", RegexOptions.CultureInvariant);

        private string Relative(string file) => Path.GetRelativePath(Path.GetDirectoryName(Root)!, file).Replace('\\', '/');

        // ------------------------------------------------------------ Verweise

        public List<(string File, int Line, string Text)> FindReferences(string id)
        {
            var pattern = TokenPattern(id);
            var result = new List<(string, int, string)>();
            foreach (var file in ContentFiles().Concat(GeneratorFiles()))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (pattern.IsMatch(lines[i])) result.Add((Relative(file), i + 1, lines[i].Trim()));
            }
            return result;
        }

        /// <summary>Welche Arten von Einträgen tragen diese ID (Gegenstand, Quest …)?</summary>
        public static List<string> KindsOf(ContentDatabase db, string id)
        {
            var kinds = new List<string>();
            if (db.Items.ContainsKey(id)) kinds.Add("item");
            if (db.Quests.ContainsKey(id)) kinds.Add("quest");
            if (db.AbilityRanks.ContainsKey(id) || db.Abilities.ContainsKey(id)) kinds.Add("ability");
            if (db.Achievements.ContainsKey(id)) kinds.Add("achievement");
            if (db.Zones.ContainsKey(id)) kinds.Add("zone");
            if (db.Creatures.ContainsKey(id)) kinds.Add("creature");
            if (db.Auras.ContainsKey(id)) kinds.Add("aura");
            if (db.Objects.ContainsKey(id)) kinds.Add("object");
            if (db.Mounts.ContainsKey(id)) kinds.Add("mount");
            if (db.Recipes.ContainsKey(id)) kinds.Add("recipe");
            if (db.Professions.ContainsKey(id)) kinds.Add("profession");
            if (db.Reputations.ContainsKey(id)) kinds.Add("reputation");
            if (db.Talents.ContainsKey(id)) kinds.Add("talent");
            if (db.WorldEvents.ContainsKey(id)) kinds.Add("event");
            if (db.TravelNodes.ContainsKey(id)) kinds.Add("travel");
            if (db.Emotes.ContainsKey(id)) kinds.Add("emote");
            return kinds;
        }

        // ------------------------------------------------------------ Umbenennen

        public sealed class RenameResult
        {
            public bool Success;
            public List<string> Messages = new();
            public List<string> ChangedFiles = new();
            public string? MigrationId;
        }

        public RenameResult Rename(string oldId, string newId, bool dryRun = false)
        {
            var res = new RenameResult();
            if (!Regex.IsMatch(newId, "^[a-z0-9_]{3,64}$")) { res.Messages.Add("Neue ID: 3–64 Zeichen, nur a–z, 0–9 und _."); return res; }
            var db = Load();
            var kinds = KindsOf(db, oldId);
            if (kinds.Count == 0) { res.Messages.Add($"Keine Definition mit der ID „{oldId}“."); return res; }
            if (KindsOf(db, newId).Count > 0) { res.Messages.Add($"Die ID „{newId}“ ist bereits vergeben."); return res; }
            var pattern = TokenPattern(oldId);
            var originals = new Dictionary<string, string>();
            bool generatedTouched = false, generatorTouched = false;
            foreach (var file in ContentFiles().Concat(GeneratorFiles()))
            {
                var text = File.ReadAllText(file);
                if (!pattern.IsMatch(text)) continue;
                originals[file] = text;
                bool isGenerator = file.EndsWith(".py", StringComparison.Ordinal);
                generatorTouched |= isGenerator;
                generatedTouched |= !isGenerator && (text.StartsWith("//") || Path.GetFileName(file) == "items.json");
                res.ChangedFiles.Add(Relative(file));
                if (!dryRun) File.WriteAllText(file, pattern.Replace(text, "$1" + newId));
            }
            // Gespeicherte Daten verweisen auf diese Arten: Content-Migration anlegen, damit Spielstände mitziehen.
            var persisted = kinds.Intersect(new[] { "item", "quest", "ability", "achievement", "zone" }).ToList();
            if (persisted.Count > 0)
            {
                res.MigrationId = $"{DateTime.UtcNow:yyyyMMdd}-umbenannt-{oldId}";
                var migFile = Path.Combine(Root, "migrations.json");
                if (!dryRun)
                {
                    if (!originals.ContainsKey(migFile)) originals[migFile] = File.Exists(migFile) ? File.ReadAllText(migFile) : "";
                    var current = File.Exists(migFile) ? File.ReadAllText(migFile) : "";
                    var list = current.Trim().Length > 0 ? JsonNode.Parse(current)!.AsArray() : new JsonArray();
                    var entry = new JsonObject { ["id"] = res.MigrationId, ["description"] = $"{string.Join("/", persisted)} {oldId} → {newId}" };
                    foreach (var kind in persisted)
                    {
                        string key = kind switch { "item" => "renameItems", "quest" => "renameQuests", "ability" => "renameAbilities", "achievement" => "renameAchievements", _ => "renameZones" };
                        entry[key] = new JsonObject { [oldId] = newId };
                    }
                    list.Add(entry);
                    File.WriteAllText(migFile, Format(list) + "\n");
                }
                if (!res.ChangedFiles.Contains("content/migrations.json")) res.ChangedFiles.Add("content/migrations.json");
            }
            if (dryRun) { res.Success = true; res.Messages.Add($"Probelauf: {res.ChangedFiles.Count} Dateien würden geändert."); return res; }
            var errors = Validate();
            if (errors.Count > 0)
            {
                Restore(originals);
                res.Messages.Add("Nach dem Umbenennen ungültig – alle Änderungen zurückgesetzt:");
                res.Messages.AddRange(errors.Take(20));
                return res;
            }
            res.Success = true;
            res.Messages.Add($"{string.Join("/", kinds)} „{oldId}“ heißt jetzt „{newId}“ ({res.ChangedFiles.Count} Dateien).");
            if (generatedTouched && !generatorTouched)
                res.Messages.Add("Hinweis: Eine erzeugte Datei wurde geändert, im Generator aber kein Text gefunden – bitte den Generator von Hand anpassen, sonst überschreibt er die Änderung.");
            else if (generatorTouched)
                res.Messages.Add("Generatoren wurden mitgeändert – beim nächsten Erzeugen bleibt die neue ID erhalten.");
            if (res.MigrationId != null) res.Messages.Add($"Content-Migration {res.MigrationId} angelegt: gespeicherte Charaktere, Post und Auktionen ziehen beim nächsten Start mit.");
            return res;
        }

        private static void Restore(Dictionary<string, string> originals)
        {
            foreach (var (file, text) in originals)
            {
                if (text.Length == 0 && Path.GetFileName(file) == "migrations.json" && File.Exists(file)) File.Delete(file);
                else File.WriteAllText(file, text);
            }
        }

        // ------------------------------------------------------------ Neue Einträge

        /// <summary>Legt einen Eintrag aus einer Vorlage an (item, creature, quest) und prüft danach.</summary>
        public (bool Success, string Message) New(string kind, string id, string name, IReadOnlyDictionary<string, string> opt)
        {
            if (!Regex.IsMatch(id, "^[a-z0-9_]{3,64}$")) return (false, "ID: 3–64 Zeichen, nur a–z, 0–9 und _.");
            var db = Load();
            if (KindsOf(db, id).Count > 0) return (false, $"Die ID „{id}“ ist bereits vergeben.");
            int level = opt.TryGetValue("level", out var lv) && int.TryParse(lv, out var l) ? Math.Clamp(l, 1, 60) : 1;
            JsonObject entry;
            string file;
            switch (kind)
            {
                case "item":
                    file = opt.GetValueOrDefault("file", "items/eigene.json");
                    entry = new JsonObject
                    {
                        ["id"] = id, ["name"] = name, ["description"] = opt.GetValueOrDefault("description", ""), ["quality"] = opt.GetValueOrDefault("quality", "Common"),
                        ["category"] = opt.GetValueOrDefault("category", "Misc"), ["itemLevel"] = level, ["requiredLevel"] = 0, ["maxStack"] = 20,
                        ["sellPrice"] = int.TryParse(opt.GetValueOrDefault("price", "1"), out var pr) ? pr : 1, ["buyPrice"] = 0, ["modifiers"] = new JsonArray(),
                    };
                    break;
                case "creature":
                    file = opt.GetValueOrDefault("file", "creatures/eigene.json");
                    entry = new JsonObject
                    {
                        ["id"] = id, ["name"] = name, ["minLevel"] = level, ["maxLevel"] = level, ["family"] = opt.GetValueOrDefault("family", "Beast"),
                        ["faction"] = opt.GetValueOrDefault("faction", "Hostile"), ["moneyMin"] = level * 3, ["moneyMax"] = level * 8, ["loot"] = new JsonArray(),
                    };
                    break;
                case "quest":
                    if (!opt.TryGetValue("giver", out var giver) || !db.Creatures.ContainsKey(giver)) return (false, "Quest: --giver <NPC-ID> fehlt oder ist unbekannt.");
                    if (!opt.TryGetValue("target", out var target) || !db.Creatures.ContainsKey(target)) return (false, "Quest: --target <Kreatur-ID> fehlt oder ist unbekannt.");
                    var zone = opt.GetValueOrDefault("zone", db.Zones.Values.FirstOrDefault(z => z.Npcs.Any(n => n.Npc == giver))?.Id ?? "");
                    if (!db.Zones.ContainsKey(zone)) return (false, "Quest: --zone <Region> fehlt oder ist unbekannt.");
                    file = opt.GetValueOrDefault("file", "quests/eigene.json");
                    int count = int.TryParse(opt.GetValueOrDefault("count", "6"), out var n) ? n : 6;
                    entry = new JsonObject
                    {
                        ["id"] = id, ["title"] = name, ["category"] = "Side", ["zone"] = zone, ["level"] = level, ["minLevel"] = Math.Max(1, level - 2),
                        ["faction"] = opt.GetValueOrDefault("faction", db.Creatures[giver].Faction.ToString()), ["class"] = "None", ["giver"] = giver,
                        ["turnIn"] = opt.GetValueOrDefault("turnin", giver), ["requires"] = new JsonArray(),
                        ["text"] = $"(Questtext für „{name}“ schreiben.)", ["objectivesText"] = $"Besiegt {count} {db.Creatures[target].Name}.",
                        ["progressText"] = "Habt Ihr es schon erledigt?", ["completionText"] = "Gut gemacht.",
                        ["objectives"] = new JsonArray(new JsonObject { ["type"] = "Kill", ["target"] = target, ["count"] = count, ["description"] = db.Creatures[target].Name + " besiegt" }),
                        ["rewardXp"] = 60 + level * 45, ["rewardMoney"] = level * 25,
                    };
                    break;
                default:
                    return (false, "Unbekannte Art – möglich sind item, creature, quest.");
            }
            var path = Path.Combine(Root, file);
            string? original = File.Exists(path) ? File.ReadAllText(path) : null;
            if (original != null && (original.StartsWith("//") || Path.GetFileName(path) == "items.json"))
                return (false, $"content/{file} ist eine erzeugte Datei – bitte eine andere Datei wählen (--file).");
            var list = original != null && original.Trim().Length > 0 ? JsonNode.Parse(original)!.AsArray() : new JsonArray();
            list.Add(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Format(list) + (original == null || original.EndsWith("\n") ? "\n" : ""));
            var errors = Validate();
            if (errors.Count > 0)
            {
                if (original == null) File.Delete(path); else File.WriteAllText(path, original);
                return (false, "Ungültig – nicht angelegt:\n  " + string.Join("\n  ", errors.Take(10)));
            }
            return (true, $"{kind} „{id}“ in content/{file} angelegt.");
        }

        /// <summary>JSON wie die übrigen Content-Dateien formatieren: Einrückung 1, Umlaute unverändert.</summary>
        public static string Format(JsonNode node)
        {
            var json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var sb = new StringBuilder();
            foreach (var line in json.Replace("\r\n", "\n").Split('\n'))
            {
                int spaces = line.TakeWhile(c => c == ' ').Count();
                sb.Append(' ', spaces / 2).Append(line, spaces, line.Length - spaces).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        // ------------------------------------------------------------ Auswertung

        public string Stats()
        {
            var db = Load();
            var sb = new StringBuilder();
            sb.AppendLine($"Content {db.Version} – {db.Manifest.Name}");
            sb.AppendLine($"  {db.Races.Count} Völker, {db.Classes.Count} Klassen, {db.AbilityRanks.Count} Fähigkeiten ({db.Abilities.Count} Ränge), {db.Talents.Count} Talente");
            sb.AppendLine($"  {db.Items.Count} Gegenstände, {db.Creatures.Count} Kreaturen ({db.Creatures.Values.Count(c => c.Boss)} Bosse), {db.Quests.Count} Quests, {db.Zones.Count} Regionen");
            sb.AppendLine($"  {db.Recipes.Count} Rezepte, {db.Achievements.Count} Erfolge, {db.WorldEvents.Count} Weltereignisse, {db.Migrations.Count} Content-Migrationen");
            sb.AppendLine("Gegenstände nach Qualität: " + string.Join(", ", db.Items.Values.GroupBy(i => i.Quality).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
            sb.AppendLine("Regionen:");
            foreach (var z in db.Zones.Values.Where(z => z.Playable).OrderBy(z => z.MinLevel))
            {
                var mobs = z.Spawns.Select(s => db.Creatures.TryGetValue(s.Creature, out var c) ? c : null).Where(c => c != null && !c.IsNpc).ToList();
                int quests = db.Quests.Values.Count(q => q.Zone == z.Id);
                string levels = mobs.Count > 0 ? $"(St. {mobs.Min(c => c!.MinLevel)}–{mobs.Max(c => c!.MaxLevel)})" : "";
                sb.AppendLine($"  {z.Name,-24} Stufe {z.MinLevel,2}–{z.MaxLevel,-2} {(z.IsInstance ? $"Dungeon ({z.MaxPlayers})" : ""),-12} {mobs.Count,3} Gegnerarten {levels,-12} {quests,3} Quests");
            }
            return sb.ToString();
        }
    }
}
