using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// HUD-Grundlagen (Kapitel 28/29): Skalierung, verschieb- und größenveränderbare Fenster, vier Aktionsleisten
    /// mit Ziehen und Ablegen, frei belegbare Tasten. Alles landet in <see cref="GameSettings"/> und wird am Charakter gespeichert.
    /// </summary>
    public sealed partial class GameUI
    {
        private GameSettings Settings => Client.Settings;
        private float Scale => Client.InWorld ? Settings.UiScale : 1f;
        /// <summary>Bildschirmgröße in Oberflächeneinheiten (nach Skalierung).</summary>
        private float ScreenW => UnityEngine.Screen.width / Scale;
        private float ScreenH => UnityEngine.Screen.height / Scale;

        private float _settingsDirtyAt = -1;
        private const string LocalSettingsKey = "er_last_settings";

        /// <summary>Änderung merken; gespeichert wird gesammelt nach zwei Sekunden Ruhe.</summary>
        private void SettingsChanged() => _settingsDirtyAt = Time.unscaledTime;

        private void UpdateSettingsPersistence()
        {
            if (_settingsDirtyAt < 0 || Time.unscaledTime - _settingsDirtyAt < 2f || !Client.InWorld) return;
            _settingsDirtyAt = -1;
            SaveSettingsNow();
        }

        private void SaveSettingsNow()
        {
            if (!Client.InWorld) return;
            Client.SaveSettings();
            try { PlayerPrefs.SetString(LocalSettingsKey, Client.Settings.ToJson()); PlayerPrefs.Save(); }
            catch (System.Exception) { /* lokale Kopie ist nur Komfort */ }
        }

        /// <summary>Neuer Charakter ohne gespeicherte Einstellungen: Tasten und Optionen des zuletzt gespielten übernehmen, Leisten leer.</summary>
        private void OnSettingsReceived(S_Settings st)
        {
            _spellBookSeen = false;
            if (st.Json.Length > 0 || !PlayerPrefs.HasKey(LocalSettingsKey)) return;
            var last = GameSettings.FromJson(PlayerPrefs.GetString(LocalSettingsKey));
            last.ActionBars = new ActionBarSet();
            last.Windows.Clear();
            Client.ApplySettings(last);
            SettingsChanged();
        }

        // ------------------------------------------------------------ Fenster

        private string? _dragWindow;
        private bool _resizingWindow;
        private Vector2 _dragStart;
        private WindowLayout? _dragLayoutStart;
        private int _windowCascade;
        private readonly Dictionary<string, System.Action> _windowClosers = new Dictionary<string, System.Action>();

        /// <summary>
        /// Zeichnet einen Fensterrahmen und liefert den Inhaltsbereich. Lage und Größe werden je Fenster (Aufrufer)
        /// gespeichert; ziehen an der Titelleiste verschiebt, die Ecke unten rechts ändert die Größe.
        /// </summary>
        private Rect Window(string title, float w, float h, [CallerMemberName] string id = "")
        {
            var l = Settings.Window(id, -1, -1, w, h);
            if (l.X < 0 || l.Y < 0)
            {
                l.X = ScreenW / 2 - w / 2 + (_windowCascade % 5) * 24;
                l.Y = ScreenH / 2 - h / 2 + (_windowCascade % 5) * 24;
                _windowCascade++;
            }
            l.Width = Mathf.Clamp(l.Width <= 0 ? w : l.Width, Mathf.Min(w, 220), ScreenW);
            l.Height = Mathf.Clamp(l.Height <= 0 ? h : l.Height, Mathf.Min(h, 140), ScreenH);
            l.X = Mathf.Clamp(l.X, 0, Mathf.Max(0, ScreenW - 60));
            l.Y = Mathf.Clamp(l.Y, 0, Mathf.Max(0, ScreenH - 30));
            var r = Box(new Rect(l.X, l.Y, l.Width, l.Height));
            GUI.Box(r, "");
            var titleBar = new Rect(r.x, r.y, r.width - 30, 28);
            var grip = new Rect(r.xMax - 16, r.yMax - 16, 16, 16);
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && (titleBar.Contains(ev.mousePosition) || grip.Contains(ev.mousePosition)))
            {
                _dragWindow = id;
                _resizingWindow = grip.Contains(ev.mousePosition);
                _dragStart = ev.mousePosition;
                _dragLayoutStart = new WindowLayout { X = l.X, Y = l.Y, Width = l.Width, Height = l.Height };
                ev.Use();
            }
            else if (_dragWindow == id && ev.type == EventType.MouseDrag && _dragLayoutStart != null)
            {
                var d = ev.mousePosition - _dragStart;
                if (_resizingWindow) { l.Width = _dragLayoutStart.Width + d.x; l.Height = _dragLayoutStart.Height + d.y; }
                else { l.X = _dragLayoutStart.X + d.x; l.Y = _dragLayoutStart.Y + d.y; }
                ev.Use();
            }
            else if (_dragWindow == id && ev.rawType == EventType.MouseUp)
            {
                _dragWindow = null;
                SettingsChanged();
            }
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 48, 24), title, _title);
            if (GUI.Button(new Rect(r.xMax - 26, r.y + 4, 22, 20), "×") && _windowClosers.TryGetValue(id, out var close)) close();
            GUI.Label(grip, "◢", _small);
            return new Rect(r.x + 8, r.y + 30, r.width - 16, r.height - 46);
        }

        private void RegisterWindowClosers()
        {
            _windowClosers["DrawQuestLog"] = () => _showQuests = false;
            _windowClosers["DrawBags"] = () => _showBags = false;
            _windowClosers["DrawSpellBook"] = () => _showBook = false;
            _windowClosers["DrawTalents"] = () => _showTalents = false;
            _windowClosers["DrawGroupWindow"] = () => _showGroup = false;
            _windowClosers["DrawNpc"] = () => _npc = null;
            _windowClosers["DrawBank"] = () => _bankOpen = false;
            _windowClosers["DrawMail"] = () => _mailOpen = false;
            _windowClosers["DrawAuctionHouse"] = () => _ahOpen = false;
            _windowClosers["DrawMountJournal"] = () => _showMounts = false;
            _windowClosers["DrawSocial"] = () => _showSocial = false;
            _windowClosers["DrawProfessions"] = () => _showProfessions = false;
            _windowClosers["DrawCharacter"] = () => _showCharacter = false;
            _windowClosers["DrawWorldMap"] = () => _showMap = false;
            _windowClosers["DrawOptions"] = () => CloseOptions();
        }

        private void CloseAllWindows()
        {
            foreach (var close in _windowClosers.Values) close();
        }

        // ------------------------------------------------------------ Tasten

        private bool _hideUi;

        private void HandleWorldHotkeys()
        {
            var k = Settings.Keys;
            if (_capturingAction != null) return; // Tastenbelegung im Optionsfenster nimmt gerade eine Taste auf
            if (UiInput.Down(k, "quest_log")) _showQuests = !_showQuests;
            if (UiInput.Down(k, "bags") || UiInput.Down(k, "all_bags")) _showBags = !_showBags;
            if (UiInput.Down(k, "character")) _showCharacter = !_showCharacter;
            if (UiInput.Down(k, "talents")) _showTalents = !_showTalents;
            if (UiInput.Down(k, "group")) _showGroup = !_showGroup;
            if (UiInput.Down(k, "spellbook")) _showBook = !_showBook;
            if (UiInput.Down(k, "world_map")) _showMap = !_showMap;
            if (UiInput.Down(k, "mount")) Client.Send(new C_Mount());
            if (UiInput.Down(k, "mounts")) _showMounts = !_showMounts;
            if (UiInput.Down(k, "professions")) _showProfessions = !_showProfessions;
            if (UiInput.Down(k, "social")) { _showSocial = !_showSocial; if (_showSocial) { Client.Send(new C_FriendList()); Client.Send(new C_GuildRoster()); } }
            if (UiInput.Down(k, "toggle_ui")) _hideUi = !_hideUi;
            if (UiInput.Down(k, "minimap_zoom_in")) { Settings.MinimapZoom = Mathf.Min(4f, Settings.MinimapZoom * 1.25f); SettingsChanged(); }
            if (UiInput.Down(k, "minimap_zoom_out")) { Settings.MinimapZoom = Mathf.Max(0.5f, Settings.MinimapZoom / 1.25f); SettingsChanged(); }
            if (UiInput.Down(k, "reply") && _lastWhisperFrom != "") { _chatInput = $"/w {_lastWhisperFrom} "; _focusChat = true; }
            if (UiInput.Down(k, "menu"))
            {
                // Escape: erst Fenster schließen, dann Ziel lösen, dann Spielmenü
                if (AnyWindowOpen || _showOptions) CloseAllWindows();
                else if (Client.Me != null && Client.Me.TargetId != 0) { Client.Target(0); Client.Me.TargetId = 0; }
                else OpenOptions();
            }
            for (int b = 0; b < ActionBarSet.BarCount; b++)
                for (int s = 0; s < ActionBar.Slots; s++)
                    if (UiInput.Down(k, $"action_{b + 1}_{s + 1}")) UseSlot(b, s);
        }

        // ------------------------------------------------------------ Aktionsleisten

        private sealed class DragPayload
        {
            public ActionKind Kind;
            public string Id = "";
            public int FromBar = -1, FromSlot = -1;
            public string Label = "";
        }

        private DragPayload? _drag;
        private (int bar, int slot)? _pressed;
        private Vector2 _pressPos;
        private HashSet<string> _knownAbilities = new HashSet<string>();
        private bool _spellBookSeen;

        /// <summary>Neue Fähigkeiten auf freie Plätze legen; beim ersten Laden nur, wenn die Leisten noch leer sind.</summary>
        private void OnSpellBook()
        {
            var ordered = Client.SpellBook.Keys.Where(k => Content.AbilityRanks.ContainsKey(k) && !k.StartsWith("item_"))
                .OrderBy(k => Content.AbilityRanks[k][0].Level).ThenBy(k => k, System.StringComparer.Ordinal).ToList();
            bool barsEmpty = Settings.ActionBars.Bars.All(b => b.Buttons.All(s => s.IsEmpty));
            var fresh = !_spellBookSeen ? (barsEmpty ? ordered : new List<string>()) : ordered.Where(a => !_knownAbilities.Contains(a)).ToList();
            _spellBookSeen = true;
            _knownAbilities = new HashSet<string>(ordered);
            if (fresh.Count == 0) return;
            Settings.ActionBars.AutoPlace(fresh);
            SettingsChanged();
        }

        private void UseSlot(int bar, int slot)
        {
            var s = Settings.ActionBars.Get(bar, slot);
            var me = Client.Me;
            if (s.IsEmpty || me == null) return;
            if (s.Kind == ActionKind.Ability)
            {
                if (!Client.SpellBook.TryGetValue(s.Id, out var rank)) return;
                var ab = Content.GetAbilityRank(s.Id, rank);
                if (ab != null) Client.Cast(ab.Id, me.TargetId);
                return;
            }
            var inv = Client.State.Inventory;
            int idx = inv?.Bag.FindIndex(b => b.ItemId == s.Id) ?? -1;
            if (idx >= 0) Client.Send(new C_UseItem { BagIndex = idx, TargetId = me.TargetId });
        }

        private Rect SlotRect(int bar, int slot)
        {
            const float w = 46, gap = 4;
            float total = ActionBar.Slots * (w + gap);
            switch (bar)
            {
                case 0: return new Rect(ScreenW / 2 - total / 2 + slot * (w + gap), ScreenH - 84, w, w);
                case 1: return new Rect(ScreenW / 2 - total / 2 + slot * (w + gap), ScreenH - 84 - (w + gap), w, w);
                case 2: return new Rect(ScreenW - (w + 8), ScreenH / 2 - total / 2 + slot * (w + gap), w, w);
                default: return new Rect(ScreenW - 2 * (w + 8), ScreenH / 2 - total / 2 + slot * (w + gap), w, w);
            }
        }

        private void DrawActionBars()
        {
            var ev = Event.current;
            bool overSlot = false;
            for (int b = 0; b < ActionBarSet.BarCount; b++)
            {
                if (!Settings.ActionBars.Bars[b].Visible) continue;
                for (int s = 0; s < ActionBar.Slots; s++)
                {
                    var r = Box(SlotRect(b, s));
                    var slot = Settings.ActionBars.Get(b, s);
                    DrawSlot(r, b, s, slot);
                    if (!r.Contains(ev.mousePosition)) continue;
                    overSlot = true;
                    if (ev.type == EventType.MouseDown && (ev.button == 0 || ev.button == 1)) { _pressed = (b, s); _pressPos = ev.mousePosition; ev.Use(); }
                    else if (ev.type == EventType.MouseDrag && _pressed == (b, s) && _drag == null && !slot.IsEmpty && (ev.mousePosition - _pressPos).sqrMagnitude > 36
                             && (!Settings.LockActionBars || ev.shift))
                    {
                        _drag = new DragPayload { Kind = slot.Kind, Id = slot.Id, FromBar = b, FromSlot = s, Label = SlotLabel(slot) };
                        ev.Use();
                    }
                    else if (ev.type == EventType.MouseUp)
                    {
                        if (_drag != null)
                        {
                            if (_drag.FromBar >= 0) Settings.ActionBars.Move(_drag.FromBar, _drag.FromSlot, b, s);
                            else Settings.ActionBars.Set(b, s, _drag.Kind, _drag.Id);
                            _drag = null;
                            SettingsChanged();
                        }
                        else if (_pressed == (b, s)) UseSlot(b, s);
                        _pressed = null;
                        ev.Use();
                    }
                }
            }
            if (ev.rawType == EventType.MouseUp && !overSlot)
            {
                // Von der Leiste weggezogen: Knopf entfernen
                if (_drag != null && _drag.FromBar >= 0) { Settings.ActionBars.Clear(_drag.FromBar, _drag.FromSlot); SettingsChanged(); }
                _drag = null;
                _pressed = null;
            }
        }

        private string SlotLabel(ActionSlot slot)
        {
            if (slot.Kind == ActionKind.Item) return Content.Items.TryGetValue(slot.Id, out var it) ? it.Name : slot.Id;
            return Client.SpellBook.TryGetValue(slot.Id, out var rank) ? Content.GetAbilityRank(slot.Id, rank)?.Name ?? slot.Id : slot.Id;
        }

        private void DrawSlot(Rect r, int bar, int index, ActionSlot slot)
        {
            var st = ActionBarSet.Evaluate(slot, Content, Client);
            var old = GUI.color;
            GUI.color = slot.IsEmpty ? new Color(1, 1, 1, 0.35f) : st.Unknown ? Color.gray : st.OutOfRange ? Palette.OutOfRange : st.NotEnoughPower ? Palette.NoPower : Color.white;
            GUI.Box(r, "");
            GUI.color = old;
            if (!slot.IsEmpty)
            {
                GUI.Label(new Rect(r.x + 2, r.y + 10, r.width - 4, r.height - 14), $"<size={Mathf.Max(8, Settings.FontSize - 4)}>{Short(st.Label)}</size>", _centerWrap);
                if (st.ItemCount >= 0) GUI.Label(new Rect(r.x, r.yMax - 16, r.width - 3, 16), $"<size=10>{st.ItemCount}</size>", _right);
                if (st.CooldownLeft > 0)
                {
                    float total = CooldownTotal(slot);
                    float frac = total > 0 ? Mathf.Clamp01(st.CooldownLeft / total) : 1;
                    GUI.color = new Color(0, 0, 0, 0.6f);
                    GUI.DrawTexture(new Rect(r.x, r.y + r.height * (1 - frac), r.width, r.height * frac), Texture2D.whiteTexture);
                    GUI.color = old;
                    GUI.Label(r, $"<b>{(st.CooldownLeft >= 10 ? st.CooldownLeft.ToString("0") : st.CooldownLeft.ToString("0.0"))}</b>", _center);
                }
                if (st.Active)
                {
                    GUI.color = Palette.Active;
                    GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(r.x, r.yMax - 2, r.width, 2), Texture2D.whiteTexture);
                    GUI.color = old;
                }
                if (r.Contains(Event.current.mousePosition) && _drag == null) _tooltip = SlotTooltip(slot);
            }
            var key = UiInput.Short(Settings.Keys.KeyFor($"action_{bar + 1}_{index + 1}"));
            if (key != "") GUI.Label(new Rect(r.x + 2, r.y, r.width - 4, 14), $"<size=9>{key}</size>", _small);
        }

        private float CooldownTotal(ActionSlot slot)
        {
            if (slot.Kind == ActionKind.Item)
                return Content.Items.TryGetValue(slot.Id, out var it) && it.UseAbility != null && Content.Abilities.TryGetValue(it.UseAbility, out var ia) ? Mathf.Max(ia.Cooldown, 1.5f) : 0;
            if (!Client.SpellBook.TryGetValue(slot.Id, out var rank)) return 0;
            var ab = Content.GetAbilityRank(slot.Id, rank);
            return ab == null ? 0 : Mathf.Max(ab.Cooldown, 1.5f);
        }

        private string SlotTooltip(ActionSlot slot)
        {
            if (slot.Kind == ActionKind.Item)
                return Content.Items.TryGetValue(slot.Id, out var it) ? ItemTooltipText(it, null) : slot.Id;
            if (!Client.SpellBook.TryGetValue(slot.Id, out var rank)) return slot.Id + "\n(nicht erlernt)";
            var ab = Content.GetAbilityRank(slot.Id, rank);
            if (ab == null) return slot.Id;
            return $"<b>{ab.Name}</b> (Rang {ab.Rank})\n{ab.Description}\nKosten: {ab.Cost} · Zauberzeit: {(ab.CastTime > 0 ? ab.CastTime + " s" : "sofort")}" +
                   $"{(ab.MaxRange > 0 ? $" · Reichweite {ab.MaxRange:0} m" : "")}{(ab.Cooldown > 0 ? " · Abklingzeit " + ab.Cooldown + " s" : "")}";
        }

        /// <summary>Ziehen beginnen (Zauberbuch, Taschen). Vor dem Zeichnen des Knopfs aufrufen, sonst verschluckt der Knopf das Ereignis.</summary>
        private void BeginDragFrom(Rect r, ActionKind kind, string id, string label)
        {
            var ev = Event.current;
            if (_drag == null && ev.type == EventType.MouseDrag && ev.button == 0 && r.Contains(ev.mousePosition))
            {
                _drag = new DragPayload { Kind = kind, Id = id, Label = label };
                GUIUtility.hotControl = 0;
                ev.Use();
            }
        }

        private void DrawDragGhost()
        {
            if (_drag == null) return;
            var m = Event.current.mousePosition;
            GUI.Box(new Rect(m.x + 8, m.y + 8, 140, 24), Short(_drag.Label));
        }

        // ------------------------------------------------------------ Tooltip

        private string _tooltip = "";

        private void DrawTooltip()
        {
            string text = _tooltip != "" ? _tooltip : GUI.tooltip;
            if (Event.current.type == EventType.Repaint) _tooltip = "";
            if (string.IsNullOrEmpty(text) || _drag != null) return;
            var m = Event.current.mousePosition;
            float w = 320;
            float h = _wrap!.CalcHeight(new GUIContent(text), w - 12) + 10;
            var r = new Rect(Mathf.Min(m.x + 16, ScreenW - w - 4), Mathf.Max(4, Mathf.Min(m.y - h - 4, ScreenH - h - 4)), w, h);
            GUI.Box(r, "");
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 6, r.y + 4, w - 12, h - 6), text, _wrap);
        }
    }
}
