using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Farben der Oberfläche (Kapitel 59). Bei Farbenblindheit werden Rot/Grün-Paare durch unterscheidbare
    /// Blau/Orange-Töne ersetzt; Bedeutung wird zusätzlich immer über Text oder Form vermittelt.
    /// </summary>
    public static class Palette
    {
        private static ColorblindMode Mode => NetworkManager.Instance != null ? NetworkManager.Instance.Client.Settings.Colorblind : ColorblindMode.None;
        private static bool RedGreen => Mode == ColorblindMode.Protanopia || Mode == ColorblindMode.Deuteranopia;

        public static Color Health => RedGreen ? new Color(0.2f, 0.55f, 1f) : Mode == ColorblindMode.Tritanopia ? new Color(0.15f, 0.75f, 0.35f) : new Color(0.1f, 0.7f, 0.1f);
        public static Color Hostile => RedGreen ? new Color(1f, 0.55f, 0.05f) : new Color(0.9f, 0.15f, 0.15f);
        public static Color Friendly => RedGreen ? new Color(0.25f, 0.6f, 1f) : Mode == ColorblindMode.Tritanopia ? new Color(0.2f, 0.8f, 0.4f) : new Color(0.2f, 0.85f, 0.2f);
        public static Color Neutral => Mode == ColorblindMode.Tritanopia ? new Color(1f, 0.6f, 0.75f) : new Color(1f, 0.9f, 0.2f);
        public static Color OutOfRange => RedGreen ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 0.45f, 0.45f);
        public static Color NoPower => Mode == ColorblindMode.Tritanopia ? new Color(0.8f, 0.5f, 0.9f) : new Color(0.45f, 0.55f, 1f);
        public static Color Active => new Color(1f, 0.85f, 0.2f);
        public static Color Damage => RedGreen ? new Color(1f, 0.6f, 0.1f) : new Color(1f, 0.25f, 0.2f);
        public static Color Heal => RedGreen ? new Color(0.35f, 0.7f, 1f) : new Color(0.3f, 1f, 0.3f);

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static string Quality(ItemQuality q) => q switch
        {
            ItemQuality.Poor => "#9d9d9d",
            // Grün ist für Rot-Grün-Schwäche kaum vom Weiß der „Gewöhnlichen“ zu unterscheiden: Türkis
            ItemQuality.Uncommon => RedGreen ? "#40e0d0" : "#1eff00",
            ItemQuality.Rare => "#3a8cff",
            ItemQuality.Epic => "#a335ee",
            ItemQuality.Legendary => "#ff8000",
            _ => "#ffffff",
        };

        /// <summary>Farbe für Schadens-/Heilzahlen; Unity-Farben aus dem WorldRenderer werden passend ersetzt.</summary>
        public static Color Floating(Color original)
        {
            if (Mode == ColorblindMode.None) return original;
            if (original.g > 0.8f && original.r < 0.5f) return Heal;
            if (original.r > 0.8f && original.g < 0.5f) return Damage;
            return original;
        }
    }
}
