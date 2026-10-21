using EternalRealms.Server;
using EternalRealms.Server.AI;
using EternalRealms.Server.Net;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;

namespace EternalRealms.Tests
{
    /// <summary>Hilfen für Simulationstests: eine echte Spielwelt mit festem Zufall, ohne Netzwerk.</summary>
    public sealed class TestWorld
    {
        public GameWorld World { get; }
        public InMemoryStore Store { get; }
        public List<string> Log { get; } = new();

        /// <param name="store">Gemeinsamer Speicher, um einen Serverneustart nachzustellen.</param>
        /// <param name="content">Eigener Content (z. B. eine Kopie mit Content-Migrationen); Standard ist der echte Content.</param>
        public TestWorld(int seed = 1234, bool ambientBots = false, bool zoneLod = true, InMemoryStore? store = null, bool tutorial = false,
                         Shared.Content.ContentDatabase? content = null, ServerConfig? config = null)
        {
            Store = store ?? new InMemoryStore();
            config ??= new ServerConfig { AmbientBots = ambientBots, DevCommands = true, ZoneLod = zoneLod, Tutorial = tutorial };
            World = new GameWorld(content ?? TestContent.Db, config, Store, new GameRandom(seed)) { Log = s => { lock (Log) Log.Add(s); } };
            World.Bots.LoadOffline(Array.Empty<CharacterData>());
        }

        public Player CreatePlayer(Race race, CharClass cls, int level = 1, Vec3? pos = null, string zone = "", string? name = null)
        {
            name ??= "Test" + (char)('a' + World.AllPlayers.Count() % 26) + World.NextEntityId() % 1000;
            var data = World.CreateCharacterTemplate(NameGenerator.Normalize(new string(name.Where(char.IsLetter).ToArray())), race, cls, Gender.Male, new AppearanceDto());
            data.Id = Store.CreateAsync(data).Result;
            data.AccountId = 1000 + data.Id;
            if (zone != "") data.ZoneId = zone;
            if (pos.HasValue) data.Position = pos.Value;
            var p = World.EnterWorld(data, new RecordingConnection());
            for (int i = 1; i < level; i++) World.Progression.LevelUp(p);
            World.Progression.AutoTrain(p);
            p.Health = p.MaxHealth;
            p.Power = p.PowerType == PowerType.Rage ? 0 : p.MaxPower;
            return p;
        }

        public Player CreateBot(Player near, Role role, CharClass? cls = null, int? level = null)
        {
            var data = World.Bots.CreateBotData(near.Faction, role, level ?? near.Level, cls, near.Zone!.Def.Id)!;
            data.Position = near.Position + new Vec3(2, 0, 2);
            var bot = World.Bots.Spawn(data, near.Zone);
            return bot;
        }

        public RecordingConnection Conn(Player p) => (RecordingConnection)p.Connection!;

        /// <summary>Simuliert die Welt mit 20 Ticks pro Sekunde.</summary>
        public void Run(double seconds, Func<bool>? until = null)
        {
            int ticks = (int)(seconds * 20);
            for (int i = 0; i < ticks; i++)
            {
                World.Update(0.05f);
                if (until != null && i % 4 == 0 && until()) return;
            }
        }

        public Creature Spawn(string creatureId, Vec3 pos, string zone = "zone_morgentauwald", int? level = null)
        {
            var c = World.Zone(zone)!.SpawnCreature(creatureId, pos, level);
            c.IsSummoned = false;
            return c;
        }

        /// <summary>Entfernt alle Kreaturen einer Zone, die nicht im Umkreis liegen, damit Tests ungestört laufen.</summary>
        public void ClearMonstersAround(Vec3 center, float radius, string zone = "zone_morgentauwald")
        {
            var z = World.Zone(zone)!;
            foreach (var c in z.Entities.Values.OfType<Creature>().Where(c => !c.Template.IsNpc && c.DistanceTo(center) < radius).ToList())
            {
                if (c.Spawn != null) { c.Spawn.RespawnTimer = 1e9f; c.Spawn.Current = null; c.Spawn = null; }
                z.Remove(c);
            }
            World.Update(0.01f);
        }
    }
}
