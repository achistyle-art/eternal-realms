using System.Text.RegularExpressions;

namespace EternalRealms.Shared.Client.Ui
{
    /// <summary>Setzt in Tutorialtexten {key:aktion} durch die tatsächliche Tastenbelegung des Spielers ein.</summary>
    public static class TutorialText
    {
        private static readonly Regex Token = new Regex(@"\{key:([a-z0-9_]+)\}", RegexOptions.Compiled);

        public static string Render(string text, Keybindings keys) => Token.Replace(text, m =>
        {
            var k = keys.KeyFor(m.Groups[1].Value);
            if (k.Length == 0) k = keys.KeyFor(m.Groups[1].Value, 1);
            return k.Length == 0 ? "(nicht belegt)" : "[" + Pretty(k) + "]";
        });

        /// <summary>Lesbare Tastennamen: "Alpha1" → "1", "Return" → "Eingabe".</summary>
        public static string Pretty(string key) => key.Replace("Alpha", "").Replace("Return", "Eingabe").Replace("Escape", "Esc").Replace("Space", "Leertaste")
            .Replace("Numlock", "Num-Taste").Replace("Ctrl", "Strg").Replace("Shift", "Umschalt");
    }
}
