using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    public class QuestTests
    {
        private static Creature Npc(TestWorld t, string id, string zone = "zone_morgentauwald") =>
            t.World.Zone(zone)!.Entities.Values.OfType<Creature>().First(c => c.Template.Id == id);

        private static void GoTo(TestWorld t, Player p, Entity e) => t.World.MovePlayerWithinZone(p, e.Position + new Vec3(2, 0, 0));

        [Fact]
        public void KillQuestFromAcceptToReward()
        {
            var t = new TestWorld();
            var jorin = Npc(t, "npc_mt_jorin");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 1, jorin.Position + new Vec3(2, 0, 0));
            t.Run(0.3);
            Assert.Equal(QuestMarker.Available, t.World.Quests.MarkerFor(p, jorin));
            Assert.Null(t.World.Quests.Accept(p, jorin, "q_mt_aldorer"));
            Assert.Equal(QuestState.Active, p.Data.Quests["q_mt_aldorer"].State);
            Assert.NotNull(t.World.Quests.Accept(p, jorin, "q_mt_aldorer")); // nicht doppelt
            for (int i = 0; i < 6; i++)
            {
                var wolf = t.Spawn("mob_young_wolf", p.Position + new Vec3(4, 0, i), level: 1);
                t.World.Combat.DealDamage(p, wolf, 9999, School.Physical, null, "test", false, false);
            }
            Assert.Equal(QuestState.Complete, p.Data.Quests["q_mt_aldorer"].State);
            var vogt = Npc(t, "npc_mt_vogt");
            Assert.Equal(QuestMarker.ReadyToTurnIn, t.World.Quests.MarkerFor(p, vogt));
            Assert.Equal("Zu weit entfernt.", t.World.Quests.TurnIn(p, vogt, "q_mt_aldorer", -1));
            GoTo(t, p, vogt);
            int xp = p.Data.Xp + p.Level * 100000;
            Assert.Null(t.World.Quests.TurnIn(p, vogt, "q_mt_aldorer", -1));
            Assert.Contains("q_mt_aldorer", p.Data.CompletedQuests);
            Assert.True(p.Data.Xp + p.Level * 100000 > xp, "Questbelohnung gibt Erfahrung");
            Assert.True(p.Data.Reputation["rep_morgentau"] > 0);
            Assert.True(p.Money >= 10);
        }

        [Fact]
        public void CollectQuestFromWorldObjects()
        {
            var t = new TestWorld();
            var dagra = Npc(t, "npc_mt_dagra");
            var p = t.CreatePlayer(Race.Felsgeborene, CharClass.Warrior, 1, dagra.Position + new Vec3(2, 0, 0));
            t.ClearMonstersAround(dagra.Position, 80);
            Assert.Null(t.World.Quests.Accept(p, dagra, "q_mt_felsgeborene"));
            var ores = t.World.Zone("zone_morgentauwald")!.Objects.Where(o => o.Def.Id == "obj_iron_ore").ToList();
            Assert.NotEmpty(ores);
            foreach (var ore in ores)
            {
                if (p.Data.Quests["q_mt_felsgeborene"].State == QuestState.Complete) break;
                t.World.MovePlayerWithinZone(p, ore.Position + new Vec3(1, 0, 0));
                Assert.Null(t.World.Interactions.Interact(p, ore.Id));
                t.Run(2.2);
                Assert.False(ore.Available);
            }
            Assert.Equal(QuestState.Complete, p.Data.Quests["q_mt_felsgeborene"].State);
            Assert.True(p.CountItem("item_q_iron_ore_chunk") >= 6);
            var vogt = Npc(t, "npc_mt_vogt");
            GoTo(t, p, vogt);
            Assert.Null(t.World.Quests.TurnIn(p, vogt, "q_mt_felsgeborene", -1));
            Assert.True(p.CountItem("item_q_iron_ore_chunk") < 2); // genau die geforderte Menge wird abgegeben
        }

        [Fact]
        public void RaceAndClassRestrictionsApply()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 6);
            Assert.NotNull(t.World.Quests.CanAccept(p, TestContent.Db.Quests["q_mt_felsgeborene"]));
            Assert.NotNull(t.World.Quests.CanAccept(p, TestContent.Db.Quests["q_mt_class_warrior"]));
            Assert.Null(t.World.Quests.CanAccept(p, TestContent.Db.Quests["q_mt_class_mage"]));
            Assert.NotNull(t.World.Quests.CanAccept(p, TestContent.Db.Quests["q_as_echsen"])); // andere Fraktion
            Assert.NotNull(t.World.Quests.CanAccept(p, TestContent.Db.Quests["q_mt_brief"])); // Vorgänger fehlt
        }

        [Fact]
        public void DeliveryAndExplorationQuest()
        {
            var t = new TestWorld();
            var vogt = Npc(t, "npc_mt_vogt");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Paladin, 6, vogt.Position + new Vec3(2, 0, 0));
            p.Data.CompletedQuests.Add("q_mt_wolfshain");
            Assert.Null(t.World.Quests.Accept(p, vogt, "q_mt_brief"));
            Assert.Equal(1, p.CountItem("item_q_sealed_letter"));
            var rhea = Npc(t, "npc_mt_rhea");
            t.ClearMonstersAround(rhea.Position, 60);
            GoTo(t, p, rhea);
            t.Run(0.5); // Gebiet "Alter Wachturm" wird betreten
            Assert.Contains("area_mt_wachturm", p.Data.DiscoveredAreas);
            Assert.Null(t.World.Interactions.Interact(p, rhea.Id)); // Gespräch übergibt den Brief
            Assert.Equal(0, p.CountItem("item_q_sealed_letter"));
            Assert.Equal(QuestState.Complete, p.Data.Quests["q_mt_brief"].State);
            Assert.Null(t.World.Quests.TurnIn(p, rhea, "q_mt_brief", -1));
            // Folgequests sind jetzt bei Rhea verfügbar
            Assert.Contains(t.World.Quests.AvailableFrom(p, rhea), q => q.Id == "q_mt_spinnen");
        }

        [Fact]
        public void ChoiceQuestsExcludeEachOther()
        {
            var t = new TestWorld();
            var lenz = Npc(t, "npc_mt_lenz");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Priest, 10, lenz.Position + new Vec3(2, 0, 0));
            p.Data.CompletedQuests.Add("q_mt_grell");
            Assert.Equal(2, t.World.Quests.AvailableFrom(p, lenz).Count());
            Assert.Null(t.World.Quests.Accept(p, lenz, "q_mt_gnade"));
            Assert.Equal("Ihr habt Euch bereits anders entschieden.", t.World.Quests.CanAccept(p, TestContent.Db.Quests["q_mt_gerechtigkeit"]));
        }

        [Fact]
        public void RewardChoiceIsRequiredAndGranted()
        {
            var t = new TestWorld();
            var vogt = Npc(t, "npc_mt_vogt");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 4, vogt.Position + new Vec3(2, 0, 0));
            Assert.Null(t.World.Quests.Accept(p, vogt, "q_mt_wolfshain"));
            p.Data.Quests["q_mt_wolfshain"].Counts[0] = 10;
            Assert.Equal("Bitte wählt eine Belohnung.", t.World.Quests.TurnIn(p, vogt, "q_mt_wolfshain", -1));
            Assert.Null(t.World.Quests.TurnIn(p, vogt, "q_mt_wolfshain", 2));
            Assert.Equal(1, p.CountItem("item_wolfshain_chain_legs"));
        }

        [Fact]
        public void EscortQuestWithBotsReachesTheVillage()
        {
            var t = new TestWorld(seed: 99);
            var bram = Npc(t, "npc_mt_bram");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 8, bram.Position + new Vec3(2, 0, 0));
            t.World.Groups.Create(p);
            var healer = t.CreateBot(p, Role.Healer, CharClass.Priest);
            var dps = t.CreateBot(p, Role.RangedDps, CharClass.Mage);
            t.World.Groups.Join(p.Group!, healer);
            t.World.Groups.Join(p.Group!, dps);
            p.Data.CompletedQuests.Add("q_mt_vermisst");
            Assert.Null(t.World.Quests.Accept(p, bram, "q_mt_bram_eskorte"));
            var escort = p.EscortNpc!;
            Assert.NotNull(escort);
            // "Spieler": bleibt beim Begleiter und greift Angreifer an.
            for (int s = 0; s < 240 && p.Data.Quests.TryGetValue("q_mt_bram_eskorte", out var prog) && prog.State == QuestState.Active; s++)
            {
                if (p.IsDead) { t.World.Death.ReleaseSpirit(p); t.World.Death.SpiritHealer(p, Npc(t, "npc_mt_spirit_healer")); }
                var threat = escort.Zone?.UnitsInRange(escort.Position, 25).OfType<Creature>().FirstOrDefault(c => c.IsAlive && c.IsHostileTo(p));
                if (threat != null) { p.Target = threat; p.AutoAttacking = true; if (p.DistanceTo(threat) > 3) t.World.MoveUnit(p, Vec3.MoveTowards(p.Position, threat.Position, 6)); }
                else if (!escort.Removed) t.World.MoveUnit(p, Vec3.MoveTowards(p.Position, escort.Position + new Vec3(2, 0, 0), 6));
                t.Run(1);
            }
            Assert.Equal(QuestState.Complete, p.Data.Quests["q_mt_bram_eskorte"].State);
            Assert.True(t.Conn(p).Of<S_Chat>().Any(c => c.Text.Contains("Spinnen")), "Hinterhalte wurden ausgelöst");
        }
    }
}
