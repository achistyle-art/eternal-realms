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
            server.World.Bots.LoadOffline(Array.Empty<CharacterData>());
            Assert.True(server.Start()); // Port 0 = freier Port
            int port = server.Network.Port;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var loop = Task.Run(() => server.Run(cts.Token));

            using var client = new GameClient();
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
        }

        private static async Task WaitFor(GameClient c, Func<bool> cond, string what)
        {
            for (int i = 0; i < 300; i++)
            {
                c.Poll();
                if (cond()) return;
                await Task.Delay(20);
            }
            Assert.Fail("Zeitüberschreitung: " + what + " – letzte Meldungen: " + string.Join(" | ", c.Notices.TakeLast(3)));
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
            await store.DeleteAsync(id);
            Assert.Null(await store.GetAsync(id));
        }
    }
}
