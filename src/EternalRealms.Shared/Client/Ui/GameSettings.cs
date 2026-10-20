using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EternalRealms.Shared.Client.Ui
{
    public enum ColorblindMode : byte { None = 0, Protanopia = 1, Deuteranopia = 2, Tritanopia = 3 }

    /// <summary>Lage und Größe eines Fensters (Kapitel 28.4). Koordinaten in Bildschirmpunkten bei Skalierung 1.</summary>
    public sealed class WindowLayout
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Scale { get; set; } = 1f;
        public bool Visible { get; set; }
    }

    public sealed class ChatTab
    {
        public string Name { get; set; } = "Allgemein";
        /// <summary>Angezeigte Kanäle (ChatChannel als Zahl); leer = alle.</summary>
        public List<int> Channels { get; set; } = new List<int>();
        public bool CombatLog { get; set; }
    }

    /// <summary>
    /// Alle Einstellungen eines Charakters: Tasten, Aktionsleisten, Fensterlagen, Oberfläche, Barrierefreiheit, Kamera,
    /// Audio. Wird als JSON lokal und auf dem Server gespeichert, sodass sie auf jedem Rechner gleich sind.
    /// </summary>
    public sealed class GameSettings
    {
        public const int Version = 1;
        public const int MaxJsonLength = 32 * 1024;

        public int SettingsVersion { get; set; } = Version;
        public Keybindings Keys { get; set; } = Keybindings.CreateDefault();
        public ActionBarSet ActionBars { get; set; } = new ActionBarSet();
        public Dictionary<string, WindowLayout> Windows { get; set; } = new Dictionary<string, WindowLayout>();
        public List<ChatTab> ChatTabs { get; set; } = new List<ChatTab>
        {
            new ChatTab { Name = "Allgemein" },
            new ChatTab { Name = "Kampf", CombatLog = true },
            new ChatTab { Name = "Gruppe", Channels = new List<int> { (int)ChatChannel.Party, (int)ChatChannel.Raid, (int)ChatChannel.Guild } },
        };

        // Oberfläche
        public float UiScale { get; set; } = 1f;
        public int FontSize { get; set; } = 13;
        public bool ShowDamageNumbers { get; set; } = true;
        public bool ChatTimestamps { get; set; } = true;
        public bool ShowQuestTracker { get; set; } = true;
        public bool ShowMinimap { get; set; } = true;
        public float MinimapZoom { get; set; } = 1f;
        public bool ShowGroupFrames { get; set; } = true;
        /// <summary>Gesperrte Leisten: Knöpfe nur mit gedrückter Umschalttaste verschieben.</summary>
        public bool LockActionBars { get; set; }

        // Barrierefreiheit (Kapitel 59)
        public ColorblindMode Colorblind { get; set; }
        public bool ReduceFlashes { get; set; }
        public bool ReduceParticles { get; set; }
        public bool Subtitles { get; set; } = true;

        // Kamera (Kapitel 29)
        public float CameraSensitivity { get; set; } = 3f;
        public float CameraSpeed { get; set; } = 1f;
        public float CameraMaxDistance { get; set; } = 25f;
        public bool CameraCollision { get; set; } = true;
        public bool InvertMouseY { get; set; }

        // Audio (Kapitel 34)
        public float VolumeMaster { get; set; } = 0.8f;
        public float VolumeMusic { get; set; } = 0.6f;
        public float VolumeEffects { get; set; } = 0.8f;
        public float VolumeAmbient { get; set; } = 0.6f;
        public float VolumeDialog { get; set; } = 0.8f;
        public bool MuteInBackground { get; set; } = true;

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false };

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        /// <summary>Liest Einstellungen; Unbekanntes oder Fehlerhaftes wird durch Standardwerte ersetzt (nie eine Ausnahme).</summary>
        public static GameSettings FromJson(string? json)
        {
            GameSettings s;
            try { s = string.IsNullOrWhiteSpace(json) ? new GameSettings() : JsonSerializer.Deserialize<GameSettings>(json!, Json) ?? new GameSettings(); }
            catch (JsonException) { s = new GameSettings(); }
            s.Normalize();
            return s;
        }

        /// <summary>Wertebereiche erzwingen und fehlende Teile ergänzen.</summary>
        public void Normalize()
        {
            Keys ??= Keybindings.CreateDefault();
            Keys.Bindings ??= new Dictionary<string, string[]>();
            Keys.Normalize();
            ActionBars ??= new ActionBarSet();
            ActionBars.Normalize();
            Windows ??= new Dictionary<string, WindowLayout>();
            foreach (var w in Windows.Values)
            {
                w.Scale = Clamp(w.Scale, 0.5f, 2f);
                w.Width = Math.Max(0, w.Width); w.Height = Math.Max(0, w.Height);
            }
            if (ChatTabs == null || ChatTabs.Count == 0) ChatTabs = new GameSettings().ChatTabs;
            UiScale = Clamp(UiScale, 0.5f, 2f);
            FontSize = (int)Clamp(FontSize, 9, 28);
            MinimapZoom = Clamp(MinimapZoom, 0.5f, 4f);
            CameraSensitivity = Clamp(CameraSensitivity, 0.2f, 10f);
            CameraSpeed = Clamp(CameraSpeed, 0.2f, 3f);
            CameraMaxDistance = Clamp(CameraMaxDistance, 3f, 40f);
            VolumeMaster = Clamp(VolumeMaster, 0, 1); VolumeMusic = Clamp(VolumeMusic, 0, 1); VolumeEffects = Clamp(VolumeEffects, 0, 1);
            VolumeAmbient = Clamp(VolumeAmbient, 0, 1); VolumeDialog = Clamp(VolumeDialog, 0, 1);
            SettingsVersion = Version;
        }

        private static float Clamp(float v, float min, float max) => float.IsNaN(v) ? min : v < min ? min : v > max ? max : v;

        public WindowLayout Window(string id, float x, float y, float w, float h)
        {
            if (!Windows.TryGetValue(id, out var l)) Windows[id] = l = new WindowLayout { X = x, Y = y, Width = w, Height = h };
            return l;
        }
    }
}
