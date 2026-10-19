using EternalRealms.Server.World;
using EternalRealms.Shared;

namespace EternalRealms.Tests
{
    /// <summary>Kapitel 9.4: Simulationsstufen und eigenständiges Leben der Weltbewohner.</summary>
    public class WorldLifeTests
    {
        [Fact]
        public void ZonesWithoutHumansRunReducedThenRestAndWakeUp()
        {
            var t = new TestWorld();
            var zone = t.World.Zone("zone_aschensteppe")!;
            var data = t.World.Bots.CreateBotData(Faction.Schattenpakt, Role.MeleeDps, 6, CharClass.Warrior, zone.Def.Id, ambient: true)!;
            data.Position = zone.Def.Areas.First(a => a.IsCity).Center;
            var bot = t.World.Bots.Spawn(data, zone);
            t.Run(2);
            Assert.Equal(SimulationLevel.Reduced, zone.Lod);
            t.Run(ZoneInstance.DormantAfter);
            Assert.Equal(SimulationLevel.Dormant, zone.Lod);
            long xp = bot.Data.Xp + bot.Level * 100000L;
            t.Run(ZoneInstance.DormantStep + 1); // erster Hintergrundschritt (belebt ggf. an der Leiche wieder)
            Assert.True(bot.IsAlive);
            var frozen = bot.Position;
            t.Run(ZoneInstance.DormantStep * 3);
            Assert.Equal(frozen, bot.Position); // keine Einzelsimulation mehr …
            Assert.True(bot.Data.Xp + bot.Level * 100000L > xp, "… aber Hintergrundfortschritt");
            // Ein Mensch betritt die Zone: sofort volle Simulation
            t.CreatePlayer(Race.Grakar, CharClass.Warrior, 5, null, zone.Def.Id);
            t.Run(0.1);
            Assert.Equal(SimulationLevel.Full, zone.Lod);
        }

        [Fact]
        public void AmbientBotsAcceptQuestsHuntForThemAndTurnThemIn()
        {
            var t = new TestWorld(seed: 9);
            var zone = t.World.Zone("zone_morgentauwald")!;
            // Ein Mensch weit weg hält die Zone in voller Simulation.
            t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 1, new Vec3(30, 0, 30));
            var town = zone.Def.Areas.First(a => a.IsCity);
            var data = t.World.Bots.CreateBotData(Faction.Sonnenbuendnis, Role.MeleeDps, 3, CharClass.Warrior, zone.Def.Id, ambient: true)!;
            data.Position = town.Center;
            var bot = t.World.Bots.Spawn(data, zone);
            bot.Bot!.ForceTownVisit();
            t.Run(120, () => bot.Data.Quests.Count > 0);
            Assert.True(bot.Data.Quests.Count > 0, "Bewohner nimmt in der Stadt Quests an");
            t.Run(1500, () => bot.Data.CompletedQuests.Count > 0);
            Assert.True(bot.Data.CompletedQuests.Count > 0, $"Bewohner gibt Quests ab (Status: {bot.Bot.AutoActivity}, Quests: {string.Join(",", bot.Data.Quests.Keys)})");
        }

        [Fact]
        public void ResidentsChatWithEachOtherInTown()
        {
            var t = new TestWorld(seed: 4);
            var zone = t.World.Zone("zone_morgentauwald")!;
            var town = zone.Def.Areas.First(a => a.IsCity);
            var listener = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 5, town.Center);
            var bots = Enumerable.Range(0, 3).Select(i =>
            {
                var d = t.World.Bots.CreateBotData(Faction.Sonnenbuendnis, Role.MeleeDps, 5, null, zone.Def.Id, ambient: true)!;
                d.Bot!.Chattiness = 1f;
                d.Position = town.Center + new Vec3(i * 2, 0, 1);
                var b = t.World.Bots.Spawn(d, zone);
                b.Bot!.ForceTownVisit(stayInTown: true);
                return b;
            }).ToList();
            t.Run(400, () => t.Conn(listener).Of<Shared.Net.S_Chat>().Any(c => c.SenderIsBot && c.Channel == ChatChannel.Say));
            Assert.Contains(t.Conn(listener).Of<Shared.Net.S_Chat>(), c => c.SenderIsBot && c.Channel == ChatChannel.Say);
        }
    }
}
