using System;

namespace EternalRealms.Shared.Client.Ui
{
    public enum GraphicsPreset : byte { Low = 0, Medium = 1, High = 2, Ultra = 3, Custom = 4 }
    public enum ShadowQuality : byte { Off = 0, Hard = 1, Soft = 2 }

    /// <summary>
    /// Grafikstufen (Kapitel 51/52). Jede Stufe ist ein fester Satz Werte; ändert der Spieler einen Wert,
    /// wird daraus „Eigene“. Die Unity-Seite überträgt die Werte auf QualitySettings, Kamera und Effekte.
    /// </summary>
    public sealed class GraphicsOptions
    {
        public GraphicsPreset Preset { get; set; } = GraphicsPreset.Medium;
        /// <summary>Sichtweite der Kamera in Metern.</summary>
        public float ViewDistance { get; set; } = 300;
        public ShadowQuality Shadows { get; set; } = ShadowQuality.Hard;
        public float ShadowDistance { get; set; } = 60;
        /// <summary>Kantenglättung: 0, 2, 4 oder 8 Abtastungen.</summary>
        public int Msaa { get; set; } = 2;
        /// <summary>Anteil der Partikel (Wetter, Zauber) 0–1.</summary>
        public float ParticleDensity { get; set; } = 0.7f;
        /// <summary>Entfernung, bis zu der Namensschilder gezeigt werden.</summary>
        public float NameplateDistance { get; set; } = 40;
        /// <summary>Einheiten jenseits dieser Entfernung werden nicht gezeichnet (Leistung in vollen Städten).</summary>
        public float EntityDistance { get; set; } = 150;
        public bool VSync { get; set; } = true;
        /// <summary>Bildratenbegrenzung; 0 = unbegrenzt.</summary>
        public int FpsLimit { get; set; } = 60;
        public bool Fullscreen { get; set; } = true;
        /// <summary>Auflösung; 0 = Bildschirmauflösung.</summary>
        public int Width { get; set; }
        public int Height { get; set; }
        /// <summary>Hintergrund-Bildrate, wenn das Fenster nicht aktiv ist (spart Strom).</summary>
        public int BackgroundFps { get; set; } = 20;

        public static GraphicsOptions For(GraphicsPreset p)
        {
            var g = new GraphicsOptions { Preset = p };
            switch (p)
            {
                case GraphicsPreset.Low:
                    g.ViewDistance = 150; g.Shadows = ShadowQuality.Off; g.ShadowDistance = 0; g.Msaa = 0; g.ParticleDensity = 0.25f;
                    g.NameplateDistance = 25; g.EntityDistance = 80; g.FpsLimit = 60; break;
                case GraphicsPreset.Medium:
                    g.ViewDistance = 300; g.Shadows = ShadowQuality.Hard; g.ShadowDistance = 60; g.Msaa = 2; g.ParticleDensity = 0.7f;
                    g.NameplateDistance = 40; g.EntityDistance = 150; g.FpsLimit = 60; break;
                case GraphicsPreset.High:
                    g.ViewDistance = 450; g.Shadows = ShadowQuality.Soft; g.ShadowDistance = 100; g.Msaa = 4; g.ParticleDensity = 1f;
                    g.NameplateDistance = 60; g.EntityDistance = 220; g.FpsLimit = 120; break;
                case GraphicsPreset.Ultra:
                    g.ViewDistance = 600; g.Shadows = ShadowQuality.Soft; g.ShadowDistance = 150; g.Msaa = 8; g.ParticleDensity = 1f;
                    g.NameplateDistance = 80; g.EntityDistance = 300; g.FpsLimit = 0; break;
            }
            return g;
        }

        /// <summary>Stufe übernehmen; Anzeige-Einstellungen (Vollbild, Auflösung, VSync) bleiben erhalten.</summary>
        public void Apply(GraphicsPreset p)
        {
            if (p == GraphicsPreset.Custom) { Preset = p; return; }
            var g = For(p);
            Preset = p; ViewDistance = g.ViewDistance; Shadows = g.Shadows; ShadowDistance = g.ShadowDistance; Msaa = g.Msaa;
            ParticleDensity = g.ParticleDensity; NameplateDistance = g.NameplateDistance; EntityDistance = g.EntityDistance; FpsLimit = g.FpsLimit;
        }

        /// <summary>Entspricht der Stand noch genau der gewählten Stufe? Sonst wird er als „Eigene“ geführt.</summary>
        public void DetectCustom()
        {
            if (Preset == GraphicsPreset.Custom) return;
            var g = For(Preset);
            if (Math.Abs(ViewDistance - g.ViewDistance) > 0.5f || Shadows != g.Shadows || Math.Abs(ShadowDistance - g.ShadowDistance) > 0.5f || Msaa != g.Msaa
                || Math.Abs(ParticleDensity - g.ParticleDensity) > 0.01f || Math.Abs(NameplateDistance - g.NameplateDistance) > 0.5f
                || Math.Abs(EntityDistance - g.EntityDistance) > 0.5f || FpsLimit != g.FpsLimit)
                Preset = GraphicsPreset.Custom;
        }

        /// <summary>Startwert nach Hardware: Grafikspeicher (MB), Prozessorkerne, Arbeitsspeicher (MB).</summary>
        public static GraphicsPreset Recommend(int gpuMemoryMb, int cpuCores, int systemMemoryMb)
        {
            if (gpuMemoryMb >= 6000 && cpuCores >= 8 && systemMemoryMb >= 16000) return GraphicsPreset.Ultra;
            if (gpuMemoryMb >= 3500 && cpuCores >= 4 && systemMemoryMb >= 8000) return GraphicsPreset.High;
            if (gpuMemoryMb >= 1500 && cpuCores >= 4) return GraphicsPreset.Medium;
            return GraphicsPreset.Low;
        }

        public void Normalize()
        {
            ViewDistance = Clamp(ViewDistance, 80, 800);
            ShadowDistance = Clamp(ShadowDistance, 0, 200);
            Msaa = Msaa >= 8 ? 8 : Msaa >= 4 ? 4 : Msaa >= 2 ? 2 : 0;
            ParticleDensity = Clamp(ParticleDensity, 0, 1);
            NameplateDistance = Clamp(NameplateDistance, 10, 100);
            EntityDistance = Clamp(EntityDistance, 50, 400);
            FpsLimit = FpsLimit <= 0 ? 0 : Math.Max(20, Math.Min(360, FpsLimit));
            BackgroundFps = Math.Max(5, Math.Min(60, BackgroundFps));
            Width = Math.Max(0, Width); Height = Math.Max(0, Height);
            if (!Enum.IsDefined(typeof(GraphicsPreset), Preset)) Preset = GraphicsPreset.Custom;
            if (!Enum.IsDefined(typeof(ShadowQuality), Shadows)) Shadows = ShadowQuality.Hard;
        }

        private static float Clamp(float v, float min, float max) => float.IsNaN(v) ? min : v < min ? min : v > max ? max : v;
    }
}
