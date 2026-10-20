using System.Linq;
using System.Collections.Generic;
using EternalRealms.Shared;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Stellt die vom Server gemeldete Welt dar. Jede Einheit bekommt ein GameObject; Positionen anderer
    /// Einheiten werden zwischen Snapshots interpoliert. Tag/Nacht und Wetter folgen dem Server.
    /// Die Darstellung nutzt Platzhalterformen, bis Modelle aus dem Content (Model-Feld) zugeordnet sind.
    /// </summary>
    public sealed class WorldRenderer : MonoBehaviour
    {
        public static WorldRenderer Instance { get; private set; } = null!;

        private readonly Dictionary<int, EntityView> _views = new Dictionary<int, EntityView>();
        private GameObject? _ground;
        private string _zone = "";
        private Light _sun = null!;
        private ParticleSystem? _rain;
        private GameObject _selection = null!;
        public readonly List<FloatingText> FloatingTexts = new List<FloatingText>();

        private GameClient Client => NetworkManager.Instance.Client;

        public EntityView? ViewOf(int id) => _views.TryGetValue(id, out var v) ? v : null;

        private void Awake()
        {
            Instance = this;
            var sunGo = new GameObject("Sonne");
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            _selection = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _selection.name = "Zielmarkierung";
            Destroy(_selection.GetComponent<Collider>());
            _selection.transform.localScale = new Vector3(1.6f, 0.02f, 1.6f);
            _selection.GetComponent<Renderer>().material.color = new Color(1f, 0.85f, 0.2f, 0.8f);
            _selection.SetActive(false);
            RenderSettings.fog = true;
            Client.MessageReceived += OnMessage;
        }

        private void OnMessage(Message m)
        {
            switch (m)
            {
                case S_CombatEvent ce:
                    var v = ViewOf(ce.TargetId);
                    if (v == null || (ce.SourceId != Client.State.MyId && ce.TargetId != Client.State.MyId && !Client.Group.Exists(g => g.EntityId == ce.SourceId))) break;
                    string text = ce.Result switch
                    {
                        CombatResult.Heal or CombatResult.CritHeal => "+" + ce.Amount,
                        CombatResult.Miss => "Verfehlt", CombatResult.Dodge => "Ausgewichen", CombatResult.Parry => "Pariert",
                        CombatResult.Resist => "Widerstanden", CombatResult.Evade => "Entkommen", CombatResult.Immune => "Immun",
                        CombatResult.Absorb => "Absorbiert",
                        _ => ce.Amount.ToString(),
                    };
                    Color c = ce.Result is CombatResult.Heal or CombatResult.CritHeal ? Color.green : ce.TargetId == Client.State.MyId ? Color.red : Color.yellow;
                    bool big = ce.Result is CombatResult.Crit or CombatResult.CritHeal;
                    FloatingTexts.Add(new FloatingText { WorldPos = v.transform.position + Vector3.up * 2.4f, Text = big ? text + "!" : text, Color = c, Big = big, Born = Time.time });
                    break;
                case S_EnterWorld:
                    break;
            }
        }

        private void LateUpdate()
        {
            var state = Client.State;
            if (!Client.InWorld) { ClearAll(); return; }
            if (Client.ZoneId != _zone) BuildZone(Client.ZoneId);
            // Neue und entfernte Einheiten
            var seen = new HashSet<int>();
            foreach (var e in state.Entities.Values)
            {
                seen.Add(e.Id);
                if (!_views.TryGetValue(e.Id, out var view))
                {
                    view = EntityView.Create(e, e.Id == state.MyId);
                    _views[e.Id] = view;
                }
                view.Sync(e, e.Id == state.MyId);
            }
            var gone = new List<int>();
            foreach (var id in _views.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone) { Destroy(_views[id].gameObject); _views.Remove(id); }

            // Zielmarkierung
            var me = state.Me;
            var target = me != null ? ViewOf(me.TargetId) : null;
            _selection.SetActive(target != null);
            if (target != null) _selection.transform.position = target.transform.position + Vector3.up * 0.05f;

            UpdateSky();
            FloatingTexts.RemoveAll(f => Time.time - f.Born > 1.6f);
        }

        private void BuildZone(string zoneId)
        {
            ClearAll();
            _zone = zoneId;
            if (_ground != null) Destroy(_ground);
            var content = NetworkManager.Instance.Content;
            if (!content.Zones.TryGetValue(zoneId, out var zone)) return;
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.name = "Boden " + zone.Name;
            _ground.layer = 0;
            float size = zone.Size;
            _ground.transform.localScale = new Vector3(size / 10f, 1, size / 10f);
            _ground.transform.position = new Vector3(size / 2f, 0, size / 2f);
            _ground.GetComponent<Renderer>().material.color = zone.Biome switch
            {
                "Steppe" => new Color(0.55f, 0.38f, 0.25f),
                "Gruft" => new Color(0.18f, 0.16f, 0.14f),
                _ => new Color(0.28f, 0.45f, 0.22f),
            };
            // Gebietsnamen als Bodenbeschriftung
            foreach (var area in zone.Areas)
            {
                var label = new GameObject("Gebiet " + area.Name);
                label.transform.position = area.Center.ToUnity() + Vector3.up * 0.2f;
                label.transform.rotation = Quaternion.Euler(90, 0, 0);
                var tm = label.AddComponent<TextMesh>();
                tm.text = area.Name;
                tm.characterSize = 1.2f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = area.IsCity ? new Color(1, 0.9f, 0.6f) : new Color(1, 1, 1, 0.6f);
                label.transform.SetParent(_ground.transform, true);
            }
            if (_rain == null)
            {
                var rainGo = new GameObject("Niederschlag");
                _rain = rainGo.AddComponent<ParticleSystem>();
                var main = _rain.main;
                main.startSpeed = 18; main.startLifetime = 1.5f; main.maxParticles = 4000; main.startSize = 0.06f;
                var shape = _rain.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(40, 1, 40);
                rainGo.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
        }

        private void ClearAll()
        {
            foreach (var v in _views.Values) if (v != null) Destroy(v.gameObject);
            _views.Clear();
            _zone = "";
        }

        /// <summary>Tag/Nacht nach Spielzeit, Nebel und Niederschlag nach Wetter.</summary>
        private void UpdateSky()
        {
            var ws = Client.State.World;
            float hour = ws?.GameHour ?? 12f;
            float sunAngle = (hour - 6f) / 24f * 360f;
            _sun.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0);
            float day = Mathf.Clamp01(Mathf.Sin((hour - 6f) / 24f * Mathf.PI * 2f) * 1.5f + 0.2f);
            _sun.intensity = Mathf.Lerp(0.08f, 1.2f, day);
            _sun.color = Color.Lerp(new Color(0.5f, 0.55f, 0.9f), new Color(1f, 0.95f, 0.85f), day);
            RenderSettings.ambientLight = Color.Lerp(new Color(0.05f, 0.06f, 0.12f), new Color(0.45f, 0.45f, 0.5f), day);
            var weather = ws?.Weather ?? Weather.Clear;
            float intensity = ws?.WeatherIntensity ?? 0;
            RenderSettings.fogDensity = weather == Weather.Fog ? 0.02f * Mathf.Max(0.3f, intensity) : 0.002f;
            RenderSettings.fogColor = Color.Lerp(new Color(0.05f, 0.05f, 0.1f), new Color(0.7f, 0.75f, 0.8f), day);
            if (Camera.main != null) Camera.main.backgroundColor = RenderSettings.fogColor;
            if (_rain != null && Client.Me != null)
            {
                _rain.transform.position = Client.Me.Position.ToUnity() + Vector3.up * 20;
                var emission = _rain.emission;
                bool wet = weather == Weather.Rain || weather == Weather.Storm || weather == Weather.Snow;
                emission.rateOverTime = wet ? 1500 * Mathf.Max(0.3f, intensity) * (Client.Settings.ReduceParticles ? 0.2f : 1f) : 0;
                var main = _rain.main;
                main.startSpeed = weather == Weather.Snow ? 3 : 18;
                main.startColor = weather == Weather.Snow ? Color.white : new Color(0.7f, 0.75f, 0.9f, 0.6f);
            }
        }
    }

    public sealed class FloatingText
    {
        public Vector3 WorldPos;
        public string Text = "";
        public Color Color;
        public bool Big;
        public float Born;
    }

    /// <summary>Darstellung einer Einheit: Platzhalterkörper, Namensschild, Interpolation.</summary>
    public sealed class EntityView : MonoBehaviour
    {
        public int EntityId;
        private TextMesh _label = null!;
        private Vector3 _from, _to;
        private float _lerpStart;
        private Renderer _body = null!;
        private GameObject? _mount;
        private string _mountId = "";

        /// <summary>Platzhalter-Reittier: liegende Kapsel unter der Figur, Farbe aus der Modell-ID.</summary>
        private void SyncMount(ClientEntity e)
        {
            if (e.MountId == _mountId) return;
            _mountId = e.MountId;
            if (_mount != null) { Destroy(_mount); _mount = null; }
            if (_mountId == "") return;
            _mount = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(_mount.GetComponent<Collider>());
            _mount.name = "Reittier " + _mountId;
            _mount.transform.SetParent(transform, false);
            _mount.transform.localPosition = new Vector3(0, -1.0f, 0);
            _mount.transform.localRotation = Quaternion.Euler(90, 0, 0);
            _mount.transform.localScale = new Vector3(0.9f, 1.1f, 0.8f);
            string model = NetworkManager.Instance.Content.Mounts.TryGetValue(_mountId, out var def) ? def.Model : _mountId;
            int h = model.Aggregate(17, (acc, ch) => acc * 31 + ch);
            _mount.GetComponent<Renderer>().material.color = Color.HSVToRGB((h & 0xffff) / 65535f, 0.35f, 0.75f);
        }

        public static EntityView Create(ClientEntity e, bool isMe)
        {
            var prim = e.Kind == EntityKind.GameObject ? PrimitiveType.Cube : PrimitiveType.Capsule;
            var go = GameObject.CreatePrimitive(prim);
            go.name = $"{e.Name} #{e.Id}";
            var view = go.AddComponent<EntityView>();
            view.EntityId = e.Id;
            view._body = go.GetComponent<Renderer>();
            var content = NetworkManager.Instance.Content;
            float scale = 1f;
            if (e.Kind == EntityKind.Creature || e.Kind == EntityKind.Npc || e.Kind == EntityKind.Pet)
                if (content.Creatures.TryGetValue(e.TemplateId, out var def)) scale = def.Scale * (e.Kind == EntityKind.Pet ? 0.7f : 1f);
            if (e.Kind == EntityKind.Player && content.Races.ContainsKey(e.Race)) scale = e.Appearance.Height;
            if (e.Kind == EntityKind.GameObject) go.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            else go.transform.localScale = new Vector3(0.9f, 1f, 0.9f) * scale;
            var labelGo = new GameObject("Namensschild");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0, 1.6f / Mathf.Max(0.5f, scale), 0);
            view._label = labelGo.AddComponent<TextMesh>();
            view._label.anchor = TextAnchor.LowerCenter;
            view._label.characterSize = 0.12f;
            view._label.fontSize = 48;
            view._from = view._to = e.Position.ToUnity() + Vector3.up * (e.Kind == EntityKind.GameObject ? 0.4f : 1f);
            go.transform.position = view._to;
            if (isMe) go.layer = 2; // Ignore Raycast – man klickt sich nicht selbst an
            return view;
        }

        public void Sync(ClientEntity e, bool isMe)
        {
            SyncMount(e);
            float yOffset = e.Kind == EntityKind.GameObject ? 0.4f : transform.localScale.y + (_mount != null ? 0.9f : 0f);
            var target = e.Position.ToUnity() + Vector3.up * yOffset;
            if (isMe)
            {
                transform.position = target;
            }
            else
            {
                if ((target - _to).sqrMagnitude > 0.0001f) { _from = transform.position; _to = target; _lerpStart = Time.time; }
                float t = Mathf.Clamp01((Time.time - _lerpStart) / 0.1f);
                transform.position = (target - transform.position).sqrMagnitude > 400 ? target : Vector3.Lerp(_from, _to, t);
                transform.rotation = Quaternion.Euler(0, Conversions.FacingToYaw(e.Facing), 0);
            }
            _body.material.color = BodyColor(e);
            string marker = e.Marker switch { QuestMarker.Available => "<color=yellow>!</color>\n", QuestMarker.ReadyToTurnIn => "<color=yellow>?</color>\n", QuestMarker.Incomplete => "<color=grey>?</color>\n", QuestMarker.AvailableLowLevel => "<color=grey>!</color>\n", _ => "" };
            var net = NetworkManager.Instance;
            if (net != null && net.Client.Markers.TryGetValue(e.Id, out var raidMark)) marker = GameUI.MarkerSymbol(raidMark) + "\n" + marker;
            string hp = e.Kind == EntityKind.GameObject || e.MaxHealth <= 0 ? "" : $"\n{Bar(e.HealthPct)}";
            string title = e.Title != "" ? $"\n<{e.Title}>" : "";
            _label.text = $"{marker}{e.Name}{(e.Kind == EntityKind.GameObject ? "" : $" ({e.Level})")}{title}{hp}";
            _label.color = LabelColor(e);
            if (Camera.main != null) _label.transform.rotation = Camera.main.transform.rotation;
        }

        private static string Bar(float pct)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(pct * 10), 0, 10);
            return new string('█', n) + new string('░', 10 - n);
        }

        private static Color BodyColor(ClientEntity e)
        {
            if (e.IsDead) return new Color(0.3f, 0.3f, 0.3f, 0.5f);
            if ((e.Flags & UnitFlags.Ghost) != 0) return new Color(0.6f, 0.8f, 1f, 0.4f);
            switch (e.Kind)
            {
                case EntityKind.Player: return Conversions.ClassColor(e.Class);
                case EntityKind.GameObject: return e.Health > 0 ? new Color(0.9f, 0.8f, 0.3f) : new Color(0.4f, 0.4f, 0.4f);
                case EntityKind.Pet: return new Color(0.5f, 0.7f, 0.5f);
                case EntityKind.Npc: return e.Roles.HasFlag(NpcRole.SpiritHealer) ? new Color(0.7f, 0.9f, 1f) : new Color(0.3f, 0.6f, 1f);
                default:
                    if ((e.Flags & UnitFlags.Boss) != 0) return new Color(0.6f, 0.1f, 0.6f);
                    if ((e.Flags & UnitFlags.Elite) != 0) return new Color(0.9f, 0.45f, 0.1f);
                    return e.Faction == Faction.Neutral ? new Color(0.85f, 0.8f, 0.3f) : new Color(0.75f, 0.2f, 0.2f);
            }
        }

        private static Color LabelColor(ClientEntity e)
        {
            var me = NetworkManager.Instance.Client.Me;
            if (e.Kind == EntityKind.Player) return me != null && e.Faction == me.Faction ? new Color(0.5f, 0.8f, 1f) : Color.red;
            if (e.Faction == Faction.Hostile) return new Color(1f, 0.35f, 0.3f);
            if (e.Faction == Faction.Neutral) return Color.yellow;
            return new Color(0.4f, 1f, 0.4f);
        }
    }
}
