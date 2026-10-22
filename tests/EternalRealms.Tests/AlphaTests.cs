using System.Text.RegularExpressions;
using EternalRealms.Server;
using EternalRealms.Server.Net;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.Security;
using EternalRealms.Server.Systems;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    /// <summary>Vorbereitung der geschlossenen Alpha: Einladungscodes, Fehlermeldungen, Begrüßung, Statistik, Kennzeichnung.</summary>
    public class AlphaTests
    {
        private static readonly Regex CodePattern = new(@"ER-[A-Z2-7]{4}-[A-Z2-7]{4}-[A-Z2-7]{4}");

        private sealed class Client
        {
            public RecordingConnection Conn = new();
            public WorldSession Session = null!;
        }

        private static Client Connect(TestWorld t, string ip)
        {
            var c = new Client();
            c.Conn.RemoteAddress = ip;
            c.Session = new WorldSession(t.World, t.Store, t.Store, t.World.Sessions, c.Conn);
            t.World.Sessions.Add(c.Session);
            return c;
        }

        private static void Pump(TestWorld t, Func<bool> cond, string what)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                t.World.Update(0.05f);
                if (cond()) return;
                Thread.Sleep(2);
            }
            Assert.Fail("Zeitüberschreitung: " + what);
        }

        private static S_LoginResult Register(TestWorld t, Client c, string user, string pass, string invite)
        {
            int before = c.Conn.Of<S_LoginResult>().Count();
            c.Session.Handle(new C_Login { Account = user, Password = pass, Register = true, InviteCode = invite });
            Pump(t, () => c.Conn.Of<S_LoginResult>().Count() > before, "Registrierung " + user);
            return c.Conn.Of<S_LoginResult>().Last();
        }

        /// <summary>Spielleitungsbefehl wie an der Serverkonsole; wartet auf die (asynchrone) Antwort.</summary>
        private static List<string> Gm(TestWorld t, string command, int level = GmLevels.Admin)
        {
            var replies = new List<string>();
            var ctx = new GmContext { Name = "Konsole", Level = level, Reply = s => { lock (replies) replies.Add(s); } };
            t.World.Gm.Execute(ctx, command);
            Pump(t, () => { lock (replies) return replies.Count > 0; }, command);
            lock (replies) return replies.SelectMany(r => r.Split('\n')).ToList();
        }

        private static TestWorld InviteOnlyWorld() =>
            new(config: new ServerConfig { AmbientBots = false, DevCommands = true, InviteOnly = true });

        [Fact]
        public void InviteCodesAreRandomAndToleratePunctuationAndCase()
        {
            var a = InviteCodes.Create();
            Assert.Matches(CodePattern, a);
            Assert.NotEqual(a, InviteCodes.Create());
            Assert.Equal(InviteCodes.Hash(a), InviteCodes.Hash(" " + a.ToLowerInvariant().Replace("-", " ") + " "));
            Assert.Equal(64, InviteCodes.Hash(a).Length);
        }

        [Fact]
        public void ClosedAlphaRegistersOnlyWithAnUnusedCode()
        {
            var t = InviteOnlyWorld();
            var lines = Gm(t, "einladung 2 Welle 1");
            var codes = lines.SelectMany(l => CodePattern.Matches(l).Select(m => m.Value)).ToList();
            Assert.Equal(2, codes.Count);

            var c = Connect(t, "10.1.0.1");
            Assert.Equal("Für die Alpha braucht Ihr einen Einladungscode.", Register(t, c, "ohnecode", "Drachen7feuer", "").Message);
            Assert.Equal("Einladungscode ungültig oder bereits benutzt.", Register(t, c, "falsch", "Drachen7feuer", "ER-AAAA-BBBB-CCCC").Message);
            var ok = Register(t, c, "tester", "Drachen7feuer", codes[0].ToLowerInvariant()); // Kleinschreibung erlaubt
            Assert.True(ok.Success, ok.Message);

            var d = Connect(t, "10.1.0.2");
            Assert.Equal("Einladungscode ungültig oder bereits benutzt.", Register(t, d, "zweiter", "Drachen7feuer", codes[0]).Message);
            Assert.True(Register(t, d, "zweiter", "Drachen7feuer", codes[1]).Success);
            Assert.Null(t.Store.FindByNameAsync("ohnecode").Result);
            Assert.Null(t.Store.FindByNameAsync("falsch").Result);
            Assert.Contains(Gm(t, "einladungen", GmLevels.GameMaster), l => l.Contains("0 offen, 2 eingelöst"));

            // Die Codes selbst stehen nirgends im Prüfprotokoll
            Assert.Contains(t.Store.Audit, e => e.Action == "gm.invite.create");
            Assert.DoesNotContain(t.Store.Audit, e => codes.Any(code => (e.Detail + e.Target).Contains(code)));
        }

        [Fact]
        public void GuessingCodesHitsTheRegistrationLimitPerAddress()
        {
            var t = InviteOnlyWorld();
            var c = Connect(t, "10.1.0.9");
            for (int i = 0; i < 5; i++) Register(t, c, "rater" + i, "Drachen7feuer", $"ER-AAAA-BBBB-CCC{(char)('A' + i)}");
            Assert.StartsWith("Zu viele neue Konten", Register(t, c, "rater9", "Drachen7feuer", "ER-AAAA-BBBB-DDDD").Message);
        }

        [Fact]
        public void OnlyAdminsCreateCodes()
        {
            var t = InviteOnlyWorld();
            Assert.Contains("Dafür fehlen Euch die Rechte.", Gm(t, "einladung 1", GmLevels.GameMaster));
        }

        [Fact]
        public void BugReportsCarryServerSideContextAndAreRateLimited()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 15, new Vec3(300, 0, 340), "zone_westmark");
            Assert.Equal("Bitte beschreibt den Fehler in ein paar Worten.", t.World.Social.ReportBug(p, "Fehler", "kaputt"[..3], ""));
            Assert.Null(t.World.Social.ReportBug(p, "balance", "Feldwühler treffen zu hart", "Konsole, Linux"));
            Assert.Null(t.World.Social.ReportBug(p, "Unsinn", "Unbekannte Kategorie wird zu Fehler", ""));
            for (int i = 0; i < SocialSystem.BugReportsPerWindow - 2; i++) Assert.Null(t.World.Social.ReportBug(p, "Text", "Tippfehler Nummer " + i, ""));
            Assert.StartsWith("Ihr habt gerade schon mehrere Meldungen", t.World.Social.ReportBug(p, "Text", "eine zu viel", ""));
            Assert.True(t.World.Persistence!.Flush(TimeSpan.FromSeconds(5)));
            var bugs = t.Store.BugReports;
            Assert.Equal(SocialSystem.BugReportsPerWindow, bugs.Count);
            var first = bugs[0];
            Assert.Equal(("Balance", "zone_westmark", 15, p.Name), (first.Category, first.Zone, first.Level, first.CharacterName));
            Assert.Equal(t.World.Content.Version, first.ContentVersion);
            Assert.Equal(300, first.PosX, 0);
            Assert.Equal("Fehler", bugs[1].Category);

            var listed = Gm(t, "fehler", GmLevels.GameMaster);
            Assert.Contains(listed, l => l.Contains("[Balance]") && l.Contains("Feldwühler treffen zu hart") && l.Contains("zone_westmark"));
            Assert.Contains(Gm(t, $"behoben {first.Id}", GmLevels.GameMaster), l => l.Contains("bearbeitet"));
            Assert.DoesNotContain(Gm(t, "fehler", GmLevels.GameMaster), l => l.Contains("Feldwühler"));
        }

        [Fact]
        public void BugReportMessageRoundTripsAndReachesTheServer()
        {
            var m = (C_BugReport)Packets.Deserialize(Packets.Serialize(new C_BugReport { Category = "Absturz", Text = "Client friert ein", ClientInfo = "Unity" }));
            Assert.Equal(("Absturz", "Client friert ein", "Unity"), (m.Category, m.Text, m.ClientInfo));
            var login = (C_Login)Packets.Deserialize(Packets.Serialize(new C_Login { Account = "a", Password = "b", Register = true, InviteCode = "ER-ABCD-EFGH-IJKL" }));
            Assert.Equal("ER-ABCD-EFGH-IJKL", login.InviteCode);
        }

        [Fact]
        public void TheWelcomeMessageIsShownAfterEnteringTheWorld()
        {
            var t = new TestWorld(config: new ServerConfig { AmbientBots = false, Motd = "Willkommen zur Alpha!\\nFehler bitte mit /bug melden." });
            var c = Connect(t, "10.1.0.3");
            c.Session.Handle(new C_Login { Account = "gast", Password = "Drachen7feuer", Register = true });
            Pump(t, () => c.Conn.Of<S_LoginResult>().Any(), "Anmeldung");
            var data = t.World.CreateCharacterTemplate("Gastina", Race.Aldorer, CharClass.Warrior, Gender.Female, new AppearanceDto());
            data.AccountId = t.Store.FindByNameAsync("gast").Result!.Id;
            long id = t.Store.CreateAsync(data).Result;
            c.Session.Handle(new C_EnterWorld { CharacterId = id });
            Pump(t, () => c.Session.Player != null, "Weltbetritt");
            var system = c.Conn.Of<S_Chat>().Where(m => m.Channel == ChatChannel.System).Select(m => m.Text).ToList();
            Assert.Contains("Willkommen zur Alpha!", system);
            Assert.Contains("Fehler bitte mit /bug melden.", system);
        }

        [Fact]
        public void MetricsCountHumansOnlyAndAreWrittenAsJsonLines()
        {
            var file = Path.Combine(Path.GetTempPath(), $"er-statistik-{Guid.NewGuid():N}.jsonl");
            try
            {
                var t = new TestWorld(config: new ServerConfig { AmbientBots = false, DevCommands = true, MetricsFile = file, MetricsMinutes = 0.1 });
                var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 3);
                var bot = t.CreateBot(p, Role.Healer);
                t.World.Progression.LevelUp(p);
                t.World.ReportProgress(p, ProgressEvent.CompleteQuest, "q_mt_aldorer");
                t.World.ReportProgress(bot, ProgressEvent.CompleteQuest, "q_mt_aldorer");
                t.World.ReportProgress(p, ProgressEvent.Die);
                t.World.ReportProgress(bot, ProgressEvent.Die);
                t.World.Metrics.OnEncounter(new EncounterSystem.Result { BossId = "boss_morrak", Won = true, Duration = 90, Deaths = 1 });
                t.World.Metrics.OnEncounter(new EncounterSystem.Result { BossId = "boss_morrak", Won = false, Duration = 40, Deaths = 5 });

                var s = t.World.Metrics.Take();
                Assert.Equal((1, 1), (s.OnlineHumans, s.OnlineBots));
                Assert.True(s.LevelUps >= 1);
                Assert.Equal(1, s.QuestsCompleted);
                Assert.Equal(1, s.TopQuests["q_mt_aldorer"]);
                Assert.Equal(1, s.DeathsByZone["zone_morgentauwald"]);
                Assert.Equal((1, 1, 6, 90.0), (s.Bosses["boss_morrak"].Wins, s.Bosses["boss_morrak"].Losses, s.Bosses["boss_morrak"].Deaths, s.Bosses["boss_morrak"].AverageWinSeconds));
                Assert.Contains("1–5: 1", string.Join("\n", AlphaMetrics.Describe(s, t.World.Content)));

                t.Run(7); // erstes Schreiben sofort, das zweite nach 6 s
                Pump(t, () => File.Exists(file) && File.ReadAllLines(file).Length >= 2, "Statistik-Datei");
                var line = File.ReadAllLines(file)[0];
                Assert.Contains("\"onlineHumans\":1", line);
                Assert.Contains("\"bosses\":{\"boss_morrak\"", line);
                Assert.DoesNotContain(p.Name, line); // keine Namen, nur Zählwerte
            }
            finally { File.Delete(file); }
        }

        /// <summary>Läuft nur mit ER_TEST_DATABASE: Einladungen und Fehlermeldungen gegen PostgreSQL (Migration 008).</summary>
        [Fact]
        public async Task PostgresRedeemsInvitesOnceAndStoresBugReports()
        {
            var cs = Environment.GetEnvironmentVariable("ER_TEST_DATABASE");
            if (string.IsNullOrEmpty(cs)) return;
            using var store = new PostgresStore(cs);
            await store.MigrateAsync(Path.Combine(Path.GetDirectoryName(TestContent.ContentRoot)!, "db", "migrations"));
            var code = InviteCodes.Create();
            var hash = InviteCodes.Hash(code);
            await store.SaveInviteCodesAsync(new[] { new InviteCode { CodeHash = hash, CreatedBy = "Test", Note = "Welle Test" } });
            var now = DateTime.UtcNow;
            // Gleichzeitige Einlöseversuche: genau einer gelingt
            var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.RedeemInviteCodeAsync(hash, now)));
            Assert.Equal(1, attempts.Count(ok => ok));
            var acc = await store.CreateAsync("inv_" + Guid.NewGuid().ToString("N")[..10], PasswordHasher.Hash("Drachen7feuer"));
            await store.BindInviteCodeAsync(hash, acc.Id);
            var expired = InviteCodes.Hash(InviteCodes.Create());
            await store.SaveInviteCodesAsync(new[] { new InviteCode { CodeHash = expired, ExpiresAt = now.AddMinutes(-1) } });
            Assert.False(await store.RedeemInviteCodeAsync(expired, now));
            var (open, used) = await store.CountInviteCodesAsync();
            Assert.True(open >= 1 && used >= 1);

            string marker = "Pg-Fehler " + Guid.NewGuid().ToString("N")[..8];
            await store.SaveBugReportAsync(new BugReport { AccountId = acc.Id, CharacterName = "Probandin", Category = "Balance", Text = marker, Zone = "zone_westmark",
                                                           PosX = 12.5f, PosZ = 7, Level = 18, ContentVersion = "0.9.0+x", ServerVersion = "1.0", ClientInfo = "Test" });
            var open1 = await store.ListOpenBugReportsAsync(50);
            var mine = Assert.Single(open1, b => b.Text == marker);
            Assert.Equal(("Balance", "zone_westmark", 18, 12.5f, acc.Id), (mine.Category, mine.Zone, mine.Level, mine.PosX, mine.AccountId!.Value));
            Assert.True(await store.MarkBugReportHandledAsync(mine.Id));
            Assert.False(await store.MarkBugReportHandledAsync(mine.Id));
            Assert.DoesNotContain(await store.ListOpenBugReportsAsync(50), b => b.Text == marker);
        }

        [Fact]
        public void TheManifestMarksThisBuildAsAlpha()
        {
            var m = TestContent.Db.Manifest;
            Assert.Equal("Alpha", m.Stage);
            Assert.Equal($"Eternal Realms Alpha {m.Version}", m.Label);
        }
    }
}
