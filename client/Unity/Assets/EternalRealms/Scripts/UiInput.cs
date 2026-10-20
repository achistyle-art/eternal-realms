using System;
using System.Collections.Generic;
using EternalRealms.Shared.Client.Ui;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Übersetzt die frei belegbaren Tasten (<see cref="Keybindings"/>, Schreibweise wie Unitys KeyCode, z. B. "Shift+Alpha1")
    /// in Abfragen des Unity-Inputs. Gedrückt (Down) verlangt genau die angegebenen Modifikatoren,
    /// gehalten (Held) nur deren Vorhandensein – so läuft man auch mit gedrückter Umschalttaste weiter.
    /// </summary>
    public static class UiInput
    {
        private readonly struct Combo
        {
            public readonly KeyCode Key;
            public readonly bool Ctrl, Alt, Shift;
            public Combo(KeyCode key, bool ctrl, bool alt, bool shift) { Key = key; Ctrl = ctrl; Alt = alt; Shift = shift; }
        }

        private static readonly Dictionary<string, Combo?> Cache = new Dictionary<string, Combo?>();

        private static Combo? Parse(string text)
        {
            if (Cache.TryGetValue(text, out var c)) return c;
            Combo? result = null;
            var parts = text.Split('+');
            if (parts.Length > 0 && Enum.TryParse<KeyCode>(parts[parts.Length - 1], true, out var key))
            {
                bool ctrl = false, alt = false, shift = false;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i] == "Ctrl") ctrl = true;
                    else if (parts[i] == "Alt") alt = true;
                    else if (parts[i] == "Shift") shift = true;
                }
                result = new Combo(key, ctrl, alt, shift);
            }
            Cache[text] = result;
            return result;
        }

        private static bool CtrlHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static bool AltHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        private static bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        public static bool Down(Keybindings keys, string action)
        {
            if (!keys.Bindings.TryGetValue(action, out var list)) return false;
            foreach (var k in list)
            {
                if (string.IsNullOrEmpty(k)) continue;
                var c = Parse(k);
                if (c == null || !Input.GetKeyDown(c.Value.Key)) continue;
                if (c.Value.Ctrl == CtrlHeld && c.Value.Alt == AltHeld && c.Value.Shift == ShiftHeld) return true;
            }
            return false;
        }

        public static bool Held(Keybindings keys, string action)
        {
            if (!keys.Bindings.TryGetValue(action, out var list)) return false;
            foreach (var k in list)
            {
                if (string.IsNullOrEmpty(k)) continue;
                var c = Parse(k);
                if (c == null || !Input.GetKey(c.Value.Key)) continue;
                if ((!c.Value.Ctrl || CtrlHeld) && (!c.Value.Alt || AltHeld) && (!c.Value.Shift || ShiftHeld)) return true;
            }
            return false;
        }

        /// <summary>Neue Belegung aus einem OnGUI-Tastenereignis; null, solange nur ein Modifikator gedrückt ist.</summary>
        public static string? Capture(Event e)
        {
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return null;
            switch (e.keyCode)
            {
                case KeyCode.LeftShift: case KeyCode.RightShift: case KeyCode.LeftControl: case KeyCode.RightControl:
                case KeyCode.LeftAlt: case KeyCode.RightAlt: case KeyCode.LeftCommand: case KeyCode.RightCommand:
                    return null;
            }
            string mods = (e.control ? "Ctrl+" : "") + (e.alt ? "Alt+" : "") + (e.shift ? "Shift+" : "");
            return Keybindings.Normalize(mods + e.keyCode);
        }

        /// <summary>Kurzform für Aktionsknöpfe und Menüs: "Shift+Alpha1" → "S-1".</summary>
        public static string Short(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return key.Replace("Ctrl+", "C-").Replace("Alt+", "A-").Replace("Shift+", "S-").Replace("Alpha", "")
                .Replace("Keypad", "N").Replace("Equals", "=").Replace("Minus", "-").Replace("Return", "Enter").Replace("Escape", "Esc").Replace("Numlock", "Num");
        }
    }
}
