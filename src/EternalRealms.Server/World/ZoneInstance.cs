using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.World
{
    /// <summary>
    /// Eine laufende Zone: offene Weltregion oder Dungeon-Instanz. Jede Zone simuliert unabhängig und
    /// kann später in einen eigenen Prozess ausgelagert werden (siehe docs/ARCHITEKTUR.md).
    /// </summary>
    public enum SimulationLevel : byte { Full, Reduced, Dormant }

    public sealed class ZoneInstance
    {
        public const float ViewRange = 110f;

        public ZoneDef Def { get; }
        public GameWorld World { get; }
        /// <summary>Für Instanzen: Schlüssel der Gruppe/des Spielers. Für die offene Welt leer.</summary>
        public string InstanceKey { get; }
        public Dictionary<int, Entity> Entities { get; } = new();
        public List<Player> Players { get; } = new();
        public List<SpawnPoint> SpawnPoints { get; } = new();
        public List<GameObjectEntity> Objects { get; } = new();
        public SpatialGrid Grid { get; } = new();
        public Weather Weather { get; set; } = Weather.Clear;
        public float WeatherIntensity { get; set; }
        public float WeatherTimer { get; set; }
        public float EmptyTimer { get; set; }

        private float _visibilityTimer, _snapshotTimer;
        private readonly List<Entity> _pendingRemove = new();
        private uint _tick;

        /// <summary>Eindeutige Instanz-ID (0 = offene Welt).</summary>
        public long InstanceId { get; set; }
        /// <summary>In dieser Instanz bereits besiegte Bosse (überdauert über die Bindungen auch einen Neustart).</summary>
        public HashSet<string> KilledBosses { get; } = new();
        public DateTime? LockoutUntil { get; set; }

        public ZoneInstance(GameWorld world, ZoneDef def, string instanceKey = "")
        {
            World = world;
            Def = def;
            InstanceKey = instanceKey;
            if (def.Weather.Count > 0) Weather = def.Weather[0];
        }

        public bool IsInstance => Def.IsInstance;
        public IEnumerable<Unit> Units => Entities.Values.OfType<Unit>();

        // ------------------------------------------------------------ Aufbau

        public void Populate()
        {
            // Jede Instanz derselben Zone hat dieselbe Aufstellung (reproduzierbar, unabhängig von der Instanz-ID).
            var rng = new Random(StableHash.Of(Def.Id));
            foreach (var s in Def.Spawns)
            {
                if (KilledBosses.Contains(s.Creature)) continue; // besiegte Bosse bleiben besiegt
                for (int i = 0; i < s.Count; i++)
                {
                    var pos = s.Position;
                    if (s.Radius > 0)
                    {
                        double ang = rng.NextDouble() * Math.PI * 2, dist = Math.Sqrt(rng.NextDouble()) * s.Radius;
                        pos = new Vec3(pos.X + (float)(Math.Cos(ang) * dist), pos.Y, pos.Z + (float)(Math.Sin(ang) * dist));
                    }
                    var sp = new SpawnPoint(s, pos, s.Facing + (float)rng.NextDouble() * 0.5f, SpawnPoints.Count, Def.Id);
                    SpawnPoints.Add(sp);
                    if (EnvironmentRules.TimeMatches(s.TimeOfDay, World.GameHour)) SpawnCreatureAt(sp, initial: true);
                }
            }
            foreach (var n in Def.Npcs)
            {
                var def = World.Content.Creatures[n.Npc];
                var c = new Creature(def, def.MinLevel) { Home = n.Position, HomeFacing = n.Facing, Position = n.Position, Facing = n.Facing };
                if (n.Patrol.Count > 0) c.AI.Patrol = n.Patrol;
                Add(c);
            }
            foreach (var o in Def.Objects)
            {
                var def = World.Content.Objects[o.Object];
                for (int i = 0; i < o.Count; i++)
                {
                    var pos = o.Position;
                    if (o.Radius > 0)
                    {
                        double ang = rng.NextDouble() * Math.PI * 2, dist = Math.Sqrt(rng.NextDouble()) * o.Radius;
                        pos = new Vec3(pos.X + (float)(Math.Cos(ang) * dist), pos.Y, pos.Z + (float)(Math.Sin(ang) * dist));
                    }
                    var go = new GameObjectEntity(def) { Position = pos, RespawnSeconds = o.RespawnSeconds };
                    Objects.Add(go);
                    Add(go);
                }
            }
        }

        /// <param name="initial">Erstbesetzung beim Start: gespeicherte Wiederkehrzeiten bleiben unangetastet (ApplySpawnTimers prüft sie danach).</param>
        public Creature SpawnCreatureAt(SpawnPoint sp, bool initial = false)
        {
            // Platzhalter: gelegentlich erscheint stattdessen der Seltene – aber nie zweimal zugleich
            string id = SpawnRules.PickCreature(sp.Def, World.Random,
                sp.Def.RareCreature != null && Entities.Values.OfType<Creature>().Any(c => c.Template.Id == sp.Def.RareCreature && c.IsAlive));
            var def = World.Content.Creatures[id];
            int level = World.Random.Range(def.MinLevel, def.MaxLevel);
            var c = new Creature(def, level) { Home = sp.Position, HomeFacing = sp.Facing, Position = sp.Position, Facing = sp.Facing, Spawn = sp };
            if (sp.Def.Patrol.Count > 0) c.AI.Patrol = sp.Def.Patrol;
            c.AI.WanderRadius = sp.Def.WanderRadius;
            sp.Current = c;
            Add(c);
            if (sp.Def.Persistent && !initial) World.SaveSpawnTimer(sp.Key, null);
            if (sp.Def.Announce.Length > 0 && !initial) BroadcastAll(new S_Chat { Channel = ChatChannel.System, Text = sp.Def.Announce.Replace("{name}", def.Name) });
            return c;
        }

        /// <summary>Kreatur eines Spawnpunkts ist gestorben oder verschwunden: Wiederkehr nach den Spawnregeln planen.</summary>
        public void OnSpawnDied(SpawnPoint sp, Creature c)
        {
            bool special = c.Template.Rare || c.Template.Boss || c.Template.WorldBoss || sp.Def.Persistent;
            sp.RespawnTimer = SpawnRules.RespawnDelay(sp.Def, World.Random, PlayersInRange(sp.Position, 60).Count(p => !p.IsBot || p.Group != null), special);
            sp.Current = null;
            if (sp.Def.Persistent) World.SaveSpawnTimer(sp.Key, DateTime.UtcNow.AddSeconds(sp.RespawnTimer));
        }

        /// <summary>Tag- oder Nachtkreaturen ohne Kampf verschwinden, wenn ihre Zeit vorbei ist, und kehren zur richtigen Zeit zurück.</summary>
        private void CheckTimeOfDay(SpawnPoint sp)
        {
            if (sp.Def.TimeOfDay.Length == 0 || sp.Current == null || sp.Current.Removed) return;
            if (EnvironmentRules.TimeMatches(sp.Def.TimeOfDay, World.GameHour) || sp.Current.InCombat || sp.Current.IsDead) return;
            Remove(sp.Current);
            sp.Current = null;
            sp.RespawnTimer = 0;
        }

        /// <summary>Erzeugt eine Kreatur außerhalb fester Spawnpunkte (Boss-Adds, Hinterhalte, Begleiter).</summary>
        public Creature SpawnCreature(string templateId, Vec3 pos, int? level = null)
        {
            var def = World.Content.Creatures[templateId];
            var c = new Creature(def, level ?? World.Random.Range(def.MinLevel, def.MaxLevel)) { Home = pos, Position = pos, IsSummoned = true };
            Add(c);
            return c;
        }

        // ------------------------------------------------------------ Verwaltung

        public void Add(Entity e)
        {
            if (e.Id == 0) e.Id = World.NextEntityId();
            e.Zone = this;
            e.Removed = false;
            Entities[e.Id] = e;
            Grid.Insert(e);
            if (e is Player p && !Players.Contains(p)) Players.Add(p);
        }

        /// <summary>Entfernt am Ende des Ticks (sicher während Iterationen).</summary>
        public void Remove(Entity e)
        {
            if (e.Removed) return;
            e.Removed = true;
            _pendingRemove.Add(e);
        }

        private void FlushRemovals()
        {
            foreach (var e in _pendingRemove)
            {
                Entities.Remove(e.Id);
                Grid.Remove(e);
                if (e is Player p) Players.Remove(p);
                foreach (var viewer in Players)
                    if (viewer.VisibleEntities.Remove(e.Id)) viewer.Send(new S_EntityDespawn { Id = e.Id });
                if (e is Unit u)
                {
                    u.ClearCombat();
                    foreach (var other in Units) if (other.Target == u) { other.Target = null; other.AutoAttacking = false; }
                    foreach (var c in Entities.Values.OfType<Creature>()) c.Threat.Remove(u);
                }
                e.Zone = null;
            }
            _pendingRemove.Clear();
        }

        /// <summary>Sofortiges Herauslösen (Zonenwechsel eines Spielers).</summary>
        public void Detach(Player p)
        {
            Entities.Remove(p.Id);
            Grid.Remove(p);
            Players.Remove(p);
            p.ClearCombat();
            foreach (var other in Units) if (other.Target == p) { other.Target = null; other.AutoAttacking = false; }
            foreach (var c in Entities.Values.OfType<Creature>()) c.Threat.Remove(p);
            foreach (var viewer in Players)
                if (viewer.VisibleEntities.Remove(p.Id)) viewer.Send(new S_EntityDespawn { Id = p.Id });
            foreach (var id in p.VisibleEntities) p.Send(new S_EntityDespawn { Id = id });
            p.VisibleEntities.Clear();
            p.Zone = null;
        }

        public void SetPosition(Entity e, Vec3 pos)
        {
            e.Position = pos;
            Grid.Moved(e);
        }

        public Entity? Get(int id) => Entities.TryGetValue(id, out var e) && !e.Removed ? e : null;
        public Unit? GetUnit(int id) => Get(id) as Unit;

        public List<Unit> UnitsInRange(Vec3 center, float radius) => Grid.Query(center, radius).OfType<Unit>().Where(u => !u.Removed).ToList();
        public List<Player> PlayersInRange(Vec3 center, float radius) => Grid.Query(center, radius).OfType<Player>().Where(u => !u.Removed).ToList();

        public AreaDef? AreaAt(Vec3 pos)
        {
            AreaDef? best = null;
            float bestR = float.MaxValue;
            foreach (var a in Def.Areas)
                if (Vec3.DistanceXZ(a.Center, pos) <= a.Radius && a.Radius < bestR) { best = a; bestR = a.Radius; }
            return best;
        }

        public Vec3 NearestGraveyard(Vec3 pos)
        {
            if (Def.Graveyards.Count == 0) return Def.InstanceEntry;
            return Def.Graveyards.OrderBy(g => Vec3.DistanceXZ(g, pos)).First();
        }

        // ------------------------------------------------------------ Nachrichten

        /// <summary>An alle Spieler, die die Quelle sehen (inklusive der Quelle selbst, wenn sie ein Spieler ist).</summary>
        public void BroadcastFrom(Entity source, Message m)
        {
            foreach (var p in Players)
                if (p == source || p.VisibleEntities.Contains(source.Id)) p.Send(m);
        }

        public void BroadcastInRange(Vec3 pos, float range, Message m)
        {
            foreach (var p in Players)
                if (Vec3.DistanceXZ(p.Position, pos) <= range) p.Send(m);
        }

        public void BroadcastAll(Message m)
        {
            foreach (var p in Players) p.Send(m);
        }

        // ------------------------------------------------------------ Simulation

        // ================================================================== Simulationsstufen (Kapitel 9.4)

        /// <summary>Voll (Menschen in der Zone), Reduziert (5 Schritte/s) oder Ruhend (alle 30 s Hintergrundfortschritt).</summary>
        public SimulationLevel Lod { get; private set; } = SimulationLevel.Full;
        public const float ReducedStep = 0.2f, DormantAfter = 300f, DormantStep = 30f;
        private float _noHumanTime, _lodAccum;

        /// <summary>
        /// Ein Weltschritt mit Detailstufe: Ohne menschliche Spieler läuft die Zone gröber, nach fünf Minuten ruht sie und
        /// schreitet nur noch ereignisbasiert voran (Bots sammeln Erfahrung, Gegner erscheinen neu). Betritt ein Mensch
        /// die Zone, läuft sie sofort wieder voll.
        /// </summary>
        public void Tick(float dt)
        {
            bool humans = IsInstance || !World.Config.ZoneLod || Players.Any(p => !p.IsBot);
            if (humans)
            {
                if (Lod != SimulationLevel.Full) { Lod = SimulationLevel.Full; _lodAccum = 0; }
                _noHumanTime = 0;
                Update(dt);
                return;
            }
            _noHumanTime += dt;
            _lodAccum += dt;
            if (_noHumanTime < DormantAfter)
            {
                Lod = SimulationLevel.Reduced;
                if (_lodAccum < ReducedStep) return;
                float step = _lodAccum;
                _lodAccum = 0;
                Update(step);
                return;
            }
            if (Lod != SimulationLevel.Dormant) { Lod = SimulationLevel.Dormant; _lodAccum = 0; return; }
            if (_lodAccum < DormantStep) return;
            float elapsed = _lodAccum;
            _lodAccum = 0;
            BackgroundStep(elapsed);
        }

        /// <summary>Ereignisbasierter Fortschritt einer ruhenden Zone.</summary>
        private void BackgroundStep(float elapsed)
        {
            foreach (var sp in SpawnPoints)
            {
                CheckTimeOfDay(sp);
                if (sp.Current != null && !sp.Current.Removed) continue;
                // Leiche anderweitig entfernt (gehäutet, Zone geleert): Wiederkehr trotzdem nach den Regeln
                if (sp.Current != null && sp.Current.IsDead) OnSpawnDied(sp, sp.Current);
                sp.Current = null;
                sp.RespawnTimer -= elapsed;
                if (sp.RespawnTimer <= 0 && EnvironmentRules.TimeMatches(sp.Def.TimeOfDay, World.GameHour)) SpawnCreatureAt(sp);
            }
            foreach (var go in Objects.Where(o => !o.Available))
            {
                go.RespawnTimer -= elapsed;
                if (go.RespawnTimer <= 0) go.Available = true;
            }
            foreach (var b in Players.Where(p => p.IsBot).ToList()) World.Bots.BackgroundProgress(b, elapsed);
            FlushRemovals();
        }

        public void Update(float dt)
        {
            _tick++;
            double now = World.Time;
            foreach (var e in Entities.Values.ToList())
            {
                if (e.Removed) continue;
                switch (e)
                {
                    case Player p:
                        World.UpdatePlayer(p, dt);
                        break;
                    case Creature c:
                        c.UpdateTimers(dt, now);
                        World.Combat.UpdateUnit(c, dt);
                        c.Threat.Update(dt);
                        c.AI.Update(dt);
                        break;
                    case GameObjectEntity go when !go.Available:
                        go.RespawnTimer -= dt;
                        if (go.RespawnTimer <= 0) go.Available = true;
                        break;
                }
            }
            foreach (var sp in SpawnPoints)
            {
                if (_tick % 20 == sp.Index % 20) CheckTimeOfDay(sp);
                if (sp.Current != null && !sp.Current.Removed) continue;
                // Leiche anderweitig entfernt (gehäutet, Zone geleert): Wiederkehr trotzdem nach den Regeln
                if (sp.Current != null && sp.Current.IsDead) OnSpawnDied(sp, sp.Current);
                sp.Current = null;
                sp.RespawnTimer -= dt;
                if (sp.RespawnTimer <= 0 && EnvironmentRules.TimeMatches(sp.Def.TimeOfDay, World.GameHour))
                {
                    // Nicht vor den Augen eines Spielers in direkter Nähe erscheinen.
                    if (PlayersInRange(sp.Position, 8).Any(p => p.IsAlive)) { sp.RespawnTimer = 2; continue; }
                    SpawnCreatureAt(sp);
                }
            }
            UpdateWeather(dt);
            FlushRemovals();

            _visibilityTimer -= dt;
            if (_visibilityTimer <= 0) { _visibilityTimer = 0.25f; UpdateVisibility(); }
            _snapshotTimer -= dt;
            if (_snapshotTimer <= 0) { _snapshotTimer = 0.1f; SendSnapshots(); }
            foreach (var u in Units)
                if (u.AurasChanged)
                {
                    u.AurasChanged = false;
                    BroadcastFrom(u, new S_AuraUpdate { TargetId = u.Id, Auras = u.Auras.Select(a => a.ToNet()).ToList() });
                }
        }

        public void SetWeather(Weather w, float intensity)
        {
            Weather = w;
            WeatherIntensity = w == Weather.Clear ? 0 : intensity;
            WeatherTimer = 600;
            BroadcastAll(World.BuildWorldState(this));
        }

        private void UpdateWeather(float dt)
        {
            WeatherTimer -= dt;
            if (WeatherTimer > 0 || Def.Weather.Count == 0) return;
            WeatherTimer = 600 + World.Random.Range(0, 600);
            var next = Def.Weather[World.Random.Range(0, Def.Weather.Count - 1)];
            if (next == Weather) return;
            Weather = next;
            WeatherIntensity = next == Weather.Clear ? 0 : 0.3f + (float)World.Random.NextDouble() * 0.7f;
            BroadcastAll(World.BuildWorldState(this));
        }

        private void UpdateVisibility()
        {
            foreach (var p in Players)
            {
                if (p.Connection == null) continue;
                var near = new HashSet<int>();
                foreach (var e in Grid.Query(p.Position, ViewRange))
                {
                    if (e == p || e.Removed) continue;
                    if (e is Unit u && u.IsStealthed && u.IsHostileTo(p) && p.DistanceTo(u) > 4) continue;
                    near.Add(e.Id);
                    if (p.VisibleEntities.Add(e.Id)) p.Send(e.BuildSpawn(p));
                }
                foreach (var id in p.VisibleEntities.Where(id => !near.Contains(id)).ToList())
                {
                    p.VisibleEntities.Remove(id);
                    p.Send(new S_EntityDespawn { Id = id });
                }
            }
        }

        /// <summary>Höchstens so viele Einheiten pro Paket: unzuverlässige UDP-Pakete dürfen die MTU (~1400 Byte) nicht überschreiten.</summary>
        /// <summary>
        /// Einheiten je Snapshot-Paket: Kopf (Opcode, Tick, Bewegung, Anzahl ≈ 14 Byte) + 44 Byte je Einheit müssen unter
        /// der kleinsten UDP-Nutzlast von LiteNetLib (1023 Byte vor der MTU-Erkennung) bleiben → 20 Einheiten ≈ 894 Byte.
        /// </summary>
        public const int SnapshotChunk = 20;

        private void SendSnapshots()
        {
            foreach (var p in Players)
            {
                if (p.Connection == null) continue;
                var snap = new S_Snapshot { Tick = _tick, LastProcessedMove = p.LastMoveSequence };
                snap.Entities.Add(p.BuildSnapshot());
                foreach (var id in p.VisibleEntities)
                {
                    if (!Entities.TryGetValue(id, out var e)) continue;
                    snap.Entities.Add(e.BuildSnapshot());
                    if (snap.Entities.Count >= SnapshotChunk)
                    {
                        p.Connection.SendUnreliable(snap);
                        snap = new S_Snapshot { Tick = _tick, LastProcessedMove = p.LastMoveSequence };
                    }
                }
                if (snap.Entities.Count > 0) p.Connection.SendUnreliable(snap);
            }
        }
    }
}
