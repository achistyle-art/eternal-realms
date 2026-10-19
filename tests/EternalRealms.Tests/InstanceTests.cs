using EternalRealms.Server.Net;
using System.Text.Json;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.Systems;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;

namespace EternalRealms.Tests
{
    public class InstanceTests
    {
        private const string Crypt = "dungeon_wurzelkrypta";

        [Fact]
        public void GroupSharesInstanceBossesBindAndLeaderCanReset()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 16);
            t.World.Groups.Create(p);
            var bot = t.CreateBot(p, Role.Healer, CharClass.Priest);
            t.World.Groups.Join(p.Group!, bot);
            var def = TestContent.Db.Zones[Crypt];
            t.World.Transfer(p, Crypt, def.InstanceEntry);
            var inst = p.Zone!;
            Assert.True(inst.InstanceId != 0);
            t.Run(1);
            Assert.Same(inst, bot.Zone); // Bot folgt in dieselbe Instanz

            var boss = inst.Units.OfType<Creature>().First(c => c.Template.Id == "boss_moderkeim");
            t.World.Combat.Kill(p, boss);
            Assert.Equal(inst.InstanceId, p.Data.InstanceBinds[Crypt].InstanceId);
            Assert.Contains("boss_moderkeim", p.Data.InstanceBinds[Crypt].KilledBosses);
            Assert.Null(p.Data.InstanceBinds[Crypt].ExpiresAt); // Dungeon: keine feste Sperre

            Assert.NotNull(t.World.Instances.Reset(p)); // noch drin
            t.World.Transfer(p, "zone_morgentauwald", def.ExitPosition);
            t.Run(1);
            Assert.Null(t.World.Instances.Reset(p));
            Assert.Null(t.World.Instances.Get(inst.InstanceId));
            Assert.False(p.Data.InstanceBinds.ContainsKey(Crypt));
            t.World.Transfer(p, Crypt, def.InstanceEntry);
            Assert.NotEqual(inst.InstanceId, p.Zone!.InstanceId);
            Assert.Contains(p.Zone.Units.OfType<Creature>(), c => c.Template.Id == "boss_moderkeim"); // frisch
        }

        [Fact]
        public void AtMostTenNewInstancesPerHour()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 16);
            var def = TestContent.Db.Zones[Crypt];
            // Allein: Wer zurückkehrt, landet wieder in seiner Instanz.
            t.World.Transfer(p, Crypt, def.InstanceEntry);
            long first = p.Zone!.InstanceId;
            t.World.Transfer(p, "zone_morgentauwald", def.ExitPosition);
            t.World.Transfer(p, Crypt, def.InstanceEntry);
            Assert.Equal(first, p.Zone!.InstanceId);
            t.World.Transfer(p, "zone_morgentauwald", def.ExitPosition);
            Assert.Null(t.World.Instances.Reset(p));
            p.Data.RecentInstances.Clear();
            for (int i = 0; i < InstanceSystem.MaxNewPerHour; i++)
            {
                t.World.Transfer(p, Crypt, def.InstanceEntry);
                Assert.True(p.Zone!.IsInstance, $"Instanz {i + 1}");
                t.World.Transfer(p, "zone_morgentauwald", def.ExitPosition);
                Assert.Null(t.World.Instances.Reset(p));
            }
            t.World.Transfer(p, Crypt, def.InstanceEntry);
            Assert.False(p.Zone!.IsInstance);
        }

        [Fact]
        public void ReconnectReturnsIntoTheSameInstance()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 16);
            var def = TestContent.Db.Zones[Crypt];
            t.World.Transfer(p, Crypt, def.InstanceEntry + new Vec3(5, 0, 0));
            long id = p.Zone!.InstanceId;
            var data = p.Data;
            t.World.Logout(p);
            Assert.NotNull(t.World.Instances.Get(id)); // leer, aber noch nicht verfallen
            var again = t.World.EnterWorld(data, new RecordingConnection());
            Assert.Equal(id, again.Zone!.InstanceId);
            // Nach dem Verfall landet man am Ausgang
            t.World.Logout(again);
            t.Run(InstanceSystem.EmptyLifetime + 2);
            var third = t.World.EnterWorld(data, new RecordingConnection());
            Assert.False(third.Zone!.IsInstance);
        }

        [Fact]
        public void RaidLockoutsSurviveARestartThroughTheBinds()
        {
            // Testschlachtzug: die Krypta als 10er mit wöchentlicher Sperre (nur in diesem Test, Content bleibt unverändert)
            var crypt = TestContent.Db.Zones[Crypt];
            var raid = JsonSerializer.Deserialize<ZoneDef>(JsonSerializer.Serialize(crypt, ContentDatabase.CreateOptions()), ContentDatabase.CreateOptions())!;
            raid.Id = "test_raid"; raid.MaxPlayers = 10; raid.LockoutDays = 7;
            Assert.True(raid.IsRaid);
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 16);
            var z = t.World.Instances.Resolve(p, raid, 0, out var err)!;
            Assert.Null(err);
            z.Add(p); // ohne Zonenwechsel: Spieler steht im Testschlachtzug
            var boss = z.Units.OfType<Creature>().First(c => c.Template.Id == "boss_moderkeim");
            t.World.Combat.Kill(p, boss);
            var bind = p.Data.InstanceBinds["test_raid"];
            Assert.NotNull(bind.ExpiresAt);
            Assert.True(bind.ExpiresAt > DateTime.UtcNow && bind.ExpiresAt <= DateTime.UtcNow.AddDays(7));

            // „Neustart“: neue Welt, dieselben Charakterdaten
            var t2 = new TestWorld();
            var p2 = t2.CreatePlayer(Race.Aldorer, CharClass.Warrior, 16);
            p2.Data.InstanceBinds["test_raid"] = bind;
            var z2 = t2.World.Instances.Resolve(p2, raid, 0, out _)!;
            Assert.Equal(bind.InstanceId, z2.InstanceId);
            Assert.DoesNotContain(z2.Units.OfType<Creature>(), c => c.Template.Id == "boss_moderkeim");
            Assert.Contains(z2.Units.OfType<Creature>(), c => c.Template.Id == "boss_morrak");
        }

        [Fact]
        public void ResetsFollowAFixedSchedule()
        {
            var wed = new DateTime(2026, 10, 21, 5, 0, 0, DateTimeKind.Utc); // Mittwoch
            Assert.Equal(wed, InstanceSystem.NextReset(7, new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(wed.AddDays(7), InstanceSystem.NextReset(7, wed));
            var three = InstanceSystem.NextReset(3, new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc));
            Assert.True(three > new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc) && three <= new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc));
            Assert.Equal(5, three.Hour);
        }
    }
}
