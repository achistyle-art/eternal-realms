using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Tests
{
    /// <summary>Kapitel 35/36/37/47: Hauptstädte, Tag/Nacht und Wetter, Weltereignisse, Spawnregeln.</summary>
    public class WorldDynamicsTests
    {
        private static readonly Vec3 Village = new(400, 0, 400);

        private sealed class FixedRandom : IRandom
        {
            private readonly double _v;
            public FixedRandom(double v) { _v = v; }
            public double NextDouble() => _v;
            public int Range(int min, int max) => min;
        }

        // ------------------------------------------------------------ Hauptstädte

        [Fact]
        public void EachFactionHasTwoFullyServicedCapitals()
        {
            var db = TestContent.Db;
            foreach (var faction in new[] { Faction.Sonnenbuendnis, Faction.Schattenpakt })
            {
                var capitals = db.Zones.Values.Where(z => z.IsCapital && z.Faction == faction).ToList();
                Assert.Equal(2, capitals.Count);
                var classes = db.Races.Values.Where(r => r.Faction == faction).SelectMany(r => r.Classes).Distinct().ToList();
                foreach (var cap in capitals)
                {
                    Assert.True(cap.Playable);
                    Assert.All(cap.Areas, a => Assert.True(a.IsCity, $"{cap.Id}/{a.Id} ist kein Stadtgebiet"));
                    Assert.Contains(cap.Areas, a => a.IsInn);
                    Assert.NotEmpty(cap.Graveyards);
                    var roles = cap.Npcs.Select(n => db.Creatures[n.Npc]).ToList();
                    foreach (var role in new[] { NpcRole.Banker, NpcRole.Auctioneer, NpcRole.Innkeeper, NpcRole.FlightMaster, NpcRole.RidingTrainer, NpcRole.Guard, NpcRole.SpiritHealer, NpcRole.Vendor })
                        Assert.True(roles.Any(c => c.Roles.HasFlag(role)), $"{cap.Name}: {role} fehlt");
                    foreach (var cls in classes) Assert.Contains(roles, c => c.Roles.HasFlag(NpcRole.ClassTrainer) && c.TrainerClass == cls);
                    foreach (var prof in db.Professions.Keys) Assert.Contains(roles, c => c.TrainerProfession == prof);
                    Assert.Contains(cap.Objects, o => db.Objects[o.Object].IsMailbox);
                    Assert.All(roles.Where(c => c.Roles.HasFlag(NpcRole.Guard)), g => Assert.True(g.Elite && g.MinLevel == 60));
                    // Erreichbar per Fernflug und Portal – nur für die eigene Fraktion
                    var node = db.TravelNodes.Values.Single(n => n.Zone == cap.Id);
                    Assert.Contains(db.FlightPaths, f => f.To == node.Id && db.TravelNodes[f.From].Zone != cap.Id);
                    Assert.Contains(db.Objects.Values, o => o.TeleportZone == cap.Id && o.RequiredFaction == faction);
                }
            }
        }

        [Fact]
        public void PortalsLeadIntoTheOwnCapitalOnly()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village);
            var portal = t.World.Zone("zone_morgentauwald")!.Entities.Values.OfType<GameObjectEntity>().First(o => o.Def.TeleportZone == "zone_sonnenfeste");
            p.Position = portal.Position + new Vec3(1, 0, 0);
            Assert.Null(t.World.Interactions.Interact(p, portal.Id));
            Assert.Equal("zone_sonnenfeste", p.Zone!.Def.Id);
            Assert.True(p.CurrentArea?.IsCity);
            // Zurück per Portal der Stadt
            var back = p.Zone.Entities.Values.OfType<GameObjectEntity>().First(o => o.Def.TeleportZone == "zone_morgentauwald");
            p.Position = back.Position + new Vec3(1, 0, 0);
            Assert.Null(t.World.Interactions.Interact(p, back.Id));
            Assert.Equal("zone_morgentauwald", p.Zone!.Def.Id);

            var foe = t.CreatePlayer(Race.Grakar, CharClass.Warrior, 5, portal.Position + new Vec3(1, 0, 1), zone: "zone_morgentauwald");
            Assert.NotNull(t.World.Interactions.Interact(foe, portal.Id));
            Assert.Equal("zone_morgentauwald", foe.Zone!.Def.Id);
        }

        [Fact]
        public void LongDistanceFlightReachesTheCapital()
        {
            var t = new TestWorld();
            var master = t.World.Zone("zone_morgentauwald")!.Entities.Values.OfType<Creature>().First(c => c.Template.Id == "npc_mt_flightmaster");
            var p = t.CreatePlayer(Race.Elarii, CharClass.Mage, 5, master.Position + new Vec3(2, 0, 0));
            t.World.Economy.Credit(p, 1000, "test");
            p.Data.KnownTravelNodes.Add("tn_sm");
            Assert.Null(t.World.Interactions.TakeFlight(p, master.Id, "tn_sm"));
            Assert.Equal("zone_silbermond", p.Zone!.Def.Id);
            Assert.Equal(800, p.Money);
        }

        [Fact]
        public void CityGuardsAttackEnemyIntruders()
        {
            var t = new TestWorld();
            var zone = t.World.Zone("zone_kharzul")!;
            var guard = zone.Entities.Values.OfType<Creature>().First(c => c.Template.Roles.HasFlag(NpcRole.Guard) && c.AI.Patrol.Count == 0);
            var intruder = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 20, guard.Position + new Vec3(8, 0, 0), zone: "zone_kharzul");
            var local = t.CreatePlayer(Race.Grakar, CharClass.Warrior, 20, guard.Position + new Vec3(-8, 0, 0), zone: "zone_kharzul");
            t.Run(2);
            // Stadtwachen sind Elite der Stufe 60: der Eindringling ist bereits gefallen oder steht im Kampf
            Assert.True(intruder.IsDead || guard.Target == intruder, $"Wache: {guard.AI.State}");
            Assert.False(local.InCombat);
        }

        // ------------------------------------------------------------ Tag, Nacht, Wetter

        [Fact]
        public void WeatherShiftsSchoolsAndSight()
        {
            Assert.True(EnvironmentRules.SchoolDamage(Weather.Rain, 1, School.Fire) < 1);
            Assert.True(EnvironmentRules.SchoolDamage(Weather.Snow, 1, School.Frost) > 1);
            Assert.True(EnvironmentRules.SchoolDamage(Weather.Storm, 1, School.Nature) > 1);
            Assert.Equal(1f, EnvironmentRules.SchoolDamage(Weather.Clear, 1, School.Fire));
            Assert.True(EnvironmentRules.SightFactor(Weather.Fog, 1, 12) < EnvironmentRules.SightFactor(Weather.Clear, 0, 12));
            Assert.True(EnvironmentRules.SightFactor(Weather.Clear, 0, 23) < 1);
            Assert.True(EnvironmentRules.IsNight(22) && EnvironmentRules.IsNight(3) && !EnvironmentRules.IsNight(12));
            Assert.True(EnvironmentRules.NightCreatureDamage("Undead", 23) > 1);
            Assert.Equal(1f, EnvironmentRules.NightCreatureDamage("Wolf", 23));
        }

        [Fact]
        public void RainDampensFireDamageInTheWorld()
        {
            var t = new TestWorld();
            var zone = t.World.Zone("zone_morgentauwald")!;
            int Burn()
            {
                var target = t.Spawn("mob_boar", Village + new Vec3(300, 0, 300), level: 3);
                int before = target.Health;
                t.World.Combat.DealDamage(null, target, 20, School.Fire, null, "test", false, false);
                int dealt = before - target.Health;
                zone.Remove(target);
                return dealt;
            }
            zone.SetWeather(Weather.Clear, 0);
            int dry = Burn();
            zone.SetWeather(Weather.Rain, 1);
            int wet = Burn();
            Assert.True(wet < dry, $"Regen {wet} vs. trocken {dry}");
        }

        [Fact]
        public void NightCreaturesAppearAtNightAndVanishAtDawn()
        {
            var t = new TestWorld();
            var zone = t.World.Zone("zone_morgentauwald")!;
            int Wisps() => zone.Entities.Values.OfType<Creature>().Count(c => c.Template.Id == "mob_irrlicht" && c.IsAlive);
            t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Village); // Region aktiv halten
            t.World.SetGameHour(12);
            t.Run(2);
            Assert.Equal(0, Wisps());
            t.World.SetGameHour(23);
            t.Run(2);
            Assert.Equal(6, Wisps());
            t.World.SetGameHour(9);
            t.Run(2);
            Assert.Equal(0, Wisps());
            Assert.True(t.World.Content.Zones["zone_morgentauwald"].Spawns.Single(s => s.Creature == "mob_graumaehne").TimeOfDay == "night");
        }

        // ------------------------------------------------------------ Spawnregeln

        [Fact]
        public void RespawnSpeedsUpWithCrowdsButNotForRares()
        {
            Assert.Equal(1f, SpawnRules.CrowdFactor(1));
            Assert.Equal(0.5f, SpawnRules.CrowdFactor(5), 3);
            Assert.Equal(SpawnRules.MinCrowdFactor, SpawnRules.CrowdFactor(40));
            var def = new SpawnDef { RespawnSeconds = 100, RespawnMaxSeconds = 300 };
            Assert.Equal(200f, SpawnRules.RespawnDelay(def, new FixedRandom(0.5), 1, false), 3);
            Assert.Equal(100f, SpawnRules.RespawnDelay(def, new FixedRandom(0.5), 5, false), 3);
            Assert.Equal(200f, SpawnRules.RespawnDelay(def, new FixedRandom(0.5), 5, true), 3);
            var ph = new SpawnDef { Creature = "a", RareCreature = "r", RareChance = 0.05f };
            Assert.Equal("r", SpawnRules.PickCreature(ph, new FixedRandom(0.01), false));
            Assert.Equal("a", SpawnRules.PickCreature(ph, new FixedRandom(0.01), true)); // nie zwei Seltene
            Assert.Equal("a", SpawnRules.PickCreature(ph, new FixedRandom(0.5), false));
        }

        [Fact]
        public void WorldBossTimerSurvivesARestart()
        {
            var t = new TestWorld();
            var zone = t.World.Zone("zone_morgentauwald")!;
            var boss = zone.Entities.Values.OfType<Creature>().Single(c => c.Template.Id == "mob_thornmaw");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 20, boss.Position + new Vec3(3, 0, 0));
            t.World.Combat.DealDamage(p, boss, boss.Health * 10, School.Physical, null, "test", false, false);
            Assert.True(boss.IsDead);
            t.Run(200); // Leiche verschwindet, Wiederkehr wird geplant und gespeichert
            t.World.Persistence!.Flush(TimeSpan.FromSeconds(5));
            var timers = t.Store.LoadSpawnTimersAsync().Result;
            var at = Assert.Single(timers).Value;
            Assert.InRange((at - DateTime.UtcNow).TotalHours, 1.9, 4.1);
            // Neustart: frische Welt mit demselben Speicher – wie GameServer.CreateAsync: erst aufbauen, dann Zeiten anwenden
            var restarted = new TestWorld(store: t.Store);
            restarted.World.Persistence!.Flush(TimeSpan.FromSeconds(5));
            var loaded = restarted.Store.LoadSpawnTimersAsync().Result;
            Assert.Single(loaded); // die Erstbesetzung darf die gespeicherte Zeit nicht löschen
            Assert.Equal(1, restarted.World.ApplySpawnTimers(loaded));
            Assert.DoesNotContain(restarted.World.Zone("zone_morgentauwald")!.Entities.Values.OfType<Creature>(), c => c.Template.Id == "mob_thornmaw" && !c.Removed);
        }

        // ------------------------------------------------------------ Weltereignisse

        [Fact]
        public void EventsAreValidAndRunThroughPhasesToRewards()
        {
            var db = TestContent.Db;
            Assert.True(db.WorldEvents.Count >= 4);
            var t = new TestWorld();
            t.World.SetGameHour(23);
            var ev = db.WorldEvents["ev_mt_wolfsnacht"];
            t.ClearMonstersAround(ev.Position, 120);
            var hero = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 8, ev.Position + new Vec3(5, 0, 0));
            var bystander = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 8, Village);
            long money = hero.Money, otherMoney = bystander.Money;
            Assert.Null(t.World.Events.Start(ev.Id, skipWarmup: true));
            var run = t.World.Events.Get(ev.Id)!;
            for (int guard = 0; guard < 400 && t.World.Events.Get(ev.Id) != null; guard++)
            {
                t.Run(0.5);
                foreach (var c in run.Spawned.Where(c => c.IsAlive && !c.Removed).ToList())
                {
                    c.EnterCombatWith(hero);
                    t.World.Combat.DealDamage(hero, c, c.Health * 10, School.Physical, null, "test", false, false);
                }
            }
            Assert.Null(t.World.Events.Get(ev.Id));
            Assert.Equal(WorldEventState.Success, run.State);
            Assert.True(hero.Money >= money + ev.Rewards.Money);
            Assert.Equal(otherMoney, bystander.Money);
            Assert.True(hero.Data.Reputation.GetValueOrDefault("rep_morgentau") >= 150);
            var last = t.Conn(hero).Of<S_WorldEvent>().Last();
            Assert.Equal(WorldEventState.Success, last.State);
            Assert.True(last.Participating);
            Assert.Contains(t.Conn(bystander).Of<S_WorldEvent>(), e => !e.Participating);
        }

        [Fact]
        public void DefendEventFailsWhenTheProtectedNpcDies()
        {
            var t = new TestWorld();
            t.World.SetGameHour(12);
            Assert.Null(t.World.Events.Start("ev_mt_karren", skipWarmup: true));
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 8, t.World.Content.WorldEvents["ev_mt_karren"].Position);
            t.Run(1);
            var run = t.World.Events.Get("ev_mt_karren")!;
            Assert.NotNull(run.Defended);
            t.World.Combat.DealDamage(null, run.Defended!, run.Defended!.Health * 10, School.Physical, null, "test", false, false);
            t.Run(1);
            Assert.Null(t.World.Events.Get("ev_mt_karren"));
            Assert.Equal(WorldEventState.Failed, run.State);
            Assert.All(run.Spawned, c => Assert.True(c.Removed || c.IsDead));
        }

        [Fact]
        public void IdleResidentsJoinRunningEvents()
        {
            var t = new TestWorld(seed: 11);
            t.World.SetGameHour(23);
            var ev = t.World.Content.WorldEvents["ev_mt_wolfsnacht"];
            var anchor = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 7, ev.Position + new Vec3(140, 0, 0));
            var resident = t.CreateBot(anchor, Role.MeleeDps, CharClass.Warrior, 7);
            Assert.Null(t.World.Events.Start(ev.Id, skipWarmup: true));
            t.Run(30, () => resident.Bot!.AutoActivity.Contains("Weltereignis"));
            Assert.Contains("Weltereignis", resident.Bot!.AutoActivity);
            float before = Vec3.DistanceXZ(resident.Position, ev.Position);
            t.Run(15);
            Assert.True(Vec3.DistanceXZ(resident.Position, ev.Position) < Math.Max(ev.Radius, before - 10), "Der Bewohner geht zum Ereignis");
        }
    }
}
