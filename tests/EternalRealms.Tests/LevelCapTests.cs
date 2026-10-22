using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Tests
{
    /// <summary>Block I: Stufenobergrenze aus dem Content und die Regionen für Stufe 12–20.</summary>
    public class LevelCapTests
    {
        private static readonly string[] NewZones = { "zone_westmark", "zone_rotsand" };

        private static Creature Npc(TestWorld t, string id, string zone) =>
            t.World.Zone(zone)!.Entities.Values.OfType<Creature>().First(c => c.Template.Id == id);

        [Fact]
        public void ManifestSetsLevelCapTwenty()
        {
            Assert.Equal(20, TestContent.Db.LevelCap);
            Assert.Equal(60, Experience.MaxLevel); // Regeln bleiben für 60 ausgelegt
        }

        [Fact]
        public void XpStopsAtTheCap()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 19);
            t.World.Progression.GiveXp(p, Experience.XpToNextLevel(19) * 5, "test");
            Assert.Equal(20, p.Level);
            Assert.Equal(0, p.Data.Xp);
            t.World.Progression.GiveXp(p, 50000, "test");
            Assert.Equal(20, p.Level);
            Assert.Equal(0, p.Data.Xp);
            Assert.Equal(0, p.BuildPlayerState().XpToNext);
        }

        [Fact]
        public void AchievementsAboveTheCapAreHidden()
        {
            var db = TestContent.Db;
            Assert.All(db.Achievements.Values.Where(a => a.Criteria.Any(c => c.Event == ProgressEvent.ReachLevel && c.Count > 20)),
                       a => Assert.False(db.IsReachable(a)));
            Assert.Contains(db.Achievements.Values, a => a.Criteria.Any(c => c.Event == ProgressEvent.ReachLevel && c.Count <= 20) && db.IsReachable(a));
        }

        [Theory]
        [InlineData("zone_westmark", Faction.Sonnenbuendnis)]
        [InlineData("zone_rotsand", Faction.Schattenpakt)]
        public void NewZonesArePlayableSettlements(string zoneId, Faction faction)
        {
            var db = TestContent.Db;
            var z = db.Zones[zoneId];
            Assert.True(z.Playable);
            Assert.Equal(faction, z.Faction);
            Assert.Equal((12, 20), (z.MinLevel, z.MaxLevel));
            var roles = z.Npcs.Select(n => db.Creatures[n.Npc]).Aggregate(NpcRole.None, (r, c) => r | c.Roles);
            foreach (var need in new[] { NpcRole.Innkeeper, NpcRole.Vendor, NpcRole.Repair, NpcRole.FlightMaster, NpcRole.SpiritHealer, NpcRole.Guard, NpcRole.QuestGiver })
                Assert.True(roles.HasFlag(need), $"{zoneId}: {need} fehlt");
            Assert.Contains(z.Areas, a => a.IsInn);
            Assert.Contains(z.Objects, o => db.Objects[o.Object].IsMailbox);
            Assert.Contains(db.TravelNodes.Values, n => n.Zone == zoneId && n.Faction == faction);
            // Gegner decken das ganze Stufenband ab, und ein Abschlussgegner der Stufe 20 wartet am Ende
            var mobs = z.Spawns.Select(s => db.Creatures[s.Creature]).ToList();
            for (int lvl = 12; lvl <= 19; lvl++) Assert.Contains(mobs, m => m.MinLevel <= lvl && m.MaxLevel >= lvl);
            Assert.Contains(mobs, m => m.Elite && m.MaxLevel == 20);
            Assert.DoesNotContain(db.Zones.Values, other => other != z && other.Name == z.Name);
        }

        [Fact]
        public void QuestChainsAreCompleteAndEndAtTwenty()
        {
            var db = TestContent.Db;
            foreach (var zoneId in NewZones)
            {
                var qs = db.Quests.Values.Where(q => q.Zone == zoneId && !q.Daily).ToList(); // Tagesquests gehören zum Endgame
                Assert.True(qs.Count >= 24, $"{zoneId}: nur {qs.Count} Quests");
                // Jede Quest ist von der Einstiegsquest aus erreichbar (keine Zyklen, keine fremden Voraussetzungen)
                var done = new HashSet<string>();
                bool progress = true;
                while (progress)
                {
                    progress = false;
                    foreach (var q in qs.Where(q => !done.Contains(q.Id) && q.Requires.All(done.Contains)).ToList())
                    {
                        done.Add(q.Id);
                        progress = true;
                    }
                }
                Assert.Equal(qs.Count, done.Count);
                var entry = Assert.Single(qs, q => q.Requires.Count == 0);
                Assert.Contains(db.Zones.Values, home => home.Npcs.Any(n => n.Npc == entry.Giver) && home.Id != zoneId); // Einstieg aus der Startregion
                Assert.Contains(qs, q => q.Level == 20 && q.SuggestedPlayers > 1);
                Assert.Equal(3, qs.Count(q => q.Category == QuestCategory.Dungeon));
                Assert.All(qs, q => Assert.InRange(q.Level, 12, 20));
            }
        }

        [Fact]
        public void QuestsCoverMostOfTheXpFromTwelveToTwenty()
        {
            var db = TestContent.Db;
            long need = Experience.TotalXpToReach(20) - Experience.TotalXpToReach(12);
            foreach (var zoneId in NewZones)
            {
                var qs = db.Quests.Values.Where(q => q.Zone == zoneId && !q.Daily).ToList(); // Tagesquests gehören zum Endgame
                long questXp = qs.Sum(q => (long)q.RewardXp);
                // Pflichtkills der Quests (ohne zusätzliche Gegner und ohne Ruhebonus)
                long killXp = qs.SelectMany(q => q.Objectives.Where(o => o.Type == ObjectiveType.Kill).Select(o => (q, o)))
                                .Sum(x => (long)x.o.Count * Experience.KillXp(x.q.Level, db.Creatures[x.o.Target].MaxLevel, db.Creatures[x.o.Target].Elite));
                // Sammelziele aus Beute: so viele Kills, wie die Fundchance im Mittel verlangt
                foreach (var q in qs)
                    foreach (var o in q.Objectives.Where(o => o.Type == ObjectiveType.Collect))
                    {
                        var src = db.Creatures.Values.Where(c => c.Loot.Any(l => l.Item == o.Target)).Select(c => (c, l: c.Loot.First(l => l.Item == o.Target))).FirstOrDefault();
                        if (src.c != null) killXp += (long)(o.Count / src.l.Chance) * Experience.KillXp(q.Level, src.c.MaxLevel, src.c.Elite);
                    }
                Assert.True(questXp >= need * 6 / 10, $"{zoneId}: Quest-EP {questXp} von {need}");
                Assert.True(questXp + killXp >= need * 85 / 100, $"{zoneId}: Quest- und Kill-EP {questXp + killXp} von {need}");
            }
        }

        [Fact]
        public void RoadFromTheStartZoneOnlyServesTheOwnFaction()
        {
            var t = new TestWorld();
            var road = t.World.Zone("zone_morgentauwald")!.Objects.First(o => o.Def.Id == "obj_weg_nach_wm");
            var enemy = t.CreatePlayer(Race.Grakar, CharClass.Warrior, 12, road.Position + new Vec3(1, 0, 0), "zone_morgentauwald");
            Assert.Equal("Dieses Portal gehorcht Euch nicht.", t.World.Interactions.Interact(enemy, road.Id));

            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12, road.Position + new Vec3(1, 0, 0), "zone_morgentauwald");
            Assert.Null(t.World.Interactions.Interact(p, road.Id));
            t.Run(0.3);
            Assert.Equal("zone_westmark", p.Zone!.Def.Id);
            var back = p.Zone.Objects.First(o => o.Def.Id == "obj_weg_wm_heim");
            t.World.MovePlayerWithinZone(p, back.Position + new Vec3(1, 0, 0));
            Assert.Null(t.World.Interactions.Interact(p, back.Id));
            t.Run(0.3);
            Assert.Equal("zone_morgentauwald", p.Zone!.Def.Id);
        }

        [Fact]
        public void ArrivalQuestLeadsFromTheStartZoneToTheSettlement()
        {
            var t = new TestWorld();
            var vogt = Npc(t, "npc_mt_vogt", "zone_morgentauwald");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12, vogt.Position + new Vec3(2, 0, 0), "zone_morgentauwald");
            Assert.Null(t.World.Quests.Accept(p, vogt, "q_wm_ankunft"));
            t.World.TeleportSpecial(p, "zone_westmark", new Vec3(500, 0, 500));
            t.Run(0.3);
            var vogtin = Npc(t, "npc_wm_main", "zone_westmark");
            t.World.MovePlayerWithinZone(p, vogtin.Position + new Vec3(2, 0, 0));
            Assert.Null(t.World.Interactions.Interact(p, vogtin.Id));
            Assert.Equal(QuestState.Complete, p.Data.Quests["q_wm_ankunft"].State);
            Assert.Null(t.World.Quests.TurnIn(p, vogtin, "q_wm_ankunft", -1));
            Assert.True(p.Data.Reputation["rep_weidenwacht"] > 0);
            Assert.Equal(QuestMarker.Available, t.World.Quests.MarkerFor(p, vogtin)); // nächste Quest: die Goldfelder
        }

        [Theory]
        [InlineData("zone_westmark", "npc_wm_stable", Race.Aldorer, "item_mount_grey_steed")]
        [InlineData("zone_rotsand", "npc_rs_stable", Race.Grakar, "item_mount_dune_wolf")]
        public void RidingStartsAtTheCapInTheNewSettlements(string zone, string stableId, Race race, string reins)
        {
            var t = new TestWorld();
            var rank = t.World.Content.RidingRanks[0];
            Assert.Equal(20, rank.Level);
            var stable = Npc(t, stableId, zone);
            var p = t.CreatePlayer(race, CharClass.Warrior, 20, stable.Position + new Vec3(2, 0, 0), zone);
            long price = t.World.Content.Items[reins].BuyPrice;
            t.World.Economy.Credit(p, rank.Cost + price - p.Money, "test"); // genau genug Gold für Reitstufe und Reittier
            Assert.Null(t.World.Mounts.LearnRiding(p, stable.Id));
            Assert.Null(t.World.Interactions.Buy(p, stable.Id, reins, 1));
            Assert.Equal(0, p.Money);
            Assert.Null(t.World.Interactions.UseItem(p, p.Data.Bag.FindIndex(s => s?.ItemId == reins), 0));
            t.ClearMonstersAround(p.Position, 80, zone);
            Assert.Null(t.World.Mounts.Mount(p, ""));
            t.Run(2);
            Assert.True(p.IsMounted);
        }

        [Fact]
        public void FloodStoneQuestWithChoiceReward()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Grakar, CharClass.Warrior, 17, new Vec3(200, 0, 180), "zone_rotsand");
            t.ClearMonstersAround(new Vec3(200, 0, 180), 120, "zone_rotsand");
            foreach (var r in new[] { "q_rs_ankunft", "q_rs_graeber", "q_rs_riss_harz", "q_rs_riss" }) p.Data.CompletedQuests.Add(r);
            var rakhim = Npc(t, "npc_rs_priest", "zone_rotsand");
            t.World.MovePlayerWithinZone(p, rakhim.Position + new Vec3(2, 0, 0));
            Assert.Null(t.World.Quests.Accept(p, rakhim, "q_rs_flutsteine"));
            var stones = p.Zone!.Objects.Where(o => o.Def.Id == "obj_rs_flutstein").ToList();
            Assert.True(stones.Count >= 5);
            foreach (var s in stones.Take(5))
            {
                t.World.MovePlayerWithinZone(p, s.Position + new Vec3(1, 0, 0));
                Assert.Null(t.World.Interactions.Interact(p, s.Id));
                t.Run(2.2);
            }
            Assert.Equal(QuestState.Complete, p.Data.Quests["q_rs_flutsteine"].State);
            t.World.MovePlayerWithinZone(p, rakhim.Position + new Vec3(2, 0, 0));
            Assert.NotNull(t.World.Quests.TurnIn(p, rakhim, "q_rs_flutsteine", -1)); // Wahlbelohnung muss gewählt werden
            Assert.Null(t.World.Quests.TurnIn(p, rakhim, "q_rs_flutsteine", 1));
            Assert.Equal(1, p.CountItem("item_rs_flutsteine_agi"));
        }
    }
}
