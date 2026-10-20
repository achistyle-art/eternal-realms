using System.Collections.Generic;
using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Minikarte, Weltkarte mit Nebel des Krieges und Quest-Tracker (Kapitel 28/30).</summary>
    public sealed partial class GameUI
    {
        private bool _showMap;
        private MapModel? _mapModel;
        private string _mapContinent = "";
        private string? _mapZone;
        private MapModel Map => _mapModel ??= new MapModel(Content);

        private static Color MarkerColor(MapMarkerKind k) => k switch
        {
            MapMarkerKind.Player => Color.white,
            MapMarkerKind.GroupMember => Palette.Friendly,
            MapMarkerKind.QuestAvailable or MapMarkerKind.QuestTurnIn or MapMarkerKind.QuestArea => new Color(1f, 0.85f, 0.1f),
            MapMarkerKind.Herb => new Color(0.4f, 0.9f, 0.4f),
            MapMarkerKind.Ore => new Color(0.8f, 0.6f, 0.4f),
            MapMarkerKind.FishPool => new Color(0.4f, 0.7f, 1f),
            MapMarkerKind.FlightMaster => new Color(0.6f, 1f, 0.6f),
            MapMarkerKind.DungeonEntrance => new Color(0.9f, 0.4f, 1f),
            MapMarkerKind.Graveyard => new Color(0.7f, 0.7f, 0.9f),
            MapMarkerKind.Corpse => new Color(1f, 0.3f, 0.3f),
            MapMarkerKind.WorldEvent => new Color(1f, 0.55f, 0.1f),
            _ => new Color(0.9f, 0.9f, 0.7f),
        };

        /// <summary>Kennbuchstabe zusätzlich zur Farbe – die Bedeutung hängt nie allein an der Farbe.</summary>
        private static string MarkerGlyph(MapMarkerKind k) => k switch
        {
            MapMarkerKind.Player => "▲", MapMarkerKind.GroupMember => "●", MapMarkerKind.QuestAvailable => "!", MapMarkerKind.QuestTurnIn => "?",
            MapMarkerKind.QuestArea => "◎", MapMarkerKind.Vendor => "H", MapMarkerKind.Trainer => "L", MapMarkerKind.FlightMaster => "F",
            MapMarkerKind.Innkeeper => "G", MapMarkerKind.Banker => "B", MapMarkerKind.Auctioneer => "A", MapMarkerKind.Mailbox => "P",
            MapMarkerKind.Herb => "♣", MapMarkerKind.Ore => "◆", MapMarkerKind.FishPool => "~", MapMarkerKind.Station => "W",
            MapMarkerKind.DungeonEntrance => "D", MapMarkerKind.Graveyard => "†", MapMarkerKind.Corpse => "✝", MapMarkerKind.WorldEvent => "⚔", _ => "•",
        };

        private void DrawMarker(Vector2 center, MapMarker m)
        {
            var r = new Rect(center.x - 7, center.y - 8, 14, 16);
            var old = GUI.contentColor;
            GUI.contentColor = MarkerColor(m.Kind);
            GUI.Label(r, MarkerGlyph(m.Kind), _center);
            GUI.contentColor = old;
            if (r.Contains(Event.current.mousePosition)) _tooltip = m.Label;
        }

        // ------------------------------------------------------------ Minikarte

        private Rect MinimapRect => new Rect(ScreenW - 200, 10, 190, 190);

        private void DrawMinimap()
        {
            var me = Client.Me;
            if (me == null || !Settings.ShowMinimap) return;
            var r = Box(MinimapRect);
            var old = GUI.color;
            GUI.color = new Color(0.05f, 0.08f, 0.05f, 0.85f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Box(r, "");
            float range = 160f / Settings.MinimapZoom;
            var c = r.center;
            foreach (var m in Map.MinimapMarkers(Client, range))
            {
                var (x, y) = MapModel.ToMinimap(m.X, m.Z, me.Position, range);
                if (Mathf.Abs(x) > 1 || Mathf.Abs(y) > 1) continue;
                DrawMarker(new Vector2(c.x + x * (r.width / 2 - 8), c.y + y * (r.height / 2 - 8)), m);
            }
            GUI.Label(new Rect(r.x, r.y + 2, r.width, 18), $"<b>{ZoneName(Client.ZoneId)}</b>", _center);
            GUI.Label(new Rect(r.x + r.width / 2 - 8, r.y + 16, 16, 16), "N", _center);
            if (GUI.Button(new Rect(r.xMax - 22, r.yMax - 46, 20, 20), "+")) { Settings.MinimapZoom = Mathf.Min(4f, Settings.MinimapZoom * 1.25f); SettingsChanged(); }
            if (GUI.Button(new Rect(r.xMax - 22, r.yMax - 24, 20, 20), "–")) { Settings.MinimapZoom = Mathf.Max(0.5f, Settings.MinimapZoom / 1.25f); SettingsChanged(); }
            if (GUI.Button(new Rect(r.x + 2, r.yMax - 24, 34, 20), "M")) _showMap = !_showMap;
        }

        // ------------------------------------------------------------ Weltkarte

        private void DrawWorldMap()
        {
            var area = Window(_mapZone != null && Content.Zones.TryGetValue(_mapZone, out var zd) ? $"Karte – {zd.Name}" : "Weltkarte", 860, 640);
            var continents = Map.Continents.ToList();
            if (continents.Count == 0) return;
            if (!continents.Contains(_mapContinent))
                _mapContinent = Content.Zones.TryGetValue(Client.ZoneId, out var cz) && continents.Contains(cz.Continent) ? cz.Continent : continents[0];
            GUILayout.BeginArea(new Rect(area.x, area.y, area.width, 26));
            GUILayout.BeginHorizontal();
            foreach (var cont in continents)
                if (GUILayout.Toggle(_mapContinent == cont && _mapZone == null, cont, GUI.skin.button)) { _mapContinent = cont; _mapZone = null; }
            if (GUILayout.Button("Aktuelle Region")) _mapZone = Content.Zones.ContainsKey(Client.ZoneId) ? Client.ZoneId : null;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            var mapRect = new Rect(area.x, area.y + 30, area.width, area.height - 30);
            float side = Mathf.Min(mapRect.width, mapRect.height);
            mapRect = new Rect(mapRect.x + (mapRect.width - side) / 2, mapRect.y, side, side);
            var old = GUI.color;
            GUI.color = new Color(0.08f, 0.12f, 0.2f, 1f);
            GUI.DrawTexture(mapRect, Texture2D.whiteTexture);
            GUI.color = old;
            if (_mapZone != null && Content.Zones.TryGetValue(_mapZone, out var zone)) DrawZoneMap(mapRect, zone);
            else DrawContinentMap(mapRect);
        }

        private void DrawContinentMap(Rect map)
        {
            float s = map.width / 1000f;
            var me = Client.Me;
            foreach (var z in Map.Zones(_mapContinent, Client.DiscoveredAreas, Client.ZoneId))
            {
                var r = new Rect(map.x + z.X * s, map.y + z.Y * s, z.W * s, z.H * s);
                var old = GUI.color;
                // Nebel des Krieges: unentdeckte Regionen bleiben dunkel und namenlos
                GUI.color = !z.Discovered ? new Color(0.12f, 0.12f, 0.14f) : z.Zone.Faction == Faction.Sonnenbuendnis ? new Color(0.35f, 0.42f, 0.25f) : z.Zone.Faction == Faction.Schattenpakt ? new Color(0.45f, 0.3f, 0.22f) : new Color(0.35f, 0.35f, 0.3f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = old;
                GUI.Box(r, "");
                string label = z.Discovered ? $"<b>{z.Zone.Name}</b>\n<size=10>Stufe {z.Zone.MinLevel}–{z.Zone.MaxLevel}</size>" : "<color=#666>???</color>";
                GUI.Label(r, label, _centerWrap);
                if (z.Discovered && Event.current.type == EventType.MouseUp && r.Contains(Event.current.mousePosition)) { _mapZone = z.Zone.Id; Event.current.Use(); }
                if (me != null && z.Zone.Id == Client.ZoneId)
                {
                    var (px, py) = MapModel.ZoneToContinent(z.Zone, me.Position.X, me.Position.Z);
                    DrawMarker(new Vector2(map.x + px * s, map.y + py * s), new MapMarker { Kind = MapMarkerKind.Player, Label = me.Name });
                }
            }
            GUI.Label(new Rect(map.x + 6, map.yMax - 22, map.width - 12, 20), "<size=11>Klick auf eine entdeckte Region öffnet ihre Karte.</size>", _small);
        }

        private void DrawZoneMap(Rect map, ZoneDef zone)
        {
            float s = map.width / Mathf.Max(1, zone.Size);
            Vector2 ToMap(float wx, float wz) => new Vector2(map.x + wx * s, map.y + (zone.Size - wz) * s);
            foreach (var a in zone.Areas)
            {
                bool known = Client.DiscoveredAreas.Contains(a.Id);
                if (!known && a.IsSecret) continue;
                var c = ToMap(a.Center.X, a.Center.Z);
                float rad = a.Radius * s;
                var r = new Rect(c.x - rad, c.y - rad, rad * 2, rad * 2);
                var old = GUI.color;
                GUI.color = known ? new Color(0.35f, 0.5f, 0.3f, 0.9f) : new Color(0.15f, 0.15f, 0.17f, 0.9f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = old;
                if (known) GUI.Label(r, $"<size=11>{a.Name}</size>", _centerWrap);
            }
            var questIds = Client.State.QuestLog?.Quests.Select(q => q.QuestId).ToList() ?? new List<string>();
            foreach (var m in Map.ZoneMarkers(zone, Client.DiscoveredAreas, Client.KnownFlightNodes, questIds)) DrawMarker(ToMap(m.X, m.Z), m);
            if (zone.Id == Client.ZoneId)
                foreach (var ev in Client.WorldEvents.Values.Where(e => e.State <= WorldEventState.Running))
                    DrawMarker(ToMap(ev.Position.X, ev.Position.Z), new MapMarker { Kind = MapMarkerKind.WorldEvent, Label = $"{ev.Name}: {ev.Text}" });
            if (zone.Id == Client.ZoneId)
            {
                var groupIds = new HashSet<int>(Client.Group.Select(g => g.EntityId));
                foreach (var e in Client.State.Entities.Values.Where(e => groupIds.Contains(e.Id) && e.Id != Client.State.MyId))
                    DrawMarker(ToMap(e.Position.X, e.Position.Z), new MapMarker { Kind = MapMarkerKind.GroupMember, Label = e.Name });
                if (Client.Me != null) DrawMarker(ToMap(Client.Me.Position.X, Client.Me.Position.Z), new MapMarker { Kind = MapMarkerKind.Player, Label = Client.Me.Name });
            }
            if (GUI.Button(new Rect(map.x + 6, map.y + 6, 120, 22), "← Kontinent")) _mapZone = null;
        }

        // ------------------------------------------------------------ Quest-Tracker

        private void DrawQuestTracker()
        {
            var log = Client.State.QuestLog;
            if (!Settings.ShowQuestTracker) return;
            float x = ScreenW - 260, y = (Settings.ShowMinimap ? 210 : 10) + 70;
            var text = "";
            // Weltereignisse der Region zuerst (Kapitel 37); beendete verschwinden nach kurzer Zeit
            foreach (var ev in Client.WorldEvents.Values)
            {
                if (ev.State > WorldEventState.Running) continue;
                string bar = ev.Goal > 0 ? $" {ev.Progress}/{ev.Goal}" : "";
                text += $"<color=#ffa040><b>⚔ {ev.Name}</b></color>{(ev.Participating ? " <color=#9f9>(dabei)</color>" : "")}\n  {ev.Text}{bar} · {ev.SecondsLeft:0} s\n";
            }
            if (log == null) { if (text != "") DrawTrackerBox(x, y, text); return; }
            foreach (var q in log.Quests.Take(8))
            {
                if (!Content.Quests.TryGetValue(q.QuestId, out var def)) continue;
                bool done = q.State == QuestState.Complete;
                text += $"<b>{def.Title}</b>{(done ? " <color=yellow>(abgeschlossen)</color>" : "")}\n";
                for (int i = 0; i < def.Objectives.Count; i++)
                {
                    var o = def.Objectives[i];
                    int have = i < q.Counts.Count ? q.Counts[i] : 0;
                    string col = have >= o.Count ? "#9f9" : "#ddd";
                    text += $"  <color={col}>{(o.Description != "" ? o.Description : o.Target)}: {Mathf.Min(have, o.Count)}/{o.Count}</color>\n";
                }
            }
            if (text == "") return;
            DrawTrackerBox(x, y, text);
        }

        private void DrawTrackerBox(float x, float y, string text)
        {
            float h = _wrap!.CalcHeight(new GUIContent(text), 246) + 6;
            var r = Box(new Rect(x, y, 250, h));
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.35f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(r.x + 4, r.y + 2, 246, h), text, _wrap);
        }
    }
}
