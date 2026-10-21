using EternalRealms.Server.AI;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.Systems;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using Xunit.Abstractions;

namespace EternalRealms.Tests
{
    /// <summary>
    /// Kampfsimulation für 5er-Dungeons: ein vom Test gesteuerter Spieler (weicht aus, schlägt zu, nimmt vorrangige Adds)
    /// und vier KI-Mitspieler einer Schwierigkeitsstufe gegen einen Boss der Wurzelkrypta.
    /// </summary>
    public static class EncounterSim
    {
        public sealed class Outcome
        {
            public string Boss = "";
            public BotDifficulty Difficulty;
            public int Seed;
            public bool Won;
            public EncounterSystem.Result? Result;
            public int BotGroundHits, HumanGroundHits;
            public bool PhaseTwoReached;
            public bool BotsHitPriorityAdd;
            public TestWorld World = null!;
            public Player Human = null!;
            public List<Player> Bots = new();
            public Creature BossUnit = null!;
        }

        public static readonly (string Id, int Level)[] Bosses = { ("boss_moderkeim", 16), ("boss_elvaine", 17), ("boss_morrak", 18) };

        public static Outcome Run(string bossId, int level, BotDifficulty difficulty, int seed, bool humanDodges = true, double maxSeconds = 300,
                                  Action<Outcome>? everySecond = null)
        {
            var t = new TestWorld(seed: seed);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, level, new Vec3(870, 0, 805), name: "Probandin");
            t.World.Groups.Create(p);
            var spec = new (Role Role, CharClass Class)[] { (Role.Tank, CharClass.Paladin), (Role.Healer, CharClass.Priest), (Role.RangedDps, CharClass.Mage), (Role.MeleeDps, CharClass.Rogue) };
            var bots = new List<Player>();
            foreach (var (role, cls) in spec)
            {
                var data = t.World.Bots.CreateBotData(p.Faction, role, level, cls, p.Zone!.Def.Id, difficulty: difficulty)!;
                data.Position = p.Position + new Vec3(2, 0, 2);
                var b = t.World.Bots.Spawn(data, p.Zone);
                t.World.Groups.Join(p.Group!, b);
                bots.Add(b);
            }
            t.Run(2);
            t.World.TeleportSpecial(p, "dungeon_wurzelkrypta", new Vec3(50, 0, 50));
            t.Run(3);
            t.World.Chat.Handle(p, ChatChannel.Party, "Wir gehen in den Dungeon", "");
            t.Run(12);
            var zone = p.Zone!;
            var boss = zone.Entities.Values.OfType<Creature>().First(c => c.Template.Id == bossId);
            foreach (var other in zone.Entities.Values.OfType<Creature>().Where(c => c != boss && !c.Template.IsNpc && !c.IsPet).ToList())
                zone.Remove(other); // nur der Boss – gemessen wird der Bosskampf, nicht der Weg dorthin
            t.World.MovePlayerWithinZone(p, boss.Position + new Vec3(14, 0, 14));
            t.Run(5);
            p.Target = boss;
            t.World.Chat.Handle(p, ChatChannel.Party, "Zieh!", "");
            t.Run(2);
            var o = new Outcome { Boss = bossId, Difficulty = difficulty, Seed = seed, World = t, Human = p, Bots = bots, BossUnit = boss };
            int resultsBefore = t.World.Encounters.Results.Count;
            int ticks = (int)(maxSeconds * 20);
            for (int i = 0; i < ticks; i++)
            {
                ScriptHuman(t, p, boss, humanDodges);
                t.World.Update(0.05f);
                if (boss.AI.PhaseIndex >= 1) o.PhaseTwoReached = true;
                if (bots.Any(b => b.Target is Creature { PriorityTarget: true })) o.BotsHitPriorityAdd = true;
                if (i % 20 == 0) everySecond?.Invoke(o);
                if (boss.IsDead || p.Group!.Members.All(m => m.IsDead)) break;
                if (t.World.Encounters.Results.Count > resultsBefore && !boss.IsDead && t.World.Encounters.Get(boss) == null) break; // Niederlage
            }
            for (int i = 0; i < 20; i++) t.World.Update(0.05f);
            o.Won = boss.IsDead;
            o.Result = t.World.Encounters.Results.Skip(resultsBefore).LastOrDefault(r => r.BossId == bossId);
            if (o.Result != null)
            {
                o.HumanGroundHits = o.Result.GroundHits.GetValueOrDefault(p.Name);
                o.BotGroundHits = o.Result.TotalGroundHits - o.HumanGroundHits;
            }
            return o;
        }

        /// <summary>Ein aufmerksamer Spieler: weicht nach etwa einer halben Sekunde aus, sonst Nahkampf am Boss bzw. vorrangigen Add.</summary>
        private static void ScriptHuman(TestWorld t, Player p, Creature boss, bool dodges)
        {
            if (p.IsDead || p.Zone == null) return;
            var w = t.World;
            var danger = dodges ? w.Encounters.DangerAt(p.Zone, p.Position, 0.5f) : null;
            float step = p.MoveSpeed * 0.05f;
            if (danger != null && w.Time - (danger.ArmAt - danger.Def.Warning) > 0.5)
            {
                var away = (p.Position - danger.Position).NormalizedXZ();
                if (away.LengthXZ < 0.1f) away = new Vec3(1, 0, 0);
                if (!p.IsRooted) w.MoveUnit(p, p.Position + away * step);
                return;
            }
            var prio = p.Zone.UnitsInRange(p.Position, 40).OfType<Creature>().FirstOrDefault(c => c.IsAlive && c.PriorityTarget);
            var target = prio ?? (boss.IsAlive ? boss : null);
            if (target == null) return;
            p.Target = target;
            p.AutoAttacking = true;
            float reach = p.MeleeReachTo(target) - 0.5f;
            if (p.DistanceTo(target) > reach && !p.IsRooted)
            {
                var next = Vec3.MoveTowards(p.Position, target.Position, Math.Min(step, p.DistanceTo(target) - reach + 0.1f));
                if (!dodges || w.Encounters.IsSafe(p.Zone, next, 0.3f)) w.MoveUnit(p, next);
            }
            else p.Facing = Vec3.FacingTowards(p.Position, target.Position);
        }
    }

    public class EncounterTests
    {
        private readonly ITestOutputHelper _out;
        public EncounterTests(ITestOutputHelper output) { _out = output; }

        private static (TestWorld t, Creature boss, Player a, Player b) Arena(string bossId = "boss_morrak")
        {
            var t = new TestWorld(seed: 3);
            var center = new Vec3(300, 0, 300);
            t.ClearMonstersAround(center, 120);
            var boss = t.Spawn(bossId, center);
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 18, center + new Vec3(3, 0, 0), name: "Standfest");
            var b = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 18, center + new Vec3(-3, 0, 0), name: "Flink");
            boss.AI.Engage(a);
            boss.AI.Engage(b);
            return (t, boss, a, b);
        }

        private static CreatureAbilityDef Ground(string ability, GroundPlacement placement, float radius = 4, float warning = 3, float duration = 0) =>
            new() { Ability = ability, Ground = new GroundZoneDef { Placement = placement, Radius = radius, Warning = warning, Duration = duration } };

        [Fact]
        public void GroundEffectsWarnFirstAndOnlyHitThoseStillInside()
        {
            var (t, boss, a, b) = Arena();
            Assert.NotNull(t.World.Encounters.Get(boss));
            t.World.Encounters.SpawnGround(boss, Ground("mob_root_eruption", GroundPlacement.EachPlayer), a);
            var zones = t.World.Encounters.ZonesIn(a.Zone).ToList();
            Assert.Equal(2, zones.Count);
            var warn = t.Conn(a).Of<S_GroundEffect>().Where(g => !g.Armed && !g.Removed).ToList();
            Assert.Equal(2, warn.Count);
            Assert.All(warn, g => Assert.InRange(g.Remaining, 2.9f, 3.01f));
            t.Run(1);
            Assert.DoesNotContain(a.Auras, x => x.Def.Id == "mob_root_snare"); // Vorwarnung: noch kein Schaden
            t.World.MovePlayerWithinZone(b, b.Position + new Vec3(-10, 0, 0)); // b weicht aus
            t.Run(2.2);
            Assert.Contains(a.Auras, x => x.Def.Id == "mob_root_snare");
            Assert.DoesNotContain(b.Auras, x => x.Def.Id == "mob_root_snare");
            Assert.Empty(t.World.Encounters.ZonesIn(a.Zone)); // Explosion: einmalig
            Assert.Contains(t.Conn(a).Of<S_GroundEffect>(), g => g.Removed);
            var e = t.World.Encounters.Get(boss)!;
            Assert.Equal(1, e.GroundHits.GetValueOrDefault("Standfest"));
            Assert.Equal(0, e.GroundHits.GetValueOrDefault("Flink"));
        }

        [Fact]
        public void LingeringZonesTickUntilTheyExpire()
        {
            var (t, boss, a, b) = Arena("boss_moderkeim");
            t.World.Encounters.SpawnGround(boss, Ground("mob_spore_carpet", GroundPlacement.Tank, radius: 5, warning: 2, duration: 6), a);
            Assert.Single(t.World.Encounters.ZonesIn(a.Zone));
            var target = boss.Threat.Top()!;
            t.Run(9);
            var hits = t.World.Encounters.Get(boss)!.GroundHits;
            Assert.InRange(hits.GetValueOrDefault(target.Name), 5, 8); // etwa einmal pro Sekunde über 6 s
            Assert.Empty(t.World.Encounters.ZonesIn(a.Zone).Where(z => z.Def.Duration == 6));
        }

        [Fact]
        public void BossEnragesWhenTheFightTakesTooLong()
        {
            var (t, boss, a, b) = Arena();
            var e = t.World.Encounters.Get(boss)!;
            Assert.Equal(240, boss.Template.EnrageAfter);
            t.Run(1.1);
            Assert.DoesNotContain(boss.Auras, x => x.Def.Id == "mob_berserk");
            Assert.InRange(t.Conn(a).Of<S_Encounter>().Last().EnrageIn, 230, 240);
            e.Start -= 240;
            t.Run(1.1);
            Assert.Contains(boss.Auras, x => x.Def.Id == "mob_berserk");
            Assert.True(e.Enraged);
            Assert.Contains(t.Conn(a).Of<S_Chat>(), c => c.Text.Contains("ungezügelte Wut"));
            Assert.Equal(0, t.Conn(a).Of<S_Encounter>().Last().EnrageIn);
        }

        [Fact]
        public void AWipeClearsTheMechanicsAndIsRecordedAsADefeat()
        {
            var (t, boss, a, b) = Arena();
            t.World.Encounters.SpawnGround(boss, Ground("mob_root_eruption", GroundPlacement.EachPlayer), a);
            t.World.Combat.Kill(boss, a);
            t.World.Combat.Kill(boss, b);
            t.Run(3);
            Assert.Null(t.World.Encounters.Get(boss));
            Assert.Empty(t.World.Encounters.ZonesIn(boss.Zone));
            var r = t.World.Encounters.Results.Last();
            Assert.False(r.Won);
            Assert.Equal(2, r.Deaths);
            Assert.Contains(t.Conn(a).Of<S_Encounter>(), m => m.State == EncounterState.Wiped);
            t.Run(10);
            Assert.Equal(boss.MaxHealth, boss.Health); // zurückgesetzt
        }

        [Fact]
        public void ContentDefinesMechanicsForAllThreeDungeonBosses()
        {
            var db = TestContent.Db;
            foreach (var (id, _) in EncounterSim.Bosses)
            {
                var c = db.Creatures[id];
                Assert.True(c.EnrageAfter > 0, id);
                var all = c.Phases.SelectMany(p => p.Abilities).ToList();
                Assert.True(all.Any(a => a.Ground != null) || all.Any(a => a.MustInterrupt), id + " ohne Mechanik");
                Assert.Contains(c.Phases.SelectMany(p => p.SummonAdds), s => s.Priority);
            }
            Assert.Contains(db.Creatures["boss_elvaine"].Phases.SelectMany(p => p.Abilities), a => a.MustInterrupt && a.Ability == "mob_dark_mending");
        }

        [Fact]
        public void BotsInterruptTheAnnouncedHealAndSwitchToPriorityAdds()
        {
            var o = EncounterSim.Run("boss_elvaine", 17, BotDifficulty.Erfahren, seed: 5);
            _out.WriteLine(o.Result?.ToString() ?? "kein Ergebnis");
            Assert.True(o.Won, o.Result?.ToString() ?? "kein Ergebnis");
            Assert.True(o.Result!.Interrupts >= 1, o.Result.ToString());
            Assert.True(o.Result.Interrupts > o.Result.MissedInterrupts, o.Result.ToString());
            Assert.Contains(o.World.Conn(o.Human).Of<S_Chat>(), c => c.Text.StartsWith("[Boss]") && c.Text.Contains("unterbrechen"));
            Assert.True(o.PhaseTwoReached && o.BotsHitPriorityAdd, "KI-Mitspieler greifen die beschworene Dryade an");
        }

        [Fact]
        public void VeteranBotsDodgeFarMoreReliablyThanBeginners()
        {
            int vet = 0, beg = 0;
            foreach (var seed in new[] { 1, 2 })
            {
                vet += EncounterSim.Run("boss_morrak", 18, BotDifficulty.Veteran, seed).BotGroundHits;
                beg += EncounterSim.Run("boss_morrak", 18, BotDifficulty.Anfaenger, seed).BotGroundHits;
            }
            _out.WriteLine($"Treffer durch Bodeneffekte bei Morrak (2 Kämpfe): Veteran {vet}, Anfänger {beg}");
            Assert.True(beg > vet * 2 + 1, $"Anfänger {beg}, Veteran {vet}");
        }

        [Fact]
        public void ABotTakesOverWhenTheTankDies()
        {
            Player? standIn = null, tank = null;
            double killedAt = -1;
            var o = EncounterSim.Run("boss_morrak", 18, BotDifficulty.Erfahren, seed: 4, maxSeconds: 60, everySecond: x =>
            {
                var w = x.World.World;
                tank ??= x.Bots.First(b => b.Bot!.Role == Role.Tank);
                if (killedAt < 0 && w.Encounters.Get(x.BossUnit) != null && x.BossUnit.HealthPct < 0.95f)
                {
                    killedAt = w.Time;
                    w.Combat.Kill(x.BossUnit, tank);
                }
                if (killedAt >= 0 && standIn == null && x.BossUnit.Target is Player tp && tp != tank && tp.IsBot && tp.Bot!.LastDecision == "übernimmt als Tank")
                    standIn = tp;
                if (killedAt >= 0 && standIn == null && x.BossUnit.Target is Player tp2 && tp2.IsBot && tp2 != tank && w.Time - killedAt > 1)
                    standIn = tp2.Bot!.Role != Role.Healer ? tp2 : null;
            });
            Assert.True(killedAt >= 0, "Tank wurde im Kampf ausgeschaltet");
            Assert.NotNull(standIn);
            Assert.NotEqual(Role.Healer, standIn!.Bot!.Role);
            var takeover = TestContent.Db.BotDialog.Where(d => d.Event == "tank_takeover").SelectMany(d => d.Lines).ToList();
            Assert.Contains(o.World.Conn(o.Human).Of<S_Chat>(), c => c.SenderIsBot && c.SenderId == standIn.Id &&
                takeover.Any(l => l.Replace("{target}", o.BossUnit.Name) == c.Text));
        }

        /// <summary>
        /// Abstimmung (Kapitel 48): jeder Boss der Wurzelkrypta gegen einen Spieler mit vier KI-Mitspielern je Schwierigkeitsstufe.
        /// Ziel: ab „Normal“ zuverlässig schaffbar, Anfänger machen sichtbar mehr Fehler.
        /// </summary>
        /// <remarks>Mit ER_BALANCE_RUNS=n werden je Boss und Stufe n Kämpfe simuliert und zusammengefasst (Standard 1).</remarks>
        [Fact]
        public void BalanceAcrossDifficulties()
        {
            int runs = int.TryParse(Environment.GetEnvironmentVariable("ER_BALANCE_RUNS"), out var n) ? Math.Clamp(n, 1, 50) : 1;
            var rows = new List<EncounterSim.Outcome>();
            foreach (var (id, level) in EncounterSim.Bosses)
                foreach (var d in new[] { BotDifficulty.Anfaenger, BotDifficulty.Normal, BotDifficulty.Erfahren, BotDifficulty.Veteran })
                {
                    if (d == BotDifficulty.Erfahren && runs == 1) continue;
                    for (int i = 0; i < runs; i++) rows.Add(EncounterSim.Run(id, level, d, seed: 11 + i));
                }
            if (runs == 1) foreach (var r in rows) _out.WriteLine(Row(r));
            else
                foreach (var g in rows.GroupBy(r => (r.Boss, r.Difficulty)))
                    _out.WriteLine($"| {TestContent.Db.Creatures[g.Key.Boss].Name} | {BotPersonality.DifficultyLabel(g.Key.Difficulty)} | {g.Count(r => r.Won)}/{g.Count()} | " +
                                   $"{g.Where(r => r.Won).Select(r => r.Result!.Duration).DefaultIfEmpty(0).Average():0} s | {g.Average(r => r.Result?.Deaths ?? 0):0.0} | " +
                                   $"{g.Average(r => r.BotGroundHits):0.0} | {g.Sum(r => r.Result?.Interrupts ?? 0)}/{g.Sum(r => (r.Result?.Interrupts ?? 0) + (r.Result?.MissedInterrupts ?? 0))} |");
            Assert.All(rows.Where(r => r.Difficulty >= BotDifficulty.Normal), r => Assert.True(r.Won, Row(r)));
            Assert.All(rows.Where(r => r.Won), r => Assert.True(r.Result!.Duration < r.World.World.Content.Creatures[r.Boss].EnrageAfter, Row(r)));
        }

        public static string Row(EncounterSim.Outcome r) =>
            $"{r.Boss,-15} {BotPersonality.DifficultyLabel(r.Difficulty),-9} {(r.Won ? "Sieg" : "verloren"),-8} {r.Result?.Duration ?? 0,5:0} s  " +
            $"Tode {r.Result?.Deaths ?? 0}  Bodentreffer Bots {r.BotGroundHits} Spieler {r.HumanGroundHits}  " +
            $"Unterbrechungen {r.Result?.Interrupts ?? 0}/{(r.Result?.Interrupts ?? 0) + (r.Result?.MissedInterrupts ?? 0)}";
    }
}
