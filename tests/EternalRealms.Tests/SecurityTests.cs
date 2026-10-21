using EternalRealms.Server.Net;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.Security;
using EternalRealms.Server.Systems;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    /// <summary>Kontoschutz, Wiederverbindung, Warteschlange, Anti-Cheat und Spielleitung (Block F).</summary>
    public class SecurityTests
    {
        // ------------------------------------------------------------ Bausteine

        [Fact]
        public void TotpMatchesRfc6238TestVectors()
        {
            // RFC 6238, Anhang B: Schlüssel "12345678901234567890", SHA-1 – die letzten sechs Ziffern der achtstelligen Werte
            var secret = Base32.Encode(System.Text.Encoding.ASCII.GetBytes("12345678901234567890"));
            Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", secret);
            Assert.Equal("287082", Totp.Code(secret, Totp.StepAt(DateTime.UnixEpoch.AddSeconds(59))));
            Assert.Equal("081804", Totp.Code(secret, Totp.StepAt(DateTime.UnixEpoch.AddSeconds(1111111109))));
            Assert.Equal("050471", Totp.Code(secret, Totp.StepAt(DateTime.UnixEpoch.AddSeconds(1111111111))));
            Assert.Equal("005924", Totp.Code(secret, Totp.StepAt(DateTime.UnixEpoch.AddSeconds(1234567890))));
            Assert.Equal("279037", Totp.Code(secret, Totp.StepAt(DateTime.UnixEpoch.AddSeconds(2000000000))));
        }

        [Fact]
        public void TotpAcceptsNeighbourStepsButNeverTheSameCodeTwice()
        {
            var secret = Totp.NewSecret();
            var now = new DateTime(2026, 10, 20, 12, 0, 10, DateTimeKind.Utc);
            long step = Totp.StepAt(now);
            Assert.Equal(step, Totp.Verify(secret, Totp.Code(secret, step), now, 0));
            Assert.Equal(step - 1, Totp.Verify(secret, Totp.Code(secret, step - 1), now, 0));
            Assert.Null(Totp.Verify(secret, Totp.Code(secret, step - 2), now, 0));
            Assert.Null(Totp.Verify(secret, Totp.Code(secret, step), now, step)); // schon verwendet
            Assert.Null(Totp.Verify(secret, "12 34", now, 0));
            Assert.Equal(Base32.Decode(secret), Base32.Decode(secret.ToLowerInvariant()));
        }

        [Fact]
        public void PasswordPolicyRejectsWeakPasswords()
        {
            Assert.NotNull(PasswordPolicy.Check("kurz1", "held"));
            Assert.NotNull(PasswordPolicy.Check("nurbuchstaben", "held"));
            Assert.NotNull(PasswordPolicy.Check("held12345", "held"));
            Assert.NotNull(PasswordPolicy.Check("Passwort123", "held"));
            Assert.Null(PasswordPolicy.Check("Drachen7feuer", "held"));
            Assert.NotEqual(SessionTokens.Create(), SessionTokens.Create());
            Assert.Equal(64, SessionTokens.Hash("x").Length);
        }

        // ------------------------------------------------------------ Sitzungen ohne Netzwerk

        private sealed class Client
        {
            public RecordingConnection Conn = new();
            public WorldSession Session = null!;
            public S_LoginResult? LastLogin => Conn.Of<S_LoginResult>().LastOrDefault();
        }

        private static Client Connect(TestWorld t, string ip = "10.0.0.1")
        {
            var c = new Client();
            c.Conn.RemoteAddress = ip;
            c.Session = new WorldSession(t.World, t.Store, t.Store, t.World.Sessions, c.Conn);
            t.World.Sessions.Add(c.Session);
            return c;
        }

        /// <summary>Welt takten, bis Datenbankaufgaben (im Hintergrund) zurückgemeldet haben.</summary>
        private static void Pump(TestWorld t, Func<bool> cond, string what)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                t.World.Update(0.05f);
                if (cond()) return;
                Thread.Sleep(2);
            }
            Assert.Fail("Zeitüberschreitung: " + what + " – " + string.Join(" | ", t.Log.TakeLast(3)));
        }

        private static S_LoginResult Login(TestWorld t, Client c, string user, string pass, bool register = false, string code = "", string token = "")
        {
            int before = c.Conn.Of<S_LoginResult>().Count();
            c.Session.Handle(new C_Login { Account = user, Password = pass, Register = register, TotpCode = code, Token = token });
            Pump(t, () => c.Conn.Of<S_LoginResult>().Count() > before, "Anmeldung " + user);
            return c.LastLogin!;
        }

        private static long CreateCharacter(TestWorld t, long accountId, string name)
        {
            var data = t.World.CreateCharacterTemplate(name, Race.Aldorer, CharClass.Warrior, Gender.Male, new AppearanceDto());
            data.AccountId = accountId;
            return t.Store.CreateAsync(data).Result;
        }

        private static Player Enter(TestWorld t, Client c, long characterId)
        {
            c.Session.Handle(new C_EnterWorld { CharacterId = characterId });
            Pump(t, () => c.Session.Player != null, "Weltbetritt");
            return c.Session.Player!;
        }

        private static Account Account(TestWorld t, string name) => t.Store.FindByNameAsync(name).Result!;

        [Fact]
        public void RegistrationEnforcesThePolicyAndLoginIssuesAToken()
        {
            var t = new TestWorld();
            var c = Connect(t);
            c.Session.Handle(new C_Login { Account = "heldin", Password = "schwach", Register = true });
            Assert.Contains("mindestens 8", c.LastLogin!.Message);
            var r = Login(t, c, "heldin", "Drachen7feuer", register: true);
            Assert.True(r.Success, r.Message);
            Assert.True(r.Token.Length >= 40);
            var r2 = Login(t, Connect(t), "heldin", "Drachen7feuer");
            Assert.True(r2.Success);
            Assert.NotEqual(r.Token, r2.Token);
            Assert.Contains(t.Store.Audit, e => e.Action == "account.create" && e.Target == "heldin");
            Assert.Contains(t.Store.Audit, e => e.Action == "login.ok" && e.Ip == "10.0.0.1");
        }

        [Fact]
        public void FiveFailedLoginsLockTheAccountEvenForTheRightPassword()
        {
            var t = new TestWorld();
            Login(t, Connect(t), "zielkonto", "Richtig4you", register: true);
            var c = Connect(t, "10.0.0.2");
            for (int i = 0; i < LoginLimiter.MaxPerAccount; i++) Assert.False(Login(t, c, "zielkonto", "falsch" + i).Success);
            var locked = Login(t, c, "zielkonto", "Richtig4you");
            Assert.False(locked.Success);
            Assert.Contains("gesperrt", locked.Message);
            Assert.NotNull(Account(t, "zielkonto").LockedUntil);
            Assert.Contains(t.Store.Audit, e => e.Action == "account.locked");
            // Unbekannte Konten und falsche Passwörter klingen gleich
            Assert.Equal("Kontoname oder Passwort falsch.", Login(t, c, "gibtsnicht", "egal123").Message);
            // Nach Ablauf der Sperre klappt es wieder, und der Zähler steht auf null
            var acc = Account(t, "zielkonto");
            acc.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
            t.Store.UpdateSecurityAsync(acc).Wait();
            Assert.True(Login(t, c, "zielkonto", "Richtig4you").Success);
            Assert.Equal(0, Account(t, "zielkonto").FailedLogins);
        }

        [Fact]
        public void ManyFailuresFromOneAddressBlockThatAddress()
        {
            var t = new TestWorld();
            Login(t, Connect(t, "10.9.9.9"), "ehrlich", "Brot42korb", register: true);
            var attacker = Connect(t, "10.6.6.6");
            for (int i = 0; i < t.World.Sessions.Logins.MaxPerIp; i++) Login(t, attacker, "opfer" + i, "rate" + i);
            attacker.Session.Handle(new C_Login { Account = "ehrlich", Password = "Brot42korb" });
            Assert.Contains("dieser Adresse", attacker.LastLogin!.Message);
            Assert.True(Login(t, Connect(t, "10.9.9.9"), "ehrlich", "Brot42korb").Success); // andere Adresse unberührt
        }

        [Fact]
        public void TwoFactorSetupLoginAndReplayProtection()
        {
            var t = new TestWorld();
            var c = Connect(t);
            Login(t, c, "vorsicht", "Sicher99heit", register: true);
            c.Session.Handle(new C_AccountSecurity { Action = AccountAction.BeginTwoFactor });
            var setup = c.Conn.Of<S_AccountSecurity>().Last();
            Assert.Equal(32, setup.PendingSecret.Length);
            Assert.StartsWith("otpauth://totp/", setup.PendingUri);
            Assert.Null(Account(t, "vorsicht").TotpSecret); // erst nach Bestätigung aktiv
            string wrong = Totp.Code(setup.PendingSecret, Totp.StepAt(DateTime.UtcNow)) == "000000" ? "111111" : "000000";
            c.Session.Handle(new C_AccountSecurity { Action = AccountAction.ConfirmTwoFactor, Code = wrong });
            Pump(t, () => c.Conn.Of<S_AccountSecurity>().Count() >= 2, "falscher Code");
            Assert.False(c.Conn.Of<S_AccountSecurity>().Last().TwoFactorEnabled);
            c.Session.Handle(new C_AccountSecurity { Action = AccountAction.ConfirmTwoFactor, Code = Totp.Code(setup.PendingSecret, Totp.StepAt(DateTime.UtcNow)) });
            Pump(t, () => c.Conn.Of<S_AccountSecurity>().Count() >= 3, "Bestätigung");
            Assert.True(c.Conn.Of<S_AccountSecurity>().Last().TwoFactorEnabled);
            Assert.Equal(setup.PendingSecret, Account(t, "vorsicht").TotpSecret);

            var d = Connect(t);
            var noCode = Login(t, d, "vorsicht", "Sicher99heit");
            Assert.False(noCode.Success);
            Assert.True(noCode.NeedsTwoFactor);
            Assert.Equal(0, Account(t, "vorsicht").FailedLogins); // fehlender Code ist kein Fehlversuch
            // Der Zeitschritt der Bestätigung ist verbraucht; der nächste gilt (Toleranz ±1 Schritt)
            long used = Account(t, "vorsicht").TotpLastStep;
            Assert.False(Login(t, d, "vorsicht", "Sicher99heit", code: Totp.Code(setup.PendingSecret, used)).Success);
            var ok = Login(t, d, "vorsicht", "Sicher99heit", code: Totp.Code(setup.PendingSecret, used + 1));
            Assert.True(ok.Success, ok.Message);
            Assert.False(Login(t, Connect(t), "vorsicht", "Sicher99heit", code: Totp.Code(setup.PendingSecret, used + 1)).Success); // Wiederholung
            // Abschalten verlangt Passwort und einen unverbrauchten Code
            d.Session.Handle(new C_AccountSecurity { Action = AccountAction.DisableTwoFactor, Password = "Sicher99heit", Code = Totp.Code(setup.PendingSecret, used + 1) });
            Pump(t, () => d.Conn.Of<S_AccountSecurity>().Any(), "Abschalten abgelehnt");
            Assert.NotNull(Account(t, "vorsicht").TotpSecret);
            // 30 Sekunden später (nachgestellt): der zuletzt verbrauchte Schritt liegt zurück, der aktuelle Code gilt
            var later = Account(t, "vorsicht");
            later.TotpLastStep = Totp.StepAt(DateTime.UtcNow) - 1;
            t.Store.UpdateSecurityAsync(later).Wait();
            d.Session.Handle(new C_AccountSecurity { Action = AccountAction.DisableTwoFactor, Password = "Sicher99heit", Code = Totp.Code(setup.PendingSecret, Totp.StepAt(DateTime.UtcNow)) });
            Pump(t, () => d.Conn.Of<S_AccountSecurity>().Count() >= 2, "Abschalten");
            Assert.Null(Account(t, "vorsicht").TotpSecret);
            Assert.Contains(t.Store.Audit, e => e.Action == "account.2fa.on");
            Assert.Contains(t.Store.Audit, e => e.Action == "account.2fa.off");
        }

        [Fact]
        public void DroppedConnectionKeepsTheCharacterAndATokenResumesIt()
        {
            var t = new TestWorld();
            var c = Connect(t);
            var first = Login(t, c, "reisende", "Wander5weg", register: true);
            var acc = Account(t, "reisende");
            var p = Enter(t, c, CreateCharacter(t, acc.Id, "Reisende"));
            p.AddItem("item_wolf_fang", 3);
            int items = p.Data.Bag.Where(s => s != null).Sum(s => s!.Count);

            c.Session.Close(); // Netzwerkabbruch
            Assert.True(p.IsLinkdead);
            Assert.Null(p.Connection);
            Assert.Contains(p, t.World.AllPlayers);

            var back = Connect(t, "10.0.0.7");
            var r = Login(t, back, "reisende", "", token: first.Token);
            Assert.True(r.Success, r.Message);
            Assert.True(r.Resumed);
            Assert.Same(p, back.Session.Player); // derselbe Charakter im Speicher – kein Neuladen, keine Verdopplung
            Assert.False(p.IsLinkdead);
            Assert.Equal(SessionState.InWorld, back.Session.State);
            Assert.Single(back.Conn.Of<S_EnterWorld>());
            Assert.Equal(items, p.Data.Bag.Where(s => s != null).Sum(s => s!.Count));
            Assert.Single(t.World.AllPlayers, x => x.Data.Id == p.Data.Id);
            t.World.Update(0.05f);
            Assert.NotEmpty(back.Conn.Of<S_EntitySpawn>()); // Umgebung wird neu geschickt

            // Ein Token gilt genau einmal
            Assert.False(Login(t, Connect(t), "reisende", "", token: first.Token).Success);
            // … und das neue Token des zweiten Logins funktioniert, auch nach erneutem Abbruch
            back.Session.Close();
            Assert.True(Login(t, Connect(t), "reisende", "", token: r.Token).Resumed);
        }

        [Fact]
        public void LinkdeadCharactersAreLoggedOutAfterTheGracePeriod()
        {
            var t = new TestWorld();
            t.World.Config.LinkdeadSeconds = 2;
            var c = Connect(t);
            Login(t, c, "kurzweil", "Kurz3weil", register: true);
            var p = Enter(t, c, CreateCharacter(t, Account(t, "kurzweil").Id, "Kurzweil"));
            c.Session.Close();
            t.Run(1);
            Assert.Contains(p, t.World.AllPlayers);
            t.Run(1.5);
            Assert.DoesNotContain(p, t.World.AllPlayers);
            // Danach normal neu anmelden und denselben Charakter betreten
            var d = Connect(t);
            var r = Login(t, d, "kurzweil", "Kurz3weil");
            Assert.False(r.Resumed);
            t.World.Persistence!.Flush(TimeSpan.FromSeconds(5));
            Assert.Equal("Kurzweil", Enter(t, d, p.Data.Id).Name);
        }

        [Fact]
        public void SecondLoginTakesOverTheCharacterWithoutDuplicatingIt()
        {
            var t = new TestWorld();
            var a = Connect(t, "10.1.1.1");
            Login(t, a, "doppelt", "Zwilling2x", register: true);
            var p = Enter(t, a, CreateCharacter(t, Account(t, "doppelt").Id, "Doppelt"));
            var b = Connect(t, "10.2.2.2");
            var r = Login(t, b, "doppelt", "Zwilling2x");
            Assert.True(r.Resumed);
            Assert.True(a.Conn.Disconnected);
            Assert.Contains("anderen Ort", a.Conn.DisconnectReason);
            Assert.Equal(SessionState.Closed, a.Session.State);
            Assert.Same(p, b.Session.Player);
            Assert.Single(t.World.AllPlayers, x => !x.IsBot);
        }

        [Fact]
        public void FullRealmQueuesPlayersAndAdmitsThemInOrder()
        {
            var t = new TestWorld();
            t.World.Config.MaxPlayers = 1;
            var a = Connect(t);
            Login(t, a, "erster", "Morgen1rot", register: true);
            Enter(t, a, CreateCharacter(t, Account(t, "erster").Id, "Erster"));
            var b = Connect(t);
            var c = Connect(t);
            Login(t, b, "zweiter", "Abend2blau", register: true);
            Login(t, c, "dritter", "Mittag3gelb", register: true);
            b.Session.Handle(new C_EnterWorld { CharacterId = CreateCharacter(t, Account(t, "zweiter").Id, "Zweiter") });
            c.Session.Handle(new C_EnterWorld { CharacterId = CreateCharacter(t, Account(t, "dritter").Id, "Dritter") });
            Assert.Equal(1, b.Conn.Of<S_QueueStatus>().Last().Position);
            Assert.Equal(2, c.Conn.Of<S_QueueStatus>().Last().Position);
            t.Run(1);
            Assert.Null(b.Session.Player);
            a.Session.Handle(new C_Logout());
            Pump(t, () => b.Session.Player != null, "Zweiter wird eingelassen");
            Assert.Null(c.Session.Player);
            Assert.Equal(1, c.Conn.Of<S_QueueStatus>().Last().Position);
            Assert.Equal(1, t.World.AllPlayers.Count(p => !p.IsBot));
        }

        [Fact]
        public void MaintenanceAdmitsOnlyGameMasters()
        {
            var t = new TestWorld();
            Login(t, Connect(t), "spieler", "Wald5weg", register: true);
            Login(t, Connect(t), "leitung", "Turm7hoch", register: true);
            var gm = Account(t, "leitung");
            gm.GmLevel = GmLevels.GameMaster;
            t.Store.UpdateSecurityAsync(gm).Wait();
            t.World.Maintenance = true;
            var r = Login(t, Connect(t), "spieler", "Wald5weg");
            Assert.False(r.Success);
            Assert.Contains("gewartet", r.Message);
            var g = Login(t, Connect(t), "leitung", "Turm7hoch");
            Assert.True(g.Success);
            Assert.Equal(GmLevels.GameMaster, g.GmLevel);
        }

        [Fact]
        public void PasswordChangeRevokesOldTokens()
        {
            var t = new TestWorld();
            var c = Connect(t);
            var first = Login(t, c, "wechsel", "Alt1passwort", register: true);
            c.Session.Handle(new C_AccountSecurity { Action = AccountAction.ChangePassword, Password = "falsch", NewPassword = "Neu2passwort" });
            Pump(t, () => c.Conn.Of<S_AccountSecurity>().Any(), "falsches altes Passwort");
            Assert.False(c.Conn.Of<S_AccountSecurity>().Last().Success);
            c.Session.Handle(new C_AccountSecurity { Action = AccountAction.ChangePassword, Password = "Alt1passwort", NewPassword = "Neu2passwort" });
            Pump(t, () => c.Conn.Of<S_AccountSecurity>().Count() >= 2, "Passwortänderung");
            var res = c.Conn.Of<S_AccountSecurity>().Last();
            Assert.True(res.Success, res.Message);
            Assert.False(Login(t, Connect(t), "wechsel", "", token: first.Token).Success);
            Assert.True(Login(t, Connect(t), "wechsel", "", token: res.Token).Success);
            Assert.False(Login(t, Connect(t), "wechsel", "Alt1passwort").Success);
            Assert.True(Login(t, Connect(t), "wechsel", "Neu2passwort").Success);
        }

        // ------------------------------------------------------------ Anti-Cheat

        [Fact]
        public void SpeedHackIsClampedFlaggedAndFinallyDisconnected()
        {
            var t = new TestWorld();
            var gm = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, name: "Aufsicht");
            gm.GmLevel = GmLevels.GameMaster;
            var c = Connect(t);
            Login(t, c, "flitzer", "Schnell9weg", register: true);
            var p = Enter(t, c, CreateCharacter(t, Account(t, "flitzer").Id, "Flitzer"));
            var start = p.Position;
            uint seq = 1;
            // Ein einzelner Ausreißer (Verzögerung) bleibt folgenlos
            t.World.Update(0.05f);
            c.Session.Handle(new C_Move { Sequence = seq++, Position = start + new Vec3(30, 0, 0) });
            Assert.True(Vec3.DistanceXZ(start, p.Position) < 10); // begrenzt
            Assert.True(t.World.AntiCheat.ScoreOf(p) < t.World.Config.CheatFlagScore);
            // Dauerhaft zu schnell: markiert, dann getrennt
            for (int i = 0; i < 60 && !c.Conn.Disconnected; i++)
            {
                t.World.Update(0.05f);
                c.Session.Handle(new C_Move { Sequence = seq++, Position = p.Position + new Vec3(40, 0, 0) });
            }
            Assert.True(c.Conn.Disconnected);
            Assert.Contains(t.World.AntiCheat.Flags, f => f.Name == "Flitzer" && !f.Kicked);
            Assert.Contains(t.World.AntiCheat.Flags, f => f.Name == "Flitzer" && f.Kicked);
            Assert.DoesNotContain(p, t.World.AllPlayers); // vom Server getrennt: sofort abgemeldet, nicht verbindungslos
            Assert.Contains(t.Conn(gm).Of<S_Chat>(), m => m.Text.StartsWith("[Anti-Cheat] Flitzer"));
            t.World.Persistence!.Flush(TimeSpan.FromSeconds(5));
            Assert.Contains(t.Store.Audit, e => e.Action == "anticheat.kick" && e.Target == "Flitzer");
            Assert.False(Account(t, "flitzer").IsBannedAt(DateTime.UtcNow)); // nie automatisch gesperrt
        }

        [Fact]
        public void ScoresDecayAndUnusualIncomeIsFlagged()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, level: 5);
            t.World.AntiCheat.Report(p, CheatKind.InvalidAction, "test", 30);
            t.Run(10);
            Assert.InRange(t.World.AntiCheat.ScoreOf(p), 19, 21);
            // Handel zählt nicht, neu entstandenes Geld über der Grenze schon
            t.World.Economy.Credit(p, 50_000_000, "trade");
            Assert.DoesNotContain(t.World.AntiCheat.Flags, f => f.Kind == CheatKind.GoldRate);
            long limit = t.World.Config.GoldPerHourPerLevel * p.Level;
            t.World.Economy.Credit(p, limit / 2, "loot");
            t.World.Economy.Credit(p, limit, "vendor_sell");
            Assert.Contains(t.World.AntiCheat.Flags, f => f.Kind == CheatKind.GoldRate && f.Name == p.Name);
        }

        // ------------------------------------------------------------ Spielleitung

        private static (TestWorld t, Client gm, Player gmPlayer, Client target, Player targetPlayer) GmSetup()
        {
            var t = new TestWorld();
            Login(t, Connect(t, "10.5.5.5"), "leiterin", "Turm7hoch", register: true);
            var acc = Account(t, "leiterin");
            acc.GmLevel = GmLevels.GameMaster;
            t.Store.UpdateSecurityAsync(acc).Wait();
            var g = Connect(t, "10.5.5.5");
            Login(t, g, "leiterin", "Turm7hoch");
            var gp = Enter(t, g, CreateCharacter(t, acc.Id, "Leiterin"));
            var c = Connect(t, "10.4.4.4");
            Login(t, c, "stoerer", "Laerm8laut", register: true);
            var tp = Enter(t, c, CreateCharacter(t, Account(t, "stoerer").Id, "Stoerer"));
            return (t, g, gp, c, tp);
        }

        private static List<string> Gm(TestWorld t, Client gm, string command)
        {
            int before = gm.Conn.Of<S_Chat>().Count();
            gm.Session.Handle(new C_Chat { Channel = ChatChannel.Say, Text = command });
            for (int i = 0; i < 40; i++) { t.World.Update(0.05f); Thread.Sleep(2); }
            t.World.Persistence?.Flush(TimeSpan.FromSeconds(5));
            t.World.Update(0.05f);
            return gm.Conn.Of<S_Chat>().Skip(before).Where(m => m.Channel == ChatChannel.System).Select(m => m.Text).ToList();
        }

        [Fact]
        public void GameMastersKickMuteAndBanWithAnAuditTrail()
        {
            var (t, gm, gp, target, tp) = GmSetup();
            Assert.True(gp.IsGm);
            Assert.Contains(Gm(t, gm, ".wer"), l => l.Contains("Stoerer"));
            Assert.Contains(Gm(t, gm, ".info Stoerer"), l => l.Contains("Konto stoerer"));

            Gm(t, gm, ".stumm Stoerer 10 Beleidigungen");
            Assert.NotNull(tp.MutedUntil);
            target.Session.Handle(new C_Chat { Channel = ChatChannel.Say, Text = "Hallo?" });
            Assert.Contains(target.Conn.Of<S_Notice>(), n => n.Text.Contains("stummgeschaltet"));
            Assert.DoesNotContain(gm.Conn.Of<S_Chat>(), m => m.Text == "Hallo?");
            Assert.NotNull(Account(t, "stoerer").MutedUntil); // bleibt auch nach Neuanmeldung
            Gm(t, gm, ".laut Stoerer");
            Assert.Null(tp.MutedUntil);

            Gm(t, gm, ".kick Stoerer Pause");
            Assert.True(target.Conn.Disconnected);
            Assert.DoesNotContain(tp, t.World.AllPlayers);

            Gm(t, gm, ".bann Stoerer 60 Wiederholte Beleidigungen");
            var banned = Login(t, Connect(t), "stoerer", "Laerm8laut");
            Assert.False(banned.Success);
            Assert.Contains("Wiederholte Beleidigungen", banned.Message);
            Gm(t, gm, ".entbann stoerer");
            Assert.True(Login(t, Connect(t), "stoerer", "Laerm8laut").Success);

            var actions = t.Store.Audit.Where(e => e.Action.StartsWith("gm.")).Select(e => e.Action).ToList();
            Assert.Equal(new[] { "gm.mute", "gm.unmute", "gm.kick", "gm.ban", "gm.unban" }, actions);
            Assert.All(t.Store.Audit.Where(e => e.Action.StartsWith("gm.")), e => Assert.Contains("leiterin", e.Actor));
            Assert.Contains(Gm(t, gm, ".protokoll gm"), l => l.Contains("gm.ban"));
        }

        [Fact]
        public void RightsAreCheckedAndNormalPlayersCannotUseCommands()
        {
            var (t, gm, gp, target, tp) = GmSetup();
            // Spielleiter dürfen kein Gold erzeugen
            Assert.Contains(Gm(t, gm, ".gold Stoerer 100"), l => l.Contains("Rechte"));
            Assert.Equal(0, tp.Money);
            // Spieler: „.kick“ ist für sie nur Chattext, der niemanden trennt
            target.Session.Handle(new C_Chat { Channel = ChatChannel.Say, Text = ".kick Leiterin" });
            Assert.False(gm.Conn.Disconnected);
            // Niemand kann gegen Gleichgestellte vorgehen
            var other = Account(t, "stoerer");
            other.GmLevel = GmLevels.GameMaster;
            t.Store.UpdateSecurityAsync(other).Wait();
            tp.GmLevel = GmLevels.GameMaster;
            Assert.Contains(Gm(t, gm, ".kick Stoerer"), l => l.Contains("Gleich"));
            Assert.False(target.Conn.Disconnected);

            // Die Konsole hat volle Rechte: Admin ernennen, dann Gold mit Protokoll und Geldbuch
            var replies = new List<string>();
            t.World.Gm.Execute(GmContext.Console(replies.Add), "rechte leiterin 2");
            Pump(t, () => replies.Count > 0, "Rechte gesetzt");
            Assert.Equal(GmLevels.Admin, gp.GmLevel);
            Gm(t, gm, ".gold Stoerer 5");
            Assert.Equal(5 * 10000, tp.Money);
            Assert.Contains(t.Store.Ledger, l => l.Reason == "gm" && l.Delta == 50000);
            Assert.Contains(t.Store.Audit, e => e.Action == "gm.gold" && e.Target == "Stoerer");
            Assert.Contains(t.Store.Audit, e => e.Action == "gm.setlevel" && e.Actor == "Konsole");
        }

        [Fact]
        public void MaintenanceCommandClearsTheRealmButKeepsGameMasters()
        {
            var (t, gm, gp, target, tp) = GmSetup();
            Gm(t, gm, ".wartung an Neues Update wird eingespielt.");
            Assert.True(t.World.Maintenance);
            Assert.Contains(target.Conn.Of<S_Chat>(), m => m.Text.Contains("Neues Update"));
            Gm(t, gm, ".wartung leeren");
            Assert.True(target.Conn.Disconnected);
            Assert.Contains(gp, t.World.AllPlayers);
            Assert.False(Login(t, Connect(t), "stoerer", "Laerm8laut").Success);
            Gm(t, gm, ".wartung aus");
            Assert.True(Login(t, Connect(t), "stoerer", "Laerm8laut").Success);
        }

        [Fact]
        public void PlayerReportsReachTheGameMasters()
        {
            var (t, gm, gp, target, tp) = GmSetup();
            target.Session.Handle(new C_Chat { Channel = ChatChannel.Say, Text = "Ich bin unhöflich." });
            gm.Session.Handle(new C_ReportPlayer { Name = "Stoerer", Reason = "Beleidigung", Text = "Hat mich beschimpft" });
            Pump(t, () => t.Store.Reports.Count > 0, "Meldung gespeichert");
            var lines = Gm(t, gm, ".meldungen");
            Assert.Contains(lines, l => l.Contains("Stoerer") && l.Contains("Beleidigung"));
            Gm(t, gm, ".erledigt " + t.Store.Reports[0].Id);
            Assert.Contains(Gm(t, gm, ".meldungen"), l => l.Contains("Keine offenen"));
        }

        // ------------------------------------------------------------ PostgreSQL

        [Fact]
        public async Task PostgresStoresSecurityStateTokensAndAudit()
        {
            var cs = Environment.GetEnvironmentVariable("ER_TEST_DATABASE");
            if (string.IsNullOrEmpty(cs)) return;
            using var store = new PostgresStore(cs);
            await store.MigrateAsync(Path.Combine(Path.GetDirectoryName(TestContent.ContentRoot)!, "db", "migrations"));
            var name = "sec_" + Guid.NewGuid().ToString("N")[..10];
            var acc = await store.CreateAsync(name, PasswordHasher.Hash("Geheim123x"));
            acc.FailedLogins = 3;
            acc.LockedUntil = DateTime.UtcNow.AddMinutes(5);
            acc.TotpSecret = Totp.NewSecret();
            acc.TotpLastStep = 4711;
            acc.GmLevel = GmLevels.Admin;
            acc.BanUntil = DateTime.UtcNow.AddHours(1);
            acc.BanReason = "Test";
            acc.MutedUntil = DateTime.UtcNow.AddMinutes(1);
            await store.UpdateSecurityAsync(acc);
            var back = (await store.GetAccountAsync(acc.Id))!;
            Assert.Equal(3, back.FailedLogins);
            Assert.Equal(acc.TotpSecret, back.TotpSecret);
            Assert.Equal(4711, back.TotpLastStep);
            Assert.Equal(GmLevels.Admin, back.GmLevel);
            Assert.True(back.IsBannedAt(DateTime.UtcNow));
            Assert.Equal("Test", back.BanReason);
            Assert.NotNull(back.MutedUntil);
            Assert.Equal(back.TotpSecret, (await store.FindByNameAsync(name))!.TotpSecret);

            var token = SessionTokens.Create();
            await store.SaveSessionTokenAsync(SessionTokens.Hash(token), acc.Id, DateTime.UtcNow.AddHours(1));
            Assert.Equal(acc.Id, await store.ConsumeSessionTokenAsync(SessionTokens.Hash(token), DateTime.UtcNow));
            Assert.Null(await store.ConsumeSessionTokenAsync(SessionTokens.Hash(token), DateTime.UtcNow));
            var expired = SessionTokens.Create();
            await store.SaveSessionTokenAsync(SessionTokens.Hash(expired), acc.Id, DateTime.UtcNow.AddHours(1));
            Assert.Null(await store.ConsumeSessionTokenAsync(SessionTokens.Hash(expired), DateTime.UtcNow.AddHours(2)));
            var revoked = SessionTokens.Create();
            await store.SaveSessionTokenAsync(SessionTokens.Hash(revoked), acc.Id, DateTime.UtcNow.AddHours(1));
            await store.RevokeSessionTokensAsync(acc.Id);
            Assert.Null(await store.ConsumeSessionTokenAsync(SessionTokens.Hash(revoked), DateTime.UtcNow));

            await store.AuditAsync(new AuditEntry { AccountId = acc.Id, Actor = name, Action = "gm.test", Target = name, Detail = "eins", Ip = "127.0.0.1" });
            await store.AuditAsync(new AuditEntry { Actor = "Konsole", Action = "gm.test", Target = name, Detail = "zwei" });
            var log = await store.ListAuditAsync("gm.test", name, 10);
            Assert.Equal(new[] { "zwei", "eins" }, log.Select(e => e.Detail));
            Assert.Null(log[0].AccountId);
            Assert.Equal(2, (await store.ListAuditAsync(null, name.ToUpperInvariant(), 10)).Count);

            await store.SaveReportAsync(new PlayerReport { ReporterName = name, TargetName = "Ziel" + name, Reason = "Test", Evidence = new() { "Zeile" } });
            var open = (await store.ListOpenReportsAsync(50)).First(r => r.ReporterName == name);
            Assert.Equal("Zeile", open.Evidence.Single());
            Assert.True(await store.MarkReportHandledAsync(open.Id));
            Assert.False(await store.MarkReportHandledAsync(open.Id));
            Assert.DoesNotContain(await store.ListOpenReportsAsync(50), r => r.Id == open.Id);
        }
    }
}
