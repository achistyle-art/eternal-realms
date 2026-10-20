using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Client.Anim;
using EternalRealms.Shared.Client.Audio;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    /// <summary>Kapitel 33/34/51/52: Emotes, Animationszustände, erzeugte Klänge, Grafikstufen, Leistungsmessung.</summary>
    public class PresentationTests
    {
        private static readonly Vec3 Village = new(400, 0, 400);

        [Fact]
        public void EmotesAreLoadedWithUniqueCommandsAndKnownAnimations()
        {
            var db = TestContent.Db;
            Assert.True(db.Emotes.Count >= 20);
            Assert.Equal("wave", db.EmoteCommands["winken"].Id);
            Assert.Equal("dance", db.EmoteCommands["tanzen"].Id);
            Assert.Empty(db.Validate());
        }

        [Fact]
        public void EmoteReachesPlayersInRangeWithTextAndAnimation()
        {
            var t = new TestWorld();
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village, name: "Winker");
            var b = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 5, Village + new Vec3(3, 0, 0), name: "Nachbar");
            var far = t.CreatePlayer(Race.Aldorer, CharClass.Priest, 5, Village + new Vec3(200, 0, 0), name: "Fern");
            foreach (var p in new[] { a, b, far }) t.Conn(p).Clear();
            Assert.Null(t.World.Emotes.Perform(a, "wave", b.Id));
            var em = t.Conn(b).Of<S_Emote>().Single();
            Assert.Equal(("wave", a.Id, b.Id), (em.EmoteId, em.SourceId, em.TargetId));
            Assert.Contains(t.Conn(b).Of<S_Chat>(), c => c.Channel == ChatChannel.Emote && c.Text == "Winker winkt Nachbar zu.");
            Assert.Empty(t.Conn(far).Of<S_Emote>());
            Assert.NotNull(t.World.Emotes.Perform(a, "gibt_es_nicht", 0));
            // Spam: zweites Emote innerhalb einer Sekunde verpufft
            t.Conn(b).Clear();
            t.World.Emotes.Perform(a, "bow", 0);
            Assert.Empty(t.Conn(b).Of<S_Emote>());
        }

        [Fact]
        public void BotsReturnGesturesAimedAtThem()
        {
            var t = new TestWorld(seed: 7);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village);
            var bot = t.CreateBot(p, Role.Healer);
            t.Conn(p).Clear();
            bool answered = false;
            for (int attempt = 0; attempt < 5 && !answered; attempt++)
            {
                t.World.Emotes.Perform(p, "wave", bot.Id);
                t.Run(3);
                answered = t.Conn(p).Of<S_Emote>().Any(e => e.SourceId == bot.Id && e.EmoteId == "wave" && e.TargetId == p.Id);
            }
            Assert.True(answered, "Der Bot hat das Winken nicht erwidert.");
        }

        [Fact]
        public void AnimationFollowsMovementCombatCastingAndDeath()
        {
            var a = new AnimationController();
            double now = 0;
            AnimPose Step(float speed, UnitFlags flags = UnitFlags.None, bool combat = false, bool cast = false, bool mounted = false)
            {
                now += 0.05;
                return a.Update(now, speed, flags, combat, cast, false, mounted);
            }
            Assert.Equal(AnimClip.Idle, Step(0).Base);
            Assert.Equal(AnimClip.Walk, Step(2).Base);
            Assert.Equal(AnimClip.Run, Step(7).Base);
            Assert.Equal(AnimClip.Backpedal, Step(-3).Base);
            Assert.Equal(AnimClip.CombatIdle, Step(0, combat: true).Base);
            Assert.Equal(AnimClip.CastLoop, Step(0, cast: true).Overlay);
            Assert.Equal(AnimClip.Mounted, Step(6, mounted: true).Base);

            a.OnAttack(now, ranged: false, armed: true);
            var pose = Step(0, combat: true);
            Assert.Equal(AnimClip.AttackMelee, pose.Overlay);
            a.OnHit(now); // Treffer unterbricht den Angriff nicht
            Assert.Equal(AnimClip.AttackMelee, Step(0, combat: true).Overlay);
            for (int i = 0; i < 20; i++) Step(0, combat: true);
            Assert.Null(Step(0, combat: true).Overlay);

            // Schleifen-Emote bleibt, bis man sich bewegt
            a.OnEmote(now, TestContent.Db.Emotes["dance"]);
            Assert.Equal(AnimClip.Dance, Step(0).Base);
            Assert.Equal(AnimClip.Dance, Step(0).Base);
            Assert.Equal(AnimClip.Walk, Step(2).Base);
            Assert.Equal(AnimClip.Idle, Step(0).Base);

            Assert.Equal(AnimClip.Death, Step(0, UnitFlags.Dead).Base);
            for (int i = 0; i < 30; i++) Step(0, UnitFlags.Dead);
            Assert.Equal(AnimClip.Dead, Step(0, UnitFlags.Dead).Base);
            Assert.Equal(AnimClip.Ghost, Step(0, UnitFlags.Dead | UnitFlags.Ghost).Base);
        }

        [Fact]
        public void WalkCycleIsCoupledToDistanceSoFeetDoNotSlide()
        {
            var a = new AnimationController();
            a.Update(0, 0, UnitFlags.None, false, false, false, false);
            // 1 s Gehen mit genau einer Schrittlänge pro Sekunde = ein voller Zyklus
            float last = 0;
            for (int i = 1; i <= 20; i++) last = a.Update(i * 0.05, AnimationController.WalkStride, UnitFlags.None, false, false, false, false).BasePhase;
            Assert.True(last < 0.06f || last > 0.94f, $"Phase nach einem Schritt: {last}");
        }

        [Fact]
        public void SynthesizedSoundsAreDeterministicAudibleAndNeverClip()
        {
            foreach (var key in Synth.KnownEffects())
            {
                var a = Synth.Effect(key);
                Assert.Equal(a, Synth.Effect(key));
                Assert.All(a, v => Assert.InRange(v, -1f, 1f));
                Assert.True(Rms(a) > 0.02, key + " ist zu leise");
                Assert.True(a.Length < Synth.DefaultRate * 3, key + " ist zu lang");
            }
            Assert.NotEqual(Synth.Effect("spell_fire_cast"), Synth.Effect("spell_frost_cast"));
        }

        [Fact]
        public void MusicAndAmbienceLoopSeamlessly()
        {
            foreach (var key in new[] { "music_morgentau_day", "music_boss", "music_combat_steppe", "music_village" })
            {
                var m = Synth.Music(key);
                Assert.Equal(m, Synth.Music(key));
                Assert.All(m, v => Assert.InRange(v, -1f, 1f));
                Assert.InRange(m.Length / (double)Synth.DefaultRate, 20, 70);
                Assert.True(Rms(m) > 0.05);
                // Nahtstelle: Sprung zwischen letztem und erstem Wert nicht größer als sonst zwischen Nachbarn
                float seam = Math.Abs(m[^1] - m[0]);
                float maxStep = 0;
                for (int i = 1; i < m.Length; i++) maxStep = Math.Max(maxStep, Math.Abs(m[i] - m[i - 1]));
                Assert.True(seam <= maxStep + 1e-4f, $"{key}: Knacken an der Schleife ({seam} > {maxStep})");
            }
            Assert.True(Synth.MoodFor("music_boss").Drums);
            Assert.False(Synth.MoodFor("music_morgentau_night").Drums);
            var amb = Synth.Ambience("amb_forest_birds", 10);
            Assert.Equal(10 * Synth.DefaultRate, amb.Length);
            Assert.True(Rms(amb) > 0.01);
        }

        private static double Rms(float[] a) => Math.Sqrt(a.Sum(v => (double)v * v) / a.Length);

        [Fact]
        public void MusicDirectorSwitchesWithHysteresis()
        {
            var db = TestContent.Db;
            var zone = db.Zones["zone_morgentauwald"];
            var md = new MusicDirector();
            var calm = new MusicSituation { Zone = zone };
            Assert.True(md.Update(0.1f, calm));
            Assert.Equal("music_morgentau_day", md.Current);
            var fight = new MusicSituation { Zone = zone, InCombat = true };
            Assert.False(md.Update(1f, fight)); // ein kurzer Treffer wechselt die Musik noch nicht
            Assert.True(md.Update(1.5f, fight));
            Assert.StartsWith("music_combat_", md.Current);
            Assert.False(md.Update(3f, calm)); // klingt nach
            Assert.True(md.Update(6f, calm));
            Assert.Equal("music_morgentau_day", md.Current);
            Assert.True(md.Update(0.1f, new MusicSituation { Zone = zone, BossFight = true }));
            Assert.Equal("music_boss", md.Current);
            Assert.Equal("music_ghost", MusicDirector.Choose(new MusicSituation { Zone = zone, Ghost = true }, false));
            Assert.Equal("music_village", MusicDirector.Choose(new MusicSituation { Zone = zone, Area = zone.Areas.First(a => a.Id == "area_mt_dorf") }, false));
            Assert.EndsWith("_night", MusicDirector.Choose(new MusicSituation { Zone = zone, Night = true }, false));
        }

        [Fact]
        public void SoundCuesOnlyForOwnInvolvement()
        {
            var db = TestContent.Db;
            var c = new GameClient();
            c.Receive(new S_EnterWorld { EntityId = 5, ZoneId = "zone_morgentauwald" });
            Assert.Equal("hit_physical", SoundEvents.For(new S_CombatEvent { SourceId = 9, TargetId = 5, Result = CombatResult.Hit, School = School.Physical }, c, db)!.Value.Key);
            Assert.Equal("spell_fire_impact", SoundEvents.For(new S_CombatEvent { SourceId = 5, TargetId = 9, Result = CombatResult.Hit, School = School.Fire }, c, db)!.Value.Key);
            Assert.Null(SoundEvents.For(new S_CombatEvent { SourceId = 8, TargetId = 9 }, c, db));
            Assert.True(SoundEvents.For(new S_LevelUp { EntityId = 5 }, c, db)!.Value.Ui);
        }

        [Fact]
        public void GraphicsPresetsScaleAndCustomIsDetected()
        {
            var low = GraphicsOptions.For(GraphicsPreset.Low);
            var ultra = GraphicsOptions.For(GraphicsPreset.Ultra);
            Assert.True(low.ViewDistance < ultra.ViewDistance && low.Msaa < ultra.Msaa && low.Shadows < ultra.Shadows);
            var g = new GraphicsOptions { Fullscreen = false, Width = 1600, Height = 900 };
            g.Apply(GraphicsPreset.High);
            Assert.Equal(GraphicsPreset.High, g.Preset);
            Assert.False(g.Fullscreen); // Anzeige-Einstellungen bleiben
            Assert.Equal(1600, g.Width);
            g.ViewDistance += 50;
            g.DetectCustom();
            Assert.Equal(GraphicsPreset.Custom, g.Preset);
            Assert.Equal(GraphicsPreset.Low, GraphicsOptions.Recommend(1024, 2, 4000));
            Assert.Equal(GraphicsPreset.Ultra, GraphicsOptions.Recommend(8192, 12, 32000));
            var s = GameSettings.FromJson("{\"Graphics\":{\"Msaa\":3,\"ViewDistance\":5000,\"FpsLimit\":5}}");
            Assert.Equal(2, s.Graphics.Msaa);
            Assert.Equal(800, s.Graphics.ViewDistance);
            Assert.Equal(20, s.Graphics.FpsLimit);
        }

        [Fact]
        public void FrameStatsReportAveragesAndPercentiles()
        {
            var f = new FrameStats(100);
            for (int i = 0; i < 99; i++) f.Add(10);
            f.Add(100);
            Assert.Equal(10.9f, f.Average, 3);
            Assert.Equal(10f, f.Percentile(99));
            Assert.Equal(100f, f.Percentile(100));
            Assert.Equal(100f, f.Max);
            for (int i = 0; i < 100; i++) f.Add(20); // Fenster rollt
            Assert.Equal(50f, f.Fps, 1);
        }

        /// <summary>Lasttest: 150 KI-Mitspieler und Spieler in einer Zone; ein Takt muss klar unter dem 50-ms-Budget (20 Hz) bleiben.</summary>
        [Fact]
        public void ServerTickStaysWithinBudgetUnderLoad()
        {
            var t = new TestWorld(ambientBots: false);
            var anchor = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, Village);
            var rng = new Random(3);
            for (int i = 0; i < 150; i++)
            {
                var bot = t.CreateBot(anchor, (Role)(i % 4));
                bot.Position = Village + new Vec3(rng.Next(-150, 150), 0, rng.Next(-150, 150));
            }
            t.Run(10);
            var stats = t.World.TickStats;
            Assert.True(stats.Count >= 150);
            Assert.True(stats.Average < 25, $"Takt Ø {stats.Average:0.0} ms");
            Assert.True(stats.Percentile(99) < 50, $"Takt 99 % {stats.Percentile(99):0.0} ms");
            var pong = new S_Pong { TickMs = stats.Average };
            Assert.Equal(stats.Average, ((S_Pong)Packets.Deserialize(Packets.Serialize(pong))).TickMs);
        }
    }
}
