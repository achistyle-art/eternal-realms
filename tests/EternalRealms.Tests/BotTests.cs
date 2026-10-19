using EternalRealms.Server.AI;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    public class BotTests
    {
        private static readonly Vec3 Field = new(520, 0, 820);

        [Fact]
        public void SearchOffersMissingRolesOfTheOwnFaction()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Grakar, CharClass.Warrior, 15, null, "zone_aschensteppe");
            var cands = t.World.Bots.FindCandidates(p, null);
            Assert.True(cands.Count >= 4);
            Assert.Contains(cands, c => c.Role == Role.Tank);
            Assert.Contains(cands, c => c.Role == Role.Healer);
            foreach (var c in cands)
            {
                Assert.Equal(Faction.Schattenpakt, TestContent.Db.Races[c.Race].Faction);
                Assert.Contains(c.Class, TestContent.Db.Races[c.Race].Classes);
                Assert.InRange(c.Level, 14, 15);
                Assert.False(string.IsNullOrWhiteSpace(c.Name));
                Assert.False(string.IsNullOrWhiteSpace(c.Greeting));
            }
            Assert.DoesNotContain(cands, c => c.Class == CharClass.Paladin); // Paladine gibt es nur im Sonnenbündnis
            var healers = t.World.Bots.FindCandidates(p, Role.Healer);
            Assert.All(healers, c => Assert.Equal(Role.Healer, c.Role));
        }

        [Fact]
        public void InvitedBotsJoinGreetAndFollow()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Field, 120);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, Field);
            var cands = t.World.Bots.FindCandidates(p, null);
            foreach (var c in cands.Take(4)) Assert.Null(t.World.Bots.Invite(p, c.CandidateId));
            Assert.Equal(5, p.Group!.Members.Count);
            Assert.NotNull(t.World.Bots.Invite(p, cands[4].CandidateId)); // Gruppe voll
            t.Run(4);
            Assert.True(t.Conn(p).Of<S_Chat>().Count(c => c.Channel == ChatChannel.Party && c.SenderIsBot) >= 4, "Bots begrüßen die Gruppe");
            t.World.MovePlayerWithinZone(p, Field + new Vec3(40, 0, 0));
            t.Run(12);
            foreach (var b in p.Group.Bots) Assert.True(b.DistanceTo(p) < 10, $"{b.Name} folgt nicht ({b.DistanceTo(p):0} m)");
            // Befehl "wartet" – Bots bleiben stehen
            t.World.Chat.Handle(p, ChatChannel.Party, "Wartet hier", "");
            var stay = p.Group.Bots.ToDictionary(b => b, b => b.Position);
            t.World.MovePlayerWithinZone(p, Field + new Vec3(70, 0, 0));
            t.Run(5);
            foreach (var b in p.Group.Bots) Assert.True(b.DistanceTo(stay[b]) < 3, $"{b.Name} sollte warten");
            t.World.Chat.Handle(p, ChatChannel.Party, "folgt mir", "");
            t.Run(10);
            foreach (var b in p.Group.Bots) Assert.True(b.DistanceTo(p) < 12);
        }

        [Fact]
        public void BotsTalkToEachOtherWhenEnteringADungeon()
        {
            var t = new TestWorld(seed: 5);
            t.ClearMonstersAround(Field, 120);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 15, Field);
            t.World.Groups.Create(p);
            var tank = t.CreateBot(p, Role.Tank, CharClass.Warrior);
            var healer = t.CreateBot(p, Role.Healer, CharClass.Priest);
            var mage = t.CreateBot(p, Role.RangedDps, CharClass.Mage);
            foreach (var b in new[] { tank, healer, mage }) t.World.Groups.Join(p.Group!, b);
            t.Run(2);
            t.Conn(p).Clear();
            t.World.Chat.Handle(p, ChatChannel.Party, "Wir gehen in den Dungeon.", "");
            t.Run(8);
            var lines = t.Conn(p).Of<S_Chat>().Where(c => c.Channel == ChatChannel.Party && c.SenderIsBot).ToList();
            Assert.True(lines.Count >= 3, "Alle drei Bots reagieren");
            Assert.Equal(tank.Name, lines[0].SenderName); // der Tank antwortet zuerst
            Assert.All(new[] { tank, healer, mage }, b => Assert.True(b.Bot!.DungeonMode));
            var dungeonLines = TestContent.Db.BotDialog.Where(d => d.Event.StartsWith("dungeon")).SelectMany(d => d.Lines).ToList();
            Assert.Contains(lines, l => dungeonLines.Any(d => d.Split('{')[0].Length > 3 && l.Text.StartsWith(d.Split('{')[0])));
        }

        [Fact]
        public void HealerBotHealsAWoundedLeader()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Field, 120);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12, Field);
            t.World.Groups.Create(p);
            var healer = t.CreateBot(p, Role.Healer, CharClass.Priest);
            t.World.Groups.Join(p.Group!, healer);
            t.Run(3);
            p.Health = p.MaxHealth / 3;
            t.Run(8);
            var heals = t.Conn(p).Of<S_CombatEvent>().Where(e => e.SourceId == healer.Id && e.TargetId == p.Id && e.Result is CombatResult.Heal or CombatResult.CritHeal).ToList();
            Assert.NotEmpty(heals);
            Assert.True(p.HealthPct > 0.5f);
        }

        [Fact]
        public void TankBotHoldsThreatAndHealerKeepsGroupAlive()
        {
            var t = new TestWorld(seed: 42);
            t.ClearMonstersAround(Field, 120);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 12, Field);
            t.World.Groups.Create(p);
            var tank = t.CreateBot(p, Role.Tank, CharClass.Warrior);
            var healer = t.CreateBot(p, Role.Healer, CharClass.Priest);
            t.World.Groups.Join(p.Group!, tank);
            t.World.Groups.Join(p.Group!, healer);
            t.Run(4); // Haltung einnehmen, Buffs verteilen
            Assert.Equal(Form.DefensiveStance, tank.Form);
            var mobs = Enumerable.Range(0, 3).Select(i => t.Spawn("mob_rotbach_bandit", Field + new Vec3(12, 0, i * 2), level: 11)).ToList();
            p.Target = mobs[0];
            t.World.Chat.Handle(p, ChatChannel.Party, "Angriff!", "");
            int tankTargetTicks = 0, total = 0;
            for (int i = 0; i < 90 && mobs.Any(m => m.IsAlive); i++)
            {
                var fb = TestContent.Db.GetAbilityRank("mage_frostbolt", p.KnownRank("mage_frostbolt"))!;
                var target = mobs.FirstOrDefault(m => m.IsAlive && m.Target == tank) ?? mobs.FirstOrDefault(m => m.IsAlive);
                if (target != null && p.Cast == null && i > 3) t.World.Combat.TryCast(p, fb, target);
                t.Run(1);
                foreach (var m in mobs.Where(m => m.IsAlive && m.Target != null)) { total++; if (m.Target == tank) tankTargetTicks++; }
            }
            Assert.All(mobs, m => Assert.True(m.IsDead, "Alle Räuber besiegt"));
            Assert.True(tank.IsAlive && healer.IsAlive && p.IsAlive, "Niemand stirbt bei einem normalen Kampf");
            Assert.True(tankTargetTicks > total * 0.6, $"Der Tank hält die Gegner ({tankTargetTicks}/{total})");
        }

        [Fact]
        public void GroupWithBotsClearsTheFirstDungeonBoss()
        {
            var t = new TestWorld(seed: 7);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 16, new Vec3(870, 0, 805));
            t.World.Groups.Create(p);
            var bots = new[]
            {
                t.CreateBot(p, Role.Tank, CharClass.Paladin), t.CreateBot(p, Role.Healer, CharClass.Priest),
                t.CreateBot(p, Role.RangedDps, CharClass.Mage), t.CreateBot(p, Role.RangedDps, CharClass.Warlock),
            };
            foreach (var b in bots) t.World.Groups.Join(p.Group!, b);
            t.Run(3);
            t.World.TeleportSpecial(p, "dungeon_wurzelkrypta", new Vec3(50, 0, 50));
            Assert.True(p.Zone!.IsInstance);
            t.Run(3);
            Assert.All(bots, b => Assert.Equal(p.Zone, b.Zone)); // Bots folgen in die Instanz
            t.World.Chat.Handle(p, ChatChannel.Party, "Wir gehen in den Dungeon", "");
            t.Run(15); // trinken, buffen
            var boss = p.Zone.Entities.Values.OfType<Creature>().First(c => c.Template.Id == "boss_moderkeim");
            foreach (var trash in p.Zone.Entities.Values.OfType<Creature>().Where(c => c != boss && c.DistanceTo(boss) < 60).ToList())
                p.Zone.Remove(trash); // Fokus auf den Boss
            t.World.MovePlayerWithinZone(p, boss.Position + new Vec3(14, 0, 14));
            t.Run(6);
            p.Target = boss;
            t.World.Chat.Handle(p, ChatChannel.Party, "Zieh!", "");
            t.Run(3);
            p.AutoAttacking = true;
            t.Run(240, () => boss.IsDead || p.Group.Members.All(m => m.IsDead));
            Assert.True(boss.IsDead, $"Moderkeim besiegt (Leben {boss.Health}/{boss.MaxHealth}, Phase {boss.AI.PhaseIndex})");
            Assert.True(boss.AI.PhaseIndex >= 1, "Der Boss hat seine zweite Phase erreicht");
            var conn = t.Conn(p);
            Assert.Contains(conn.Of<S_Chat>(), c => c.Channel == ChatChannel.Yell && c.SenderName == "Moderkeim");
        }

        [Fact]
        public void EveryBotProfileCanFightAlone()
        {
            var t = new TestWorld(seed: 11);
            var failures = new List<string>();
            foreach (var prof in TestContent.Db.BotProfiles)
            {
                var race = TestContent.Db.Races.Values.First(r => r.Classes.Contains(prof.Class));
                var zone = race.Faction == Faction.Sonnenbuendnis ? "zone_morgentauwald" : "zone_aschensteppe";
                var spot = new Vec3(530, 0, 300);
                var data = t.World.Bots.CreateBotData(race.Faction, prof.Role, 22, prof.Class, zone)!;
                data.Position = spot;
                var bot = t.World.Bots.Spawn(data, t.World.Zone(zone)!);
                var mob = t.Spawn(zone == "zone_morgentauwald" ? "mob_forest_wolf" : "mob_ember_lizard", spot + new Vec3(24, 0, 0), zone, level: 20);
                // Der Bot jagt eigenständig: Er sucht sich das passende Ziel selbst und eröffnet aus seiner Reichweite.
                // Heiler kämpfen allein langsamer (wie im klassischen Vorbild), dürfen aber nicht verlieren.
                t.Run(prof.Role == Role.Healer ? 180 : 90, () => mob.IsDead || bot.IsDead);
                if (!mob.IsDead) failures.Add($"{prof.Class}/{prof.Role}: Gegner lebt noch ({mob.Health}/{mob.MaxHealth}), Bot {(bot.IsDead ? "tot" : "lebt")}, letzte Entscheidung '{bot.Bot!.LastDecision}'");
                t.World.Bots.DespawnBot(bot);
                if (!mob.Removed) mob.Zone!.Remove(mob);
                t.Run(0.2);
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void AmbientBotsPopulateZonesAndLevelThemselves()
        {
            var t = new TestWorld(seed: 3, ambientBots: true);
            t.Run(25);
            var zone = t.World.Zone("zone_morgentauwald")!;
            var ambient = zone.Players.Where(p => p.IsBot && p.Data.Bot!.Ambient).ToList();
            Assert.True(ambient.Count >= 3, $"nur {ambient.Count} Bewohner");
            long xpBefore = ambient.Sum(b => b.Data.Xp + b.Level * 100000L);
            t.Run(240);
            long xpAfter = ambient.Sum(b => b.Data.Xp + b.Level * 100000L);
            Assert.True(xpAfter > xpBefore, "Eigenständige Bots sammeln selbst Erfahrung");
        }

        [Fact]
        public void BotsLevelWithTheGroupAndLearnAbilities()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Field, 120);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 9, Field);
            t.World.Groups.Create(p);
            var bot = t.CreateBot(p, Role.Healer, CharClass.Priest, 9);
            t.World.Groups.Join(p.Group!, bot);
            int known = bot.Data.Abilities.Count;
            t.World.Progression.GiveXp(bot, 50000, "test");
            Assert.True(bot.Level > 9);
            Assert.True(bot.Data.Abilities.Count > known, "Neue Fähigkeiten beim Stufenaufstieg");
            Assert.True(bot.TalentPointsSpent > 0, "Talentpunkte werden verteilt");
            Assert.Equal(0, bot.TalentPointsFree);
        }

        [Fact]
        public void CommandParserUnderstandsGerman()
        {
            Assert.Equal(BotCommand.Dungeon, BotCommandParser.Parse("Wir gehen in den Dungeon."));
            Assert.Equal(BotCommand.Follow, BotCommandParser.Parse("Folgt mir!"));
            Assert.Equal(BotCommand.Stay, BotCommandParser.Parse("Wartet hier kurz"));
            Assert.Equal(BotCommand.Attack, BotCommandParser.Parse("Angriff!"));
            Assert.Equal(BotCommand.Rest, BotCommandParser.Parse("Kurze Pause"));
            Assert.Equal(BotCommand.None, BotCommandParser.Parse("Schönes Wetter heute"));
        }
    }
}
