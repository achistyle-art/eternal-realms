using EternalRealms.ContentTool;
using EternalRealms.Server;
using EternalRealms.Server.Net;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    /// <summary>Kopie von content/ und tools/gen_*.py in einem temporären Ordner – Tests verändern nie den echten Content.</summary>
    public sealed class ContentSandbox : IDisposable
    {
        public string Dir { get; }
        public string Content => Path.Combine(Dir, "content");
        public string Tools => Path.Combine(Dir, "tools");

        public ContentSandbox()
        {
            Dir = Path.Combine(Path.GetTempPath(), "er-content-" + Guid.NewGuid().ToString("N")[..8]);
            Copy(TestContent.ContentRoot, Content);
            Directory.CreateDirectory(Tools);
            var tools = Path.Combine(Path.GetDirectoryName(TestContent.ContentRoot)!, "tools");
            foreach (var f in Directory.GetFiles(tools, "gen_*.py")) File.Copy(f, Path.Combine(Tools, Path.GetFileName(f)));
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(from)) Copy(d, Path.Combine(to, Path.GetFileName(d)));
        }

        public void WriteMigrations(string json) => File.WriteAllText(Path.Combine(Content, "migrations.json"), json);
        public ContentDatabase Load() => ContentDatabase.LoadFromDirectory(Content);
        public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }
    }

    public class ContentVersionTests
    {
        [Fact]
        public void VersionCombinesTheManifestWithAHashThatIgnoresLineEndings()
        {
            using var box = new ContentSandbox();
            var a = box.Load();
            Assert.Equal("0.9.0", a.Manifest.Version);
            Assert.Matches(@"^0\.9\.0\+[0-9a-f]{8}$", a.Version);
            Assert.Equal(TestContent.Db.Version, a.Version); // Kopie = gleicher Stand
            var races = Path.Combine(box.Content, "races.json");
            File.WriteAllText(races, File.ReadAllText(races).Replace("\n", "\r\n")); // Windows-Zeilenenden (git autocrlf)
            Assert.Equal(a.Hash, box.Load().Hash);
            File.AppendAllText(races, " ");
            Assert.NotEqual(a.Hash, box.Load().Hash);
        }

        [Fact]
        public void LoginReportsRealmModeAndContentVersion()
        {
            var t = new TestWorld();
            var conn = new RecordingConnection();
            var s = new Server.Net.WorldSession(t.World, t.Store, t.Store, t.World.Sessions, conn);
            s.Handle(new C_Login { Account = "versionstest", Password = "Kompass7nord", Register = true });
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!conn.Of<S_LoginResult>().Any() && DateTime.UtcNow < until) { t.World.Update(0.05f); Thread.Sleep(2); }
            var r = conn.Of<S_LoginResult>().Single();
            Assert.True(r.Success);
            Assert.Equal(TestContent.Db.Version, r.ContentVersion);
            Assert.Equal((byte)ServerMode.KiUnterstuetzt, r.RealmMode);
        }
    }

    public class ServerModeTests
    {
        [Fact]
        public void ModesSetDefaultsThatSingleSettingsCanStillOverride()
        {
            var classic = new ServerConfig().ApplyMode(ServerMode.Klassisch);
            Assert.False(classic.AmbientBots);
            Assert.False(classic.BotCompanions);
            Assert.False(classic.DevCommands);
            var dev = new ServerConfig().ApplyMode(ServerMode.Entwicklung);
            Assert.True(dev.DevCommands && dev.Console && dev.BotCompanions);
            Assert.True(ServerConfig.TryParseMode("KI", out var m) && m == ServerMode.KiUnterstuetzt);
            Assert.True(ServerConfig.TryParseMode("classic", out m) && m == ServerMode.Klassisch);
            Assert.False(ServerConfig.TryParseMode("hardcore", out _));
        }

        [Fact]
        public void AClassicRealmHasNoAiCompanions()
        {
            var t = new TestWorld(config: new ServerConfig().ApplyMode(ServerMode.Klassisch));
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10);
            Assert.Equal(Server.AI.BotDirector.NoCompanions, t.World.Bots.Invite(p, 1));
            Assert.Equal(Server.AI.BotDirector.NoCompanions, t.World.Bots.InviteFavorite(p, 1));
            t.Run(30);
            Assert.DoesNotContain(t.World.AllPlayers, x => x.IsBot); // keine KI-Bewohner
        }
    }

    public class ContentMigrationTests
    {
        private const string Migrations = @"[
 { ""id"": ""2026-test-1"", ""renameItems"": { ""item_old_fang"": ""item_wolf_fang"" }, ""removeItems"": { ""item_gone"": 50 },
   ""renameQuests"": { ""q_old"": ""q_as_grakar"" }, ""removeAbilities"": [ ""old_skill"" ] }
]";

        [Fact]
        public void InvalidMigrationsAreRejectedByValidation()
        {
            using var box = new ContentSandbox();
            box.WriteMigrations(@"[ { ""id"": ""x"", ""renameItems"": { ""item_wolf_fang"": ""item_nirgends"" }, ""removeItems"": { ""item_wolf_fang"": 5 } } ]");
            var errors = box.Load().Validate();
            Assert.Contains(errors, e => e.Contains("item_wolf_fang → item_nirgends"));
            Assert.Contains(errors, e => e.Contains("entfernter Gegenstand item_wolf_fang existiert noch"));
        }

        [Fact]
        public void SavedCharactersFollowRenamesAreRefundedOnceAndUnknownItemsAreKeptSafe()
        {
            using var box = new ContentSandbox();
            box.WriteMigrations(Migrations);
            var db = box.Load();
            Assert.Empty(db.Validate());
            var t = new TestWorld(content: db);
            var data = t.World.CreateCharacterTemplate("Altstand", Race.Grakar, CharClass.Warrior, Gender.Male, new AppearanceDto());
            data.Bag[0] = new ItemStack { ItemId = "item_old_fang", Count = 3 };
            data.Bag[1] = new ItemStack { ItemId = "item_gone", Count = 2 };
            data.Bag[2] = new ItemStack { ItemId = "item_aus_der_zukunft", Count = 1 };
            data.Quests["q_old"] = new QuestProgress { QuestId = "q_old", State = QuestState.Active, Counts = new[] { 2 } };
            data.CompletedQuests.Add("q_old");
            data.Abilities["old_skill"] = 1;
            long money = data.Money;
            data.Id = t.Store.CreateAsync(data).Result;

            var p = t.World.EnterWorld(t.Store.GetAsync(data.Id).Result!, new RecordingConnection());
            Assert.Equal(3, p.Data.Bag.Where(s => s?.ItemId == "item_wolf_fang").Sum(s => s!.Count));
            Assert.DoesNotContain(p.Data.Bag, s => s?.ItemId is "item_old_fang" or "item_gone" or "item_aus_der_zukunft");
            Assert.Equal(money + 100, p.Money);
            t.World.Persistence!.Flush(TimeSpan.FromSeconds(5));
            Assert.Contains(t.Store.Ledger, l => l.Reason == "content_refund" && l.Delta == 100);
            Assert.Equal("item_aus_der_zukunft", p.Data.Quarantine.Single().ItemId);
            Assert.True(p.Data.Quests.ContainsKey("q_as_grakar") && p.Data.CompletedQuests.Contains("q_as_grakar"));
            Assert.False(p.Data.Abilities.ContainsKey("old_skill"));
            Assert.Contains("2026-test-1", p.Data.ContentMigrations);
            Assert.Contains(((RecordingConnection)p.Connection!).Of<S_Notice>(), n => n.Text.Contains("Erstattung"));

            // Erneutes Laden: keine zweite Erstattung, der verwahrte Gegenstand bleibt verwahrt
            t.World.Logout(p);
            t.World.Persistence!.Flush(TimeSpan.FromSeconds(5));
            var again = t.World.EnterWorld(t.Store.GetAsync(data.Id).Result!, new RecordingConnection());
            Assert.Equal(money + 100, again.Money);
            Assert.Single(again.Data.Quarantine);
        }

        [Fact]
        public void QuarantinedItemsReturnWhenTheirDefinitionComesBack()
        {
            var d = new CharacterData { Name = "Rueckkehr" };
            for (int i = 0; i < 16; i++) d.Bag.Add(null);
            d.Quarantine.Add(new ItemStack { ItemId = "item_wolf_fang", Count = 2 });
            var r = ContentMigrator.Apply(d, TestContent.Db);
            Assert.Empty(d.Quarantine);
            Assert.Equal(2, d.Bag.Single(s => s != null)!.Count);
            Assert.Contains(r.Changes, c => c.Contains("zurück in die Tasche"));
        }

        [Fact]
        public async Task MailAndAuctionsAreMigratedWithoutLosingValue()
        {
            using var box = new ContentSandbox();
            box.WriteMigrations(Migrations);
            var db = box.Load();
            var store = new InMemoryStore();
            var mail = new List<MailData>
            {
                new() { Id = 1, RecipientId = 7, Subject = "Alt", Items = { new ItemStack { ItemId = "item_old_fang", Count = 2 }, new ItemStack { ItemId = "item_gone", Count = 3 } } },
            };
            var auctions = new List<AuctionData>
            {
                new() { Id = 1, SellerId = 7, ItemId = "item_old_fang", Count = 1, StartBid = 10 },
                new() { Id = 2, SellerId = 7, ItemId = "item_gone", Count = 4, StartBid = 10, CurrentBid = 25, BidderId = 8 },
            };
            int changed = await GameServer.MigrateEconomyAsync(db, store, mail, auctions);
            Assert.True(changed >= 4);
            Assert.Equal("item_wolf_fang", mail[0].Items.Single().ItemId);
            Assert.Equal(150, mail[0].Money); // 3 × 50 Kupfer Erstattung im Brief
            Assert.Equal("item_wolf_fang", auctions.Single().ItemId);
            Assert.Contains(mail, m => m.RecipientId == 7 && m.Money == 200 && m.Subject.Contains("aufgelöst")); // 4 × 50 an den Verkäufer
            Assert.Contains(mail, m => m.RecipientId == 8 && m.Money == 25); // Gebot zurück
        }
    }

    public class ContentToolTests
    {
        [Fact]
        public void RenameUpdatesEveryReferenceTheGeneratorAndCreatesAMigration()
        {
            using var box = new ContentSandbox();
            var ed = new ContentEditor(box.Content, box.Tools);
            int refsBefore = ed.FindReferences("item_wolf_fang").Count;
            Assert.True(refsBefore >= 3);
            var res = ed.Rename("item_wolf_fang", "item_waldwolf_fang");
            Assert.True(res.Success, string.Join("\n", res.Messages));
            // Übrig bleibt nur die Migration selbst, die den alten Namen kennen muss
            Assert.All(ed.FindReferences("item_wolf_fang"), r => Assert.Equal("content/migrations.json", r.File));
            Assert.Equal(refsBefore + 1, ed.FindReferences("item_waldwolf_fang").Count); // + die Migration
            Assert.Contains(res.ChangedFiles, f => f == "tools/gen_items.py");
            Assert.Contains(res.ChangedFiles, f => f == "content/migrations.json");
            var db = box.Load();
            Assert.Empty(db.Validate());
            Assert.True(db.Items.ContainsKey("item_waldwolf_fang"));
            Assert.Equal("item_waldwolf_fang", db.Migrations.Single().RenameItems["item_wolf_fang"]);
            // Gespeicherte Stände ziehen mit
            var d = new CharacterData();
            d.Bag.Add(new ItemStack { ItemId = "item_wolf_fang", Count = 4 });
            ContentMigrator.Apply(d, db);
            Assert.Equal("item_waldwolf_fang", d.Bag[0]!.ItemId);
            // Formatierung bleibt: Einrückung 1, Umlaute unverändert
            var mig = File.ReadAllText(Path.Combine(box.Content, "migrations.json"));
            Assert.StartsWith("[\n {\n  \"id\": ", mig);
            Assert.Contains("→", mig);
        }

        [Fact]
        public void RenameRefusesTakenOrUnknownIdsAndChangesNothing()
        {
            using var box = new ContentSandbox();
            var ed = new ContentEditor(box.Content, box.Tools);
            string before = TestContent.Db.Hash;
            Assert.False(ed.Rename("item_wolf_fang", "item_spider_leg").Success);
            Assert.False(ed.Rename("gibt_es_nicht", "neu_id").Success);
            Assert.False(ed.Rename("item_wolf_fang", "Ungültig!").Success);
            Assert.True(ed.Rename("item_wolf_fang", "item_probe", dryRun: true).Success);
            Assert.Equal(before, box.Load().Hash);
        }

        [Fact]
        public void NewEntriesComeFromValidTemplates()
        {
            using var box = new ContentSandbox();
            var ed = new ContentEditor(box.Content, box.Tools);
            var item = ed.New("item", "item_test_feder", "Prüffeder", new Dictionary<string, string> { ["price"] = "12" });
            Assert.True(item.Success, item.Message);
            var mob = ed.New("creature", "mob_test_kaefer", "Prüfkäfer", new Dictionary<string, string> { ["level"] = "4" });
            Assert.True(mob.Success, mob.Message);
            var giver = TestContent.Db.Quests["q_as_grakar"].Giver;
            var quest = ed.New("quest", "q_test_kaefer", "Käferplage", new Dictionary<string, string> { ["giver"] = giver, ["target"] = "mob_dust_hyena", ["level"] = "3" });
            Assert.True(quest.Success, quest.Message);
            var db = box.Load();
            Assert.Empty(db.Validate());
            Assert.Equal("Prüffeder", db.Items["item_test_feder"].Name);
            Assert.Equal(4, db.Creatures["mob_test_kaefer"].MinLevel);
            Assert.Equal("zone_aschensteppe", db.Quests["q_test_kaefer"].Zone);
            Assert.False(ed.New("item", "item_test_feder", "Doppelt", new Dictionary<string, string>()).Success);
            Assert.False(ed.New("item", "item_x_gen", "Erzeugt", new Dictionary<string, string> { ["file"] = "items.json" }).Success);
            Assert.False(ed.New("quest", "q_kaputt", "Ohne Geber", new Dictionary<string, string>()).Success);
            Assert.Contains("Regionen:", ed.Stats());
        }
    }

    public class SchemaGuardTests
    {
        [Fact]
        public async Task AServerRefusesADatabaseThatIsNewerThanItself()
        {
            var baseCs = Environment.GetEnvironmentVariable("ER_TEST_DATABASE");
            if (string.IsNullOrEmpty(baseCs)) return;
            // Eigene, kurzlebige Datenbank: die Fremdmigration darf parallel laufende Tests nicht stören.
            var dbName = "er_guard_" + Guid.NewGuid().ToString("N")[..8];
            var admin = new Npgsql.NpgsqlConnectionStringBuilder(baseCs) { Database = "postgres", Pooling = false }.ConnectionString;
            await using (var ds = Npgsql.NpgsqlDataSource.Create(admin))
            await using (var cmd = ds.CreateCommand($"CREATE DATABASE {dbName}")) await cmd.ExecuteNonQueryAsync();
            var cs = new Npgsql.NpgsqlConnectionStringBuilder(baseCs) { Database = dbName, Pooling = false }.ConnectionString;
            try { await GuardScenario(cs); }
            finally
            {
                await using var ds = Npgsql.NpgsqlDataSource.Create(admin);
                await using var cmd = ds.CreateCommand($"DROP DATABASE IF EXISTS {dbName} WITH (FORCE)");
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task GuardScenario(string cs)
        {
            var migrations = Path.Combine(Path.GetDirectoryName(TestContent.ContentRoot)!, "db", "migrations");
            using var store = new PostgresStore(cs);
            Assert.Equal(7, (await store.MigrationStatusAsync(migrations)).Pending.Count); // leere Datenbank
            await store.MigrateAsync(migrations);
            var status = await store.MigrationStatusAsync(migrations);
            Assert.Empty(status.Pending);
            Assert.Contains("007_datamodel", status.Applied);
            var fake = "999_zukunft_" + Guid.NewGuid().ToString("N")[..6];
            await using (var ds = Npgsql.NpgsqlDataSource.Create(cs))
            await using (var cmd = ds.CreateCommand($"INSERT INTO schema_migrations (version) VALUES ('{fake}')")) await cmd.ExecuteNonQueryAsync();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.MigrateAsync(migrations));
            Assert.Contains(fake, ex.Message);
            Assert.Contains(fake, (await store.MigrationStatusAsync(migrations)).Unknown);
            await store.SetRealmInfoAsync("test_key", "1");
            Assert.Equal("1", (await store.GetRealmInfoAsync())["test_key"]);
        }
    }
}
