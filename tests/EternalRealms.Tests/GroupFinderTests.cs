using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    public class GroupFinderTests
    {
        private const string Crypt = "dungeon:dungeon_wurzelkrypta";

        private static void AcceptAll(TestWorld t, params Player[] humans)
        {
            foreach (var h in humans)
                t.World.Finder.Answer(h, t.Conn(h).Of<S_FinderProposal>().Last().ProposalId, true);
        }

        [Fact]
        public void HumansFirstThenAiFillsAfterTheWait()
        {
            var t = new TestWorld(seed: 2);
            var tank = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 15, name: "Schildmaid");
            var heal = t.CreatePlayer(Race.Elarii, CharClass.Priest, 15, name: "Lichtfunke");
            Assert.Contains(t.World.Finder.BuildActivityList().Activities, a => a.Id == Crypt && a.Size == 5);
            Assert.Null(t.World.Finder.Join(tank, new[] { Crypt }, new[] { Role.Tank }, FinderAiMode.PreferHumans));
            Assert.Null(t.World.Finder.Join(heal, new[] { Crypt }, new[] { Role.Healer }, FinderAiMode.PreferHumans));
            t.Run(10);
            Assert.Empty(t.Conn(tank).Of<S_FinderProposal>()); // noch warten – vielleicht kommen Menschen
            t.Run(40);
            var prop = t.Conn(tank).Of<S_FinderProposal>().Last();
            Assert.Equal(Role.Tank, prop.Role);
            Assert.Equal(3, prop.Members.Count(m => m.StartsWith("KI-Mitspieler")));
            AcceptAll(t, tank, heal);
            t.Run(2);
            var g = tank.Group!;
            Assert.Same(g, heal.Group);
            Assert.Equal(5, g.Members.Count);
            Assert.Equal(3, g.Bots.Count());
            Assert.True(g.Bots.All(b => b.Bot!.Role is Role.MeleeDps or Role.RangedDps));
            Assert.True(tank.Zone!.IsInstance);
            Assert.Same(tank.Zone, heal.Zone);
            Assert.All(g.Bots, b => Assert.Same(tank.Zone, b.Zone));
            Assert.Equal(LootMethod.GroupLoot, g.LootMethod);
        }

        [Fact]
        public void FillWithAiIsImmediateAndHumansOnlyNeverFills()
        {
            var t = new TestWorld(seed: 3);
            var solo = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 15, name: "Eilig");
            var purist = t.CreatePlayer(Race.Aldorer, CharClass.Rogue, 15, name: "Purist");
            t.World.Finder.Join(purist, new[] { Crypt }, new[] { Role.MeleeDps }, FinderAiMode.HumansOnly);
            t.World.Finder.Join(solo, new[] { Crypt }, new[] { Role.RangedDps }, FinderAiMode.FillWithAi);
            t.Run(2);
            var prop = t.Conn(solo).Of<S_FinderProposal>().Last();
            Assert.Equal(4, prop.Members.Count(m => m.StartsWith("KI")));
            Assert.Empty(t.Conn(purist).Of<S_FinderProposal>()); // nicht mit KI zusammengewürfelt
            t.World.Finder.Answer(solo, prop.ProposalId, false);
            t.Run(1);
            Assert.False(t.World.Finder.IsQueued(solo)); // abgelehnt = raus
            t.Run(120);
            Assert.Empty(t.Conn(purist).Of<S_FinderProposal>());
            Assert.True(t.World.Finder.IsQueued(purist));
        }

        [Fact]
        public void AHumanTakesOverABotSlotButNotDuringABossFight()
        {
            var t = new TestWorld(seed: 4);
            var solo = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 15, name: "Anfuehrer");
            t.World.Finder.Join(solo, new[] { Crypt }, new[] { Role.Tank }, FinderAiMode.FillWithAi);
            t.Run(2);
            AcceptAll(t, solo);
            t.Run(2);
            var g = solo.Group!;
            var botHealer = g.Bots.Single(b => b.Bot!.Role == Role.Healer);
            // Kampf gegen einen Boss läuft
            var boss = solo.Zone!.Units.OfType<Creature>().First(c => c.Template.Id == "boss_moderkeim");
            solo.EnterCombatWith(boss);
            boss.EnterCombatWith(solo);

            var priest = t.CreatePlayer(Race.Elarii, CharClass.Priest, 15, name: "Nachzuegler");
            t.World.Finder.Join(priest, new[] { Crypt }, new[] { Role.Healer }, FinderAiMode.PreferHumans);
            t.Run(2);
            var prop = t.Conn(priest).Of<S_FinderProposal>().Last();
            Assert.True(prop.IsReplacement);
            AcceptAll(t, priest);
            t.Run(2);
            Assert.Same(g, botHealer.Group); // nicht mitten im Bosskampf
            Assert.Null(priest.Group);
            solo.ClearCombat(); boss.ClearCombat();
            t.Run(2);
            Assert.Null(botHealer.Group);
            Assert.Same(g, priest.Group);
            Assert.Same(solo.Zone, priest.Zone);
            Assert.Equal(Role.Healer, priest.SelectedRole);
        }
    }

    public class GroupToolTests
    {
        [Fact]
        public void ReadyCheckBotsAnswerHonestly()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12);
            t.World.Groups.Create(p);
            var fit = t.CreateBot(p, Role.Healer, CharClass.Priest);
            var hurt = t.CreateBot(p, Role.MeleeDps, CharClass.Rogue);
            t.World.Groups.Join(p.Group!, fit);
            t.World.Groups.Join(p.Group!, hurt);
            hurt.Health = hurt.MaxHealth / 3;
            Assert.Null(t.World.Groups.StartReadyCheck(p));
            t.Run(5);
            var res = t.Conn(p).Of<S_ReadyCheckResult>().Single();
            Assert.Contains(fit.Name, res.Ready);
            Assert.Contains(hurt.Name, res.NotReady);
            Assert.Contains(p.Name, res.Ready);
        }

        [Fact]
        public void KickNeedsAMajorityOfTheOtherHumans()
        {
            var t = new TestWorld();
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12, name: "Erste");
            var b = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 12, name: "Zweite");
            var c = t.CreatePlayer(Race.Aldorer, CharClass.Priest, 12, name: "Dritte");
            var g = t.World.Groups.Create(a);
            t.World.Groups.Join(g, b);
            t.World.Groups.Join(g, c);
            Assert.Null(t.World.Groups.StartKickVote(a, c.Id, "abwesend"));
            Assert.Same(g, c.Group); // 1 von 2 Stimmen reicht nicht
            var vote = t.Conn(b).Of<S_VoteKick>().Single();
            Assert.Equal("Dritte", vote.TargetName);
            t.World.Groups.VoteResponse(b, vote.VoteId, true);
            Assert.Null(c.Group);
        }

        [Fact]
        public void RolesRaidAndSubgroups()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12);
            var g = t.World.Groups.Create(p);
            var druid = t.CreateBot(p, Role.Healer, CharClass.Druid);
            t.World.Groups.Join(g, druid);
            Assert.Null(t.World.Groups.SetRole(p, Role.Tank));
            Assert.Equal(Role.Tank, g.BuildUpdate().Members.First(m => m.EntityId == p.Id).Role);
            Assert.Null(t.World.Groups.SetBotRole(p, druid.Id, Role.RangedDps));
            Assert.Equal(Role.RangedDps, druid.Bot!.Role);
            var priestBot = t.CreateBot(p, Role.Healer, CharClass.Priest);
            t.World.Groups.Join(g, priestBot);
            Assert.NotNull(t.World.Groups.SetBotRole(p, priestBot.Id, Role.Tank)); // Priester kann nicht tanken

            Assert.NotNull(t.World.Groups.SetSubgroup(p, druid.Id, 2)); // erst Schlachtzug
            Assert.Null(t.World.Groups.ConvertToRaid(p));
            Assert.Null(t.World.Groups.SetSubgroup(p, druid.Id, 2));
            Assert.Equal(2, g.BuildUpdate().Members.First(m => m.EntityId == druid.Id).Subgroup);
        }
    }
}
