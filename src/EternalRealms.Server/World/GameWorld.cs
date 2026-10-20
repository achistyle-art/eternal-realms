using System.Collections.Concurrent;
using EternalRealms.Server.AI;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.Systems;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.World
{
    /// <summary>
    /// Die gesamte simulierte Welt eines Realms. Läuft in einem einzigen Spielthread (keine Sperren nötig);
    /// Datenbankzugriffe laufen im Hintergrund und melden sich über <see cref="Post"/> zurück.
    /// </summary>
    public sealed class GameWorld
    {
        public ContentDatabase Content { get; }
        public ServerConfig Config { get; }
        public IRandom Random { get; }
        public ICharacterRepository? Characters { get; }
        public IEconomyRepository? EconomyStore { get; }
        /// <summary>Geordnete Datenbankaufträge (null ohne Datenbank).</summary>
        public PersistenceQueue? Persistence { get; }
        public IWorldRepository? WorldStore { get; }

        /// <summary>Wiederkehrzeit eines dauerhaften Spawns speichern (null = lebt).</summary>
        public void SaveSpawnTimer(string key, DateTime? at)
        {
            if (WorldStore == null || Persistence == null) return;
            var store = WorldStore;
            Persistence.Enqueue(() => store.SaveSpawnTimerAsync(key, at));
        }

        /// <summary>Beim Start: Weltbosse, deren Zeit noch läuft, wieder entfernen und die Restzeit übernehmen.</summary>
        public int ApplySpawnTimers(Dictionary<string, DateTime> timers)
        {
            int n = 0;
            foreach (var z in OpenZones)
                foreach (var sp in z.SpawnPoints.Where(s => s.Def.Persistent))
                {
                    if (!timers.TryGetValue(sp.Key, out var at) || at <= DateTime.UtcNow) continue;
                    if (sp.Current != null) { z.Remove(sp.Current); sp.Current = null; }
                    sp.RespawnTimer = (float)(at - DateTime.UtcNow).TotalSeconds;
                    n++;
                }
            return n;
        }
        public EconomySystem Economy { get; }
        public MailSystem Mail { get; }
        public MountSystem Mounts { get; }
        public GuildSystem Guilds { get; }
        public InstanceSystem Instances { get; }
        public GroupFinderSystem Finder { get; }
        public ProfessionSystem Professions { get; }
        public SocialSystem Social { get; }
        public TradeSystem Trades { get; }
        public EmoteSystem Emotes { get; }
        public WorldEventSystem Events { get; }
        public AchievementSystem Achievements { get; }
        public TutorialSystem Tutorial { get; }

        /// <summary>Fortschrittsereignis an Erfolge und Tutorial (Kapitel 38/39).</summary>
        public void ReportProgress(Player p, ProgressEvent ev, string target = "", int amount = 1)
        {
            if (p.Removed && p.Zone == null) return;
            Achievements.Report(p, ev, target, amount);
            Tutorial.Report(p, ev, target, amount);
        }
        public LootRollSystem LootRolls { get; }
        public AuctionSystem Auctions { get; }
        public double Time { get; private set; }
        public Action<string> Log { get; set; } = Console.WriteLine;

        public CombatSystem Combat { get; }
        public QuestSystem Quests { get; }
        public ProgressionSystem Progression { get; }
        public LootSystem Loot { get; }
        public DeathSystem Death { get; }
        public InteractionSystem Interactions { get; }
        public ChatSystem Chat { get; }
        public GroupSystem Groups { get; }
        public BotDirector Bots { get; }
        public DevCommands DevCommands { get; }

        private readonly Dictionary<string, ZoneInstance> _zones = new();
        private readonly Dictionary<int, Player> _players = new();
        private readonly ConcurrentQueue<Action> _mainThread = new();
        private int _nextEntityId = 1;
        private double _lastWorldStateBroadcast;

        public GameWorld(ContentDatabase content, ServerConfig? config = null, ICharacterRepository? characters = null, IRandom? random = null)
        {
            Content = content;
            Config = config ?? new ServerConfig();
            Characters = characters;
            EconomyStore = characters as IEconomyRepository;
            WorldStore = characters as IWorldRepository;
            if (characters != null) Persistence = new PersistenceQueue(ex => Post(() => Log("Datenbankfehler: " + ex.Message)));
            Random = random ?? new GameRandom();
            Economy = new EconomySystem(this);
            Mail = new MailSystem(this);
            Mounts = new MountSystem(this);
            Guilds = new GuildSystem(this);
            Instances = new InstanceSystem(this);
            Finder = new GroupFinderSystem(this);
            Professions = new ProfessionSystem(this);
            Social = new SocialSystem(this);
            Trades = new TradeSystem(this);
            Emotes = new EmoteSystem(this);
            Events = new WorldEventSystem(this);
            Achievements = new AchievementSystem(this);
            Tutorial = new TutorialSystem(this);
            LootRolls = new LootRollSystem(this);
            Auctions = new AuctionSystem(this);
            Combat = new CombatSystem(this);
            Quests = new QuestSystem(this);
            Progression = new ProgressionSystem(this);
            Loot = new LootSystem(this);
            Death = new DeathSystem(this);
            Interactions = new InteractionSystem(this);
            Chat = new ChatSystem(this);
            Groups = new GroupSystem(this);
            Bots = new BotDirector(this);
            DevCommands = new DevCommands(this);
            foreach (var z in content.Zones.Values.Where(z => z.Playable && !z.IsInstance))
            {
                var zi = new ZoneInstance(this, z);
                _zones[z.Id] = zi;
                zi.Populate();
            }
        }

        public int NextEntityId() => _nextEntityId++;
        public IEnumerable<ZoneInstance> OpenZones => _zones.Values;
        public IEnumerable<ZoneInstance> AllZones => _zones.Values.Concat(Instances.All);
        public IEnumerable<Player> AllPlayers => _players.Values;
        public ZoneInstance? Zone(string id) => _zones.TryGetValue(id, out var z) ? z : null;

        /// <summary>Spielzeit in Stunden (0–24). Die Welt startet um 8 Uhr morgens.</summary>
        public float GameHour => (float)(((8 + _hourOffset + Time * Config.TimeScale / 3600.0) % 24 + 24) % 24);
        private double _hourOffset;

        /// <summary>Uhrzeit setzen (GM-Befehl, Tests); alle Regionen erfahren es sofort.</summary>
        public void SetGameHour(float hour)
        {
            _hourOffset += hour - GameHour;
            foreach (var z in AllZones) z.BroadcastAll(BuildWorldState(z));
        }

        /// <summary>Führt eine Aktion im Spielthread aus (z. B. Ergebnis einer Datenbankabfrage).</summary>
        public void Post(Action a) => _mainThread.Enqueue(a);

        // ================================================================== Tick

        /// <summary>Dauer der letzten 1200 Takte (ca. eine Minute bei 20 Hz) in Millisekunden (Kapitel 52).</summary>
        public FrameStats TickStats { get; } = new(1200);
        /// <summary>Takte, die länger als ihr Zeitbudget gebraucht haben.</summary>
        public long TickOverruns { get; private set; }

        public void Update(float dt)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdateCore(dt); }
            finally
            {
                float ms = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                TickStats.Add(ms);
                if (ms > dt * 1000) TickOverruns++;
            }
        }

        public int EntityCount => AllZones.Sum(z => z.Entities.Count);

        private void UpdateCore(float dt)
        {
            Time += dt;
            while (_mainThread.TryDequeue(out var a))
            {
                try { a(); } catch (Exception ex) { Log("Fehler im Spielthread: " + ex); }
            }
            foreach (var z in _zones.Values) z.Tick(dt);
            Instances.Update(dt);
            Groups.Update(dt);
            Finder.Update(dt);
            Trades.Update();
            Events.Update(dt);
            Tutorial.Update(dt);
            Achievements.Flush();
            Bots.Update(dt);
            Mail.Update(dt);
            LootRolls.Update();
            Auctions.Update(dt);
            Economy.Update(dt);
            if (Time - _lastWorldStateBroadcast > 30)
            {
                _lastWorldStateBroadcast = Time;
                foreach (var z in AllZones) z.BroadcastAll(BuildWorldState(z));
            }
        }

        public void UpdatePlayer(Player p, float dt)
        {
            p.UpdateTimers(dt, Time);
            Combat.UpdateUnit(p, dt);
            if (p.Removed) return;
            if (p.IsFlying) UpdateFlight(p, dt);
            var area = p.Zone!.AreaAt(p.Position);
            if (area != p.CurrentArea)
            {
                p.CurrentArea = area;
                if (area != null && p.IsAlive)
                {
                    Progression.DiscoverArea(p, area);
                    Quests.OnAreaEntered(p, area);
                }
            }
            p.RestTickTimer += dt;
            if (p.RestTickTimer >= 60)
            {
                Progression.AccrueRested(p, p.RestTickTimer);
                p.RestTickTimer = 0;
            }
            p.Bot?.Update(dt);
            if (p.Data.Id > 0 && !p.IsBot)
            {
                p.SaveTimer += dt;
                if (p.SaveTimer >= Config.AutosaveSeconds) { p.SaveTimer = 0; Save(p); }
            }
            else if (p.IsBot && p.Data.Id > 0)
            {
                p.SaveTimer += dt;
                if (p.SaveTimer >= Config.AutosaveSeconds * 5) { p.SaveTimer = 0; Save(p); }
            }
        }

        private void UpdateFlight(Player p, float dt)
        {
            p.FlightTime += dt;
            float t = Math.Min(1, p.FlightTime / p.FlightDuration);
            var pos = p.FlightFrom + (p.FlightTo - p.FlightFrom) * t;
            p.Zone!.SetPosition(p, new Vec3(pos.X, 40 * (float)Math.Sin(Math.PI * t), pos.Z));
            if (t >= 1)
            {
                p.FlightDuration = 0;
                p.Zone.SetPosition(p, p.FlightTo);
                p.Notice("Ihr seid angekommen.");
            }
        }

        // ================================================================== Spieler betreten/verlassen

        /// <summary>Bringt einen geladenen Charakter in die Welt (an seine gespeicherte Position).</summary>
        public Player EnterWorld(CharacterData data, Net.IPlayerConnection? connection)
        {
            var p = new Player(data, Content) { Connection = connection };
            p.EnsureBagSize();
            // Offline gesammelter Ruhebonus
            double offline = (DateTime.UtcNow - data.LastLogout).TotalSeconds;
            if (offline > 0)
            {
                int gain = Experience.RestedGain(p.Level, offline, data.LoggedOutInInn);
                p.Data.RestedXp = Math.Min(Experience.MaxRestedXp(p.Level), p.Data.RestedXp + gain);
            }
            string zoneId = data.ZoneId;
            var pos = data.Position;
            // In einer Instanz ausgeloggt und die Instanz besteht noch: dorthin zurück (Wiederverbindung).
            var rejoin = Content.Zones.TryGetValue(zoneId, out var zdef) && zdef.IsInstance ? Instances.Get(data.CurrentInstanceId) : null;
            if (rejoin != null && rejoin.Def.Id == zoneId)
            {
                AddToZone(p, rejoin, pos);
                SendFullState(p);
                if (p.IsDead) p.Send(new S_DeathState { State = p.DeathState, CorpsePosition = p.Data.CorpsePosition });
                return p;
            }
            if (!_zones.ContainsKey(zoneId))
            {
                // In einer Instanz ausgeloggt (oder Zone nicht mehr verfügbar): zurück an den Rückkehrpunkt.
                if (data.ReturnZone != null && _zones.ContainsKey(data.ReturnZone)) { zoneId = data.ReturnZone; pos = data.ReturnPosition; }
                else (zoneId, pos) = FactionHome(p.Faction);
            }
            AddToZone(p, _zones[zoneId], pos);
            SendFullState(p);
            if (p.IsDead) p.Send(new S_DeathState { State = p.DeathState, CorpsePosition = p.Data.CorpsePosition });
            Tutorial.OnEnterWorld(p);
            return p;
        }

        public void AddToZone(Player p, ZoneInstance zone, Vec3 pos)
        {
            p.Position = pos;
            zone.Add(p);
            _players[p.Id] = p;
            p.LastMoveTime = Time;
            p.CurrentArea = zone.AreaAt(pos);
            p.Data.ZoneId = zone.Def.Id;
            p.Data.CurrentInstanceId = zone.InstanceId;
            ReportProgress(p, zone.Def.IsInstance ? ProgressEvent.EnterDungeon : ProgressEvent.VisitZone, zone.Def.Id);
        }

        public void SendFullState(Player p)
        {
            if (p.Connection == null) return;
            p.Send(new S_EnterWorld { EntityId = p.Id, CharacterId = p.Data.Id, ZoneId = p.Zone!.Def.Id, Position = p.Position, Facing = p.Facing });
            p.Send(Mounts.BuildJournal(p));
            p.Send(Professions.Build(p));
            if (!p.IsBot)
            {
                Guilds.OnLogin(p);
                p.Send(Social.BuildList(p));
                Social.NotifyFriends(p, true);
                p.Send(new S_Settings { Json = p.Data.ClientSettings });
                p.Send(Chat.BuildChannels(p));
                p.Send(Achievements.Build(p));
                p.Send(new S_Exploration { Full = true, Areas = p.Data.DiscoveredAreas.OrderBy(a => a).ToList(), FlightNodes = p.Data.KnownTravelNodes.OrderBy(n => n).ToList() });
            }
            p.Send(p.BuildSpawn(p));
            p.Send(p.BuildPlayerState());
            p.Send(p.BuildSpellBook());
            p.Send(p.BuildTalents());
            p.Send(p.BuildInventory());
            p.Send(p.BuildQuestLog());
            p.Send(BuildWorldState(p.Zone));
            if (p.Group != null) p.Send(p.Group.BuildUpdate());
        }

        /// <summary>Spieler loggt aus: speichern, Begleiter und Bots entlassen, aus der Welt nehmen.</summary>
        public void Logout(Player p)
        {
            if (p.Removed && p.Zone == null) return;
            p.Data.LoggedOutInInn = p.CurrentArea != null && (p.CurrentArea.IsInn || p.CurrentArea.IsCity);
            p.Data.LastLogout = DateTime.UtcNow;
            Finder.Leave(p);
            Trades.Cancel(p, $"{p.Name} hat sich abgemeldet.");
            Tutorial.OnLogout(p);
            if (p.Group != null) Groups.Leave(p);
            Save(p);
            RemoveFromWorld(p);
            if (!p.IsBot) Social.NotifyFriends(p, false);
        }

        public void RemoveFromWorld(Player p)
        {
            if (p.Pet != null) { p.Pet.Zone?.Remove(p.Pet); p.Pet = null; }
            if (p.EscortNpc != null) { p.EscortNpc.Zone?.Remove(p.EscortNpc); p.EscortNpc = null; }
            p.Zone?.Remove(p);
            _players.Remove(p.Id);
        }

        public void Save(Player p)
        {
            if (Characters == null || p.Data.Id <= 0) return;
            p.SyncToData();
            SaveData(p.Data);
        }

        /// <summary>Speichert eine Momentaufnahme in Auftragsreihenfolge (nie ein älterer Stand über einem neueren).</summary>
        public void SaveData(CharacterData data)
        {
            if (Characters == null || Persistence == null || data.Id <= 0) return;
            var snapshot = data.Clone();
            var repo = Characters;
            Persistence.Enqueue(async () =>
            {
                try { await repo.SaveAsync(snapshot); }
                catch (Exception ex) { Post(() => Log($"Speichern von {snapshot.Name} fehlgeschlagen: {ex.Message}")); }
            });
        }

        public void SaveAll()
        {
            foreach (var p in _players.Values.ToList()) Save(p);
        }

        public Player? FindPlayer(string name) => _players.Values.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        public Player? GetPlayer(int entityId) => _players.TryGetValue(entityId, out var p) ? p : null;
        public bool NameTaken(string name) => FindPlayer(name) != null;

        // ================================================================== Bewegung

        /// <summary>Prüft eine Bewegungsmeldung des Clients: Tempo, Kontrolleffekte, Grenzen. Der Server hat das letzte Wort.</summary>
        public void HandleMove(Player p, C_Move m)
        {
            if (m.Sequence <= p.LastMoveSequence && p.LastMoveSequence - m.Sequence < 1_000_000) return;
            p.LastMoveSequence = m.Sequence;
            if (p.Zone == null || p.IsFlying || p.DeathState == DeathState.Dead || !p.CanMove || p.IsFeared)
            {
                p.LastMoveTime = Time;
                return;
            }
            double since = Math.Clamp(Time - p.LastMoveTime, 0.05, 1.0);
            float maxDist = (float)(p.MoveSpeed * since * 1.25 + 0.6);
            var target = m.Position;
            if (float.IsNaN(target.X) || float.IsNaN(target.Z) || float.IsNaN(target.Y)) return;
            float size = p.Zone.Def.Size;
            target = new Vec3(Math.Clamp(target.X, 0, size), Math.Clamp(target.Y, -200, 1000), Math.Clamp(target.Z, 0, size));
            float dist = Vec3.DistanceXZ(target, p.Position);
            if (dist > maxDist)
            {
                var dir = (target - p.Position).NormalizedXZ();
                target = new Vec3(p.Position.X + dir.X * maxDist, target.Y, p.Position.Z + dir.Z * maxDist);
            }
            p.LastMoveTime = Time;
            p.Facing = float.IsNaN(m.Facing) ? p.Facing : m.Facing;
            MoveUnit(p, target);
        }

        /// <summary>Bewegt eine Spielfigur (Mensch oder Bot) und wendet Bewegungsregeln an.</summary>
        public void MoveUnit(Player p, Vec3 target)
        {
            float moved = Vec3.DistanceXZ(target, p.Position);
            if (moved > 0.08f)
            {
                if (p.Cast != null) Combat.CancelCast(p, "Bewegt");
                if (p.Auras.Any(a => a.Def.BreaksOnMove)) p.RemoveAuras(a => a.Def.BreaksOnMove);
            }
            p.Zone!.SetPosition(p, target);
        }

        public void MovePlayerWithinZone(Player p, Vec3 pos)
        {
            if (p.Zone == null) return;
            p.Zone.SetPosition(p, pos);
            p.LastMoveTime = Time;
            if (p.Cast != null) Combat.CancelCast(p, "Teleport");
            if (p.Connection != null) p.Send(new S_EnterWorld { EntityId = p.Id, CharacterId = p.Data.Id, ZoneId = p.Zone.Def.Id, Position = pos, Facing = p.Facing });
        }

        // ================================================================== Zonenwechsel & Instanzen

        /// <summary>Wechselt die Zone. Für Instanzen wird die Instanz der Gruppe (oder des Spielers) verwendet bzw. erstellt.</summary>
        public void Transfer(Player p, string zoneId, Vec3 pos, string? instanceKey = null)
        {
            if (!Content.Zones.TryGetValue(zoneId, out var def)) return;
            if (def.IsInstance && p.IsMounted) Mounts.Dismount(p);
            ZoneInstance target;
            if (def.IsInstance)
            {
                long explicitId = instanceKey != null && instanceKey.StartsWith("i") && long.TryParse(instanceKey.Substring(1), out var iid) ? iid : 0;
                var resolved = Instances.Resolve(p, def, explicitId, out var instErr);
                if (resolved == null) { p.Notice(instErr ?? "Instanz nicht verfügbar.", true); return; }
                target = resolved;
                if (target.Players.Count(x => !x.IsBot) >= Math.Max(1, def.MaxPlayers) && !p.IsBot) { p.Notice("Diese Instanz ist voll.", true); return; }
                if (p.Zone != null && !p.Zone.IsInstance)
                {
                    p.Data.ReturnZone = p.Zone.Def.Id;
                    p.Data.ReturnPosition = p.Position;
                }
            }
            else
            {
                if (!_zones.TryGetValue(zoneId, out target!)) { p.Notice("Diese Region ist noch nicht erschlossen.", true); return; }
            }
            if (p.Zone == target) { MovePlayerWithinZone(p, pos); return; }
            if (p.Cast != null) Combat.CancelCast(p, "Teleport");
            var pet = p.Pet;
            p.Zone?.Detach(p);
            AddToZone(p, target, pos);
            if (pet != null)
            {
                pet.Zone?.Remove(pet);
                p.Pet = null;
                SummonPet(p, pet.Template.Id);
            }
            SendFullState(p);
            if (!p.IsBot && p.Group != null)
                foreach (var b in p.Group.Bots.Where(b => b.Zone != target))
                    Transfer(b, zoneId, pos + new Vec3(Random.Range(-2, 2), 0, Random.Range(-2, 2)), target.InstanceKey);
        }

        /// <summary>Spezialziele: @bind (Ruhestein), @home (Fraktionsheimat), @return (aus Instanz zurück).</summary>
        public void TeleportSpecial(Player p, string spec, Vec3 pos)
        {
            switch (spec)
            {
                case "@bind":
                    if (string.IsNullOrEmpty(p.Data.BindZone)) { var (z, hp) = FactionHome(p.Faction); Transfer(p, z, hp); }
                    else Transfer(p, p.Data.BindZone, p.Data.BindPosition);
                    break;
                case "@home":
                {
                    var (z, hp) = FactionHome(p.Faction);
                    Transfer(p, z, hp);
                    break;
                }
                case "@return":
                {
                    var def = p.Zone?.Def;
                    if (p.Data.ReturnZone != null && _zones.ContainsKey(p.Data.ReturnZone)) Transfer(p, p.Data.ReturnZone, p.Data.ReturnPosition);
                    else if (def?.ExitZone != null) Transfer(p, def.ExitZone, def.ExitPosition);
                    else { var (z, hp) = FactionHome(p.Faction); Transfer(p, z, hp); }
                    break;
                }
                default:
                    Transfer(p, spec, pos);
                    break;
            }
        }

        /// <summary>Gasthaus des Fraktionsstartgebiets (Ziel für Ruhestein ohne Bindung und Magier-Teleport).</summary>
        public (string zone, Vec3 pos) FactionHome(Faction f)
        {
            var zone = _zones.Values.Select(z => z.Def).FirstOrDefault(z => z.Faction == f) ?? _zones.Values.First().Def;
            var inn = zone.Areas.FirstOrDefault(a => a.IsInn) ?? zone.Areas.First();
            return (zone.Id, inn.Center);
        }

        public void SummonPet(Player owner, string templateId)
        {
            if (owner.Zone == null) return;
            if (owner.Pet != null) { owner.Pet.Zone?.Remove(owner.Pet); owner.Pet = null; }
            var def = Content.Creatures[templateId];
            var pet = new Creature(def, owner.Level) { Owner = owner, IsPet = true, Home = owner.Position, Position = owner.Position + new Vec3(1.5f, 0, -1.5f), Faction = Faction.Friendly };
            pet.Name = $"{def.Name} von {owner.Name}";
            pet.MarkStatsDirty();
            pet.EnsureStats();
            pet.Health = pet.MaxHealth;
            pet.Power = pet.MaxPower;
            owner.Zone.Add(pet);
            owner.Pet = pet;
        }

        public S_WorldState BuildWorldState(ZoneInstance z) => new() { GameHour = GameHour, Weather = z.Weather, WeatherIntensity = z.WeatherIntensity };

        // ================================================================== Charaktererstellung

        /// <summary>Neue Figur mit Startausrüstung, Startfähigkeiten und Startposition ihres Volkes.</summary>
        public CharacterData CreateCharacterTemplate(string name, Race race, CharClass cls, Gender gender, AppearanceDto appearance)
        {
            var r = Content.Races[race];
            var c = Content.Classes[cls];
            var data = new CharacterData
            {
                Name = name, Race = race, Class = cls, Gender = gender, Appearance = appearance, Level = 1, ZoneId = r.StartZone,
                Position = r.StartPosition, Facing = r.StartFacing, Money = 0,
            };
            foreach (var a in c.StartingAbilities) data.Abilities[a] = 1;
            foreach (var t in r.Traits) if (t.Ability != null) data.Abilities[t.Ability] = 1;
            for (int i = 0; i < Player.BagSize; i++) data.Bag.Add(null);
            var tmp = new Player(data, Content);
            foreach (var itemId in c.StartingItems)
            {
                var item = Content.Items[itemId];
                if (item.IsEquippable && tmp.CanEquip(item) == null && !data.Equipment.ContainsKey(tmp.SlotFor(item)))
                    data.Equipment[tmp.SlotFor(item)] = new ItemStack { ItemId = itemId, Count = 1, Durability = item.MaxDurability };
                else tmp.AddItem(itemId, item.Category == ItemCategory.Consumable ? 5 : 1);
            }
            var (homeZone, homePos) = FactionHome(r.Faction);
            data.BindZone = homeZone;
            data.BindPosition = homePos;
            return data;
        }
    }
}
