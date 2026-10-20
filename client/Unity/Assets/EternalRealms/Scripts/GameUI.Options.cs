using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Spielmenü und Optionen (Kapitel 28/29/59): Oberfläche, Tastenbelegung, Barrierefreiheit, Kamera, Ton.
    /// Jede Änderung wirkt sofort und wird am Charakter gespeichert.
    /// </summary>
    public sealed partial class GameUI
    {
        private bool _showOptions;
        private int _optionsTab;
        private Vector2 _scrollOptions;
        private string? _capturingAction;
        private int _capturingIndex;
        private string _newTabName = "";

        private void OpenOptions() { _showOptions = true; _optionsTab = 0; }

        private void CloseOptions()
        {
            if (!_showOptions) return;
            _showOptions = false;
            _capturingAction = null;
            SaveSettingsNow();
        }

        private static readonly string[] OptionTabs = { "Spiel", "Oberfläche", "Chat", "Tasten", "Barrierefreiheit", "Grafik", "Kamera", "Ton" };

        private void DrawOptions()
        {
            var area = Window("Optionen", 720, 560);
            GUILayout.BeginArea(area);
            _optionsTab = GUILayout.Toolbar(_optionsTab, OptionTabs);
            _scrollOptions = GUILayout.BeginScrollView(_scrollOptions);
            var before = Settings.ToJson();
            switch (_optionsTab)
            {
                case 0: DrawGameMenu(); break;
                case 1: DrawInterfaceOptions(); break;
                case 2: DrawChatOptions(); break;
                case 3: DrawKeyOptions(); break;
                case 4: DrawAccessibilityOptions(); break;
                case 5: DrawGraphicsOptions(); break;
                case 6: DrawCameraOptions(); break;
                default: DrawAudioOptions(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (Event.current.type != EventType.Layout && Settings.ToJson() != before) { Settings.Normalize(); SettingsChanged(); }
        }

        private void DrawGameMenu()
        {
            GUILayout.Label("<b>Spielmenü</b>", _wrap);
            if (GUILayout.Button("Zurück zum Spiel", GUILayout.Height(30))) CloseOptions();
            if (GUILayout.Button("Abmelden (zur Charakterauswahl)", GUILayout.Height(30))) { SaveSettingsNow(); Client.Send(new C_Logout()); CloseOptions(); }
            if (GUILayout.Button("Spiel beenden", GUILayout.Height(30))) { SaveSettingsNow(); Application.Quit(); }
            GUILayout.Space(8);
            GUILayout.Label("Einstellungen werden am Charakter auf dem Server gespeichert und gelten auf jedem Rechner.", _wrap);
        }

        private float Slider(string label, float value, float min, float max, string format = "0.00")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value.ToString(format)}", GUILayout.Width(260));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }

        private void DrawInterfaceOptions()
        {
            Settings.UiScale = Mathf.Round(Slider("Oberflächengröße", Settings.UiScale, 0.5f, 2f) * 20) / 20;
            Settings.FontSize = Mathf.RoundToInt(Slider("Schriftgröße", Settings.FontSize, 9, 28, "0"));
            Settings.ShowDamageNumbers = GUILayout.Toggle(Settings.ShowDamageNumbers, "Schadens- und Heilzahlen anzeigen");
            Settings.ShowQuestTracker = GUILayout.Toggle(Settings.ShowQuestTracker, "Quest-Tracker anzeigen");
            Settings.ShowMinimap = GUILayout.Toggle(Settings.ShowMinimap, "Minikarte anzeigen");
            Settings.ShowGroupFrames = GUILayout.Toggle(Settings.ShowGroupFrames, "Gruppenrahmen anzeigen");
            Settings.LockActionBars = GUILayout.Toggle(Settings.LockActionBars, "Aktionsleisten sperren (verschieben nur mit Umschalt)");
            for (int b = 1; b < ActionBarSet.BarCount; b++)
                Settings.ActionBars.Bars[b].Visible = GUILayout.Toggle(Settings.ActionBars.Bars[b].Visible, $"Aktionsleiste {b + 1} anzeigen");
            GUILayout.Space(6);
            if (GUILayout.Button("Fensterpositionen zurücksetzen")) { Settings.Windows.Clear(); _windowCascade = 0; }
            if (GUILayout.Button("Aktionsleisten neu befüllen")) { Settings.ActionBars = new ActionBarSet(); _spellBookSeen = false; OnSpellBook(); }
            GUILayout.Label("Fenster lassen sich an der Titelleiste verschieben und an der Ecke unten rechts in der Größe ändern. " +
                            "Fähigkeiten und Gegenstände zieht man aus Zauberbuch und Taschen auf die Leisten; wegziehen entfernt sie.", _wrap);
        }

        private void DrawChatOptions()
        {
            Settings.ChatTimestamps = GUILayout.Toggle(Settings.ChatTimestamps, "Zeitstempel anzeigen");
            GUILayout.Label("<b>Öffentliche Kanäle</b> (Befehle: /1 Gebiet, /2 Handel – nur in Städten, /4 Gruppensuche)", _wrap);
            GUILayout.BeginHorizontal();
            foreach (var pub in new[] { ChatChannel.General, ChatChannel.Trade, ChatChannel.LookingForGroup })
            {
                bool joined = Client.JoinedChannels.Contains(pub);
                if (GUILayout.Toggle(joined, ChatFormat.ChannelPrefix(pub).Trim('[', ']', ' ')) != joined) Client.Send(new C_ChatJoin { Channel = pub, Join = !joined });
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("<b>Chatreiter</b> (ohne gewählte Kanäle zeigt ein Reiter alle)", _wrap);
            var channels = new[] { ChatChannel.Say, ChatChannel.Yell, ChatChannel.Party, ChatChannel.Raid, ChatChannel.Guild, ChatChannel.Whisper, ChatChannel.General, ChatChannel.Trade, ChatChannel.LookingForGroup, ChatChannel.Emote };
            foreach (var tab in Settings.ChatTabs.ToList())
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                tab.Name = GUILayout.TextField(tab.Name, 16, GUILayout.Width(160));
                tab.CombatLog = GUILayout.Toggle(tab.CombatLog, "Kampflog");
                if (Settings.ChatTabs.Count > 1 && GUILayout.Button("Entfernen", GUILayout.Width(90))) Settings.ChatTabs.Remove(tab);
                GUILayout.EndHorizontal();
                if (!tab.CombatLog)
                {
                    GUILayout.BeginHorizontal();
                    foreach (var ch in channels)
                    {
                        bool on = tab.Channels.Contains((int)ch);
                        if (GUILayout.Toggle(on, ChatFormat.ChannelPrefix(ch).Trim('[', ']', ' ') is var n && n != "" ? n : ch.ToString()) != on)
                        {
                            if (on) tab.Channels.Remove((int)ch); else tab.Channels.Add((int)ch);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
            GUILayout.BeginHorizontal();
            _newTabName = GUILayout.TextField(_newTabName, 16, GUILayout.Width(160));
            if (GUILayout.Button("Reiter hinzufügen", GUILayout.Width(160)) && _newTabName.Trim() != "" && Settings.ChatTabs.Count < 8)
            {
                Settings.ChatTabs.Add(new ChatTab { Name = _newTabName.Trim() });
                _newTabName = "";
            }
            GUILayout.EndHorizontal();
        }

        private void DrawKeyOptions()
        {
            GUILayout.Label("Klick auf eine Belegung und dann die neue Taste drücken (mit Strg/Alt/Umschalt kombinierbar). Esc bricht ab, Entf löscht.", _wrap);
            if (GUILayout.Button("Alle Tasten auf Standard zurücksetzen")) Settings.Keys.ResetToDefaults();
            foreach (var (action, label, _) in Keybindings.Defaults)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(300));
                for (int i = 0; i < 2; i++)
                {
                    bool capturing = _capturingAction == action && _capturingIndex == i;
                    string key = Settings.Keys.KeyFor(action, i);
                    if (GUILayout.Button(capturing ? "<color=yellow>Taste drücken …</color>" : key == "" ? "–" : key, GUILayout.Width(150)))
                    {
                        _capturingAction = action;
                        _capturingIndex = i;
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>Wird vor allen anderen Oberflächenteilen in OnGUI aufgerufen, damit die Taste keine Aktion auslöst.</summary>
        private void HandleKeyCapture()
        {
            if (_capturingAction == null) return;
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;
            if (ev.keyCode == KeyCode.Escape) { _capturingAction = null; ev.Use(); return; }
            if (ev.keyCode == KeyCode.Delete || ev.keyCode == KeyCode.Backspace)
            {
                Settings.Keys.Unbind(_capturingAction, _capturingIndex);
                _capturingAction = null;
                SettingsChanged();
                ev.Use();
                return;
            }
            var key = UiInput.Capture(ev);
            if (key == null) return;
            var stolen = Settings.Keys.Bind(_capturingAction, key, _capturingIndex);
            if (stolen != null)
            {
                var label = Keybindings.Defaults.FirstOrDefault(d => d.Action == stolen).Label;
                Toast($"{key} war „{label}“ zugewiesen – diese Belegung wurde entfernt.");
            }
            _capturingAction = null;
            SettingsChanged();
            ev.Use();
        }

        private void DrawAccessibilityOptions()
        {
            GUILayout.Label("<b>Farbenblindheit</b>", _wrap);
            Settings.Colorblind = (ColorblindMode)GUILayout.Toolbar((int)Settings.Colorblind, new[] { "Aus", "Protanopie", "Deuteranopie", "Tritanopie" });
            GUILayout.Label("Ersetzt Rot/Grün bzw. Blau/Gelb in Leisten, Karten, Schadenszahlen und Gegenstandsqualitäten. Markierungen tragen zusätzlich Symbole.", _wrap);
            Settings.FontSize = Mathf.RoundToInt(Slider("Schriftgröße", Settings.FontSize, 9, 28, "0"));
            Settings.UiScale = Mathf.Round(Slider("Oberflächengröße", Settings.UiScale, 0.5f, 2f) * 20) / 20;
            Settings.HighContrast = GUILayout.Toggle(Settings.HighContrast, "Kontrastreiche Oberfläche (schwarze Flächen, weiße Rahmen und Schrift)");
            Settings.ReduceFlashes = GUILayout.Toggle(Settings.ReduceFlashes, "Blitz- und Pulseffekte reduzieren (keine vergrößerten Kritzahlen)");
            Settings.ReduceParticles = GUILayout.Toggle(Settings.ReduceParticles, "Partikel reduzieren (Regen, Schnee)");
            Settings.Subtitles = GUILayout.Toggle(Settings.Subtitles, "Untertitel für Sprechtexte von NPCs");
            Settings.ChatTimestamps = GUILayout.Toggle(Settings.ChatTimestamps, "Zeitstempel im Chat");
        }

        private void DrawCameraOptions()
        {
            Settings.CameraSensitivity = Slider("Mausempfindlichkeit", Settings.CameraSensitivity, 0.2f, 10f, "0.0");
            Settings.CameraSpeed = Slider("Drehtempo mit Tasten", Settings.CameraSpeed, 0.2f, 3f, "0.0");
            Settings.CameraMaxDistance = Slider("Maximale Entfernung", Settings.CameraMaxDistance, 3f, 40f, "0");
            Settings.CameraCollision = GUILayout.Toggle(Settings.CameraCollision, "Kamera weicht Gelände und Wänden aus");
            Settings.InvertMouseY = GUILayout.Toggle(Settings.InvertMouseY, "Maus-Y-Achse umkehren");
        }

        private void DrawAudioOptions()
        {
            Settings.VolumeMaster = Slider("Gesamtlautstärke", Settings.VolumeMaster, 0, 1);
            Settings.VolumeMusic = Slider("Musik", Settings.VolumeMusic, 0, 1);
            Settings.VolumeEffects = Slider("Effekte", Settings.VolumeEffects, 0, 1);
            Settings.VolumeAmbient = Slider("Umgebung", Settings.VolumeAmbient, 0, 1);
            Settings.VolumeDialog = Slider("Sprache", Settings.VolumeDialog, 0, 1);
            Settings.MuteInBackground = GUILayout.Toggle(Settings.MuteInBackground, "Stumm, wenn das Fenster im Hintergrund ist");
            GUILayout.Label("Alle Klänge und die Musik werden im Spiel erzeugt; die Musik passt sich Region, Gebiet, Tageszeit, Kampf und Bossen an.", _wrap);
            if (GUILayout.Button("Probeton")) AudioManager.Instance.PlayUi("levelup");
        }
    }
}
