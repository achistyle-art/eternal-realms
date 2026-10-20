using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Emote-Auswahl (Kapitel 33), Leistungsanzeige (Kapitel 52), Grafik-Optionen (Kapitel 51), Fensterklänge.</summary>
    public sealed partial class GameUI
    {
        private bool _showEmotes;
        private int _openWindows;
        private GUISkin? _baseSkin;

        private void ApplySkin()
        {
            _baseSkin ??= GUI.skin;
            GUI.skin = UiSkin.Get(_baseSkin, Client.InWorld && Settings.HighContrast);
        }

        /// <summary>Klang beim Öffnen und Schließen von Fenstern.</summary>
        private void UpdateWindowSounds()
        {
            int open = new[] { _showQuests, _showBags, _showTalents, _showGroup, _showBook, _showCharacter, _showMap, _showMounts, _showSocial, _showProfessions, _showOptions, _showEmotes, _npc != null }.Count(b => b);
            if (open != _openWindows && AudioManager.Instance != null && Client.InWorld) AudioManager.Instance.PlayUi(open > _openWindows ? "ui_open" : "ui_close", 0.5f);
            _openWindows = open;
        }

        /// <summary>Chatbefehl als Emote: /winken, /tanzen … – mit aktuellem Ziel.</summary>
        private bool TryEmoteCommand(string command)
        {
            if (!Content.EmoteCommands.TryGetValue(command.TrimStart('/').ToLowerInvariant(), out var emote)) return false;
            Client.Send(new C_Emote { EmoteId = emote.Id, TargetId = Client.Me?.TargetId ?? 0 });
            return true;
        }

        private void DrawEmotes()
        {
            var area = Window("Emotes", 360, 420);
            GUILayout.BeginArea(area);
            var target = Client.Me != null ? Client.State.Get(Client.Me.TargetId) : null;
            GUILayout.Label(target != null ? $"Ziel: {target.Name}" : "Kein Ziel – Emotes ohne Ziel", _small);
            var list = Content.Emotes.Values.OrderBy(e => e.Commands[0]).ToList();
            for (int i = 0; i < list.Count; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 3 && j < list.Count; j++)
                {
                    var e = list[j];
                    if (GUILayout.Button(new GUIContent("/" + e.Commands[0], target != null ? e.TextTarget.Replace("{me}", "Ihr").Replace("{target}", target.Name) : e.TextNoTarget.Replace("{me}", "Ihr"))))
                        Client.Send(new C_Emote { EmoteId = e.Id, TargetId = target?.Id ?? 0 });
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("<size=11>Im Chat: /winken, /tanzen, /verbeugen … oder /e freier Text. KI-Mitspieler erwidern Gesten.</size>", _wrap);
            GUILayout.EndArea();
        }

        private void DrawPerformance()
        {
            if (!Settings.ShowPerformance) return;
            var f = GraphicsApplier.Instance != null ? GraphicsApplier.Instance.Frames : null;
            var sp = Client.ServerPerf;
            int views = Client.State.Entities.Count;
            string text = f == null ? "" :
                $"<b>{f.Fps:0} FPS</b> ({f.Average:0.0} ms, 1 % tief {f.OnePercentLowFps:0} FPS)\n" +
                $"Ping {Client.Ping} ms · Server-Takt Ø {sp.TickMs:0.0} ms / 99 % {sp.TickP99Ms:0.0} ms\n" +
                $"Sichtbar {views} Einheiten · Welt {sp.Players} Spieler/KI, {sp.Entities} Einheiten\n" +
                $"Grafik: {PresetName(Settings.Graphics.Preset)}";
            var r = new Rect(10, ScreenH * 0.4f, 360, 72);
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(r.x + 4, r.y + 2, r.width - 8, r.height), $"<size=11>{text}</size>", _wrap);
        }

        private static string PresetName(GraphicsPreset p) => p switch
        {
            GraphicsPreset.Low => "Niedrig", GraphicsPreset.Medium => "Mittel", GraphicsPreset.High => "Hoch", GraphicsPreset.Ultra => "Ultra", _ => "Eigene",
        };

        private void DrawGraphicsOptions()
        {
            var g = Settings.Graphics;
            GUILayout.Label("<b>Grafikstufe</b>", _wrap);
            int sel = GUILayout.Toolbar((int)g.Preset, new[] { "Niedrig", "Mittel", "Hoch", "Ultra", "Eigene" });
            if (sel != (int)g.Preset) g.Apply((GraphicsPreset)sel);
            if (GUILayout.Button("Für diesen Rechner empfehlen"))
                g.Apply(GraphicsOptions.Recommend(SystemInfo.graphicsMemorySize, SystemInfo.processorCount, SystemInfo.systemMemorySize));
            GUILayout.Label($"<size=11>{SystemInfo.graphicsDeviceName} · {SystemInfo.graphicsMemorySize} MB Grafikspeicher · {SystemInfo.processorCount} Kerne · {SystemInfo.systemMemorySize} MB</size>", _wrap);
            g.ViewDistance = Mathf.Round(Slider("Sichtweite (m)", g.ViewDistance, 80, 800, "0") / 10) * 10;
            g.EntityDistance = Mathf.Round(Slider("Figuren zeichnen bis (m)", g.EntityDistance, 50, 400, "0") / 10) * 10;
            g.NameplateDistance = Mathf.Round(Slider("Namensschilder bis (m)", g.NameplateDistance, 10, 100, "0"));
            g.Shadows = (ShadowMode)GUILayout.Toolbar((int)g.Shadows, new[] { "Keine Schatten", "Harte Schatten", "Weiche Schatten" });
            g.ShadowDistance = Mathf.Round(Slider("Schattenweite (m)", g.ShadowDistance, 0, 200, "0") / 5) * 5;
            int msaaIdx = g.Msaa >= 8 ? 3 : g.Msaa >= 4 ? 2 : g.Msaa >= 2 ? 1 : 0;
            msaaIdx = GUILayout.Toolbar(msaaIdx, new[] { "Kantenglättung aus", "2×", "4×", "8×" });
            g.Msaa = msaaIdx == 0 ? 0 : 1 << msaaIdx;
            g.ParticleDensity = Slider("Partikeldichte", g.ParticleDensity, 0, 1);
            GUILayout.Space(6);
            GUILayout.Label("<b>Anzeige</b>", _wrap);
            g.Fullscreen = GUILayout.Toggle(g.Fullscreen, "Vollbild (randloses Fenster)");
            g.VSync = GUILayout.Toggle(g.VSync, "Vertikale Synchronisation");
            if (!g.VSync)
            {
                int[] limits = { 30, 60, 120, 144, 0 };
                int idx = System.Array.IndexOf(limits, g.FpsLimit);
                idx = GUILayout.Toolbar(idx < 0 ? 1 : idx, new[] { "30", "60", "120", "144", "unbegrenzt" });
                g.FpsLimit = limits[idx];
            }
            g.BackgroundFps = Mathf.RoundToInt(Slider("Bildrate im Hintergrund", g.BackgroundFps, 5, 60, "0"));
            var res = UnityEngine.Screen.resolutions;
            if (res.Length > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Auflösung: {(g.Width > 0 ? $"{g.Width}×{g.Height}" : "Bildschirm")}", GUILayout.Width(240));
                if (GUILayout.Button("Bildschirm")) { g.Width = g.Height = 0; }
                foreach (var r in res.Select(r => (r.width, r.height)).Distinct().Reverse().Take(4))
                    if (GUILayout.Button($"{r.width}×{r.height}")) { g.Width = r.width; g.Height = r.height; }
                GUILayout.EndHorizontal();
            }
            g.DetectCustom();
            GUILayout.Space(6);
            Settings.ShowPerformance = GUILayout.Toggle(Settings.ShowPerformance, $"Leistungsanzeige (Bildrate, Ping, Server-Takt){KeyHint("perf_overlay")}");
        }
    }
}
