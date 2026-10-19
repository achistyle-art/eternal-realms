using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    /// <summary>Kapitel 18: Reiten lernen, Reittiere sammeln, Tempo, Abwerfen.</summary>
    public class MountTests
    {
        private static readonly Vec3 Field = new(520, 0, 820);

        private static (TestWorld t, Player p, Creature stable) Rider(int level)
        {
            var t = new TestWorld();
            var stable = t.World.Zone("zone_morgentauwald")!.Entities.Values.OfType<Creature>().First(c => c.Template.Id == "npc_mt_stablemaster");
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, level, stable.Position + new Vec3(2, 0, 0));
            return (t, p, stable);
        }

        [Fact]
        public void RidingRequiresLevelSkillAndGold()
        {
            var (t, p, stable) = Rider(39);
            Assert.NotNull(t.World.Mounts.LearnRiding(p, stable.Id)); // zu niedrig
            t.World.Progression.LevelUp(p);
            Assert.NotNull(t.World.Mounts.LearnRiding(p, stable.Id)); // kein Geld
            t.World.Economy.Credit(p, 1_000_000, "test");
            Assert.Null(t.World.Mounts.LearnRiding(p, stable.Id));
            Assert.Equal(75, p.Data.RidingSkill);
            Assert.NotNull(t.World.Mounts.LearnRiding(p, stable.Id)); // Experte erst ab 60

            Assert.Null(t.World.Interactions.Buy(p, stable.Id, "item_mount_grey_steed", 1));
            int idx = p.Data.Bag.FindIndex(s => s?.ItemId == "item_mount_grey_steed");
            Assert.Null(t.World.Interactions.UseItem(p, idx, 0));
            Assert.Contains("mount_grey_steed", p.Data.Mounts);
            Assert.Equal(0, p.CountItem("item_mount_grey_steed"));

            p.AddItem("item_mount_dune_wolf", 1); // andere Fraktion
            Assert.NotNull(t.World.Interactions.UseItem(p, p.Data.Bag.FindIndex(s => s?.ItemId == "item_mount_dune_wolf"), 0));
            p.AddItem("item_mount_swift_white_steed", 1); // +100 % erst mit Experte
            Assert.NotNull(t.World.Interactions.UseItem(p, p.Data.Bag.FindIndex(s => s?.ItemId == "item_mount_swift_white_steed"), 0));
        }

        [Fact]
        public void MountingTakesACastIncreasesSpeedAndDamageDismounts()
        {
            var (t, p, _) = Rider(40);
            p.Data.RidingSkill = 75;
            p.Data.Mounts.Add("mount_grey_steed");
            t.World.MovePlayerWithinZone(p, Field);
            t.ClearMonstersAround(Field, 100);
            float walk = p.MoveSpeed;
            Assert.Null(t.World.Mounts.Mount(p, ""));
            Assert.False(p.IsMounted); // 1,5 s Zauberzeit
            t.Run(2);
            Assert.True(p.IsMounted);
            Assert.Equal("mount_grey_steed", p.ActiveMount);
            p.EnsureStats();
            Assert.Equal(walk * 1.6f, p.MoveSpeed, 2);
            Assert.Contains(t.Conn(p).Of<S_EntitySpawn>(), s => s.Id == p.Id && s.MountId == "mount_grey_steed");

            var wolf = t.Spawn("mob_forest_wolf", p.Position + new Vec3(2, 0, 0), level: 40);
            t.Run(4, () => !p.IsMounted);
            Assert.False(p.IsMounted); // vom Angriff abgeworfen
            Assert.Equal("", p.ActiveMount);
            Assert.NotNull(t.World.Mounts.Mount(p, "")); // im Kampf nicht möglich
            t.World.Combat.Kill(p, wolf);
        }

        [Fact]
        public void BotsFollowTheLeaderOnTheirOwnMounts()
        {
            var (t, p, _) = Rider(40);
            t.World.MovePlayerWithinZone(p, Field);
            t.ClearMonstersAround(Field, 100);
            p.Data.RidingSkill = 75;
            p.Data.Mounts.Add("mount_grey_steed");
            t.World.Groups.Create(p);
            var bot = t.CreateBot(p, Role.Healer, CharClass.Priest);
            t.World.Groups.Join(p.Group!, bot);
            t.World.Economy.Credit(bot, 1_000_000, "test");
            t.World.Mounts.BotProgress(bot); // lernt Reiten und kauft ein Fraktionsreittier
            Assert.Equal(75, bot.Data.RidingSkill);
            Assert.NotEmpty(bot.Data.Mounts);
            t.World.Mounts.Mount(p, "");
            t.Run(5, () => bot.IsMounted);
            Assert.True(bot.IsMounted);
            t.World.Mounts.Dismount(p);
            t.Run(2);
            Assert.False(bot.IsMounted);
        }
    }
}
