using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    public class GuildTests
    {
        [Fact]
        public void FoundInviteRanksChatAndLeadership()
        {
            var t = new TestWorld();
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, name: "Gruender");
            var b = t.CreatePlayer(Race.Elarii, CharClass.Mage, 10, name: "Rekrutin");
            var c = t.CreatePlayer(Race.Elarii, CharClass.Priest, 10, name: "Drittes");
            Assert.NotNull(t.World.Guilds.Create(a, "Die Morgenwacht")); // kein Geld
            t.World.Economy.Credit(a, 20_000, "test");
            Assert.Null(t.World.Guilds.Create(a, "Die Morgenwacht"));
            Assert.NotNull(t.World.Guilds.Create(b, "die morgenwacht")); // Name vergeben (Groß/Klein egal)
            Assert.Null(t.World.Guilds.Invite(a, "Rekrutin"));
            Assert.Contains(t.Conn(b).Of<S_GuildInvite>(), i => i.GuildName == "Die Morgenwacht");
            Assert.Null(t.World.Guilds.Respond(b, true));
            var g = t.World.Guilds.GuildOf(b)!;
            Assert.Equal(GuildSystemRanks.Recruit, g.Members.Single(m => m.Name == "Rekrutin").Rank);
            Assert.NotNull(t.World.Guilds.Invite(b, "Drittes")); // Rekruten dürfen nicht einladen

            t.Conn(b).Clear();
            t.World.Chat.Handle(a, ChatChannel.Guild, "Willkommen!", "");
            Assert.Contains(t.Conn(b).Of<S_Chat>(), m => m.Channel == ChatChannel.Guild && m.Text == "Willkommen!");
            Assert.DoesNotContain(t.Conn(c).Of<S_Chat>(), m => m.Text == "Willkommen!");

            Assert.Null(t.World.Guilds.SetRank(a, b.Data.Id, GuildSystemRanks.Officer));
            Assert.NotNull(t.World.Guilds.Leave(a)); // Gildenmeister muss erst übergeben
            Assert.Null(t.World.Guilds.SetRank(a, b.Data.Id, 0));
            Assert.Equal(b.Data.Id, g.LeaderId);
            Assert.Null(t.World.Guilds.Leave(a));
            Assert.Null(t.World.Guilds.GuildOf(a));
            Assert.True(t.World.Persistence!.Flush(TimeSpan.FromSeconds(5)));
            var stored = t.Store.LoadGuildsAsync().Result.Single();
            Assert.Equal(b.Data.Id, stored.LeaderId);
            Assert.Single(stored.Members);
        }

        [Fact]
        public void BotsJoinGuildsOnlyWhenTheyKnowTheInviter()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, new Vec3(520, 0, 820));
            t.World.Economy.Credit(p, 20_000, "test");
            t.World.Guilds.Create(p, "Lindenbund");
            var stranger = t.CreateBot(p, Role.Tank, CharClass.Warrior);
            stranger.Data.Id = 9001;
            t.World.Guilds.Invite(p, stranger.Name);
            Assert.Null(t.World.Guilds.GuildOf(stranger));
            t.World.Groups.Create(p);
            var mate = t.CreateBot(p, Role.Healer, CharClass.Priest);
            mate.Data.Id = 9002;
            t.World.Groups.Join(p.Group!, mate);
            t.World.Guilds.Invite(p, mate.Name);
            Assert.NotNull(t.World.Guilds.GuildOf(mate));
        }
    }

    internal static class GuildSystemRanks
    {
        public const int Officer = EternalRealms.Server.Systems.GuildSystem.Officer, Recruit = EternalRealms.Server.Systems.GuildSystem.Recruit;
    }

    public class FriendTests
    {
        [Fact]
        public void FriendsIgnoreAndReports()
        {
            var t = new TestWorld();
            var a = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, new Vec3(400, 0, 400), name: "Anna");
            var b = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 12, new Vec3(402, 0, 400), name: "Bert");
            var orc = t.CreatePlayer(Race.Grakar, CharClass.Warrior, 10, name: "Grukk", zone: "zone_aschensteppe");
            t.World.Social.AddFriend(a, "Bert");
            t.World.Social.AddFriend(a, "Grukk"); // andere Fraktion
            var list = t.Conn(a).Of<S_FriendList>().Last();
            var bert = Assert.Single(list.Friends);
            Assert.True(bert.Online);
            Assert.Equal(12, bert.Level);
            Assert.Equal("Morgentauwald", bert.Zone);
            t.World.Social.SetShareLocation(b, false);
            Assert.Equal("", t.World.Social.BuildList(a).Friends.Single().Zone);

            t.World.Chat.Handle(b, ChatChannel.Say, "Hallo Anna", "");
            Assert.Contains(t.Conn(a).Of<S_Chat>(), m => m.Text == "Hallo Anna");
            t.World.Social.Ignore(a, "Bert");
            Assert.Empty(t.World.Social.BuildList(a).Friends); // Ignorieren entfernt aus Freunden
            t.Conn(a).Clear();
            t.World.Chat.Handle(b, ChatChannel.Say, "Hörst du mich?", "");
            t.World.Chat.Handle(b, ChatChannel.Whisper, "Psst", "Anna");
            Assert.DoesNotContain(t.Conn(a).Of<S_Chat>(), m => m.SenderName == "Bert");
            Assert.Contains(t.Conn(b).Of<S_Notice>(), n => n.Text.Contains("ignoriert"));

            t.World.Social.Report(a, "Bert", "Belästigung", "Schreibt ständig.");
            Assert.True(t.World.Persistence!.Flush(TimeSpan.FromSeconds(5)));
            var rep = Assert.Single(t.Store.Reports);
            Assert.Equal("Bert", rep.TargetName);
            Assert.Contains(rep.Evidence, e => e.Contains("Hörst du mich?"));
        }

        [Fact]
        public void CompanionListShowsFavoritesAndCanSummonThem()
        {
            var t = new TestWorld();
            var field = new Vec3(520, 0, 820);
            t.ClearMonstersAround(field, 100);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12, field);
            t.World.Groups.Create(p);
            var bot = t.CreateBot(p, Role.Healer, CharClass.Priest);
            t.World.Groups.Join(p.Group!, bot);
            bot.Data.Id = 4242; // gespeichert
            t.World.Bots.SetFavorite(p, bot.Id, true);
            bot.Bot!.RecordShared("Moderkeim besiegt", 3);
            var comp = Assert.Single(t.World.Social.BuildList(p).Companions);
            Assert.Equal("in Eurer Gruppe", comp.Availability);
            Assert.Equal(Role.Healer, comp.Role);
            Assert.Contains("Moderkeim besiegt", comp.SharedAdventures);

            t.World.Bots.Dismiss(p, bot.Id);
            t.Run(1);
            comp = Assert.Single(t.World.Social.BuildList(p).Companions);
            Assert.False(comp.Online);
            Assert.Null(t.World.Bots.InviteFavorite(p, 4242));
            Assert.Contains(p.Group!.Members, m => m.Data.Id == 4242);
        }
    }
}
