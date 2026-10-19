using EternalRealms.Server.AI;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    public class BotCommandTests
    {
        private static readonly Vec3 Field = new(520, 0, 820);

        [Fact]
        public void ParserUnderstandsAddressedOrders()
        {
            var names = new[] { "Brannoc" };
            var o = BotCommandParser.Parse("Tank, greif den Boss an.", names);
            Assert.Equal(BotCommand.Attack, o.Command);
            Assert.True(o.MentionsBoss);
            Assert.Equal(new[] { "Tank" }, o.Addressees);

            o = BotCommandParser.Parse("Heiler, bitte Mana auffüllen.", names);
            Assert.Equal(BotCommand.Mana, o.Command);
            Assert.Equal(new[] { "Heiler" }, o.Addressees);

            o = BotCommandParser.Parse("Magier, kontrolliere den linken Gegner.", names);
            Assert.Equal(BotCommand.Control, o.Command);
            Assert.Equal(OrderSide.Left, o.Side);
            Assert.Equal(new[] { "Magier" }, o.Addressees);

            o = BotCommandParser.Parse("Alle zurückziehen.", names);
            Assert.Equal(BotCommand.Retreat, o.Command);
            Assert.Empty(o.Addressees);

            o = BotCommandParser.Parse("Brannoc: unterbrich die Zauber!", names);
            Assert.Equal(BotCommand.Interrupt, o.Command);
            Assert.Equal(new[] { "Brannoc" }, o.Addressees);

            Assert.Equal(BotCommand.AvoidAoe, BotCommandParser.ParseCommand("Verteilt euch!"));
            Assert.Equal(BotCommand.Focus, BotCommandParser.ParseCommand("Fokus auf den Schamanen"));
        }

        [Fact]
        public void OnlyAddressedBotsReact()
        {
            var t = new TestWorld(seed: 3);
            t.ClearMonstersAround(Field, 120);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12, Field);
            t.World.Groups.Create(p);
            var tank = t.CreateBot(p, Role.Tank, CharClass.Warrior);
            var healer = t.CreateBot(p, Role.Healer, CharClass.Priest);
            foreach (var b in new[] { tank, healer }) t.World.Groups.Join(p.Group!, b);
            t.Run(2);
            t.World.Chat.Handle(p, ChatChannel.Party, "Heiler, warte hier.", "");
            Assert.False(healer.Bot!.Following);
            Assert.True(tank.Bot!.Following);
        }

        [Fact]
        public void MageControlsTheLeftEnemyAndOthersLeaveItAlone()
        {
            var t = new TestWorld(seed: 7);
            t.ClearMonstersAround(Field, 150);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 14, Field);
            p.Facing = 0; // Blick nach Norden (+Z): links = Westen (−X)
            t.World.Groups.Create(p);
            var mage = t.CreateBot(p, Role.RangedDps, CharClass.Mage);
            t.World.Groups.Join(p.Group!, mage);
            Assert.NotNull(mage.Bot!.ControlAbility());
            var left = t.Spawn("mob_rotbach_bandit", Field + new Vec3(-9, 0, 22), level: 12);
            var right = t.Spawn("mob_rotbach_bandit", Field + new Vec3(9, 0, 22), level: 12);
            t.Run(1);
            t.World.Chat.Handle(p, ChatChannel.Party, "Magier, kontrolliere den linken Gegner.", "");
            Assert.Equal(left.Id, p.Group!.Markers[Group.MarkMoon]);
            Assert.NotEqual(right.Id, p.Group.Markers[Group.MarkMoon]);
            t.Run(4);
            Assert.Contains(left.Auras, a => a.Def.BreaksOnDamage && a.Def.IsDebuff);
            Assert.True(t.Conn(p).Of<S_GroupMarkers>().Any(m => m.Markers.Contains(Group.MarkMoon)), "Markierung wird an die Gruppe gesendet");
        }

        [Fact]
        public void RetreatBreaksOffTheFight()
        {
            var t = new TestWorld(seed: 11);
            t.ClearMonstersAround(Field, 150);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12, Field);
            t.World.Groups.Create(p);
            var tank = t.CreateBot(p, Role.Tank, CharClass.Warrior);
            var dps = t.CreateBot(p, Role.MeleeDps, CharClass.Rogue);
            foreach (var b in new[] { tank, dps }) t.World.Groups.Join(p.Group!, b);
            var wolf = t.Spawn("mob_forest_wolf", Field + new Vec3(25, 0, 0), level: 12);
            t.World.Chat.Handle(p, ChatChannel.Party, "Angriff!", "");
            p.Target = wolf;
            t.World.Chat.Handle(p, ChatChannel.Party, "Angriff!", "");
            t.Run(3);
            t.World.MovePlayerWithinZone(p, Field + new Vec3(-30, 0, 0));
            t.World.Chat.Handle(p, ChatChannel.Party, "Alle zurückziehen.", "");
            t.Run(1);
            Assert.False(tank.AutoAttacking);
            Assert.False(dps.AutoAttacking);
            t.Run(5);
            foreach (var b in new[] { tank, dps }) Assert.True(b.DistanceTo(p) < 15, $"{b.Name} zieht sich nicht zurück ({b.DistanceTo(p):0} m)");
        }

        [Fact]
        public void UiCommandsAreValidated()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Field, 120);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, Field);
            var session = t.Conn(p);
            t.World.Groups.Create(p);
            var healer = t.CreateBot(p, Role.Healer, CharClass.Priest);
            t.World.Groups.Join(p.Group!, healer);
            healer.Bot!.Execute(new BotOrder { Command = BotCommand.Stay }, p);
            Assert.False(healer.Bot.Following);
            healer.Bot.Execute(new BotOrder { Command = BotCommand.Follow }, p);
            Assert.True(healer.Bot.Following);
            Assert.NotNull(t.World.Bots.SetFavorite(p, 999999, true));
            Assert.Null(t.World.Bots.SetFavorite(p, healer.Id, true));
            Assert.Contains(p.Data.AccountId ?? -1, healer.Data.Bot!.FavoriteOf);
        }

        [Fact]
        public void FavoritesAppearFirstAndCatchUpWhileAway()
        {
            var t = new TestWorld(seed: 21);
            t.ClearMonstersAround(Field, 120);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 20, Field);
            var data = t.World.Bots.CreateBotData(p.Faction, Role.Healer, 14, CharClass.Priest, p.Zone!.Def.Id, difficulty: BotDifficulty.Erfahren)!;
            Assert.Equal(BotDifficulty.Erfahren, data.Bot!.Difficulty);
            data.Bot.LastActive = DateTime.UtcNow.AddHours(-4.5);
            t.World.Bots.CatchUp(data, p.Level);
            Assert.Equal(18, data.Level);
            Assert.Contains(data.Bot.History, h => h.Contains("Stufe 18"));
            t.World.Bots.CatchUp(data, p.Level); // gerade aktualisiert – kein weiterer Aufstieg
            Assert.Equal(18, data.Level);
        }

        [Fact]
        public void DifficultyShapesMistakesAndVeteransKnowBosses()
        {
            Assert.True(BotPersonality.MistakeChance(BotDifficulty.Anfaenger) > BotPersonality.MistakeChance(BotDifficulty.Veteran));
            var t = new TestWorld();
            foreach (var boss in new[] { "boss_moderkeim", "boss_elvaine", "boss_morrak" })
                Assert.False(string.IsNullOrEmpty(t.World.Bots.BossTip(boss)));
        }
    }
}
