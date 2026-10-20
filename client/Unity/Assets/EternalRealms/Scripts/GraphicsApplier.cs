using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Überträgt die Grafikeinstellungen (Kapitel 51) auf Unity: Schatten, Kantenglättung, Sichtweite, Bildrate,
    /// Auflösung. Misst die Bildzeiten für die Leistungsanzeige (Kapitel 52). Beim ersten Start wird die Stufe
    /// nach der Hardware gewählt.
    /// </summary>
    public sealed class GraphicsApplier : MonoBehaviour
    {
        public static GraphicsApplier Instance { get; private set; } = null!;
        public FrameStats Frames { get; } = new FrameStats(300);
        private string _applied = "", _display = "";

        private GameSettings Settings => NetworkManager.Instance.Client.Settings;

        private void Awake() { Instance = this; }

        private void Update()
        {
            Frames.Add(Time.unscaledDeltaTime * 1000f);
            var s = Settings;
            if (NetworkManager.Instance.Client.SettingsLoaded && !s.GraphicsDetected)
            {
                s.GraphicsDetected = true;
                s.Graphics.Apply(GraphicsOptions.Recommend(SystemInfo.graphicsMemorySize, SystemInfo.processorCount, SystemInfo.systemMemorySize));
                GameUI.Instance.NotifySettingsChanged();
            }
            var g = s.Graphics;
            string key = $"{g.Preset}|{g.ViewDistance}|{g.Shadows}|{g.ShadowDistance}|{g.Msaa}|{g.VSync}|{g.FpsLimit}|{g.BackgroundFps}|{g.Fullscreen}|{g.Width}x{g.Height}|{Application.isFocused}";
            if (key == _applied) return;
            _applied = key;
            string display = $"{g.Fullscreen}|{g.Width}x{g.Height}";
            // Auflösung nur bei echter Änderung setzen (nicht beim Start, damit Fenstermodus des Editors bleibt)
            bool displayChanged = _display != "" && display != _display;
            _display = display;
            Apply(g, displayChanged);
        }

        private static void Apply(GraphicsOptions g, bool display)
        {
            QualitySettings.shadows = g.Shadows switch { ShadowMode.Off => UnityEngine.ShadowQuality.Disable, ShadowMode.Hard => UnityEngine.ShadowQuality.HardOnly, _ => UnityEngine.ShadowQuality.All };
            QualitySettings.shadowDistance = g.ShadowDistance;
            QualitySettings.antiAliasing = g.Msaa;
            QualitySettings.vSyncCount = g.VSync && Application.isFocused ? 1 : 0;
            Application.targetFrameRate = !Application.isFocused ? g.BackgroundFps : g.VSync ? -1 : g.FpsLimit <= 0 ? -1 : g.FpsLimit;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) light.shadows = g.Shadows switch { ShadowMode.Off => LightShadows.None, ShadowMode.Hard => LightShadows.Hard, _ => LightShadows.Soft };
            var cam = Camera.main;
            if (cam != null) cam.farClipPlane = g.ViewDistance;
            RenderSettings.fogStartDistance = g.ViewDistance * 0.5f;
            RenderSettings.fogEndDistance = g.ViewDistance;
            if (display)
            {
                int w = g.Width > 0 ? g.Width : Screen.currentResolution.width;
                int h = g.Height > 0 ? g.Height : Screen.currentResolution.height;
                Screen.SetResolution(w, h, g.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            }
        }
    }
}
