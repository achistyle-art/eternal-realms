using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EternalRealms.Shared.Content
{
    /// <summary>
    /// Lädt alle Inhalte aus dem content-Verzeichnis, löst Fähigkeitsränge auf und prüft Querverweise.
    /// Ein Inhaltsfehler (Quest verweist auf unbekannten NPC usw.) verhindert den Serverstart.
    /// </summary>
    public sealed class ContentDatabase
    {
        public ContentManifest Manifest { get; private set; } = new ContentManifest();
        public List<ContentMigrationDef> Migrations { get; } = new List<ContentMigrationDef>();
        /// <summary>SHA-256 über alle Content-Dateien (Zeilenenden vereinheitlicht), hexadezimal.</summary>
        public string Hash { get; private set; } = "";
        /// <summary>Versionskennung für Client/Server-Abgleich, z. B. „0.9.0+3fa2c1d0“.</summary>
        /// <summary>Mit der aktuellen Stufengrenze erreichbar? Erfolge für höhere Stufen bleiben bis zu späteren Inhalten verborgen.</summary>
        public bool IsReachable(AchievementDef a) => !a.Criteria.Any(c => c.Event == ProgressEvent.ReachLevel && c.Count > LevelCap);

        /// <summary>Stufenobergrenze des Contents (1–60).</summary>
        public int LevelCap => Math.Max(1, Math.Min(Rules.Experience.MaxLevel, Manifest.LevelCap));
        public string Version => Hash.Length >= 8 ? Manifest.Version + "+" + Hash.Substring(0, 8) : Manifest.Version;
        public Dictionary<Faction, FactionDef> Factions { get; } = new Dictionary<Faction, FactionDef>();
        public Dictionary<Race, RaceDef> Races { get; } = new Dictionary<Race, RaceDef>();
        public Dictionary<CharClass, ClassDef> Classes { get; } = new Dictionary<CharClass, ClassDef>();
        /// <summary>Alle Fähigkeiten nach Rang-ID ("mage_fireball#2").</summary>
        public Dictionary<string, AbilityDef> Abilities { get; } = new Dictionary<string, AbilityDef>();
        /// <summary>Basis-ID -> Ränge aufsteigend.</summary>
        public Dictionary<string, List<AbilityDef>> AbilityRanks { get; } = new Dictionary<string, List<AbilityDef>>();
        public Dictionary<string, AuraDef> Auras { get; } = new Dictionary<string, AuraDef>();
        public Dictionary<string, TalentTreeDef> TalentTrees { get; } = new Dictionary<string, TalentTreeDef>();
        public Dictionary<string, TalentDef> Talents { get; } = new Dictionary<string, TalentDef>();
        public Dictionary<string, string> TalentToTree { get; } = new Dictionary<string, string>();
        public Dictionary<string, ItemDef> Items { get; } = new Dictionary<string, ItemDef>();
        public Dictionary<string, CreatureDef> Creatures { get; } = new Dictionary<string, CreatureDef>();
        public Dictionary<string, GameObjectDef> Objects { get; } = new Dictionary<string, GameObjectDef>();
        public Dictionary<string, ZoneDef> Zones { get; } = new Dictionary<string, ZoneDef>();
        public Dictionary<string, QuestDef> Quests { get; } = new Dictionary<string, QuestDef>();
        public Dictionary<string, ReputationDef> Reputations { get; } = new Dictionary<string, ReputationDef>();
        public Dictionary<string, MountDef> Mounts { get; } = new Dictionary<string, MountDef>();
        /// <summary>Reitstufen, aufsteigend nach Fertigkeit.</summary>
        public List<RidingRankDef> RidingRanks { get; } = new List<RidingRankDef>();

        /// <summary>Kleinste Reitstufe, die ein Reittier dieses Tempos erlaubt (null: keine).</summary>
        public RidingRankDef? RidingRankFor(int speed) => RidingRanks.FirstOrDefault(r => r.Speed >= speed);
        public Dictionary<string, ProfessionDef> Professions { get; } = new Dictionary<string, ProfessionDef>();
        public Dictionary<string, RecipeDef> Recipes { get; } = new Dictionary<string, RecipeDef>();
        public Dictionary<string, TravelNodeDef> TravelNodes { get; } = new Dictionary<string, TravelNodeDef>();
        public List<FlightPathDef> FlightPaths { get; } = new List<FlightPathDef>();
        public List<BotDialogDef> BotDialog { get; } = new List<BotDialogDef>();
        public Dictionary<string, EmoteDef> Emotes { get; } = new Dictionary<string, EmoteDef>();
        public Dictionary<string, WorldEventDef> WorldEvents { get; } = new Dictionary<string, WorldEventDef>();
        public Dictionary<string, AchievementDef> Achievements { get; } = new Dictionary<string, AchievementDef>();
        public List<TutorialStepDef> Tutorial { get; } = new List<TutorialStepDef>();
        /// <summary>Chatbefehl ohne Schrägstrich (klein) → Emote.</summary>
        public Dictionary<string, EmoteDef> EmoteCommands { get; } = new Dictionary<string, EmoteDef>();
        public List<BotProfileDef> BotProfiles { get; } = new List<BotProfileDef>();

        public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

        public static JsonSerializerOptions CreateOptions()
        {
            var o = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            o.Converters.Add(new JsonStringEnumConverter());
            o.Converters.Add(new Vec3JsonConverter());
            return o;
        }

        // ----------------------------------------------------------------- Laden

        public static ContentDatabase LoadFromDirectory(string root)
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Content-Verzeichnis nicht gefunden: " + root);
            var db = new ContentDatabase();
            var manifest = Path.Combine(root, "manifest.json");
            if (File.Exists(manifest)) db.Manifest = JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(manifest), CreateOptions()) ?? new ContentManifest();
            db.Migrations.AddRange(ReadList<ContentMigrationDef>(root, "migrations"));
            db.Hash = ComputeHash(root);
            foreach (var f in ReadList<FactionDef>(root, "factions")) db.Factions[f.Id] = f;
            foreach (var r in ReadList<RaceDef>(root, "races")) db.Races[r.Id] = r;
            foreach (var c in ReadList<ClassDef>(root, "classes")) db.Classes[c.Id] = c;
            foreach (var a in ReadList<AuraDef>(root, "auras")) AddUnique(db.Auras, a.Id, a, "Aura");
            foreach (var a in ReadList<AbilityDef>(root, "abilities")) db.AddAbility(a);
            foreach (var t in ReadList<TalentTreeDef>(root, "talents"))
            {
                AddUnique(db.TalentTrees, t.Id, t, "Talentbaum");
                foreach (var tal in t.Talents) { AddUnique(db.Talents, tal.Id, tal, "Talent"); db.TalentToTree[tal.Id] = t.Id; }
            }
            foreach (var i in ReadList<ItemDef>(root, "items")) AddUnique(db.Items, i.Id, i, "Gegenstand");
            foreach (var c in ReadList<CreatureDef>(root, "creatures")) AddUnique(db.Creatures, c.Id, c, "Kreatur");
            foreach (var o in ReadList<GameObjectDef>(root, "objects")) AddUnique(db.Objects, o.Id, o, "Objekt");
            foreach (var z in ReadList<ZoneDef>(root, "zones")) AddUnique(db.Zones, z.Id, z, "Zone");
            foreach (var q in ReadList<QuestDef>(root, "quests")) AddUnique(db.Quests, q.Id, q, "Quest");
            foreach (var r in ReadList<ReputationDef>(root, "reputations")) AddUnique(db.Reputations, r.Id, r, "Ruf");
            foreach (var m in ReadList<MountDef>(root, "mounts")) AddUnique(db.Mounts, m.Id, m, "Reittier");
            db.RidingRanks.AddRange(ReadList<RidingRankDef>(root, "riding").OrderBy(r => r.Skill));
            foreach (var pr in ReadList<ProfessionDef>(root, "professions")) AddUnique(db.Professions, pr.Id, pr, "Beruf");
            foreach (var r in ReadList<RecipeDef>(root, "recipes")) AddUnique(db.Recipes, r.Id, r, "Rezept");
            foreach (var n in ReadList<TravelNodeDef>(root, "travel/nodes")) AddUnique(db.TravelNodes, n.Id, n, "Reisepunkt");
            db.FlightPaths.AddRange(ReadList<FlightPathDef>(root, "travel/paths"));
            db.BotDialog.AddRange(ReadList<BotDialogDef>(root, "bots/dialog"));
            db.BotProfiles.AddRange(ReadList<BotProfileDef>(root, "bots/profiles"));
            foreach (var ev in ReadList<WorldEventDef>(root, "events")) AddUnique(db.WorldEvents, ev.Id, ev, "Weltereignis");
            foreach (var a in ReadList<AchievementDef>(root, "achievements")) AddUnique(db.Achievements, a.Id, a, "Erfolg");
            db.Tutorial.AddRange(ReadList<TutorialStepDef>(root, "tutorial"));
            foreach (var e in ReadList<EmoteDef>(root, "emotes"))
            {
                AddUnique(db.Emotes, e.Id, e, "Emote");
                foreach (var c in e.Commands) AddUnique(db.EmoteCommands, c.ToLowerInvariant(), e, "Emote-Befehl");
            }
            return db;
        }

        /// <summary>
        /// Prüfsumme über alle JSON-Dateien (Pfad relativ, mit „/“, und Inhalt ohne Wagenrücklauf) – identisch auf Windows und Linux,
        /// damit Server und Client (StreamingAssets) denselben Stand erkennen.
        /// </summary>
        public static string ComputeHash(string root)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(f => (Rel: f.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/'), Full: f))
                .OrderBy(f => f.Rel, StringComparer.Ordinal);
            using var ms = new MemoryStream();
            foreach (var (rel, full) in files)
            {
                var name = System.Text.Encoding.UTF8.GetBytes(rel + "\n");
                ms.Write(name, 0, name.Length);
                var bytes = File.ReadAllBytes(full).Where(b => b != (byte)'\r').ToArray();
                ms.Write(bytes, 0, bytes.Length);
                ms.WriteByte(0);
            }
            return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Liest "name.json" (Array) und zusätzlich alle "name/*.json" (je Datei ein Array). So können
        /// große Inhaltsmengen auf viele Dateien verteilt werden, z. B. eine Datei pro Zone.
        /// </summary>
        private static List<T> ReadList<T>(string root, string name)
        {
            var result = new List<T>();
            var single = Path.Combine(root, name + ".json");
            if (File.Exists(single)) result.AddRange(ReadFile<T>(single));
            var dir = Path.Combine(root, name);
            if (Directory.Exists(dir))
                foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                    result.AddRange(ReadFile<T>(file));
            return result;
        }

        private static List<T> ReadFile<T>(string file)
        {
            try
            {
                return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(file), JsonOptions) ?? new List<T>();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Inhaltsdatei {file} ist fehlerhaft: {ex.Message}", ex);
            }
        }

        private static void AddUnique<T>(Dictionary<string, T> dict, string id, T value, string kind)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"{kind} ohne ID gefunden.");
            if (dict.ContainsKey(id)) throw new InvalidDataException($"{kind} '{id}' ist doppelt definiert.");
            dict[id] = value;
        }

        /// <summary>Fügt eine Fähigkeit hinzu und erzeugt aus der Rangtabelle einzelne Ränge.</summary>
        public void AddAbility(AbilityDef template)
        {
            if (AbilityRanks.ContainsKey(template.Id)) throw new InvalidDataException($"Fähigkeit '{template.Id}' ist doppelt definiert.");
            var ranks = new List<AbilityDef>();
            if (template.Ranks.Count == 0)
            {
                template.Rank = 1;
                ranks.Add(template);
            }
            else
            {
                for (int i = 0; i < template.Ranks.Count; i++)
                {
                    var r = template.Ranks[i];
                    var a = CloneAbility(template);
                    a.Rank = i + 1;
                    a.Level = r.Level;
                    if (r.Cost > 0) a.Cost = r.Cost;
                    a.TrainingCost = r.TrainingCost;
                    for (int e = 0; e < r.Values.Count && e < a.Effects.Count; e++)
                    {
                        var v = r.Values[e];
                        if (v == null || v.Length == 0) continue;
                        a.Effects[e].Min = v[0];
                        a.Effects[e].Max = v.Length > 1 ? v[1] : v[0];
                    }
                    if (r.Aura != null)
                        foreach (var e in a.Effects)
                            if (e.Type == EffectType.ApplyAura) { e.Aura = r.Aura; break; }
                    ranks.Add(a);
                }
            }
            AbilityRanks[template.Id] = ranks;
            foreach (var a in ranks) Abilities[a.RankedId] = a;
        }

        private static AbilityDef CloneAbility(AbilityDef t)
        {
            var json = JsonSerializer.Serialize(t, JsonOptions);
            var a = JsonSerializer.Deserialize<AbilityDef>(json, JsonOptions)!;
            a.Ranks = new List<AbilityRankDef>();
            return a;
        }

        // ----------------------------------------------------------------- Abfragen

        public AbilityDef? GetAbilityRank(string baseId, int rank)
        {
            if (!AbilityRanks.TryGetValue(baseId, out var list) || list.Count == 0) return null;
            if (rank < 1) rank = 1;
            return list[Math.Min(rank, list.Count) - 1];
        }

        /// <summary>Höchster Rang, den eine Figur dieser Stufe lernen kann (0 = noch keiner).</summary>
        public int HighestRankForLevel(string baseId, int level)
        {
            if (!AbilityRanks.TryGetValue(baseId, out var list)) return 0;
            int best = 0;
            foreach (var a in list) if (a.Level <= level) best = a.Rank;
            return best;
        }

        public IEnumerable<AbilityDef> ClassAbilityBases(CharClass cls) =>
            AbilityRanks.Values.Select(l => l[0]).Where(a => a.Class == cls);

        public IEnumerable<QuestDef> QuestsGivenBy(string npcId) => Quests.Values.Where(q => q.Giver == npcId);
        public IEnumerable<QuestDef> QuestsEndingAt(string npcId) => Quests.Values.Where(q => q.TurnIn == npcId);

        public BotProfileDef? GetBotProfile(CharClass cls, Role role) =>
            BotProfiles.FirstOrDefault(p => p.Class == cls && p.Role == role);

        public IEnumerable<Role> BotRolesFor(CharClass cls) => BotProfiles.Where(p => p.Class == cls).Select(p => p.Role).Distinct();

        public RaceDef RaceFor(Race r) => Races[r];
        public ClassDef ClassFor(CharClass c) => Classes[c];

        // ----------------------------------------------------------------- Prüfung

        /// <summary>Prüft alle Querverweise. Rückgabe: Liste lesbarer Fehler (leer = in Ordnung).</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            void Need(bool ok, string message) { if (!ok) errors.Add(message); }
            bool HasAbility(string id) => AbilityRanks.ContainsKey(id) || Abilities.ContainsKey(id);

            var titles = new HashSet<string>();
            foreach (var a in Achievements.Values)
            {
                Need(a.Criteria.Count > 0, $"Erfolg {a.Id}: keine Bedingungen");
                Need(a.Points >= 0 && a.Points <= 100, $"Erfolg {a.Id}: Punkte 0–100");
                if (a.Title != null) Need(titles.Add(a.Title), $"Erfolg {a.Id}: Titel '{a.Title}' doppelt");
                foreach (var c in a.Criteria)
                {
                    Need(c.Count > 0, $"Erfolg {a.Id}: Anzahl muss positiv sein");
                    if (c.Target.Length == 0) continue;
                    bool ok = c.Event switch
                    {
                        ProgressEvent.Kill or ProgressEvent.KillBoss => Creatures.ContainsKey(c.Target),
                        ProgressEvent.CompleteQuest or ProgressEvent.AcceptQuest => Quests.ContainsKey(c.Target),
                        ProgressEvent.Discover => Zones.Values.Any(z => z.Areas.Any(ar => ar.Id == c.Target)),
                        ProgressEvent.VisitZone or ProgressEvent.EnterDungeon => Zones.ContainsKey(c.Target),
                        ProgressEvent.ProfessionSkill or ProgressEvent.LearnProfession or ProgressEvent.Craft or ProgressEvent.Gather => Professions.ContainsKey(c.Target),
                        ProgressEvent.WorldEvent => WorldEvents.ContainsKey(c.Target),
                        ProgressEvent.Emote => Emotes.ContainsKey(c.Target),
                        ProgressEvent.Reputation => Reputations.ContainsKey(c.Target),
                        _ => true,
                    };
                    Need(ok, $"Erfolg {a.Id}: Ziel '{c.Target}' für {c.Event} unbekannt");
                }
            }
            Need(Tutorial.Count == 0 || Tutorial.Select(t => t.Id).Distinct().Count() == Tutorial.Count, "Tutorial: doppelte Schritt-IDs");
            foreach (var t in Tutorial) Need(t.Trigger != null || t.AutoSeconds > 0, $"Tutorial {t.Id}: braucht Auslöser oder AutoSeconds");

            foreach (var ev in WorldEvents.Values)
            {
                Need(Zones.ContainsKey(ev.Zone), $"Ereignis {ev.Id}: Zone '{ev.Zone}' fehlt");
                Need(ev.Phases.Count > 0, $"Ereignis {ev.Id}: keine Abschnitte");
                Need(ev.IntervalMinutes.Count == 2 && ev.IntervalMinutes[0] > 0 && ev.IntervalMinutes[1] >= ev.IntervalMinutes[0], $"Ereignis {ev.Id}: Intervall ungültig");
                foreach (var ph in ev.Phases)
                {
                    Need(Creatures.ContainsKey(ph.Creature), $"Ereignis {ev.Id}: Kreatur '{ph.Creature}' fehlt");
                    Need(ph.Type != WorldEventPhaseType.Defend || Creatures.ContainsKey(ph.Defend), $"Ereignis {ev.Id}: zu schützender NPC '{ph.Defend}' fehlt");
                    Need(ph.Count > 0 && ph.PerWave > 0, $"Ereignis {ev.Id}: Anzahl ungültig");
                }
                foreach (var it in ev.Rewards.Items) Need(Items.ContainsKey(it.Item), $"Ereignis {ev.Id}: Gegenstand '{it.Item}' fehlt");
                foreach (var r in ev.Rewards.Reputation) Need(Reputations.ContainsKey(r.Faction), $"Ereignis {ev.Id}: Ruf '{r.Faction}' fehlt");
            }
            foreach (var e in Emotes.Values)
            {
                Need(e.Commands.Count > 0, $"Emote {e.Id}: keine Befehle");
                Need(e.TextNoTarget.Contains("{me}") && e.TextTarget.Contains("{target}"), $"Emote {e.Id}: Texte brauchen {{me}} bzw. {{target}}");
                Need(e.Animation == "" || Enum.IsDefined(typeof(Client.Anim.AnimClip), e.Animation), $"Emote {e.Id}: unbekannte Animation '{e.Animation}'");
                Need(e.Loop || e.Duration > 0, $"Emote {e.Id}: Dauer fehlt");
            }

            foreach (var race in Races.Values)
            {
                Need(Zones.ContainsKey(race.StartZone), $"Volk {race.Id}: Startzone '{race.StartZone}' fehlt");
                Need(race.Classes.Count > 0, $"Volk {race.Id}: keine Klassen");
                foreach (var c in race.Classes) Need(Classes.ContainsKey(c), $"Volk {race.Id}: Klasse {c} fehlt");
                Need(race.NameStarts.Count > 0 && race.NameEnds.Count > 0, $"Volk {race.Id}: Namenssilben fehlen");
                foreach (var t in race.Traits) if (t.Ability != null) Need(HasAbility(t.Ability), $"Volk {race.Id}: Fähigkeit '{t.Ability}' fehlt");
            }
            foreach (var cls in Classes.Values)
            {
                foreach (var a in cls.StartingAbilities) Need(HasAbility(a), $"Klasse {cls.Id}: Startfähigkeit '{a}' fehlt");
                foreach (var a in cls.PreviewAbilities) Need(HasAbility(a), $"Klasse {cls.Id}: Vorschau '{a}' fehlt");
                foreach (var i in cls.StartingItems) Need(Items.ContainsKey(i), $"Klasse {cls.Id}: Startgegenstand '{i}' fehlt");
                Need(cls.TalentTrees.Count == 3, $"Klasse {cls.Id}: braucht genau drei Talentbäume (hat {cls.TalentTrees.Count})");
                foreach (var t in cls.TalentTrees) Need(TalentTrees.ContainsKey(t), $"Klasse {cls.Id}: Talentbaum '{t}' fehlt");
            }
            foreach (var a in Abilities.Values)
            {
                foreach (var e in a.Effects)
                {
                    if (e.Aura != null) Need(Auras.ContainsKey(e.Aura), $"Fähigkeit {a.RankedId}: Aura '{e.Aura}' fehlt");
                    if (e.Type == EffectType.ApplyAura) Need(e.Aura != null, $"Fähigkeit {a.RankedId}: ApplyAura ohne Aura");
                    if (e.Type == EffectType.SummonPet) Need(e.Pet != null && Creatures.ContainsKey(e.Pet), $"Fähigkeit {a.RankedId}: Begleiter '{e.Pet}' fehlt");
                    if (e.Type == EffectType.Teleport) Need(e.Zone != null && (e.Zone.StartsWith("@") || Zones.ContainsKey(e.Zone)), $"Fähigkeit {a.RankedId}: Teleportziel '{e.Zone}' fehlt");
                }
                Need(a.Level >= 1 && a.Level <= 60, $"Fähigkeit {a.RankedId}: Stufe {a.Level} außerhalb 1-60");
                Need(a.Effects.Count > 0, $"Fähigkeit {a.RankedId}: hat keine Wirkung");
            }
            foreach (var tree in TalentTrees.Values)
            {
                Need(Classes.ContainsKey(tree.Class), $"Talentbaum {tree.Id}: Klasse fehlt");
                Need(tree.Talents.Count >= 5, $"Talentbaum {tree.Id}: zu wenige Talente ({tree.Talents.Count})");
                foreach (var t in tree.Talents)
                {
                    Need(t.Tier >= 0 && t.Tier <= 6, $"Talent {t.Id}: Reihe {t.Tier} ungültig");
                    Need(t.MaxRank >= 1, $"Talent {t.Id}: MaxRank < 1");
                    Need(t.Modifiers.Count > 0 || t.GrantsAbility != null, $"Talent {t.Id}: hat keine Wirkung");
                    if (t.Requires != null) Need(tree.Talents.Any(x => x.Id == t.Requires && x.Tier < t.Tier), $"Talent {t.Id}: Voraussetzung '{t.Requires}' fehlt oder liegt nicht darüber");
                    if (t.GrantsAbility != null) Need(HasAbility(t.GrantsAbility), $"Talent {t.Id}: Fähigkeit '{t.GrantsAbility}' fehlt");
                }
            }
            foreach (var item in Items.Values)
            {
                if (item.UseAbility != null) Need(HasAbility(item.UseAbility), $"Gegenstand {item.Id}: Fähigkeit '{item.UseAbility}' fehlt");
                if (item.StartsQuest != null) Need(Quests.ContainsKey(item.StartsQuest), $"Gegenstand {item.Id}: Quest '{item.StartsQuest}' fehlt");
                if (item.Category == ItemCategory.Weapon) Need(item.Speed > 0 && item.MaxDamage >= item.MinDamage, $"Gegenstand {item.Id}: Waffe ohne Tempo/Schaden");
            }
            foreach (var c in Creatures.Values)
            {
                Need(c.MinLevel >= 1 && c.MaxLevel >= c.MinLevel && c.MaxLevel <= 63, $"Kreatur {c.Id}: Stufenbereich ungültig");
                foreach (var l in c.Loot)
                {
                    Need(Items.ContainsKey(l.Item), $"Kreatur {c.Id}: Beute '{l.Item}' fehlt");
                    if (l.Quest != null) Need(Quests.ContainsKey(l.Quest), $"Kreatur {c.Id}: Beute-Quest '{l.Quest}' fehlt");
                }
                foreach (var a in c.Abilities.Concat(c.Phases.SelectMany(p => p.Abilities)))
                {
                    Need(HasAbility(a.Ability), $"Kreatur {c.Id}: Fähigkeit '{a.Ability}' fehlt");
                    if (a.Ground is GroundZoneDef g)
                        Need(g.Radius > 0 && g.Radius <= 30 && g.Warning >= 1 && g.Duration >= 0 && g.TickInterval >= 0.5f && g.Count is >= 1 and <= 10,
                             $"Kreatur {c.Id}: Bodeneffekt von '{a.Ability}' ungültig (Radius 0–30, Vorwarnung ≥ 1 s, Anzahl 1–10)");
                    if (a.MustInterrupt && HasAbility(a.Ability))
                        Need(GetAbilityRank(a.Ability, 1)!.CastTime >= 1.5f, $"Kreatur {c.Id}: Pflicht-Unterbrechung '{a.Ability}' braucht eine Zauberzeit ab 1,5 s");
                }
                if (c.EnrageAfter > 0) Need(HasAbility(c.EnrageAbility) && c.Boss, $"Kreatur {c.Id}: Wut nur für Bosse mit gültiger Fähigkeit '{c.EnrageAbility}'");
                foreach (var add in c.Phases.SelectMany(p => p.SummonAdds)) Need(Creatures.ContainsKey(add.Creature), $"Kreatur {c.Id}: Add '{add.Creature}' fehlt");
                foreach (var v in c.VendorItems) Need(Items.ContainsKey(v), $"Händler {c.Id}: Ware '{v}' fehlt");
                if (c.Roles.HasFlag(NpcRole.ClassTrainer)) Need(c.TrainerClass != CharClass.None, $"Trainer {c.Id}: keine Klasse");
                if (c.ReputationFaction != null) Need(Reputations.ContainsKey(c.ReputationFaction), $"Kreatur {c.Id}: Ruf '{c.ReputationFaction}' fehlt");
            }
            foreach (var o in Objects.Values)
            {
                foreach (var l in o.Loot) Need(Items.ContainsKey(l.Item), $"Objekt {o.Id}: Beute '{l.Item}' fehlt");
                if (o.Quest != null) Need(Quests.ContainsKey(o.Quest), $"Objekt {o.Id}: Quest '{o.Quest}' fehlt");
                if (o.TeleportZone != null) Need(o.TeleportZone.StartsWith("@") || Zones.ContainsKey(o.TeleportZone), $"Objekt {o.Id}: Zielzone '{o.TeleportZone}' fehlt");
            }
            foreach (var z in Zones.Values)
            {
                foreach (var s in z.Spawns) Need(Creatures.ContainsKey(s.Creature), $"Zone {z.Id}: Kreatur '{s.Creature}' fehlt");
                foreach (var n in z.Npcs) Need(Creatures.ContainsKey(n.Npc), $"Zone {z.Id}: NPC '{n.Npc}' fehlt");
                foreach (var o in z.Objects) Need(Objects.ContainsKey(o.Object), $"Zone {z.Id}: Objekt '{o.Object}' fehlt");
                if (z.Playable && !z.IsInstance) Need(z.Graveyards.Count > 0, $"Zone {z.Id}: spielbar, aber ohne Friedhof");
                if (z.ExitZone != null) Need(Zones.ContainsKey(z.ExitZone), $"Zone {z.Id}: Ausgangszone '{z.ExitZone}' fehlt");
                Need(z.MinLevel <= z.MaxLevel, $"Zone {z.Id}: Stufenbereich ungültig");
            }
            var npcZones = new Dictionary<string, string>();
            foreach (var z in Zones.Values) foreach (var n in z.Npcs) npcZones[n.Npc] = z.Id;
            foreach (var z in Zones.Values.Where(z => z.RequiredLevel > 0 && z.Playable))
                Need(z.RequiredLevel <= LevelCap, $"Zone {z.Id}: Mindeststufe {z.RequiredLevel} liegt über der Stufenobergrenze {LevelCap}");
            foreach (var it in Items.Values.Where(i => i.RequiredReputation != null))
                Need(Reputations.ContainsKey(it.RequiredReputation!) && it.RequiredReputationValue > 0 && it.RequiredReputationValue <= Rules.Reputation.Max,
                     $"Gegenstand {it.Id}: Rufanforderung ungültig");
            foreach (var q in Quests.Values)
            {
                Need(Creatures.ContainsKey(q.Giver), $"Quest {q.Id}: Questgeber '{q.Giver}' fehlt");
                Need(Creatures.ContainsKey(q.TurnIn), $"Quest {q.Id}: Abgabe-NPC '{q.TurnIn}' fehlt");
                Need(npcZones.ContainsKey(q.Giver) || Items.Values.Any(i => i.StartsQuest == q.Id), $"Quest {q.Id}: Questgeber '{q.Giver}' steht in keiner Zone");
                Need(npcZones.ContainsKey(q.TurnIn), $"Quest {q.Id}: Abgabe-NPC '{q.TurnIn}' steht in keiner Zone");
                Need(Zones.ContainsKey(q.Zone), $"Quest {q.Id}: Zone '{q.Zone}' fehlt");
                Need(q.Objectives.Count > 0, $"Quest {q.Id}: keine Ziele (Platzhalterquests sind nicht erlaubt)");
                Need(!string.IsNullOrWhiteSpace(q.Text) && !string.IsNullOrWhiteSpace(q.CompletionText), $"Quest {q.Id}: Questtext fehlt");
                Need(q.RewardXp > 0 || q.RewardItems.Count > 0 || q.RewardChoices.Count > 0 || q.RewardAbility != null, $"Quest {q.Id}: keine Belohnung");
                Need(q.MinLevel <= q.Level, $"Quest {q.Id}: Mindeststufe über Queststufe");
                foreach (var r in q.Requires) Need(Quests.ContainsKey(r), $"Quest {q.Id}: Vorgänger '{r}' fehlt");
                foreach (var r in q.ExclusiveWith) Need(Quests.ContainsKey(r), $"Quest {q.Id}: Alternative '{r}' fehlt");
                // Tagesquests landen nie in den abgeschlossenen Quests – sie dürfen nichts freischalten.
                foreach (var r in q.Requires.Concat(q.ExclusiveWith)) Need(!Quests.TryGetValue(r, out var dq) || !dq.Daily, $"Quest {q.Id}: Tagesquest '{r}' kann keine Voraussetzung sein");
                if (q.Daily) Need(q.Escort == null && q.ProvidedItems.Count == 0, $"Quest {q.Id}: Tagesquests ohne Eskorte und mitgegebene Gegenstände");
                foreach (var r in q.RewardItems.Concat(q.RewardChoices).Concat(q.ProvidedItems)) Need(Items.ContainsKey(r.Item), $"Quest {q.Id}: Gegenstand '{r.Item}' fehlt");
                foreach (var r in q.RewardReputation) Need(Reputations.ContainsKey(r.Faction), $"Quest {q.Id}: Ruf '{r.Faction}' fehlt");
                if (q.RewardAbility != null) Need(HasAbility(q.RewardAbility), $"Quest {q.Id}: Fähigkeit '{q.RewardAbility}' fehlt");
                foreach (var o in q.Objectives)
                {
                    switch (o.Type)
                    {
                        case ObjectiveType.Kill:
                            Need(Creatures.ContainsKey(o.Target), $"Quest {q.Id}: Ziel-Kreatur '{o.Target}' fehlt");
                            Need(Zones.Values.Any(z => z.Spawns.Any(s => s.Creature == o.Target)) || Creatures.Values.Any(c => c.Phases.Any(p => p.SummonAdds.Any(a => a.Creature == o.Target))) || (q.Escort?.Ambushes.Any(a => a.Creature == o.Target) ?? false),
                                $"Quest {q.Id}: Kreatur '{o.Target}' erscheint nirgends");
                            break;
                        case ObjectiveType.Collect:
                            Need(Items.ContainsKey(o.Target), $"Quest {q.Id}: Sammelgegenstand '{o.Target}' fehlt");
                            Need(Creatures.Values.Any(c => c.Loot.Any(l => l.Item == o.Target)) || Objects.Values.Any(ob => ob.Loot.Any(l => l.Item == o.Target)),
                                $"Quest {q.Id}: '{o.Target}' ist nirgends erhältlich");
                            break;
                        case ObjectiveType.Explore:
                            Need(Zones.Values.Any(z => z.Areas.Any(a => a.Id == o.Target)), $"Quest {q.Id}: Gebiet '{o.Target}' fehlt");
                            break;
                        case ObjectiveType.Interact:
                            Need(Objects.ContainsKey(o.Target), $"Quest {q.Id}: Objekt '{o.Target}' fehlt");
                            break;
                        case ObjectiveType.Talk:
                        case ObjectiveType.Deliver:
                            Need(Creatures.ContainsKey(o.Target) && npcZones.ContainsKey(o.Target), $"Quest {q.Id}: NPC '{o.Target}' fehlt oder steht nirgends");
                            break;
                        case ObjectiveType.Escort:
                            Need(q.Escort != null && Creatures.ContainsKey(q.Escort.Npc) && q.Escort.Waypoints.Count > 0, $"Quest {q.Id}: Eskorte unvollständig");
                            break;
                    }
                }
                if (q.Escort != null) foreach (var a in q.Escort.Ambushes) Need(Creatures.ContainsKey(a.Creature), $"Quest {q.Id}: Hinterhalt '{a.Creature}' fehlt");
            }
            foreach (var n in TravelNodes.Values) Need(Zones.ContainsKey(n.Zone), $"Reisepunkt {n.Id}: Zone '{n.Zone}' fehlt");
            foreach (var p in FlightPaths) Need(TravelNodes.ContainsKey(p.From) && TravelNodes.ContainsKey(p.To), $"Flugroute {p.From}->{p.To}: Reisepunkt fehlt");
            foreach (var p in BotProfiles)
            {
                foreach (var r in p.Rotation) Need(HasAbility(r.Ability), $"Bot-Profil {p.Class}/{p.Role}: Fähigkeit '{r.Ability}' fehlt");
                foreach (var h in p.Heals.Concat(p.Buffs)) Need(HasAbility(h), $"Bot-Profil {p.Class}/{p.Role}: Fähigkeit '{h}' fehlt");
                foreach (var x in new[] { p.Resurrect, p.Taunt, p.Interrupt, p.Pet }) if (x != null) Need(HasAbility(x), $"Bot-Profil {p.Class}/{p.Role}: Fähigkeit '{x}' fehlt");
                Need(TalentTrees.ContainsKey(p.TalentTree), $"Bot-Profil {p.Class}/{p.Role}: Talentbaum '{p.TalentTree}' fehlt");
            }
            foreach (var m in Mounts.Values)
            {
                Need(m.Speed == 60 || m.Speed == 100, $"Reittier {m.Id}: Tempo muss 60 oder 100 sein");
                var rank = RidingRankFor(m.Speed);
                Need(rank != null && m.RequiredLevel >= rank.Level, $"Reittier {m.Id}: braucht eine Reitstufe für +{m.Speed} % und mindestens deren Stufe");
                Need(Items.TryGetValue(m.Item, out var mi) && mi.TeachesMount == m.Id, $"Reittier {m.Id}: Zügel {m.Item} fehlt oder lehrt ein anderes Reittier");
                Need(mi == null || (mi.RequiredReputation == m.RequiredReputation && mi.RequiredReputationValue == m.RequiredReputationValue),
                     $"Reittier {m.Id}: Zügel tragen eine andere Rufanforderung");
                Need(Abilities.ContainsKey("mount_ride_" + m.Speed) || AbilityRanks.ContainsKey("mount_ride_" + m.Speed), $"Reittier {m.Id}: Fähigkeit mount_ride_{m.Speed} fehlt");
            }
            Need(RidingRanks.Count > 0, "riding.json: keine Reitstufen");
            for (int i = 0; i < RidingRanks.Count; i++)
            {
                var r = RidingRanks[i];
                Need(r.Skill > 0 && r.Cost >= 0 && (r.Speed == 60 || r.Speed == 100) && r.Level >= 1 && r.Level <= Rules.Experience.MaxLevel, $"Reitstufe {r.Name}: Werte ungültig");
                if (i > 0) Need(r.Level >= RidingRanks[i - 1].Level && r.Speed > RidingRanks[i - 1].Speed, $"Reitstufe {r.Name}: Stufe und Tempo müssen steigen");
            }
            foreach (var r in Recipes.Values)
            {
                Need(Professions.ContainsKey(r.Profession), $"Rezept {r.Id}: unbekannter Beruf {r.Profession}");
                Need(r.Creates != null ^ r.Enchant != null, $"Rezept {r.Id}: braucht genau ein Ergebnis (Gegenstand oder Verzauberung)");
                if (r.Creates != null) Need(Items.ContainsKey(r.Creates.Item), $"Rezept {r.Id}: unbekanntes Ergebnis {r.Creates.Item}");
                foreach (var g in r.Reagents) Need(Items.ContainsKey(g.Item) && g.Count > 0, $"Rezept {r.Id}: unbekannte Zutat {g.Item}");
                if (r.Tool != null) Need(Items.ContainsKey(r.Tool), $"Rezept {r.Id}: unbekanntes Werkzeug {r.Tool}");
                Need(r.SkillRequired < r.Yellow && r.Yellow <= r.Green && r.Green <= r.Gray, $"Rezept {r.Id}: Farbstufen ungültig");
            }
            foreach (var o in Objects.Values.Where(o => o.Profession != null)) Need(Professions.ContainsKey(o.Profession!), $"Objekt {o.Id}: unbekannter Beruf {o.Profession}");
            foreach (var c in Creatures.Values.Where(c => c.TrainerProfession != null)) Need(Professions.ContainsKey(c.TrainerProfession!), $"NPC {c.Id}: unbekannter Beruf");
            foreach (var pr in Professions.Values) if (pr.Tool != null) Need(Items.ContainsKey(pr.Tool), $"Beruf {pr.Id}: unbekanntes Werkzeug {pr.Tool}");
            foreach (var it in Items.Values.Where(i => i.TeachesMount != null))
                Need(Mounts.ContainsKey(it.TeachesMount!), $"Gegenstand {it.Id}: unbekanntes Reittier {it.TeachesMount}");
            // Content-Migrationen (Kapitel 56): Ziele müssen existieren, entfernte/umbenannte Quellen dürfen es nicht mehr.
            var migIds = new HashSet<string>();
            Need(Manifest.Stage.Length <= 20, "Manifest: stage zu lang (höchstens 20 Zeichen)");
            Need(Manifest.LevelCap >= 1 && Manifest.LevelCap <= Rules.Experience.MaxLevel, $"Manifest: levelCap {Manifest.LevelCap} muss zwischen 1 und {Rules.Experience.MaxLevel} liegen");
            Need(System.Text.RegularExpressions.Regex.IsMatch(Manifest.Version, @"^\d+\.\d+\.\d+$"), $"Manifest: Version '{Manifest.Version}' muss die Form 1.2.3 haben");
            foreach (var m in Migrations)
            {
                Need(m.Id.Length > 0 && migIds.Add(m.Id), $"Migration '{m.Id}': Id fehlt oder doppelt");
                foreach (var kv in m.RenameItems) Need(!Items.ContainsKey(kv.Key) && Items.ContainsKey(kv.Value), $"Migration {m.Id}: Gegenstand {kv.Key} → {kv.Value} (alt darf nicht, neu muss existieren)");
                foreach (var kv in m.RemoveItems) Need(!Items.ContainsKey(kv.Key) && kv.Value >= 0, $"Migration {m.Id}: entfernter Gegenstand {kv.Key} existiert noch oder Erstattung negativ");
                foreach (var kv in m.RenameQuests) Need(!Quests.ContainsKey(kv.Key) && Quests.ContainsKey(kv.Value), $"Migration {m.Id}: Quest {kv.Key} → {kv.Value}");
                foreach (var q in m.RemoveQuests) Need(!Quests.ContainsKey(q), $"Migration {m.Id}: entfernte Quest {q} existiert noch");
                foreach (var kv in m.RenameAbilities) Need(!HasAbility(kv.Key) && HasAbility(kv.Value), $"Migration {m.Id}: Fähigkeit {kv.Key} → {kv.Value}");
                foreach (var a in m.RemoveAbilities) Need(!HasAbility(a), $"Migration {m.Id}: entfernte Fähigkeit {a} existiert noch");
                foreach (var kv in m.RenameZones) Need(!Zones.ContainsKey(kv.Key) && Zones.ContainsKey(kv.Value), $"Migration {m.Id}: Region {kv.Key} → {kv.Value}");
                foreach (var kv in m.RenameAchievements) Need(!Achievements.ContainsKey(kv.Key) && Achievements.ContainsKey(kv.Value), $"Migration {m.Id}: Erfolg {kv.Key} → {kv.Value}");
            }
            return errors;
        }
    }

    /// <summary>Vec3 als kompaktes JSON-Array [x, y, z].</summary>
    public sealed class Vec3JsonConverter : JsonConverter<Vec3>
    {
        public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Position muss [x, y, z] sein.");
            var v = new float[3];
            int i = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (i < 3) v[i] = reader.GetSingle();
                i++;
            }
            if (i == 2) return new Vec3(v[0], 0, v[1]); // [x, z] ohne Höhe
            return new Vec3(v[0], v[1], v[2]);
        }

        public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteEndArray();
        }
    }
}
