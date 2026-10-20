using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Eigener Look der Oberfläche ohne Grafikdateien: Flächen, Rahmen und Verläufe werden als kleine Texturen
    /// erzeugt (dunkles Holz/Pergament mit Goldrand). Variante „kontrastreich“ für Barrierefreiheit (Kapitel 59).
    /// </summary>
    public static class UiSkin
    {
        private static GUISkin? _normal, _contrast;

        public static GUISkin Get(GUISkin baseSkin, bool highContrast)
        {
            if (highContrast) return _contrast ??= Build(baseSkin, true);
            return _normal ??= Build(baseSkin, false);
        }

        /// <summary>9-Slice-Textur: Füllung mit leichtem Verlauf, 2 px Rahmen, abgerundete Ecken.</summary>
        private static Texture2D Panel(Color fillTop, Color fillBottom, Color border, int size = 16, int radius = 3)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int dx = Mathf.Min(x, size - 1 - x), dy = Mathf.Min(y, size - 1 - y);
                    bool corner = dx < radius && dy < radius && (radius - dx) * (radius - dx) + (radius - dy) * (radius - dy) > radius * radius;
                    var c = corner ? Color.clear : dx < 2 || dy < 2 ? border : Color.Lerp(fillBottom, fillTop, y / (float)(size - 1));
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        private static void Style(GUIStyle s, Texture2D normal, Texture2D hover, Texture2D active, Color text, Color hoverText)
        {
            s.normal.background = normal; s.normal.textColor = text;
            s.hover.background = hover; s.hover.textColor = hoverText;
            s.active.background = active; s.active.textColor = hoverText;
            s.focused.background = hover; s.focused.textColor = hoverText;
            s.onNormal.background = active; s.onNormal.textColor = hoverText;
            s.onHover.background = active; s.onHover.textColor = hoverText;
            s.onActive.background = active; s.onActive.textColor = hoverText;
            s.border = new RectOffset(5, 5, 5, 5);
            s.richText = true;
        }

        private static GUISkin Build(GUISkin baseSkin, bool contrast)
        {
            var skin = Object.Instantiate(baseSkin);
            skin.hideFlags = HideFlags.HideAndDontSave;
            Color gold = contrast ? Color.white : new Color(0.78f, 0.62f, 0.32f);
            Color text = contrast ? Color.white : new Color(0.95f, 0.9f, 0.8f);
            Color hot = contrast ? Color.yellow : new Color(1f, 0.92f, 0.6f);
            var box = Panel(contrast ? new Color(0, 0, 0, 0.97f) : new Color(0.16f, 0.12f, 0.09f, 0.93f), contrast ? new Color(0, 0, 0, 0.97f) : new Color(0.09f, 0.07f, 0.05f, 0.93f), gold);
            var button = Panel(contrast ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.34f, 0.24f, 0.15f), contrast ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.22f, 0.15f, 0.09f), gold);
            var buttonHover = Panel(contrast ? new Color(0.25f, 0.25f, 0f) : new Color(0.46f, 0.33f, 0.2f), contrast ? new Color(0.15f, 0.15f, 0f) : new Color(0.3f, 0.21f, 0.12f), hot);
            var buttonDown = Panel(contrast ? new Color(0.4f, 0.4f, 0f) : new Color(0.18f, 0.12f, 0.07f), contrast ? new Color(0.3f, 0.3f, 0f) : new Color(0.28f, 0.2f, 0.12f), hot);
            var field = Panel(contrast ? Color.black : new Color(0.05f, 0.04f, 0.03f, 0.95f), contrast ? Color.black : new Color(0.08f, 0.06f, 0.05f, 0.95f), contrast ? Color.white : new Color(0.45f, 0.36f, 0.2f));
            Style(skin.box, box, box, box, text, text);
            Style(skin.window, box, box, box, text, text);
            Style(skin.button, button, buttonHover, buttonDown, text, hot);
            Style(skin.toggle, skin.toggle.normal.background, skin.toggle.hover.background, skin.toggle.active.background, text, hot);
            skin.toggle.onNormal.background = baseSkin.toggle.onNormal.background;
            skin.toggle.onHover.background = baseSkin.toggle.onHover.background;
            skin.toggle.border = baseSkin.toggle.border;
            Style(skin.textField, field, field, field, text, hot);
            Style(skin.textArea, field, field, field, text, hot);
            skin.label.normal.textColor = text;
            skin.label.richText = true;
            skin.horizontalSlider.normal.background = field;
            skin.horizontalSliderThumb.normal.background = button;
            skin.horizontalSliderThumb.hover.background = buttonHover;
            skin.horizontalSliderThumb.active.background = buttonDown;
            skin.verticalScrollbar.normal.background = field;
            skin.verticalScrollbarThumb.normal.background = button;
            skin.horizontalScrollbar.normal.background = field;
            skin.horizontalScrollbarThumb.normal.background = button;
            return skin;
        }
    }
}
