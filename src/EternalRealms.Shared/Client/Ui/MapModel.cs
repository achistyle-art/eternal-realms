using System;
using System.Collections.Generic;
using System.Linq;
using EternalRealms.Shared.Content;

namespace EternalRealms.Shared.Client.Ui
{
    public enum MapMarkerKind : byte
    {
        Player, GroupMember, QuestAvailable, QuestTurnIn, QuestIncomplete, Vendor, Trainer, FlightMaster, Innkeeper, Banker, Auctioneer,
        Mailbox, Herb, Ore, FishPool, Station, DungeonEntrance, Graveyard, Corpse, QuestArea, WorldEvent,
    }

    public struct MapMarker
    {
        public MapMarkerKind Kind;
        public string Label;
        /// <summary>Position in Weltkoordinaten (X/Z).</summary>
        public float X, Z;
        public int EntityId;
    }

    /// <summary>
    /// Minikarte und Weltkarte (Kapitel 30): Markierungen aus dem Clientzustand und den Inhalten, Umrechnung von Welt- in
    /// Kartenkoordinaten, entdeckte und verdeckte Gebiete. Unity zeichnet nur noch, was dieses Modell liefert.
    /// </summary>
    public sealed class MapModel
    {
        public static readonly float[] MinimapRanges = { 60f, 100f, 160f, 240f };

        private readonly ContentDatabase _content;
        public MapModel(ContentDatabase content) { _content = content; }

        /// <summary>Markierungen für die Minikarte im Umkreis um den Spieler.</summary>
        public List<MapMarker> MinimapMarkers(GameClient client, float range)
        {
            var list = new List<MapMarker>();
            var me = client.Me;
            if (me == null) return list;
            var gatherProfs = new HashSet<string>(client.Professions.Select(p => p.Id));
            var groupIds = new HashSet<int>(client.Group.Select(g => g.EntityId));
            foreach (var e in client.State.Entities.Values)
            {
                if (e.Id == me.Id || Vec3.DistanceXZ(e.Position, me.Position) > range) continue;
                var kind = Classify(e, groupIds, gatherProfs);
                if (kind != null) list.Add(new MapMarker { Kind = kind.Value, Label = e.Name, X = e.Position.X, Z = e.Position.Z, EntityId = e.Id });
            }
            if (client.State.DeathState == DeathState.Ghost)
                list.Add(new MapMarker { Kind = MapMarkerKind.Corpse, Label = "Leichnam", X = client.State.CorpsePosition.X, Z = client.State.CorpsePosition.Z });
            foreach (var ev in client.WorldEvents.Values)
                if (ev.State <= Net.WorldEventState.Running)
                    list.Add(new MapMarker { Kind = MapMarkerKind.WorldEvent, Label = ev.Name + ": " + ev.Text, X = ev.Position.X, Z = ev.Position.Z });
            list.Add(new MapMarker { Kind = MapMarkerKind.Player, Label = me.Name, X = me.Position.X, Z = me.Position.Z, EntityId = me.Id });
            return list;
        }

        private MapMarkerKind? Classify(ClientEntity e, HashSet<int> group, HashSet<string> professions)
        {
            if (e.Kind == EntityKind.Player) return group.Contains(e.Id) ? MapMarkerKind.GroupMember : (MapMarkerKind?)null;
            if (e.Kind == EntityKind.GameObject)
            {
                if (!_content.Objects.TryGetValue(e.TemplateId, out var o)) return null;
                if (o.IsMailbox) return MapMarkerKind.Mailbox;
                if (o.Station != null) return MapMarkerKind.Station;
                if (o.TeleportZone != null && o.TeleportZone.StartsWith("dungeon_", StringComparison.Ordinal)) return MapMarkerKind.DungeonEntrance;
                // Sammelressourcen nur mit dem passenden Beruf (wie im Vorbild)
                if (o.Profession != null && professions.Contains(o.Profession))
                    return o.Profession switch { "herbalism" => MapMarkerKind.Herb, "mining" => MapMarkerKind.Ore, "fishing" => MapMarkerKind.FishPool, _ => (MapMarkerKind?)null };
                return null;
            }
            switch (e.Marker)
            {
                case QuestMarker.Available: return MapMarkerKind.QuestAvailable;
                case QuestMarker.ReadyToTurnIn: return MapMarkerKind.QuestTurnIn;
            }
            var r = e.Roles;
            if ((r & NpcRole.FlightMaster) != 0) return MapMarkerKind.FlightMaster;
            if ((r & NpcRole.Innkeeper) != 0) return MapMarkerKind.Innkeeper;
            if ((r & NpcRole.Banker) != 0) return MapMarkerKind.Banker;
            if ((r & NpcRole.Auctioneer) != 0) return MapMarkerKind.Auctioneer;
            if ((r & (NpcRole.ClassTrainer | NpcRole.ProfessionTrainer | NpcRole.RidingTrainer)) != 0) return MapMarkerKind.Trainer;
            if ((r & NpcRole.Vendor) != 0) return MapMarkerKind.Vendor;
            return null;
        }

        /// <summary>Weltposition → Minikartenpunkt (−1…1, Norden oben), optional mit der Blickrichtung des Spielers gedreht.</summary>
        public static (float x, float y) ToMinimap(float wx, float wz, Vec3 center, float range, float rotate = 0)
        {
            float dx = (wx - center.X) / range, dz = (wz - center.Z) / range;
            if (rotate != 0)
            {
                float c = (float)Math.Cos(-rotate), s = (float)Math.Sin(-rotate);
                (dx, dz) = (dx * c - dz * s, dx * s + dz * c);
            }
            return (dx, -dz);
        }

        // ------------------------------------------------------------ Weltkarte

        public sealed class ZoneOnMap
        {
            public ZoneDef Zone = null!;
            public float X, Y, W, H;
            public bool Discovered;
        }

        public IEnumerable<string> Continents => _content.Zones.Values.Select(z => z.Continent).Where(c => c.Length > 0 && c != "Unterwelt").Distinct().OrderBy(c => c);

        /// <summary>Regionen eines Kontinents; „entdeckt“, sobald mindestens ein Gebiet der Region entdeckt ist.</summary>
        public List<ZoneOnMap> Zones(string continent, ICollection<string> discoveredAreas, string currentZone) =>
            _content.Zones.Values.Where(z => z.Continent == continent && z.MapRect.Count == 4 && !z.IsInstance)
                .Select(z => new ZoneOnMap
                {
                    Zone = z, X = z.MapRect[0], Y = z.MapRect[1], W = z.MapRect[2], H = z.MapRect[3],
                    Discovered = z.Id == currentZone || z.Areas.Any(a => discoveredAreas.Contains(a.Id)),
                }).OrderBy(z => z.Zone.MinLevel).ToList();

        /// <summary>Weltposition innerhalb einer Region → Punkt auf der Kontinentkarte.</summary>
        public static (float x, float y) ZoneToContinent(ZoneDef zone, float wx, float wz)
        {
            if (zone.MapRect.Count != 4 || zone.Size <= 0) return (0, 0);
            return (zone.MapRect[0] + wx / zone.Size * zone.MapRect[2], zone.MapRect[1] + (1 - wz / zone.Size) * zone.MapRect[3]);
        }

        /// <summary>Markierungen der Regionskarte: Gebiete (verdeckt, wenn unentdeckt), Flugpunkte, Friedhöfe, Dungeon-Eingänge, Questgebiete.</summary>
        public List<MapMarker> ZoneMarkers(ZoneDef zone, ICollection<string> discoveredAreas, ICollection<string> knownFlightNodes, IEnumerable<string> activeQuestIds)
        {
            var list = new List<MapMarker>();
            foreach (var n in _content.TravelNodes.Values.Where(n => n.Zone == zone.Id))
                list.Add(new MapMarker { Kind = MapMarkerKind.FlightMaster, Label = n.Name + (knownFlightNodes.Contains(n.Id) ? "" : " (unbekannt)"), X = n.Position.X, Z = n.Position.Z });
            foreach (var g in zone.Graveyards) list.Add(new MapMarker { Kind = MapMarkerKind.Graveyard, Label = "Friedhof", X = g.X, Z = g.Z });
            foreach (var o in zone.Objects)
                if (_content.Objects.TryGetValue(o.Object, out var od) && od.TeleportZone != null && _content.Zones.TryGetValue(od.TeleportZone, out var dz) && dz.IsInstance)
                    list.Add(new MapMarker { Kind = MapMarkerKind.DungeonEntrance, Label = dz.Name, X = o.Position.X, Z = o.Position.Z });
            foreach (var qid in activeQuestIds)
            {
                if (!_content.Quests.TryGetValue(qid, out var q)) continue;
                foreach (var obj in q.Objectives.Where(o => o.Type == ObjectiveType.Explore))
                {
                    var area = zone.Areas.FirstOrDefault(a => a.Id == obj.Target);
                    if (area != null) list.Add(new MapMarker { Kind = MapMarkerKind.QuestArea, Label = q.Title, X = area.Center.X, Z = area.Center.Z });
                }
                foreach (var obj in q.Objectives.Where(o => o.Type == ObjectiveType.Kill))
                {
                    var spawn = zone.Spawns.FirstOrDefault(s => s.Creature == obj.Target);
                    if (spawn != null) list.Add(new MapMarker { Kind = MapMarkerKind.QuestArea, Label = q.Title, X = spawn.Position.X, Z = spawn.Position.Z });
                }
            }
            return list;
        }
    }
}
