using System.Reflection;
using EternalRealms.Server;
using EternalRealms.Server.Persistence;
using EternalRealms.Shared;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    public class ProtocolTests
    {
        [Fact]
        public void EveryMessageSurvivesARoundTrip()
        {
            var types = typeof(Message).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(Message).IsAssignableFrom(t)).ToList();
            Assert.True(types.Count >= 60, $"nur {types.Count} Nachrichtentypen");
            var rng = new Random(1);
            foreach (var type in types)
            {
                var m = (Message)Activator.CreateInstance(type)!;
                Fill(m, rng);
                var bytes = Packets.Serialize(m);
                var back = Packets.Deserialize(bytes);
                Assert.Equal(type, back.GetType());
                Assert.Equal(Packets.Serialize(back), bytes);
            }
        }

        private static void Fill(object o, Random rng)
        {
            foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = f.FieldType;
                if (t == typeof(string)) f.SetValue(o, "Wert" + rng.Next(100));
                else if (t == typeof(int)) f.SetValue(o, rng.Next(-1000, 1000));
                else if (t == typeof(long)) f.SetValue(o, (long)rng.Next());
                else if (t == typeof(uint)) f.SetValue(o, (uint)rng.Next());
                else if (t == typeof(float)) f.SetValue(o, (float)rng.NextDouble() * 100);
                else if (t == typeof(bool)) f.SetValue(o, true);
                else if (t == typeof(byte)) f.SetValue(o, (byte)rng.Next(255));
                else if (t == typeof(ushort)) f.SetValue(o, (ushort)rng.Next(60000));
                else if (t.IsEnum) f.SetValue(o, Enum.GetValues(t).GetValue(rng.Next(Enum.GetValues(t).Length)));
                else if (t == typeof(Vec3)) f.SetValue(o, new Vec3(1.5f, 2.5f, 3.5f));
                else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var list = (System.Collections.IList)Activator.CreateInstance(t)!;
                    var et = t.GetGenericArguments()[0];
                    for (int i = 0; i < 2; i++)
                    {
                        object item = et == typeof(string) ? "x" + i : et == typeof(int) ? i : Activator.CreateInstance(et)!;
                        if (!et.IsPrimitive && et != typeof(string)) Fill(item, rng);
                        list.Add(item);
                    }
                    f.SetValue(o, list);
                }
                else if (t.IsClass && t != typeof(object)) { var inner = Activator.CreateInstance(t)!; Fill(inner, rng); f.SetValue(o, inner); }
            }
        }

        [Fact]
        public void SnapshotChunksFitIntoTheSmallestUnreliablePacket()
        {
            var snap = new S_Snapshot { Tick = uint.MaxValue, LastProcessedMove = uint.MaxValue };
            for (int i = 0; i < EternalRealms.Server.World.ZoneInstance.SnapshotChunk; i++)
                snap.Entities.Add(new EntitySnapshotData { Id = int.MaxValue, Health = int.MaxValue, MaxHealth = int.MaxValue, Flags = uint.MaxValue });
            Assert.True(Packets.Serialize(snap).Length <= 1000, "Snapshot-Paket zu groß für unzuverlässiges UDP");
        }

        [Fact]
        public void MalformedPacketsAreRejected()
        {
            Assert.ThrowsAny<Exception>(() => Packets.Deserialize(new byte[] { 0xff, 0xff, 1, 2 }));
        }
    }

    public class NetworkTests
    {
        /// <summary>Kompletter Ablauf über echtes UDP: Konto anlegen, Charakter erschaffen, Welt betreten, KI-Mitspieler einladen.</summary>
        [Fact]
        public async Task ClientCanRegisterCreateEnterAndInviteBots()
        {
            var config = new ServerConfig { Port = 0, AmbientBots = false, ContentPath = TestContent.ContentRoot };
            var store = new InMemoryStore();
            using var server = new GameServer(config, TestContent.Db, store, store);
            var serverLog = new System.Collections.Concurrent.ConcurrentQueue<string>();
            server.World.Log = l => serverLog.Enqueue(l);
            _serverLog = serverLog;
            server.World.Bots.LoadOffline(Array.Empty<CharacterData>());
            Assert.True(server.Start()); // Port 0 = freier Port
            int port = server.Network.Port;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var loop = Task.Run(() => server.Run(cts.Token));

            using var client = new GameClient();
            var received = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            client.MessageReceived += m => received.AddOrUpdate(m.Op.ToString(), 1, (_, n) => n + 1);
            _received = received;
            Assert.True(client.Connect("127.0.0.1", port));
            await WaitFor(client, () => client.Connected, "Verbindung");
            client.Login("testkonto", "geheim123", register: true);
            await WaitFor(client, () => client.LoggedIn, "Anmeldung");
            client.Send(new C_CreateCharacter { Name = "Netztest", Race = Race.Elarii, Class = CharClass.Druid, Gender = Gender.Female, Appearance = new AppearanceData { Height = 1.0f } });
            await WaitFor(client, () => client.Characters.Count == 1, "Charakterliste");
            Assert.Equal("Netztest", client.Characters[0].Name);
            client.Send(new C_EnterWorld { CharacterId = client.Characters[0].Id });
            await WaitFor(client, () => client.InWorld && client.State.Entities.Count > 5, "Weltbetritt und sichtbare Einheiten");
            Assert.Equal("zone_morgentauwald", client.ZoneId);
            Assert.NotNull(client.Me);
            Assert.True(client.SpellBook.ContainsKey("druid_wrath"));
            client.Send(new C_BotSearch());
            await WaitFor(client, () => client.BotCandidates.Count > 0, "KI-Mitspieler-Vorschläge");
            client.Send(new C_BotInvite { CandidateId = client.BotCandidates[0].CandidateId });
            await WaitFor(client, () => client.Group.Count == 2, "Gruppe mit Bot");
            await WaitFor(client, () => client.ChatLog.Any(c => c.SenderIsBot), "Begrüßung des Bots");
            // Kommandooberfläche, Favoriten und Zielmarkierungen
            int botId = client.Group.First(m => m.IsBot).EntityId;
            client.BotCommand(2, botId); // Position halten
            await WaitFor(client, () => server.World.AllPlayers.First(p => p.Id == botId).Bot!.Following == false, "Befehl „Halten“");
            client.Send(new C_BotFavorite { EntityId = botId });
            await WaitFor(client, () => client.Notices.Any(n => n.Contains("bevorzugter Begleiter")), "Favorit gespeichert");
            var mob = client.State.Entities.Values.First(e => e.Kind == EntityKind.Creature);
            client.SetMarker(8, mob.Id);
            await WaitFor(client, () => client.Markers.TryGetValue(mob.Id, out var m) && m == 8, "Totenkopf-Markierung");
            client.Move(client.Me!.Position + new Vec3(2, 0, 0), 0);
            await WaitFor(client, () => Vec3.DistanceXZ(server.World.AllPlayers.First(p => !p.IsBot).Position, client.Me!.Position) < 0.5f, "Bewegung beim Server");
            cts.Cancel();
            await loop;
            var tickErrors = serverLog.Where(l => l.StartsWith("Fehler")).ToList();
            Assert.True(tickErrors.Count == 0, "Fehler im Server: " + string.Join("\n", tickErrors.Take(2)));
        }

        private static System.Collections.Concurrent.ConcurrentQueue<string>? _serverLog;
        private static System.Collections.Concurrent.ConcurrentDictionary<string, int>? _received;

        private static async Task WaitFor(GameClient c, Func<bool> cond, string what)
        {
            // Zeitbasiert statt nach Schleifendurchläufen: unter paralleler Testlast braucht der Server manchmal länger.
            var until = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < until)
            {
                c.Poll();
                if (cond()) return;
                await Task.Delay(20);
            }
            Assert.Fail("Zeitüberschreitung: " + what + " – letzte Meldungen: " + string.Join(" | ", c.Notices.TakeLast(3)) +
                        " – Server: " + string.Join(" | ", (_serverLog ?? new()).TakeLast(5)) +
                        " – empfangen: " + string.Join(", ", (_received ?? new()).Select(kv => kv.Key + "×" + kv.Value)));
        }
    }

    public class PersistenceTests
    {
        /// <summary>Läuft nur, wenn ER_TEST_DATABASE gesetzt ist (z. B. Host=localhost;Username=realms;Password=realms;Database=eternal_realms_test).</summary>
        [Fact]
        public async Task PostgresStoresAndLoadsACharacterCompletely()
        {
            var cs = Environment.GetEnvironmentVariable("ER_TEST_DATABASE");
            if (string.IsNullOrEmpty(cs)) return;
            using var store = new PostgresStore(cs);
            var root = Path.GetDirectoryName(TestContent.ContentRoot)!;
            await store.MigrateAsync(Path.Combine(root, "db", "migrations"));
            await store.MigrateAsync(Path.Combine(root, "db", "migrations")); // idempotent
            var name = "Pg" + new string(Guid.NewGuid().ToString("N").Where(char.IsLetter).Take(8).ToArray());
            var acc = await store.CreateAsync("acc_" + name.ToLowerInvariant(), PasswordHasher.Hash("geheim123"));
            Assert.True(PasswordHasher.Verify("geheim123", (await store.FindByNameAsync(acc.Username))!.PasswordHash));

            var t = new TestWorld();
            var data = t.World.CreateCharacterTemplate(name, Race.Grakar, CharClass.Shaman, Gender.Male, new AppearanceDto { Face = 3, Height = 1.02f });
            data.AccountId = acc.Id;
            long id = await store.CreateAsync(data);
            data.Level = 17; data.Xp = 1234; data.Money = 98765; data.Position = new Vec3(10, 2, 30);
            data.Talents["se_convection"] = 3;
            data.Quests["q_as_echsen"] = new QuestProgress { QuestId = "q_as_echsen", State = QuestState.Active, Counts = new[] { 7 } };
            data.CompletedQuests.Add("q_as_grakar");
            data.Reputation["rep_kargrak"] = 450;
            data.DiscoveredAreas.Add("area_as_lager");
            data.Bag[3] = new ItemStack { ItemId = "item_minor_healing_potion", Count = 4 };
            await store.SaveAsync(data);

            var back = (await store.GetAsync(id))!;
            Assert.Equal(name, back.Name);
            Assert.Equal(17, back.Level);
            Assert.Equal(98765, back.Money);
            Assert.Equal(new Vec3(10, 2, 30), back.Position);
            Assert.Equal(3, back.Talents["se_convection"]);
            Assert.Equal(7, back.Quests["q_as_echsen"].Counts[0]);
            Assert.Contains("q_as_grakar", back.CompletedQuests);
            Assert.Equal(450, back.Reputation["rep_kargrak"]);
            Assert.Equal(4, back.Bag[3]!.Count);
            Assert.Equal(data.Equipment.Count, back.Equipment.Count);
            Assert.Equal(data.Abilities.Count, back.Abilities.Count);
            Assert.Equal(3, back.Appearance.Face);
            Assert.True(await store.NameExistsAsync(name.ToUpperInvariant()));
            Assert.Single(await store.ListForAccountAsync(acc.Id));

            // Bank, Taschen, Talent-Resets und Wirtschaft: alles in einer Transaktion
            back.Bank.Add(null); back.Bank.Add(new ItemStack { ItemId = "item_wolf_fang", Count = 5 });
            back.BagContainers[1] = new ItemStack { ItemId = "item_bag_leather", Count = 1 };
            back.TalentResets = 2;
            back.ClientSettings = "{\"UiScale\":1.25}";
            back.Professions["mining"] = new ProfessionProgress { Skill = 42, MaxSkill = 75, Recipes = { "smelt_copper" } };
            foreach (var eq in back.Equipment.Values.Take(1)) eq.Enchant = "ench_wrist_0";
            back.Bag[3] = null; // Gegenstand wandert in einen Brief
            long mailId = DateTime.UtcNow.Ticks, auctionId = mailId + 1;
            var commit = new EconomyCommit();
            commit.Characters.Add(back);
            commit.UpsertMail.Add(new MailData { Id = mailId, RecipientId = id, SenderName = "Test", Subject = "Paket", Money = 77,
                Items = { new ItemStack { ItemId = "item_minor_healing_potion", Count = 4 } }, DeliverAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30) });
            commit.UpsertAuctions.Add(new AuctionData { Id = auctionId, House = 2, SellerId = id, SellerName = name, ItemId = "item_wolf_fang", Count = 2, StartBid = 10, Buyout = 20,
                DurationHours = 24, ExpiresAt = DateTime.UtcNow.AddHours(24) });
            commit.Ledger.Add(new LedgerEntry { CharacterId = id, CharacterName = name, Delta = -30, Balance = 98735, Reason = "mail_send", Reference = "Test" });
            await store.CommitAsync(commit);
            var again = (await store.GetAsync(id))!;
            Assert.True(again.Bag.Count <= 3 || again.Bag[3] == null);
            Assert.Equal(5, again.Bank[1]!.Count);
            Assert.Equal("item_bag_leather", again.BagContainers[1].ItemId);
            Assert.Equal(2, again.TalentResets);
            Assert.Equal("{\"UiScale\":1.25}", again.ClientSettings);
            Assert.Equal(42, again.Professions["mining"].Skill);
            Assert.Contains("smelt_copper", again.Professions["mining"].Recipes);
            if (again.Equipment.Count > 0) Assert.Contains(again.Equipment.Values, e => e.Enchant == "ench_wrist_0");
            var mail = (await store.LoadMailAsync()).Single(m => m.Id == mailId);
            Assert.Equal(4, mail.Items.Single().Count);
            Assert.Equal(77, mail.Money);
            Assert.Contains(await store.LoadAuctionsAsync(), a => a.Id == auctionId && a.Buyout == 20);
            Assert.Equal(-30, (await store.LedgerForAsync(id, 5)).Single().Delta);

            // Weltboss-Wiederkehr (Block D) übersteht Neustarts
            var key = "test:" + name;
            var when = DateTime.UtcNow.AddHours(3);
            await store.SaveSpawnTimerAsync(key, when);
            Assert.Equal(when, (await store.LoadSpawnTimersAsync())[key], TimeSpan.FromSeconds(1));
            await store.SaveSpawnTimerAsync(key, null);
            Assert.False((await store.LoadSpawnTimersAsync()).ContainsKey(key));
            Assert.Equal(id, (await store.FindCharacterAsync(name.ToLowerInvariant()))!.Id);
            var del = new EconomyCommit();
            del.DeleteMail.Add(mailId);
            del.DeleteAuctions.Add(auctionId);
            await store.CommitAsync(del);
            Assert.DoesNotContain(await store.LoadMailAsync(), m => m.Id == mailId);

            // Freunde, Ignorieren und Gilde (Migration 003)
            var other = new CharacterData { Name = name + "b", Race = Race.Grakar, Class = CharClass.Warrior, AccountId = acc.Id, ZoneId = "zone_aschensteppe" };
            long otherId = await store.CreateAsync(other);
            again.Friends.Add(new SocialEntry { CharacterId = otherId, Name = other.Name, Note = "Kumpel" });
            again.Ignored.Add(new SocialEntry { CharacterId = otherId, Name = other.Name });
            again.ShareLocation = false;
            await store.SaveAsync(again);
            var social = (await store.GetAsync(id))!;
            Assert.Equal("Kumpel", social.Friends.Single().Note);
            Assert.Equal(other.Name, social.Ignored.Single().Name);
            Assert.False(social.ShareLocation);
            long gid = DateTime.UtcNow.Ticks % 1_000_000_000;
            var guild = new GuildData { Id = gid, Name = "Testgilde " + name, Faction = Faction.Schattenpakt, LeaderId = id, Motd = "Hallo" };
            guild.Members.Add(new GuildMemberData { CharacterId = id, Rank = 0 });
            guild.Members.Add(new GuildMemberData { CharacterId = otherId, Rank = 4 });
            await store.SaveGuildAsync(guild);
            guild.Members.RemoveAt(1);
            guild.Motd = "Neu";
            await store.SaveGuildAsync(guild);
            var loaded = (await store.LoadGuildsAsync()).Single(g => g.Id == gid);
            Assert.Equal("Neu", loaded.Motd);
            Assert.Equal(name, loaded.Members.Single().Name);
            await store.SaveReportAsync(new PlayerReport { ReporterId = id, ReporterName = name, TargetId = otherId, TargetName = other.Name, Reason = "Test", Evidence = { "x" } });
            await store.DeleteGuildAsync(gid);
            Assert.DoesNotContain(await store.LoadGuildsAsync(), g => g.Id == gid);
            await store.DeleteAsync(otherId);

            await store.DeleteAsync(id);
            Assert.Null(await store.GetAsync(id));
        }
    }
}
