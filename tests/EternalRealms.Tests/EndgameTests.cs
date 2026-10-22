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
    /// <summary>Block I: Endgame auf Stufe 20 – heroische Wurzelkrypta, Tagesquests, kosmetische Rufbelohnungen.</summary>
    public class EndgameTests
    {
        private const string Heroic = "dungeon_wurzelkrypta_heroisch";
        private readonly ITestOutputHelper _out;
        public EndgameTests(ITestOutputHelper output) { _out = output; }

        private static Creature Npc(TestWorld t, string id, string zone) =>
            t.World.Zone(zone)!.Entities.Values.OfType<Creature>().First(c => c.Template.Id == id);

        [Fact]
        public void HeroicCryptIsDerivedForLevelTwenty()
        {
            var db = TestContent.Db;
            var z = db.Zones[Heroic];
            Assert.True(z.Heroic && z.IsInstance && z.HasLockout && !z.IsRaid);
            Assert.Equal((20, 1, 5), (z.RequiredLevel, z.LockoutDays, z.MaxPlayers));
            var normal = db.Zones["dungeon_wurzelkrypta"];
            Assert.Equal(normal.Spawns.Count, z.Spawns.Count);
            foreach (var (n, h) in normal.Spawns.Zip(z.Spawns))
            {
                var nc = db.Creatures[n.Creature];
                var hc = db.Creatures[h.Creature];
                Assert.Equal("h" + nc.Id, hc.Id);
                Assert.True(hc.MinLevel >= db.LevelCap && (!hc.Boss || hc.MinLevel > db.LevelCap) && hc.HealthMultiplier > nc.HealthMultiplier && hc.DamageMultiplier > nc.DamageMultiplier, hc.Id);
                // Beschwörungen der Bosse rufen ebenfalls heroische Gegner
                Assert.All(hc.Phases.SelectMany(p => p.SummonAdds), a => Assert.StartsWith("hdmob_", a.Creature));
            }
            // Heroische Beute: selten, Stufe 26, mit Stufe 20 tragbar
            var loot = z.Spawns.Select(s => db.Creatures[s.Creature]).Where(c => c.Boss).SelectMany(c => c.Loot).Select(l => db.Items[l.Item]).Where(i => i.IsEquippable).ToList();
            Assert.Equal(9, loot.Count);
            Assert.All(loot, i => Assert.True(i.Quality == ItemQuality.Rare && i.ItemLevel == 26 && i.RequiredLevel <= db.LevelCap, i.Id));
        }

        [Fact]
        public void HeroicEntryRequiresLevelTwentyAndBossesStayDeadUntilTheDailyReset()
        {
            var t = new TestWorld();
            var low = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 19, new Vec3(170, 0, 150), "zone_westmark");
            var door = low.Zone!.Objects.First(o => o.Def.Id == "obj_crypt_heroic_wm");
            t.World.MovePlayerWithinZone(low, door.Position + new Vec3(1, 0, 0));
            Assert.Null(t.World.Interactions.Interact(low, door.Id));
            t.Run(0.3);
            Assert.Equal("zone_westmark", low.Zone!.Def.Id);
            Assert.Contains(t.Conn(low).Of<S_Notice>(), n => n.IsError && n.Text.Contains("erst ab Stufe 20"));

            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 20, door.Position + new Vec3(1, 0, 0), "zone_westmark");
            Assert.Null(t.World.Interactions.Interact(p, door.Id));
            t.Run(0.3);
            Assert.Equal(Heroic, p.Zone!.Def.Id);
            var inst = p.Zone;
            var boss = inst.Entities.Values.OfType<Creature>().First(c => c.Template.Id == "hboss_moderkeim");
            t.World.Combat.DealDamage(p, boss, 10_000_000, School.Physical, null, "test", false, false);
            Assert.True(boss.IsDead);
            var bind = p.Data.InstanceBinds[Heroic];
            Assert.Equal(InstanceSystem.NextReset(1, DateTime.UtcNow), bind.ExpiresAt);
            Assert.Contains("hboss_moderkeim", bind.KilledBosses);

            // Zurücksetzen geht nicht, und auch nach dem Verfall der leeren Instanz bleibt der Boss tot
            t.World.TeleportSpecial(p, "@return", default);
            t.Run(0.3);
            Assert.NotNull(t.World.Instances.Reset(p));
            t.World.Instances.Update(InstanceSystem.EmptyLifetime + 1);
            Assert.Null(t.World.Instances.Get(inst.InstanceId));
            t.World.Transfer(p, Heroic, new Vec3(50, 0, 50));
            t.Run(0.3);
            Assert.Equal(inst.InstanceId, p.Zone!.InstanceId);
            Assert.DoesNotContain(p.Zone.Entities.Values.OfType<Creature>(), c => c.Template.Id == "hboss_moderkeim" && c.IsAlive);
            Assert.Contains(p.Zone.Entities.Values.OfType<Creature>(), c => c.Template.Id == "hboss_morrak" && c.IsAlive);
        }

        /// <summary>
        /// Ausrüstung, die man vor dem heroischen Modus sammeln kann: Questbelohnungen, grüne Beute und Beute der normalen Krypta
        /// bis Stufe 20 – keine heroischen Gegenstände. Angelegt wird, was für Klasse und Rolle besser ist.
        /// </summary>
        public static void GearUpBeforeHeroic(TestWorld t, Player m)
        {
            var pool = t.World.Content.Items.Values
                .Where(i => i.IsEquippable && i.RequiredLevel <= 20 && i.Quality >= ItemQuality.Uncommon && i.Quality <= ItemQuality.Rare && !i.Id.StartsWith("item_wkh_"))
                .OrderByDescending(i => i.ItemLevel).ThenBy(i => i.Id, StringComparer.Ordinal);
            foreach (var item in pool)
            {
                if (!t.World.Bots.IsUpgrade(m, item)) continue;
                m.AddItem(item.Id, 1);
                int idx = m.Data.Bag.FindIndex(s => s?.ItemId == item.Id);
                if (idx >= 0 && m.Equip(idx) != null) m.RemoveItem(item.Id, 1);
            }
            m.EnsureStats();
            m.Health = m.MaxHealth;
        }

        [Fact]
        public void ExperiencedBotsBeatHeroicMorrakAtLevelTwenty()
        {
            var rows = new[] { BotDifficulty.Erfahren, BotDifficulty.Veteran }
                .SelectMany(d => new[] { 21, 22 }.Select(seed => EncounterSim.Run("hboss_morrak", 20, d, seed, dungeon: Heroic, prepare: GearUpBeforeHeroic))).ToList();
            foreach (var o in rows) _out.WriteLine(EncounterTests.Row(o));
            foreach (var o in rows)
            {
                Assert.True(o.Won, EncounterTests.Row(o));
                Assert.True(o.Result!.Duration < TestContent.Db.Creatures["hboss_morrak"].EnrageAfter, EncounterTests.Row(o));
            }
            // Heroisch heißt: ohne gesammelte Ausrüstung scheitert dieselbe Gruppe
            var bare = EncounterSim.Run("hboss_morrak", 20, BotDifficulty.Erfahren, 21, dungeon: Heroic);
            _out.WriteLine("ohne Ausrüstung: " + EncounterTests.Row(bare));
            Assert.False(bare.Won, EncounterTests.Row(bare));
        }

        [Fact]
        public void DailyQuestCanBeRepeatedAfterTheReset()
        {
            var t = new TestWorld();
            var mason = Npc(t, "npc_wm_mason", "zone_westmark");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 20, mason.Position + new Vec3(2, 0, 0), "zone_westmark");
            t.ClearMonstersAround(mason.Position, 200, "zone_westmark");
            const string q = "q_wm_t_sensen";
            Assert.Equal("Ihr müsst zuerst eine andere Aufgabe erledigen.", t.World.Quests.CanAccept(p, t.World.Content.Quests[q]));
            p.Data.CompletedQuests.Add("q_wm_malachias");
            for (int round = 0; round < 2; round++)
            {
                Assert.Null(t.World.Quests.Accept(p, mason, q));
                foreach (var (mob, n) in new[] { ("mob_wm_brandschatzer", 10), ("mob_wm_schuetze", 6) })
                    for (int i = 0; i < n; i++)
                        t.World.Combat.DealDamage(p, t.Spawn(mob, p.Position + new Vec3(4, 0, i), "zone_westmark"), 9999, School.Physical, null, "test", false, false);
                long money = p.Money;
                int rep = p.Data.Reputation.GetValueOrDefault("rep_weidenwacht");
                Assert.Null(t.World.Quests.TurnIn(p, mason, q, -1));
                Assert.True(p.Money > money, "Auf Stufe 20 gibt es Gold statt Erfahrung");
                Assert.Equal(rep + 275, p.Data.Reputation["rep_weidenwacht"]); // 250 + 10 % Volksfähigkeit „Diplomatie“ der Aldorer
                Assert.DoesNotContain(q, p.Data.CompletedQuests);
                Assert.Equal("Diese Aufgabe gibt es erst morgen wieder.", t.World.Quests.CanAccept(p, t.World.Content.Quests[q]));
                p.Data.DailyQuests[q] = DateTime.UtcNow.AddSeconds(-1); // der nächste Tag bricht an
            }
        }

        [Fact]
        public void DailiesExistInBothRegionsAndUnlockNothing()
        {
            var db = TestContent.Db;
            foreach (var zone in new[] { "zone_westmark", "zone_rotsand" })
            {
                var daily = db.Quests.Values.Where(q => q.Zone == zone && q.Daily).ToList();
                Assert.Equal(4, daily.Count);
                Assert.Contains(daily, q => q.Objectives.Any(o => o.Target == "hboss_morrak"));
                Assert.All(daily, q => Assert.Equal(20, q.MinLevel));
            }
            Assert.DoesNotContain(db.Quests.Values, q => q.Requires.Any(r => db.Quests[r].Daily));
        }

        [Theory]
        [InlineData("zone_westmark", "npc_wm_qm", Race.Aldorer, "rep_weidenwacht", "item_mount_weidenwacht_charger")]
        [InlineData("zone_rotsand", "npc_rs_qm", Race.Grakar, "rep_zharas_rast", "item_mount_zharas_lizard")]
        public void QuartermasterSellsCosmeticMountsForReputation(string zone, string qmId, Race race, string rep, string reins)
        {
            var t = new TestWorld();
            var qm = Npc(t, qmId, zone);
            var p = t.CreatePlayer(race, CharClass.Warrior, 20, qm.Position + new Vec3(2, 0, 0), zone);
            p.Data.RidingSkill = 75;
            t.World.Economy.Credit(p, 1_000_000, "test");
            Assert.Equal("Benötigt „Wohlwollend“ bei " + t.World.Content.Reputations[rep].Name + ".", t.World.Interactions.Buy(p, qm.Id, reins, 1));
            // Zügel ohne Ruf (z. B. aus einem Test oder Fehler) lassen sich auch nicht lernen
            p.AddItem(reins, 1);
            Assert.NotNull(t.World.Interactions.UseItem(p, p.Data.Bag.FindIndex(s => s?.ItemId == reins), 0));
            p.RemoveItem(reins, 1);

            t.World.Progression.AddReputation(p, rep, 9000);
            Assert.Null(t.World.Interactions.Buy(p, qm.Id, reins, 1));
            Assert.Null(t.World.Interactions.UseItem(p, p.Data.Bag.FindIndex(s => s?.ItemId == reins), 0));
            Assert.Contains(t.World.Content.Items[reins].TeachesMount!, p.Data.Mounts);
            // Die Rufstufe „Respektvoll“ fehlt noch für das zweite Reittier
            var second = t.World.Content.Mounts.Values.First(m => m.RequiredReputation == rep && m.RequiredReputationValue == 21000);
            Assert.NotNull(t.World.Interactions.Buy(p, qm.Id, second.Item, 1));
        }

        [Fact]
        public void ReputationRewardsAreCosmeticOnly()
        {
            var db = TestContent.Db;
            var repMounts = db.Mounts.Values.Where(m => m.RequiredReputation != null).ToList();
            Assert.Equal(4, repMounts.Count);
            int vendorSpeed = db.Mounts.Values.Where(m => m.Source == "vendor" && m.RequiredLevel <= db.LevelCap).Max(m => m.Speed);
            Assert.All(repMounts, m => Assert.True(m.Speed <= vendorSpeed, $"{m.Id} wäre schneller als ein gekauftes Reittier"));
            var rewards = db.Items.Values.Where(i => i.RequiredReputation != null).ToList();
            Assert.All(rewards, i => Assert.True(i.Modifiers.Count == 0 && !i.IsEquippable && i.TeachesMount != null, i.Id));
            foreach (var rep in new[] { "rep_weidenwacht", "rep_zharas_rast" })
                Assert.Contains(db.Achievements.Values, a => a.Title != null && a.Criteria.Any(c => c.Event == ProgressEvent.Reputation && c.Target == rep));
        }

        [Fact]
        public void HeroicDungeonIsOfferedInTheGroupFinder()
        {
            var t = new TestWorld();
            var act = t.World.Finder.Activities.Single(a => a.Zone.Id == Heroic);
            Assert.Equal("Heroischer Dungeon", act.Kind);
            Assert.Equal((20, 20, 5), (act.MinLevel, act.MaxLevel, act.Size));
        }
    }
}
