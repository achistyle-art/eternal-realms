using System;
using System.Collections.Generic;
using System.Linq;

namespace EternalRealms.Shared.Client.Ui
{
    /// <summary>
    /// Frei belegbare Tasten (Kapitel 29). Eine Taste wird als Text gespeichert, z. B. "W", "Shift+B", "Alpha1", "Mouse4".
    /// Jede Aktion hat eine Haupt- und eine optionale Zweitbelegung; eine Taste gehört immer nur einer Aktion.
    /// </summary>
    public sealed class Keybindings
    {
        public static readonly IReadOnlyList<(string Action, string Label, string Key)> Defaults = BuildDefaults();

        private static List<(string, string, string)> BuildDefaults()
        {
            var list = new List<(string, string, string)>
            {
                ("move_forward", "Vorwärts", "W"), ("move_back", "Rückwärts", "S"), ("turn_left", "Nach links", "A"), ("turn_right", "Nach rechts", "D"),
                ("strafe_left", "Seitwärts links", "Q"), ("strafe_right", "Seitwärts rechts", "E"), ("jump", "Springen", "Space"),
                ("autorun", "Automatisch laufen", "Numlock"), ("walk_toggle", "Gehen/Rennen", "Slash"),
                ("target_next", "Nächstes Ziel", "Tab"), ("interact", "Interagieren", "F"), ("mount", "Aufsitzen", "Y"),
                ("bags", "Inventar", "I"), ("all_bags", "Alle Taschen", "Shift+B"), ("character", "Charakterfenster", "C"), ("world_map", "Weltkarte", "M"),
                ("quest_log", "Questlog", "L"), ("spellbook", "Zauberbuch", "P"), ("talents", "Talente", "N"), ("social", "Sozialmenü", "O"),
                ("group", "Gruppe und Gruppensuche", "G"), ("mounts", "Reittiere", "J"), ("professions", "Berufe", "U"),
                ("chat", "Chat", "Return"), ("reply", "Antworten", "R"), ("menu", "Hauptmenü", "Escape"), ("toggle_ui", "Oberfläche ein/aus", "Alt+Z"),
                ("minimap_zoom_in", "Minikarte vergrößern", "Equals"), ("minimap_zoom_out", "Minikarte verkleinern", "Minus"),
            };
            for (int i = 0; i < 12; i++)
            {
                string key = i < 9 ? "Alpha" + (i + 1) : i == 9 ? "Alpha0" : i == 10 ? "Minus" : "Equals";
                list.Add(("action_1_" + (i + 1), $"Aktionsleiste 1, Platz {i + 1}", i < 10 ? key : ""));
                list.Add(("action_2_" + (i + 1), $"Aktionsleiste 2, Platz {i + 1}", i < 10 ? "Shift+" + key : ""));
                list.Add(("action_3_" + (i + 1), $"Aktionsleiste 3, Platz {i + 1}", ""));
                list.Add(("action_4_" + (i + 1), $"Aktionsleiste 4, Platz {i + 1}", ""));
            }
            // Minus/Gleich gehören standardmäßig der Minikarte; Plätze 11/12 sind frei belegbar.
            return list;
        }

        /// <summary>Aktion → Tasten (0 = Haupt-, 1 = Zweitbelegung).</summary>
        public Dictionary<string, string[]> Bindings { get; set; } = new Dictionary<string, string[]>();

        public static Keybindings CreateDefault()
        {
            var k = new Keybindings();
            foreach (var (action, _, key) in Defaults) k.Bindings[action] = new[] { key, "" };
            return k;
        }

        /// <summary>Fehlende Aktionen (neue Versionen) mit Standard ergänzen, ohne eigene Belegungen zu überschreiben.</summary>
        public void Normalize()
        {
            foreach (var keys in Bindings.Values) if (keys != null) for (int i = 0; i < keys.Length; i++) keys[i] = Normalize(keys[i] ?? "");
            foreach (var (action, _, key) in Defaults)
            {
                if (!Bindings.TryGetValue(action, out var keys) || keys == null || keys.Length != 2)
                    Bindings[action] = new[] { IsBound(key) ? "" : key, keys != null && keys.Length > 1 ? keys[1] : "" };
            }
            foreach (var unknown in Bindings.Keys.Where(a => Defaults.All(d => d.Action != a)).ToList()) Bindings.Remove(unknown);
        }

        public string? ActionFor(string key)
        {
            key = Normalize(key);
            if (key.Length == 0) return null;
            foreach (var kv in Bindings) if (kv.Value.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))) return kv.Key;
            return null;
        }

        public bool IsBound(string key) => ActionFor(key) != null;

        public string KeyFor(string action, int index = 0) => Bindings.TryGetValue(action, out var k) && index < k.Length ? k[index] : "";

        /// <summary>
        /// Belegt eine Taste neu. War sie schon vergeben, verliert die andere Aktion diese Belegung.
        /// Rückgabe: die Aktion, der die Taste weggenommen wurde (für einen Hinweis), oder null.
        /// </summary>
        public string? Bind(string action, string key, int index = 0)
        {
            if (Defaults.All(d => d.Action != action)) throw new ArgumentException("Unbekannte Aktion: " + action);
            if (index < 0 || index > 1) throw new ArgumentOutOfRangeException(nameof(index));
            key = Normalize(key);
            string? previous = null;
            if (key.Length > 0)
                foreach (var kv in Bindings)
                    for (int i = 0; i < kv.Value.Length; i++)
                        if (string.Equals(kv.Value[i], key, StringComparison.OrdinalIgnoreCase) && !(kv.Key == action && i == index))
                        {
                            kv.Value[i] = "";
                            previous = kv.Key;
                        }
            if (!Bindings.TryGetValue(action, out var keys)) Bindings[action] = keys = new[] { "", "" };
            keys[index] = key;
            return previous;
        }

        public void Unbind(string action, int index = 0) => Bind(action, "", index);

        public void ResetToDefaults() => Bindings = CreateDefault().Bindings;

        /// <summary>Einheitliche Schreibweise: Modifikatoren in fester Reihenfolge (Ctrl, Alt, Shift), dann die Taste.</summary>
        public static string Normalize(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            var parts = key.Split('+').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            var main = parts.Last();
            var mods = parts.Take(parts.Count - 1).Select(m => m.ToLowerInvariant() switch
            {
                "strg" or "control" or "ctrl" => "Ctrl", "alt" => "Alt", "umschalt" or "shift" => "Shift", _ => m,
            }).Distinct().OrderBy(m => m == "Ctrl" ? 0 : m == "Alt" ? 1 : 2);
            return string.Join("+", mods.Concat(new[] { main }));
        }
    }
}
