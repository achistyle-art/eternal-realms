using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Instanzen (Kapitel 10/11): eindeutige IDs, Bindungen, Schlachtzugsperren mit festem Reset, Zurücksetzen von
    /// Dungeons durch den Anführer, höchstens 10 neue Instanzen pro Stunde und Charakter, Wiederverbindung in dieselbe
    /// Instanz. Heroische Dungeons sperren wie Schlachtzüge, aber täglich. Besiegte Bosse stehen in den Bindungen der Charaktere – eine Schlachtzugsperre übersteht so auch einen
    /// Serverneustart.
    /// </summary>
    public sealed class InstanceSystem
    {
        public const int MaxNewPerHour = 10;
        public const float EmptyLifetime = 30 * 60;
        /// <summary>Bezugspunkt der Resets: Mittwoch, 1.1.2025 um 05:00 UTC.</summary>
        public static readonly DateTime ResetAnchor = new(2025, 1, 1, 5, 0, 0, DateTimeKind.Utc);

        private readonly GameWorld _w;
        private readonly Dictionary<long, ZoneInstance> _byId = new();
        /// <summary>Instanz eines Charakters ohne Gruppe je Zone (wie die Instanz-ID einer Gruppe).</summary>
        private readonly Dictionary<(long character, string zone), long> _personal = new();
        private long _nextId;

        public InstanceSystem(GameWorld world)
        {
            _w = world;
            _nextId = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * 1000; // eindeutig auch über Neustarts hinweg
        }

        public IEnumerable<ZoneInstance> All => _byId.Values;
        public ZoneInstance? Get(long id) => _byId.TryGetValue(id, out var z) ? z : null;

        public static DateTime NextReset(int days, DateTime now)
        {
            var period = TimeSpan.FromDays(Math.Max(1, days));
            long n = (long)Math.Floor((now - ResetAnchor).TotalSeconds / period.TotalSeconds) + 1;
            return ResetAnchor + TimeSpan.FromSeconds(period.TotalSeconds * n);
        }

        private InstanceBind? ValidBind(Player p, ZoneDef def)
        {
            if (!p.Data.InstanceBinds.TryGetValue(def.Id, out var b)) return null;
            if (b.ExpiresAt is DateTime until && until <= DateTime.UtcNow) { p.Data.InstanceBinds.Remove(def.Id); return null; }
            // Dungeonbindungen gelten nur, solange die Instanz besteht.
            if (b.ExpiresAt == null && !_byId.ContainsKey(b.InstanceId)) { p.Data.InstanceBinds.Remove(def.Id); return null; }
            return b;
        }

        /// <summary>Instanz für einen Spieler finden oder anlegen. explicitId: Bots folgen ihrem Anführer in dessen Instanz.</summary>
        public ZoneInstance? Resolve(Player p, ZoneDef def, long explicitId, out string? error)
        {
            error = null;
            if (explicitId != 0 && _byId.TryGetValue(explicitId, out var exact)) return exact;
            var bind = ValidBind(p, def);
            long groupId = 0;
            if (p.Group != null && p.Group.InstanceIds.TryGetValue(def.Id, out var gid) && (_byId.ContainsKey(gid) || def.HasLockout)) groupId = gid;
            else if (p.Group == null && _personal.TryGetValue((p.Data.Id, def.Id), out var pid) && _byId.ContainsKey(pid)) groupId = pid;
            // Der Anführer bestimmt die Instanz der Gruppe; Gebundene dürfen nur in ihre eigene.
            if (bind != null && groupId != 0 && bind.InstanceId != groupId)
            {
                error = $"Ihr seid an eine andere Instanz von {def.Name} gebunden.";
                return null;
            }
            long id = bind?.InstanceId ?? groupId;
            if (id != 0 && _byId.TryGetValue(id, out var existing)) return Remember(p, def, existing);
            if (id == 0)
            {
                if (!p.IsBot)
                {
                    p.Data.RecentInstances.RemoveAll(t => t < DateTime.UtcNow.AddHours(-1));
                    if (p.Data.RecentInstances.Count >= MaxNewPerHour) { error = "Ihr habt zu viele Instanzen betreten. Wartet ein wenig."; return null; }
                    p.Data.RecentInstances.Add(DateTime.UtcNow);
                }
                id = ++_nextId;
            }
            var z = new ZoneInstance(_w, def, "i" + id) { InstanceId = id };
            // Aus einer Schlachtzugbindung wiederhergestellt: besiegte Bosse und Sperre übernehmen.
            if (bind != null) { foreach (var b in bind.KilledBosses) z.KilledBosses.Add(b); z.LockoutUntil = bind.ExpiresAt; }
            z.Populate();
            _byId[id] = z;
            return Remember(p, def, z);
        }

        private ZoneInstance Remember(Player p, ZoneDef def, ZoneInstance z)
        {
            if (p.Group != null && (p.Group.Leader == p || !p.Group.InstanceIds.ContainsKey(def.Id))) p.Group.InstanceIds[def.Id] = z.InstanceId;
            if (p.Group == null && !p.IsBot) _personal[(p.Data.Id, def.Id)] = z.InstanceId;
            return z;
        }

        /// <summary>Boss besiegt: alle Anwesenden binden; bei Schlachtzügen mit Sperre bis zum nächsten Reset.</summary>
        public void OnBossKilled(Creature boss)
        {
            var z = boss.Zone;
            if (z == null || !z.IsInstance || z.InstanceId == 0) return;
            z.KilledBosses.Add(boss.Template.Id);
            if (z.Def.HasLockout && z.LockoutUntil == null) z.LockoutUntil = NextReset(z.Def.LockoutDays > 0 ? z.Def.LockoutDays : 7, DateTime.UtcNow);
            foreach (var p in z.Players.ToList())
            {
                bool fresh = !p.Data.InstanceBinds.TryGetValue(z.Def.Id, out var b) || b.InstanceId != z.InstanceId;
                if (fresh) b = p.Data.InstanceBinds[z.Def.Id] = new InstanceBind { InstanceId = z.InstanceId };
                b!.ExpiresAt = z.LockoutUntil;
                b.KilledBosses = z.KilledBosses.ToList();
                if (fresh && !p.IsBot)
                    p.Notice(z.LockoutUntil is DateTime u ? $"Ihr seid an diese Instanz gebunden bis {u:dd.MM. HH:mm} UTC." : "Ihr seid an diese Instanz gebunden.");
            }
            int total = z.Def.Spawns.Select(s => s.Creature).Distinct().Count(id => _w.Content.Creatures.TryGetValue(id, out var c) && c.Boss);
            if (total > 0 && z.KilledBosses.Count(k => _w.Content.Creatures.TryGetValue(k, out var c) && c.Boss) >= total)
                foreach (var p in z.Players.ToList())
                {
                    if (!p.IsBot) p.Notice($"{z.Def.Name} abgeschlossen!");
                    p.Bot?.RecordShared($"{z.Def.Name} abgeschlossen", 5);
                }
        }

        /// <summary>Anführer setzt die Dungeons der Gruppe zurück (nicht Instanzen mit Sperre, nicht solange jemand drin ist).</summary>
        public string? Reset(Player p)
        {
            var g = p.Group;
            if (g != null && g.Leader != p) return "Nur der Gruppenanführer kann Instanzen zurücksetzen.";
            var ids = g != null ? g.InstanceIds.ToList()
                : _personal.Where(kv => kv.Key.character == p.Data.Id).Select(kv => new KeyValuePair<string, long>(kv.Key.zone, kv.Value)).ToList();
            int reset = 0;
            foreach (var (zoneId, id) in ids)
            {
                if (!_w.Content.Zones.TryGetValue(zoneId, out var def) || def.HasLockout) continue;
                if (_byId.TryGetValue(id, out var z))
                {
                    if (z.Players.Any(x => !x.IsBot)) { p.Notice($"{def.Name} kann nicht zurückgesetzt werden – es sind noch Spieler darin.", true); continue; }
                    foreach (var bot in z.Players.ToList()) bot.Group?.Leader.Notice($"{bot.Name} verlässt {def.Name}.");
                    _byId.Remove(id);
                }
                g?.InstanceIds.Remove(zoneId);
                _personal.Remove((p.Data.Id, zoneId));
                foreach (var m in g?.Members ?? new List<Player> { p })
                    if (m.Data.InstanceBinds.TryGetValue(zoneId, out var b) && b.InstanceId == id) m.Data.InstanceBinds.Remove(zoneId);
                reset++;
                (g?.Members.Where(m => !m.IsBot) ?? new[] { p }).ToList().ForEach(m => m.Notice($"{def.Name} wurde zurückgesetzt."));
            }
            return reset == 0 ? "Es gibt keine Instanzen zum Zurücksetzen." : null;
        }

        public void Update(float dt)
        {
            foreach (var z in _byId.Values.ToList())
            {
                z.Update(dt);
                if (z.Players.Count == 0)
                {
                    z.EmptyTimer += dt;
                    // Leere Dungeons verfallen nach 30 Minuten; Schlachtzüge leben über die Bindungen weiter.
                    if (z.EmptyTimer > EmptyLifetime)
                    {
                        _byId.Remove(z.InstanceId);
                        foreach (var k in _personal.Where(kv => kv.Value == z.InstanceId).Select(kv => kv.Key).ToList()) _personal.Remove(k);
                    }
                }
                else z.EmptyTimer = 0;
            }
        }

        public S_InstanceInfo BuildInfo(Player p)
        {
            var info = new S_InstanceInfo();
            foreach (var (zoneId, b) in p.Data.InstanceBinds.ToList())
            {
                if (!_w.Content.Zones.TryGetValue(zoneId, out var def) || ValidBind(p, def) == null) continue;
                int total = def.Spawns.Select(s => s.Creature).Distinct().Count(id => _w.Content.Creatures.TryGetValue(id, out var c) && c.Boss);
                info.Binds.Add(new InstanceBindData
                {
                    ZoneId = zoneId, Name = def.Name, InstanceId = b.InstanceId, BossesKilled = b.KilledBosses.Count, BossesTotal = total,
                    HoursLeft = b.ExpiresAt is DateTime u ? (float)(u - DateTime.UtcNow).TotalHours : -1,
                });
            }
            return info;
        }
    }
}
