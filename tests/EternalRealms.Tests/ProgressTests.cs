using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    /// <summary>Kapitel 38/39: Erfolge und Titel ohne Spielvorteil, interaktives Tutorial mit KI-Erklärerin.</summary>
    public class ProgressTests
    {
        private static readonly Vec3 Village = new(400, 0, 400);

        // ------------------------------------------------------------ Erfolge

        [Fact]
        public void AchievementContentIsValidAndRewardsOnlyTitles()
        {
            var db = TestContent.Db;
            Assert.True(db.Achievements.Count >= 40);
            Assert.Empty(db.Validate());
            var titles = db.Achievements.Values.Where(a => a.Title != null).Select(a => a.Title).ToList();
            Assert.Equal(titles.Count, titles.Distinct().Count());
            // Es gibt bewusst keine Felder für Gegenstände, Werte oder Gold – nur Punkte und Titel
            var props = typeof(AchievementDef).GetProperties().Select(p => p.Name).ToHashSet();
            Assert.DoesNotContain("Items", props);
            Assert.DoesNotContain("Modifiers", props);
            Assert.DoesNotContain("Money", props);
        }

        [Fact]
        public void KillsCountForTheWholeGroupAndCompleteOnTarget()
        {
            var t = new TestWorld();
            var field = Village + new Vec3(200, 0, -150);
            t.ClearMonstersAround(field, 80);
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, field);
            var b = t.CreatePlayer(Race.Elarii, CharClass.Priest, 10, field + new Vec3(4, 0, 0));
            t.World.Groups.Create(a);
            t.World.Groups.Join(a.Group!, b);
            for (int i = 0; i < 50; i++)
            {
                var wolf = t.Spawn("mob_forest_wolf", field + new Vec3(2, 0, 2), level: 4);
                wolf.TappedBy = a;
                t.World.Combat.DealDamage(a, wolf, wolf.Health * 10, School.Holy, null, "test", false, false);
            }
            t.Run(0.1);
            Assert.True(t.World.Achievements.Has(a, "ach_wolves_50"));
            Assert.True(t.World.Achievements.Has(b, "ach_wolves_50"));
            Assert.Contains("Wolfsjäger", t.World.Achievements.Titles(a));
            Assert.Equal(50, a.Data.Achievements["ach_kills_100"].Counts[0]);
            Assert.False(t.World.Achievements.Has(a, "ach_kills_100"));
            var earned = t.Conn(a).Of<S_AchievementEarned>().Single(e => e.Id == "ach_wolves_50");
            Assert.Equal("Wolfsjäger", earned.Title);
            Assert.Contains(t.Conn(b).Of<S_Chat>(), c => c.Text.Contains(a.Name) && c.Text.Contains("Wolfsjäger"));
        }

        [Fact]
        public void LevelAndFactionAchievements()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 1, Village);
            for (int i = 1; i < 20; i++) t.World.Progression.LevelUp(p);
            Assert.True(t.World.Achievements.Has(p, "ach_level_10"));
            Assert.True(t.World.Achievements.Has(p, "ach_level_20"));
            Assert.False(t.World.Achievements.Has(p, "ach_level_30"));
            Assert.Equal(20, p.Data.Achievements["ach_level_30"].Counts[0]);

            t.World.Transfer(p, "zone_sonnenfeste", new Vec3(256, 0, 400));
            Assert.False(t.World.Achievements.Has(p, "ach_capitals_sb"));
            t.World.Transfer(p, "zone_silbermond", new Vec3(256, 0, 400));
            Assert.True(t.World.Achievements.Has(p, "ach_capitals_sb"));
            Assert.False(p.Data.Achievements.ContainsKey("ach_capitals_sp")); // fremde Fraktion: zählt gar nicht erst
        }

        [Fact]
        public void TitlesAreChosenFreelyAndShownOnTheNameplate()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village);
            Assert.NotNull(t.World.Achievements.SetTitle(p, "Wurzelbrecher"));
            t.World.ReportProgress(p, ProgressEvent.KillBoss, "mob_thornmaw");
            Assert.Null(t.World.Achievements.SetTitle(p, "Wurzelbrecher"));
            Assert.Equal("Wurzelbrecher", p.BuildSpawn(null).Title);
            Assert.Null(t.World.Achievements.SetTitle(p, ""));
            Assert.Equal("", p.BuildSpawn(null).Title);
            var bot = t.CreateBot(p, Role.Healer);
            t.World.ReportProgress(bot, ProgressEvent.KillBoss, "mob_thornmaw");
            t.World.Achievements.SetTitle(bot, "Wurzelbrecher");
            Assert.Equal("KI-Mitspieler · Wurzelbrecher", bot.BuildSpawn(null).Title); // Kennzeichnung bleibt immer
            var state = t.World.Achievements.Build(p);
            Assert.Contains("Wurzelbrecher", state.Titles);
            Assert.Equal(25, state.Points);
            var back = (S_Achievements)Packets.Deserialize(Packets.Serialize(state));
            Assert.Equal(state.Points, back.Points);
            Assert.True(back.Progress.Single(x => x.Id == "ach_thornmaw").CompletedAt > 0);
        }

        [Fact]
        public void ProgressSurvivesSaveAndLoad()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village);
            t.World.ReportProgress(p, ProgressEvent.KillBoss, "mob_thornmaw");
            t.World.Achievements.SetTitle(p, "Wurzelbrecher");
            t.World.ReportProgress(p, ProgressEvent.Kill, "mob_boar", 7);
            p.Data.Tutorial = new Server.Persistence.TutorialState { Step = 3, Counter = 1 };
            var back = p.Data.Clone();
            Assert.NotNull(back.Achievements["ach_thornmaw"].CompletedAt);
            Assert.Equal(7, back.Achievements["ach_kills_100"].Counts[0]);
            Assert.Equal("Wurzelbrecher", back.ActiveTitle);
            Assert.Equal(3, back.Tutorial!.Step);
        }

        // ------------------------------------------------------------ Tutorial

        private static (TestWorld t, Player p) Newcomer()
        {
            var t = new TestWorld(tutorial: true);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 1, Village);
            return (t, p);
        }

        [Fact]
        public void NewCharactersGetALabeledGuideThatExplainsWithTheirOwnKeys()
        {
            var t = new TestWorld(tutorial: true);
            var settings = new GameSettings();
            settings.Keys.Bind("move_forward", "UpArrow");
            var data = t.World.CreateCharacterTemplate("Neuling", Race.Aldorer, CharClass.Warrior, Gender.Female, new Server.Persistence.AppearanceDto());
            data.Id = t.Store.CreateAsync(data).Result;
            data.ClientSettings = settings.ToJson();
            data.Position = Village;
            var p = t.World.EnterWorld(data, new Server.Net.RecordingConnection());
            var conn = t.Conn(p);
            var guide = t.World.Tutorial.GuideOf(p)!;
            Assert.Equal("KI-Erklärerin", guide.Template.Title);
            Assert.Equal(guide.Id, conn.Of<S_Tutorial>().Last().GuideId);
            Assert.Contains(conn.Of<S_Chat>(), c => c.Channel == ChatChannel.Whisper && c.SenderName.Contains("KI-Erklärerin") && c.SenderIsBot);
            t.Run(9); // Begrüßung geht von allein weiter
            var move = conn.Of<S_Tutorial>().Last();
            Assert.Equal("Bewegen", move.Title);
            Assert.Contains("[UpArrow]", move.Text);
            Assert.DoesNotContain("{key:", move.Text);
            // Niemand kann die Erklärerin angreifen
            var wolf = t.Spawn("mob_forest_wolf", guide.Position + new Vec3(2, 0, 0));
            Assert.False(wolf.CanAttack(guide));
        }

        [Fact]
        public void StepsCompleteThroughRealActionsUntilTheTutorialIsDone()
        {
            var (t, p) = Newcomer();
            var conn = t.Conn(p);
            t.Run(9);
            var order = new (ProgressEvent ev, string target, int count)[]
            {
                (ProgressEvent.Move, "", 15), (ProgressEvent.AcceptQuest, "q_any", 1), (ProgressEvent.Target, "mob_young_wolf", 1),
                (ProgressEvent.CastAbility, "x", 1), (ProgressEvent.Kill, "mob_young_wolf", 1), (ProgressEvent.Loot, "", 1),
                (ProgressEvent.CompleteQuest, "q_any", 1), (ProgressEvent.LearnAbility, "x", 1), (ProgressEvent.GroupWithBot, "", 1),
                (ProgressEvent.Bind, "", 1),
            };
            foreach (var (ev, target, count) in order)
            {
                int before = conn.Of<S_Tutorial>().Last().Step;
                // Falsche Handlungen bringen nichts
                t.World.ReportProgress(p, ProgressEvent.Emote, "wave");
                Assert.Equal(before, conn.Of<S_Tutorial>().Last().Step);
                for (int i = 0; i < count; i++) t.World.ReportProgress(p, ev, target);
                Assert.Equal(before + 1, conn.Of<S_Tutorial>().Last().Step);
            }
            Assert.True(conn.Of<S_Tutorial>().Last().Done);
            Assert.True(p.Data.Tutorial!.Done);
            Assert.True(t.World.Achievements.Has(p, "ach_tutorial"));
            Assert.False(t.World.Tutorial.IsActive(p));
            var guide = t.World.Zone("zone_morgentauwald")!.Entities.Values.OfType<Creature>().Single(c => c.GuideFor == p);
            t.Run(8);
            Assert.True(guide.Removed); // verabschiedet sich und geht
        }

        [Fact]
        public void GuideHelpsWhenStuckAndPointsAtTheGoal()
        {
            var (t, p) = Newcomer();
            var conn = t.Conn(p);
            t.Run(9);
            for (int i = 0; i < 15; i++) t.World.ReportProgress(p, ProgressEvent.Move);
            var quest = conn.Of<S_Tutorial>().Last();
            Assert.Equal("Eine Aufgabe", quest.Title);
            var focus = p.Zone!.GetUnit(quest.FocusEntityId) as Creature;
            Assert.NotNull(focus);
            Assert.True(focus!.Template.Roles.HasFlag(NpcRole.QuestGiver));
            conn.Clear();
            t.Run(TutorialSystemHint + 2);
            Assert.NotEqual("", conn.Of<S_Tutorial>().Last().Hint);
            Assert.Contains(conn.Of<S_Emote>(), e => e.EmoteId == "point" && e.TargetId == focus.Id);
        }

        private const float TutorialSystemHint = Server.Systems.TutorialSystem.HintAfter;

        [Fact]
        public void GuideFollowsAndCanBeDismissedOrRecalled()
        {
            var (t, p) = Newcomer();
            var guide = t.World.Tutorial.GuideOf(p)!;
            t.World.MoveUnit(p, p.Position + new Vec3(25, 0, 0));
            t.Run(6);
            Assert.True(guide.DistanceTo(p) < 6, $"Abstand {guide.DistanceTo(p):0.0}");
            t.World.Tutorial.Skip(p);
            Assert.True(p.Data.Tutorial!.Skipped);
            Assert.True(t.Conn(p).Of<S_Tutorial>().Last().Done);
            t.Run(8);
            Assert.True(guide.Removed);
            t.World.Tutorial.Restart(p);
            Assert.Equal(0, p.Data.Tutorial!.Step);
            Assert.NotNull(t.World.Tutorial.GuideOf(p));
            Assert.Equal("Willkommen", t.Conn(p).Of<S_Tutorial>().Last().Title);
        }

        [Fact]
        public void VeteransAndBotsGetNoTutorial()
        {
            var t = new TestWorld(tutorial: true);
            var data = t.World.CreateCharacterTemplate("Veteranin", Race.Aldorer, CharClass.Warrior, Gender.Female, new Server.Persistence.AppearanceDto());
            data.Level = 20;
            data.Id = t.Store.CreateAsync(data).Result;
            var vet = t.World.EnterWorld(data, new Server.Net.RecordingConnection());
            Assert.False(t.World.Tutorial.IsActive(vet));
            Assert.True(t.Conn(vet).Of<S_Tutorial>().Single().Done);
            var bot = t.CreateBot(vet, Role.Healer);
            t.World.Tutorial.OnEnterWorld(bot);
            Assert.False(t.World.Tutorial.IsActive(bot));
            // Wer übersprungen hat, bekommt beim nächsten Betreten kein Tutorial mehr
            var (t2, p) = Newcomer();
            t2.World.Tutorial.Skip(p);
            t2.World.Logout(p);
            t2.World.Persistence!.Flush(TimeSpan.FromSeconds(5));
            var again = t2.World.EnterWorld(t2.Store.GetAsync(p.Data.Id).Result!, new Server.Net.RecordingConnection());
            Assert.False(t2.World.Tutorial.IsActive(again));
        }

        [Fact]
        public void KeyTokensRenderWithBindings()
        {
            var k = Keybindings.CreateDefault();
            Assert.Equal("Drückt [1] oder [Tab].", TutorialText.Render("Drückt {key:action_1_1} oder {key:target_next}.", k));
            k.Unbind("target_next");
            Assert.Contains("nicht belegt", TutorialText.Render("{key:target_next}", k));
        }
    }
}
