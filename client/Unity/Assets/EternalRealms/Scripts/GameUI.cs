using System.Collections.Generic;
using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Komplette Spieloberfläche im Sofortmodus (IMGUI) – bewusst ohne Prefabs, damit das Projekt ohne
    /// Grafik-Assets lauffähig ist. Eine UI-Toolkit-Oberfläche mit eigenem Look ersetzt sie in Meilenstein 3.
    /// </summary>
    public sealed partial class GameUI : MonoBehaviour
    {
        public static GameUI Instance { get; private set; } = null!;
        public static bool PointerOverUi { get; private set; }
        public static bool TypingInChat { get; private set; }
        public static bool AnyWindowOpen => Instance != null && (Instance._showQuests || Instance._showBags || Instance._showTalents || Instance._showGroup || Instance._showBook || Instance._npc != null);

        private enum Screen { Login, Characters, Create, World }

        private GameClient Client => NetworkManager.Instance.Client;
        private ContentDatabase Content => NetworkManager.Instance.Content;

        // Anmeldung
        private string _account = "", _password = "", _status = "";
        private bool _connecting;
        // Erstellung
        private Faction _faction = Faction.Sonnenbuendnis;
        private Race _race = Race.Aldorer;
        private CharClass _class = CharClass.Warrior;
        private Gender _gender = Gender.Male;
        private string _newName = "";
        private readonly int[] _look = new int[8];
        private float _height = 1f;
        private Vector2 _scrollCreate;
        // Spiel
        private bool _showQuests, _showBags, _showTalents, _showGroup, _showBook;
        private S_NpcInteraction? _npc;
        private int _choice = -1;
        private string _chatInput = "";
        private bool _chatFocus;
        private Vector2 _scrollChat, _scrollQuest, _scrollTalents, _scrollNpc, _scrollBook;
        private readonly List<Rect> _uiRects = new List<Rect>();
        private int _noticeIndex;
        private readonly List<(string text, float time)> _toasts = new List<(string, float)>();
        private GUIStyle? _small, _title, _wrap, _center;

        private void Awake()
        {
            Instance = this;
            Client.MessageReceived += m =>
            {
                if (m is S_NpcInteraction ni) { _npc = ni; _choice = -1; }
                OnEconomyMessage(m);
                if (m is S_LoginResult lr) _status = lr.Message;
                if (m is S_CreateCharacterResult cr) { _status = cr.Message; if (cr.Success) _screen = Screen.Characters; }
                if (m is S_LevelUp lu && lu.EntityId == Client.State.MyId) Toast($"Stufe {lu.NewLevel} erreicht!");
            };
        }

        private Screen _screen = Screen.Login;

        private void Toast(string s) => _toasts.Add((s, Time.time));

        private void Update()
        {
            var mouse = new Vector2(Input.mousePosition.x, UnityEngine.Screen.height - Input.mousePosition.y);
            PointerOverUi = _uiRects.Any(r => r.Contains(mouse));
            TypingInChat = _chatFocus;
            if (!Client.LoggedIn) _screen = Screen.Login;
            else if (Client.InWorld) _screen = Screen.World;
            else if (_screen == Screen.Login || _screen == Screen.World) _screen = Screen.Characters;
            if (_screen == Screen.World && !_chatFocus)
            {
                if (Input.GetKeyDown(KeyCode.L)) _showQuests = !_showQuests;
                if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.C)) _showBags = !_showBags;
                if (Input.GetKeyDown(KeyCode.N)) _showTalents = !_showTalents;
                if (Input.GetKeyDown(KeyCode.P)) _showGroup = !_showGroup;
                if (Input.GetKeyDown(KeyCode.K)) _showBook = !_showBook;
                if (Input.GetKeyDown(KeyCode.Y)) Client.Send(new C_Mount());
                if (Input.GetKeyDown(KeyCode.J)) _showMounts = !_showMounts;
                if (Input.GetKeyDown(KeyCode.O)) { _showSocial = !_showSocial; if (_showSocial) { Client.Send(new C_FriendList()); Client.Send(new C_GuildRoster()); } }
                if (Input.GetKeyDown(KeyCode.Escape)) { _npc = null; _showQuests = _showBags = _showTalents = _showGroup = _showBook = false; _bankOpen = _mailOpen = _ahOpen = false; }
            }
            while (_noticeIndex < Client.Notices.Count) Toast(Client.Notices[_noticeIndex++]);
            _toasts.RemoveAll(t => Time.time - t.time > 5);
        }

        private void Styles()
        {
            if (_small != null) return;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, richText = true };
            _wrap = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            _center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, fontStyle = FontStyle.Bold };
        }

        private void OnGUI()
        {
            Styles();
            if (Event.current.type == EventType.Layout) _uiRects.Clear();
            if (NetworkManager.Instance.LoadError != null) { GUI.Label(new Rect(20, 20, 900, 60), "<color=red>" + NetworkManager.Instance.LoadError + "</color>", _wrap); return; }
            switch (_screen)
            {
                case Screen.Login: DrawLogin(); break;
                case Screen.Characters: DrawCharacters(); break;
                case Screen.Create: DrawCreate(); break;
                case Screen.World: DrawWorld(); break;
            }
            DrawToasts();
        }

        private Rect Box(Rect r) { _uiRects.Add(r); return r; }

        // ================================================================== Anmeldung

        private void DrawLogin()
        {
            var r = Box(new Rect(UnityEngine.Screen.width / 2 - 180, UnityEngine.Screen.height / 2 - 150, 360, 300));
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("ETERNAL REALMS", _title);
            GUILayout.Label("Server");
            GUILayout.BeginHorizontal();
            NetworkManager.Instance.Host = GUILayout.TextField(NetworkManager.Instance.Host, GUILayout.Width(220));
            int.TryParse(GUILayout.TextField(NetworkManager.Instance.Port.ToString(), GUILayout.Width(80)), out NetworkManager.Instance.Port);
            GUILayout.EndHorizontal();
            GUILayout.Label("Konto");
            _account = GUILayout.TextField(_account, 32);
            GUILayout.Label("Passwort");
            _password = GUILayout.PasswordField(_password, '•', 64);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Anmelden")) Login(false);
            if (GUILayout.Button("Konto erstellen")) Login(true);
            GUILayout.EndHorizontal();
            GUILayout.Label(Client.Connected ? "<color=lime>verbunden</color>" : _connecting ? "verbinde …" : "", _small);
            GUILayout.Label(_status, _wrap);
            GUILayout.EndArea();
            if (_connecting && Client.Connected) { _connecting = false; Client.Login(_account, _password, _pendingRegister); }
        }

        private bool _pendingRegister;

        private void Login(bool register)
        {
            _pendingRegister = register;
            if (Client.Connected) { Client.Login(_account, _password, register); return; }
            _connecting = NetworkManager.Instance.Connect();
            if (!_connecting) _status = "Verbindung fehlgeschlagen.";
        }

        // ================================================================== Charakterauswahl

        private void DrawCharacters()
        {
            var r = Box(new Rect(UnityEngine.Screen.width / 2 - 260, 80, 520, 520));
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("Eure Charaktere", _title);
            foreach (var c in Client.Characters.ToList())
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{c.Name}</b>\nStufe {c.Level} {Content.Races[c.Race].Name} {Content.Classes[c.Class].Name} – {ZoneName(c.ZoneId)}", _wrap, GUILayout.Width(330));
                if (GUILayout.Button("Spielen", GUILayout.Height(36))) Client.Send(new C_EnterWorld { CharacterId = c.Id });
                if (GUILayout.Button("Löschen", GUILayout.Height(36))) Client.Send(new C_DeleteCharacter { CharacterId = c.Id });
                GUILayout.EndHorizontal();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Neuen Charakter erschaffen", GUILayout.Height(32))) { _screen = Screen.Create; RandomName(); }
            GUILayout.Label(_status, _wrap);
            GUILayout.EndArea();
        }

        private string ZoneName(string id) => Content.Zones.TryGetValue(id, out var z) ? z.Name : id;

        // ================================================================== Charaktererstellung

        private void DrawCreate()
        {
            var r = Box(new Rect(40, 40, UnityEngine.Screen.width - 80, UnityEngine.Screen.height - 80));
            GUILayout.BeginArea(r, GUI.skin.box);
            _scrollCreate = GUILayout.BeginScrollView(_scrollCreate);
            GUILayout.Label("Charaktererstellung", _title);
            GUILayout.BeginHorizontal();
            foreach (var f in Content.Factions.Values)
                if (GUILayout.Toggle(_faction == f.Id, f.Name, GUI.skin.button)) { if (_faction != f.Id) { _faction = f.Id; _race = Content.Races.Values.First(x => x.Faction == f.Id).Id; FixClass(); RandomName(); } }
            GUILayout.EndHorizontal();
            GUILayout.Label(Content.Factions[_faction].Description, _wrap);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            foreach (var race in Content.Races.Values.Where(x => x.Faction == _faction))
                if (GUILayout.Toggle(_race == race.Id, race.Name, GUI.skin.button)) { if (_race != race.Id) { _race = race.Id; FixClass(); RandomName(); } }
            GUILayout.EndHorizontal();
            var rd = Content.Races[_race];
            GUILayout.Label(rd.Lore, _wrap);
            foreach (var t in rd.Traits) GUILayout.Label($"• <b>{t.Name}</b>: {t.Description}", _wrap);
            GUILayout.Label($"Startgebiet: {ZoneName(rd.StartZone)}", _small);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            foreach (var c in rd.Classes)
                if (GUILayout.Toggle(_class == c, Content.Classes[c].Name, GUI.skin.button)) _class = c;
            GUILayout.EndHorizontal();
            var cd = Content.Classes[_class];
            GUILayout.Label(cd.Description, _wrap);
            GUILayout.Label("Rollen: " + string.Join(", ", cd.Roles.Select(RoleName)) + " – Talentbäume: " + string.Join(", ", cd.TalentTrees.Select(t => Content.TalentTrees[t].Name)), _small);
            GUILayout.Label("<b>Vorschau der Fähigkeiten</b>", _wrap);
            foreach (var a in cd.PreviewAbilities)
            {
                var ab = Content.AbilityRanks[a][0];
                GUILayout.Label($"• <b>{ab.Name}</b> (ab Stufe {ab.Level}): {ab.Description}", _wrap);
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_gender == Gender.Male, "Männlich", GUI.skin.button)) _gender = Gender.Male;
            if (GUILayout.Toggle(_gender == Gender.Female, "Weiblich", GUI.skin.button)) _gender = Gender.Female;
            GUILayout.EndHorizontal();
            var o = rd.Appearance;
            string[] labels = { "Hautfarbe", "Gesicht", "Frisur", "Haarfarbe", "Bart", "Augenfarbe", "Narben", "Tätowierung" };
            int[] max = { o.SkinColors, o.Faces, o.HairStyles, o.HairColors, o.Beards, o.EyeColors, o.Scars, o.Tattoos };
            for (int i = 0; i < labels.Length; i++)
            {
                if (max[i] <= 0) { _look[i] = 0; continue; }
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{labels[i]}: {_look[i] + 1}/{max[i]}", GUILayout.Width(170));
                _look[i] = Mathf.RoundToInt(GUILayout.HorizontalSlider(Mathf.Min(_look[i], max[i] - 1), 0, max[i] - 1, GUILayout.Width(260)));
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Körpergröße: {_height:0.00}", GUILayout.Width(170));
            _height = GUILayout.HorizontalSlider(Mathf.Clamp(_height, rd.MinHeight, rd.MaxHeight), rd.MinHeight, rd.MaxHeight, GUILayout.Width(260));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(60));
            _newName = GUILayout.TextField(_newName, 12, GUILayout.Width(200));
            if (GUILayout.Button("Zufällig", GUILayout.Width(90))) RandomName();
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Zurück", GUILayout.Height(32))) _screen = Screen.Characters;
            if (GUILayout.Button("Erschaffen", GUILayout.Height(32)))
                Client.Send(new C_CreateCharacter
                {
                    Name = _newName, Race = _race, Class = _class, Gender = _gender,
                    Appearance = new AppearanceData
                    {
                        SkinColor = (byte)_look[0], Face = (byte)_look[1], HairStyle = (byte)_look[2], HairColor = (byte)_look[3], Beard = (byte)_look[4],
                        EyeColor = (byte)_look[5], Scar = (byte)_look[6], Tattoo = (byte)_look[7], Height = _height,
                    },
                });
            GUILayout.EndHorizontal();
            GUILayout.Label(_status, _wrap);
            GUILayout.EndArea();
        }

        private void FixClass()
        {
            var rd = Content.Races[_race];
            if (!rd.Classes.Contains(_class)) _class = rd.Classes[0];
            _height = (rd.MinHeight + rd.MaxHeight) / 2;
        }

        private void RandomName()
        {
            var rd = Content.Races[_race];
            var rng = new System.Random();
            var s = rd.NameStarts[rng.Next(rd.NameStarts.Count)] + rd.NameEnds[rng.Next(rd.NameEnds.Count)];
            _newName = char.ToUpper(s[0]) + s.Substring(1).ToLower();
        }

        private static string RoleName(Role r) => r switch { Role.Tank => "Tank", Role.Healer => "Heiler", Role.MeleeDps => "Nahkampf", _ => "Fernkampf" };

        // ================================================================== Spielwelt

        private void DrawWorld()
        {
            var me = Client.Me;
            if (me == null) return;
            DrawUnitFrame(new Rect(10, 10, 240, 70), me, true);
            var target = Client.State.Get(me.TargetId);
            if (target != null)
            {
                DrawUnitFrame(new Rect(260, 10, 240, 70), target, false);
                if (Client.Group.Count > 0 && target.Kind != EntityKind.Player)
                {
                    // Markierungen: Totenkopf (zuerst), Kreuz (danach), Mond (kontrollieren), entfernen
                    var marks = new (byte m, string label)[] { (8, "☠"), (7, "✖"), (5, "☾"), (0, "–") };
                    for (int i = 0; i < marks.Length; i++)
                        if (GUI.Button(Box(new Rect(260 + i * 30, 82, 28, 22)), marks[i].label))
                        {
                            byte m = marks[i].m;
                            if (m == 0 && Client.Markers.TryGetValue(target.Id, out var cur)) Client.SetMarker((byte)cur, 0);
                            else if (m != 0) Client.SetMarker(m, target.Id);
                        }
                }
            }
            DrawGroupFrames();
            DrawInfo();
            DrawActionBar(me);
            DrawBotCommandBar(me);
            DrawCastBar(me);
            DrawXpBar();
            DrawChat();
            DrawMenuButtons();
            DrawFloatingText();
            DrawBuffs(me);
            if (Client.State.DeathState != DeathState.Alive) DrawDeath();
            if (Client.PendingInviteFrom != null) DrawInvite();
            if (_showQuests) DrawQuestLog();
            if (_showBags) DrawBags();
            if (_showTalents) DrawTalents();
            if (_showGroup) DrawGroupWindow();
            if (_showBook) DrawSpellBook();
            if (_npc != null) DrawNpc();
            DrawEconomyWindows();
            if (_showMounts) DrawMountJournal();
            if (_showSocial) DrawSocial();
            if (Client.PendingGuildInvite != null) DrawGuildInvite();
        }

        private void DrawUnitFrame(Rect r, ClientEntity e, bool me)
        {
            GUI.Box(Box(r), "");
            string cls = e.Kind == EntityKind.Player ? Content.Classes.TryGetValue(e.Class, out var c) ? c.Name : "" : e.Title;
            string elite = (e.Flags & UnitFlags.Boss) != 0 ? " <color=magenta>Boss</color>" : (e.Flags & UnitFlags.Elite) != 0 ? " <color=orange>Elite</color>" : (e.Flags & UnitFlags.Rare) != 0 ? " <color=silver>Selten</color>" : "";
            GUI.Label(new Rect(r.x + 6, r.y + 2, r.width - 12, 20), $"<b>{e.Name}</b> ({e.Level}){elite} {cls}{(e.IsBot ? " <color=cyan>KI</color>" : "")}", _small);
            Bar(new Rect(r.x + 6, r.y + 22, r.width - 12, 18), e.HealthPct, new Color(0.1f, 0.7f, 0.1f), $"{e.Health} / {e.MaxHealth}");
            if (e.MaxPower > 0)
            {
                var pc = e.PowerType == PowerType.Rage ? new Color(0.8f, 0.1f, 0.1f) : e.PowerType == PowerType.Energy ? new Color(0.9f, 0.8f, 0.1f) : new Color(0.15f, 0.35f, 0.9f);
                Bar(new Rect(r.x + 6, r.y + 42, r.width - 12, 14), e.Power / (float)Mathf.Max(1, e.MaxPower), pc, $"{e.Power} / {e.MaxPower}");
            }
            if (!me && e.Casting != null)
            {
                float t = Mathf.Clamp01((float)(Client.Now - e.CastStartedAt) / Mathf.Max(0.1f, e.Casting.CastTime));
                Bar(new Rect(r.x + 6, r.y + r.height + 2, r.width - 12, 14), t, new Color(1f, 0.7f, 0.1f), AbilityName(e.Casting.AbilityId));
            }
        }

        private static void Bar(Rect r, float pct, Color color, string text)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(pct), r.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(r, text, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 });
        }

        private void DrawGroupFrames()
        {
            float y = 90;
            foreach (var m in Client.Group.Where(m => m.EntityId != Client.State.MyId))
            {
                var r = Box(new Rect(10, y, 200, 34));
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 4, r.y, 190, 16), $"{m.Name} ({m.Level}){(m.IsBot ? " <color=cyan>KI " + RoleName(m.Role) + "</color>" : "")}", _small);
                Bar(new Rect(r.x + 4, r.y + 16, 192, 14), m.Health / (float)Mathf.Max(1, m.MaxHealth), new Color(0.1f, 0.7f, 0.1f), $"{m.Health}/{m.MaxHealth}");
                if (Event.current.type == EventType.MouseUp && r.Contains(Event.current.mousePosition)) Client.Target(m.EntityId);
                y += 38;
            }
        }

        private void DrawInfo()
        {
            var r = Box(new Rect(UnityEngine.Screen.width - 260, 10, 250, 64));
            GUI.Box(r, "");
            var ws = Client.State.World;
            string time = ws != null ? $"{(int)ws.GameHour:00}:{(int)(ws.GameHour % 1 * 60):00}" : "--:--";
            string weather = ws?.Weather switch { Weather.Rain => "Regen", Weather.Snow => "Schnee", Weather.Fog => "Nebel", Weather.Storm => "Sturm", _ => "Klar" };
            var me = Client.Me!;
            GUI.Label(new Rect(r.x + 6, r.y + 2, 240, 60), $"<b>{ZoneName(Client.ZoneId)}</b>\n{time} Uhr · {weather} · {Client.Ping} ms\n{me.Position.X:0} / {me.Position.Z:0}", _small);
        }

        private List<string> ActionAbilities() =>
            Client.SpellBook.Keys.Where(k => Content.AbilityRanks.ContainsKey(k) && !k.StartsWith("item_"))
                .OrderBy(k => Content.AbilityRanks[k][0].Level).ThenBy(k => k).Take(12).ToList();

        public void UseActionSlot(int slot)
        {
            var list = ActionAbilities();
            if (slot >= list.Count || Client.Me == null) return;
            Client.Cast(list[slot], Client.Me.TargetId);
        }

        private void DrawActionBar(ClientEntity me)
        {
            var list = ActionAbilities();
            float w = 52, total = list.Count * (w + 4);
            float x0 = UnityEngine.Screen.width / 2 - total / 2, y = UnityEngine.Screen.height - 86;
            for (int i = 0; i < list.Count; i++)
            {
                var ab = Content.GetAbilityRank(list[i], Client.SpellBook[list[i]])!;
                var r = Box(new Rect(x0 + i * (w + 4), y, w, w));
                string cd = Client.Cooldowns.TryGetValue(ab.Id, out var until) ? $"\n<color=red>{until - Client.Now:0}</color>" : "";
                if (GUI.Button(r, new GUIContent($"<size=9>{(i + 1) % 10}</size>\n<size=9>{Short(ab.Name)}</size>{cd}", $"{ab.Name} (Rang {ab.Rank})\n{ab.Description}\nKosten: {ab.Cost} · Zauberzeit: {(ab.CastTime > 0 ? ab.CastTime + " s" : "sofort")}{(ab.Cooldown > 0 ? " · Abklingzeit " + ab.Cooldown + " s" : "")}"), new GUIStyle(GUI.skin.button) { richText = true, wordWrap = true }))
                    Client.Cast(ab.Id, me.TargetId);
            }
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                var tr = new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y - 90, 300, 85);
                GUI.Box(tr, "");
                GUI.Label(new Rect(tr.x + 6, tr.y + 4, 290, 80), GUI.tooltip, _wrap);
            }
        }

        // ================================================================== KI-Befehlsleiste (Kapitel 9.7)

        private int _commandBot; // 0 = alle KI-Mitspieler
        private static readonly (byte cmd, string label, string tip)[] BotCommands =
        {
            (1, "Folgen", "Folgt mir"), (2, "Halten", "Position halten"), (3, "Angriff", "Greift mein Ziel an"), (7, "Verteidigen", "Nur angreifen, was uns angreift"),
            (6, "Passiv", "Nicht kämpfen"), (12, "Fokus", "Alle auf mein Ziel"), (13, "Unterbrechen", "Zauber des Gegners unterbrechen"), (14, "Heilen", "Heilt mich"),
            (15, "Mana", "Mana auffüllen"), (16, "Verteilen", "Flächenschaden meiden"), (17, "Kontrolle", "Mein Ziel kontrollieren (Mond)"), (18, "Rückzug", "Alle zurückziehen"),
        };

        private void DrawBotCommandBar(ClientEntity me)
        {
            var bots = Client.Group.Where(m => m.IsBot).ToList();
            if (bots.Count == 0 || Client.GroupLeaderId != me.Id) return;
            float w = 74, h = 24, total = BotCommands.Length * (w + 2);
            float x0 = UnityEngine.Screen.width / 2 - total / 2, y = UnityEngine.Screen.height - 146;
            // Empfänger: alle oder ein einzelner Bot
            var names = new List<string> { "Alle" };
            names.AddRange(bots.Select(b => b.Name));
            int sel = System.Math.Max(0, bots.FindIndex(b => b.EntityId == _commandBot) + 1);
            var rr = Box(new Rect(x0, y - h - 2, System.Math.Min(total, names.Count * 110), h));
            sel = GUI.Toolbar(rr, sel, names.ToArray());
            _commandBot = sel == 0 ? 0 : bots[sel - 1].EntityId;
            for (int i = 0; i < BotCommands.Length; i++)
            {
                var (cmd, label, tip) = BotCommands[i];
                if (GUI.Button(Box(new Rect(x0 + i * (w + 2), y, w, h)), new GUIContent(label, tip)))
                    Client.BotCommand(cmd, _commandBot, me.TargetId);
            }
        }

        public static string MarkerSymbol(int m) => m switch
        {
            8 => "<color=white>☠</color>", 7 => "<color=red>✖</color>", 6 => "<color=#4af>■</color>", 5 => "<color=#cdf>☾</color>",
            4 => "<color=green>▲</color>", 3 => "<color=#c4f>◆</color>", 2 => "<color=orange>●</color>", 1 => "<color=yellow>★</color>", _ => "",
        };

        private static string Short(string s) => s.Length <= 12 ? s : s.Substring(0, 11) + "…";

        private void DrawCastBar(ClientEntity me)
        {
            if (me.Casting == null) return;
            float t = Mathf.Clamp01((float)(Client.Now - me.CastStartedAt) / Mathf.Max(0.1f, me.Casting.CastTime));
            Bar(new Rect(UnityEngine.Screen.width / 2 - 150, UnityEngine.Screen.height - 130, 300, 18), t, new Color(1f, 0.75f, 0.1f), AbilityName(me.Casting.AbilityId));
        }

        private string AbilityName(string rankedId) => rankedId == "interact" ? "Benutzen" : Content.Abilities.TryGetValue(rankedId, out var a) ? a.Name : rankedId;

        private void DrawXpBar()
        {
            var s = Client.State.Stats;
            if (s == null) return;
            float pct = s.XpToNext > 0 ? s.Xp / (float)s.XpToNext : 1;
            Bar(new Rect(UnityEngine.Screen.width / 2 - 300, UnityEngine.Screen.height - 26, 600, 14), pct, s.RestedXp > 0 ? new Color(0.25f, 0.4f, 0.9f) : new Color(0.6f, 0.2f, 0.7f),
                s.XpToNext > 0 ? $"Stufe {s.Level} · EP {s.Xp} / {s.XpToNext}{(s.RestedXp > 0 ? $" · Ruhebonus {s.RestedXp}" : "")}" : $"Stufe {s.Level} (Höchststufe)");
        }

        private void DrawBuffs(ClientEntity me)
        {
            float x = UnityEngine.Screen.width - 270;
            foreach (var a in me.Auras)
            {
                if (!Content.Auras.TryGetValue(a.AuraId, out var def)) continue;
                var r = Box(new Rect(x, 80, 64, 34));
                GUI.Box(r, new GUIContent($"<size=9>{Short(def.Name)}\n{(a.Remaining < 0 ? "" : a.Remaining.ToString("0") + " s")}</size>", def.Description), new GUIStyle(GUI.skin.box) { richText = true, normal = { textColor = def.IsDebuff ? Color.red : Color.white } });
                if (!def.IsDebuff && Event.current.type == EventType.MouseUp && Event.current.button == 1 && r.Contains(Event.current.mousePosition))
                    Client.Send(new C_CancelAura { AuraId = a.AuraId });
                x -= 68;
            }
        }

        private void DrawChat()
        {
            var r = Box(new Rect(10, UnityEngine.Screen.height - 250, 440, 200));
            GUI.Box(r, "");
            GUILayout.BeginArea(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 34));
            _scrollChat = GUILayout.BeginScrollView(new Vector2(0, float.MaxValue));
            foreach (var c in Client.ChatLog.Skip(Mathf.Max(0, Client.ChatLog.Count - 40)))
            {
                string col = c.Channel switch { ChatChannel.Party => "#7fb2ff", ChatChannel.Yell => "#ff5050", ChatChannel.Whisper => "#ff80ff", ChatChannel.General => "#ffc080", ChatChannel.System => "#ffff60", _ => "#ffffff" };
                string ch = c.Channel switch { ChatChannel.Party => "[Gruppe] ", ChatChannel.General => "[Allgemein] ", ChatChannel.Whisper => "[Flüstern] ", ChatChannel.Yell => "[Schreien] ", _ => "" };
                GUILayout.Label($"<color={col}>{ch}{(c.SenderName != "" ? $"{c.SenderName}{(c.SenderIsBot ? " (KI)" : "")}: " : "")}{c.Text}</color>", _wrap);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.SetNextControlName("chat");
            _chatInput = GUI.TextField(new Rect(r.x + 4, r.yMax - 26, r.width - 8, 22), _chatInput, 255);
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter))
            {
                if (_chatFocus) { SendChat(_chatInput); _chatInput = ""; GUI.FocusControl(null); }
                else GUI.FocusControl("chat");
                ev.Use();
            }
            _chatFocus = GUI.GetNameOfFocusedControl() == "chat";
        }

        /// <summary>Chatbefehle: /p Gruppe, /s Sagen, /y Schreien, /g Allgemein, /w Name Text.</summary>
        private void SendChat(string text)
        {
            text = text.Trim();
            if (text.Length == 0) return;
            var ch = Client.Group.Count > 1 ? ChatChannel.Party : ChatChannel.Say;
            string whisper = "";
            if (text.StartsWith("/"))
            {
                var parts = text.Split(new[] { ' ' }, 2);
                string body = parts.Length > 1 ? parts[1] : "";
                switch (parts[0].ToLowerInvariant())
                {
                    case "/p": ch = ChatChannel.Party; text = body; break;
                    case "/s": ch = ChatChannel.Say; text = body; break;
                    case "/y": ch = ChatChannel.Yell; text = body; break;
                    case "/g": ch = ChatChannel.General; text = body; break;
                    case "/w":
                        var wp = body.Split(new[] { ' ' }, 2);
                        ch = ChatChannel.Whisper; whisper = wp[0]; text = wp.Length > 1 ? wp[1] : ""; break;
                    case "/inv": Client.Send(new C_GroupInvite { Name = body }); return;
                }
            }
            Client.Send(new C_Chat { Channel = ch, Text = text, WhisperTo = whisper });
        }

        private void DrawMenuButtons()
        {
            var r = Box(new Rect(UnityEngine.Screen.width - 420, UnityEngine.Screen.height - 44, 410, 30));
            GUILayout.BeginArea(r);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Quests (L)")) _showQuests = !_showQuests;
            if (GUILayout.Button("Taschen (B)")) _showBags = !_showBags;
            if (GUILayout.Button("Zauber (K)")) _showBook = !_showBook;
            if (GUILayout.Button("Talente (N)")) _showTalents = !_showTalents;
            if (GUILayout.Button("Gruppe (P)")) _showGroup = !_showGroup;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawFloatingText()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var f in WorldRenderer.Instance.FloatingTexts)
            {
                float age = Time.time - f.Born;
                var p = cam.WorldToScreenPoint(f.WorldPos + Vector3.up * age * 1.2f);
                if (p.z < 0) continue;
                var style = new GUIStyle(_center) { fontSize = f.Big ? 22 : 15, normal = { textColor = new Color(f.Color.r, f.Color.g, f.Color.b, 1 - age / 1.6f) } };
                GUI.Label(new Rect(p.x - 60, UnityEngine.Screen.height - p.y - 12, 120, 24), f.Text, style);
            }
        }

        private void DrawToasts()
        {
            float y = UnityEngine.Screen.height * 0.22f;
            foreach (var (text, _) in _toasts.Skip(Mathf.Max(0, _toasts.Count - 4)))
            {
                GUI.Label(new Rect(UnityEngine.Screen.width / 2 - 300, y, 600, 24), $"<color=#ffe080>{text}</color>", _center);
                y += 24;
            }
        }

        private void DrawDeath()
        {
            var r = Box(new Rect(UnityEngine.Screen.width / 2 - 170, 120, 340, 110));
            GUILayout.BeginArea(r, GUI.skin.box);
            if (Client.State.DeathState == DeathState.Dead)
            {
                GUILayout.Label("Ihr seid gestorben.", _title);
                if (GUILayout.Button("Geist freilassen")) Client.Send(new C_ReleaseSpirit());
            }
            else
            {
                GUILayout.Label("Ihr seid ein Geist. Lauft zu Eurer Leiche oder sprecht mit dem Geisterheiler.", _wrap);
                var me = Client.Me!;
                float d = Vec3.DistanceXZ(me.Position, Client.State.CorpsePosition);
                GUILayout.Label($"Entfernung zur Leiche: {d:0} m", _small);
                GUI.enabled = d < 40;
                if (GUILayout.Button("Leichnam wiederbeleben")) Client.Send(new C_ReclaimCorpse());
                GUI.enabled = true;
            }
            GUILayout.EndArea();
        }

        private void DrawInvite()
        {
            var r = Box(new Rect(UnityEngine.Screen.width / 2 - 160, 240, 320, 80));
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label($"{Client.PendingInviteFrom} lädt Euch in eine Gruppe ein.", _wrap);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Annehmen")) { Client.Send(new C_GroupAccept { Accept = true }); ClearInvite(); }
            if (GUILayout.Button("Ablehnen")) { Client.Send(new C_GroupAccept { Accept = false }); ClearInvite(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ClearInvite() => Client.ClearInvite();

        // ------------------------------------------------------------ Fenster

        private Rect Window(string title, float w, float h)
        {
            var r = Box(new Rect(UnityEngine.Screen.width / 2 - w / 2, UnityEngine.Screen.height / 2 - h / 2, w, h));
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 8, r.y + 4, w - 40, 24), title, _title);
            return new Rect(r.x + 8, r.y + 30, w - 16, h - 38);
        }

        private void DrawQuestLog()
        {
            var area = Window("Questlog", 520, 440);
            GUILayout.BeginArea(area);
            _scrollQuest = GUILayout.BeginScrollView(_scrollQuest);
            var log = Client.State.QuestLog;
            if (log == null || log.Quests.Count == 0) GUILayout.Label("Keine aktiven Quests. Sprecht mit NPCs, die ein gelbes ! tragen.");
            else foreach (var q in log.Quests)
            {
                var def = Content.Quests[q.QuestId];
                string state = q.State == QuestState.Complete ? " <color=lime>(abgeschlossen)</color>" : q.State == QuestState.Failed ? " <color=red>(fehlgeschlagen)</color>" : "";
                GUILayout.Label($"<b>[{def.Level}] {def.Title}</b>{state}", _wrap);
                GUILayout.Label(def.ObjectivesText, _wrap);
                for (int i = 0; i < def.Objectives.Count; i++) GUILayout.Label($"   • {def.Objectives[i].Description}: {q.Counts[i]}/{def.Objectives[i].Count}", _small);
                if (GUILayout.Button("Aufgeben", GUILayout.Width(90))) Client.Send(new C_QuestAbandon { QuestId = q.QuestId });
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Schließen")) _showQuests = false;
            GUILayout.EndArea();
        }

        private void DrawBags()
        {
            var area = Window("Charakter & Taschen", 560, 480);
            GUILayout.BeginArea(area);
            var inv = Client.State.Inventory;
            var st = Client.State.Stats;
            if (st != null)
                GUILayout.Label($"Stärke {st.Strength:0} · Beweglichkeit {st.Agility:0} · Ausdauer {st.Stamina:0} · Intelligenz {st.Intellect:0} · Willenskraft {st.Spirit:0}\n" +
                                $"Rüstung {st.Armor} · Angriffskraft {st.AttackPower} · Zaubermacht {st.SpellPower} · Krit {st.MeleeCrit:0.0}% / {st.SpellCrit:0.0}% · Ausweichen {st.Dodge:0.0}%", _wrap);
            if (inv != null)
            {
                GUILayout.Label($"Geld: {Money(inv.Money)}");
                GUILayout.Label("<b>Angelegt</b> (Klick legt ab)", _wrap);
                for (int i = 1; i < inv.Equipment.Count; i++)
                    if (inv.Equipment[i].ItemId != "" && GUILayout.Button($"{(EquipSlot)i}: {ItemLabel(inv.Equipment[i].ItemId)}", GUI.skin.label)) Client.Send(new C_UnequipItem { Slot = (EquipSlot)i });
                GUILayout.BeginHorizontal();
                for (int c = 0; c < inv.Containers.Count; c++)
                {
                    var bag = inv.Containers[c];
                    if (GUILayout.Button(bag.ItemId == "" ? "– leer –" : ItemLabel(bag.ItemId), GUILayout.Width(120)) && bag.ItemId != "") Client.Send(new C_UnequipBag { ContainerSlot = c });
                }
                if (GUILayout.Button("Sortieren")) Client.Send(new C_SortBags());
                GUILayout.EndHorizontal();
                GUILayout.Label($"<b>Taschen</b> {inv.Bag.Count(b => b.ItemId != "")}/{inv.BagSize} (Klick: benutzen/anlegen · Rechtsklick: verkaufen, einlagern, anhängen oder versteigern – je nach offenem Fenster)", _wrap);
                for (int i = 0; i < inv.Bag.Count; i++)
                {
                    if (inv.Bag[i].ItemId == "") continue;
                    var rr = GUILayoutUtility.GetRect(new GUIContent("x"), GUI.skin.button);
                    if (GUI.Button(rr, $"{inv.Bag[i].Count}× {ItemLabel(inv.Bag[i].ItemId)}"))
                    {
                        if (Event.current.button == 1 && _bankOpen && Client.Bank != null) Client.Send(new C_BankDeposit { NpcId = Client.Bank.NpcId, BagIndex = i });
                        else if (Event.current.button == 1 && _mailOpen) ToggleAttachment(i);
                        else if (Event.current.button == 1 && _ahOpen) _ahSellIndex = i;
                        else if (Event.current.button == 1 && _npc != null) Client.Send(new C_SellItem { NpcId = _npc.NpcId, BagIndex = i });
                        else if (Content.Items.TryGetValue(inv.Bag[i].ItemId, out var bagDef) && bagDef.Category == ItemCategory.Container) Client.Send(new C_EquipBag { BagIndex = i });
                        else Client.Send(new C_UseItem { BagIndex = i, TargetId = Client.Me?.TargetId ?? 0 });
                    }
                }
            }
            if (GUILayout.Button("Schließen")) _showBags = false;
            GUILayout.EndArea();
        }

        private string ItemLabel(string id)
        {
            if (!Content.Items.TryGetValue(id, out var it)) return id;
            string col = it.Quality switch { ItemQuality.Poor => "#9d9d9d", ItemQuality.Uncommon => "#1eff00", ItemQuality.Rare => "#0070dd", ItemQuality.Epic => "#a335ee", ItemQuality.Legendary => "#ff8000", _ => "#ffffff" };
            return $"<color={col}>{it.Name}</color>";
        }

        private static string Money(long c) => $"{c / 10000}g {c / 100 % 100}s {c % 100}k";

        private void DrawSpellBook()
        {
            var area = Window("Zauberbuch", 560, 460);
            GUILayout.BeginArea(area);
            _scrollBook = GUILayout.BeginScrollView(_scrollBook);
            foreach (var kv in Client.SpellBook.OrderBy(k => Content.AbilityRanks.TryGetValue(k.Key, out var l) ? l[0].Level : 0))
            {
                var ab = Content.GetAbilityRank(kv.Key, kv.Value);
                if (ab == null) continue;
                GUILayout.Label($"<b>{ab.Name}</b> (Rang {kv.Value}) – {ab.Description}", _wrap);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Schließen")) _showBook = false;
            GUILayout.EndArea();
        }

        private void DrawTalents()
        {
            var me = Client.Me;
            if (me == null || !Content.Classes.TryGetValue(me.Class, out var cd)) return;
            var area = Window($"Talente – frei: {Client.State.Talents?.FreePoints ?? 0}", 900, 520);
            GUILayout.BeginArea(area);
            _scrollTalents = GUILayout.BeginScrollView(_scrollTalents);
            GUILayout.BeginHorizontal();
            foreach (var treeId in cd.TalentTrees)
            {
                var tree = Content.TalentTrees[treeId];
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(280));
                GUILayout.Label($"<b>{tree.Name}</b>", _wrap);
                GUILayout.Label(tree.Description, _small);
                foreach (var t in tree.Talents.OrderBy(t => t.Tier))
                {
                    int idx = Client.State.Talents?.TalentIds.IndexOf(t.Id) ?? -1;
                    int rank = idx >= 0 ? Client.State.Talents!.Ranks[idx] : 0;
                    if (GUILayout.Button(new GUIContent($"[{t.Tier}] {t.Name} {rank}/{t.MaxRank}", t.Description))) Client.Send(new C_LearnTalent { TalentId = t.Id });
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.Label(GUI.tooltip, _wrap);
            if (GUILayout.Button("Schließen")) _showTalents = false;
            GUILayout.EndArea();
        }

        private int _botDifficulty;

        private void DrawGroupWindow()
        {
            var area = Window("Gruppe", 600, 480);
            GUILayout.BeginArea(area);
            foreach (var m in Client.Group)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{m.Name} – Stufe {m.Level} {Content.Classes[m.Class].Name}{(m.IsBot ? $" <color=cyan>(KI, {RoleName(m.Role)})</color>" : "")}{(m.EntityId == Client.GroupLeaderId ? " ★" : "")}", _wrap, GUILayout.Width(420));
                if (m.IsBot && GUILayout.Button(new GUIContent("★", "Als bevorzugten Begleiter merken"), GUILayout.Width(30))) Client.Send(new C_BotFavorite { EntityId = m.EntityId, Favorite = true });
                if (m.IsBot && GUILayout.Button("Entlassen")) Client.Send(new C_BotDismiss { EntityId = m.EntityId });
                GUILayout.EndHorizontal();
            }
            if (Client.Group.Count > 0) DrawLootRule();
            if (Client.Group.Count > 0 && GUILayout.Button("Gruppe verlassen")) Client.Send(new C_GroupLeave());
            GUILayout.Space(8);
            GUILayout.Label("<b>KI-Mitspieler suchen</b> – Bots füllen fehlende Rollen, kämpfen, heilen, folgen und verstehen Gruppenchat-Befehle wie „Tank, greif den Boss an.“, „Heiler, bitte Mana auffüllen.“, „Magier, kontrolliere den linken Gegner.“ oder „Alle zurückziehen.“ Mit ★ merkt Ihr Euch Lieblingsbegleiter – sie entwickeln sich weiter und erinnern sich an gemeinsame Abenteuer.", _wrap);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Schwierigkeit:", GUILayout.Width(90));
            _botDifficulty = GUILayout.Toolbar(_botDifficulty, new[] { "Beliebig", "Anfänger", "Normal", "Erfahren", "Veteran" });
            GUILayout.EndHorizontal();
            byte diff = _botDifficulty == 0 ? (byte)255 : (byte)(_botDifficulty - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Alle Rollen")) Client.Send(new C_BotSearch { Difficulty = diff });
            if (GUILayout.Button("Tank")) Client.Send(new C_BotSearch { Role = (byte)Role.Tank, Difficulty = diff });
            if (GUILayout.Button("Heiler")) Client.Send(new C_BotSearch { Role = (byte)Role.Healer, Difficulty = diff });
            if (GUILayout.Button("Nahkampf")) Client.Send(new C_BotSearch { Role = (byte)Role.MeleeDps, Difficulty = diff });
            if (GUILayout.Button("Fernkampf")) Client.Send(new C_BotSearch { Role = (byte)Role.RangedDps, Difficulty = diff });
            GUILayout.EndHorizontal();
            foreach (var c in Client.BotCandidates.ToList())
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{c.Name}</b> – {Content.Races[c.Race].Name} {Content.Classes[c.Class].Name}, Stufe {c.Level}, {RoleName(c.Role)} <i>({c.Personality})</i>\n„{c.Greeting}“", _wrap, GUILayout.Width(450));
                if (GUILayout.Button("Einladen", GUILayout.Height(36))) { Client.Send(new C_BotInvite { CandidateId = c.CandidateId }); Client.BotCandidates.Remove(c); }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Schließen")) _showGroup = false;
            GUILayout.EndArea();
        }

        private void DrawNpc()
        {
            var ni = _npc!;
            if (!Content.Creatures.TryGetValue(ni.TemplateId, out var npc)) { _npc = null; return; }
            var area = Window(npc.Name + (npc.Title != "" ? $" <{npc.Title}>" : ""), 560, 520);
            GUILayout.BeginArea(area);
            _scrollNpc = GUILayout.BeginScrollView(_scrollNpc);
            if (npc.Gossip != "") GUILayout.Label($"<i>{npc.Gossip}</i>", _wrap);
            foreach (var id in ni.QuestsToTurnIn.ToList())
            {
                var q = Content.Quests[id];
                GUILayout.Label($"<color=yellow>?</color> <b>{q.Title}</b>", _wrap);
                GUILayout.Label(q.CompletionText, _wrap);
                if (q.RewardChoices.Count > 0)
                {
                    GUILayout.Label("Wählt eine Belohnung:");
                    for (int i = 0; i < q.RewardChoices.Count; i++)
                        if (GUILayout.Toggle(_choice == i, ItemLabel(q.RewardChoices[i].Item), GUI.skin.button)) _choice = i;
                }
                if (GUILayout.Button("Quest abschließen")) { Client.Send(new C_QuestTurnIn { NpcId = ni.NpcId, QuestId = id, ChoiceIndex = _choice }); Client.Send(new C_Interact { EntityId = ni.NpcId }); }
            }
            foreach (var id in ni.QuestsAvailable.ToList())
            {
                var q = Content.Quests[id];
                GUILayout.Label($"<color=yellow>!</color> <b>[{q.Level}] {q.Title}</b>", _wrap);
                GUILayout.Label(q.Text, _wrap);
                GUILayout.Label("<b>Ziele:</b> " + q.ObjectivesText, _wrap);
                var rewards = new List<string>();
                if (q.RewardXp > 0) rewards.Add($"{q.RewardXp} EP");
                if (q.RewardMoney > 0) rewards.Add(Money(q.RewardMoney));
                rewards.AddRange(q.RewardItems.Select(r => ItemLabel(r.Item)));
                if (q.RewardChoices.Count > 0) rewards.Add("Wahl: " + string.Join(" / ", q.RewardChoices.Select(r => ItemLabel(r.Item))));
                GUILayout.Label("<b>Belohnung:</b> " + string.Join(", ", rewards), _wrap);
                if (q.SuggestedPlayers > 1) GUILayout.Label($"<color=orange>Empfohlen: Gruppe mit {q.SuggestedPlayers} Spielern (KI-Mitspieler helfen).</color>", _wrap);
                if (GUILayout.Button("Annehmen")) { Client.Send(new C_QuestAccept { NpcId = ni.NpcId, QuestId = id }); ni.QuestsAvailable.Remove(id); }
            }
            foreach (var id in ni.QuestsInProgress) GUILayout.Label($"<color=grey>?</color> {Content.Quests[id].Title}: {Content.Quests[id].ProgressText}", _wrap);
            if (ni.VendorItems.Count > 0)
            {
                GUILayout.Label("<b>Waren</b> (Rechtsklick in den Taschen verkauft)", _wrap);
                foreach (var it in ni.VendorItems)
                    if (GUILayout.Button($"{ItemLabel(it)} – {Money(Content.Items[it].BuyPrice)}")) Client.Send(new C_BuyItem { NpcId = ni.NpcId, ItemId = it, Count = 1 });
            }
            if (ni.Trainable.Count > 0)
            {
                GUILayout.Label("<b>Lernbare Fähigkeiten</b>", _wrap);
                foreach (var r in ni.Trainable.ToList())
                {
                    var ab = Content.Abilities[r];
                    if (GUILayout.Button($"{ab.Name} (Rang {ab.Rank}) – {Money(ab.TrainingCost)}")) { Client.Send(new C_TrainAbility { NpcId = ni.NpcId, AbilityId = ab.Id }); ni.Trainable.Remove(r); }
                }
            }
            foreach (var dest in ni.FlightDestinations)
                if (GUILayout.Button($"Flug nach {Content.TravelNodes[dest].Name}")) { Client.Send(new C_TakeFlight { NpcId = ni.NpcId, Destination = dest }); _npc = null; }
            if (ni.CanResurrect && GUILayout.Button("Wiederbeleben lassen (Haltbarkeitsverlust, Wiederbelebungsschwäche)")) Client.Send(new C_SpiritHealerResurrect { NpcId = ni.NpcId });
            if ((npc.Roles & NpcRole.RidingTrainer) != 0 && GUILayout.Button("Reiten lernen")) Client.Send(new C_LearnRiding { NpcId = ni.NpcId });
            if ((npc.Roles & NpcRole.Auctioneer) != 0 && GUILayout.Button("Auktionshaus öffnen")) { OpenAuctionHouse(ni.NpcId); _npc = null; }
            if ((npc.Roles & NpcRole.ClassTrainer) != 0 && npc.TrainerClass == (Client.Me?.Class ?? CharClass.None) && GUILayout.Button("Talente zurücksetzen …"))
                Client.Send(new C_ResetTalents { NpcId = ni.NpcId, Confirm = false });
            if (ni.IsInnkeeper) GUILayout.Label("<i>Euer Ruhestein ist an dieses Gasthaus gebunden. Hier sammelt Ihr Ruhebonus.</i>", _wrap);
            GUILayout.EndScrollView();
            if (GUILayout.Button("Auf Wiedersehen")) _npc = null;
            GUILayout.EndArea();
        }
    }
}
