using System.Text.RegularExpressions;
using EternalRealms.Shared;

namespace EternalRealms.Server.AI
{
    public enum BotCommand
    {
        None, Follow, Stay, Attack, Dungeon, Rest, Passive, Assist, Aggressive, Status, Pull, Buff,
        Focus, Interrupt, Heal, Mana, AvoidAoe, Control, Retreat,
    }

    public enum OrderSide { None, Left, Right }

    /// <summary>Ein verstandener Befehl aus dem Gruppenchat.</summary>
    public sealed class BotOrder
    {
        public BotCommand Command;
        /// <summary>Angesprochene (Namen, Rollen- oder Klassenwörter). Leer = alle.</summary>
        public List<string> Addressees = new();
        public OrderSide Side;
        public bool MentionsBoss;
        public string Text = "";
    }

    /// <summary>
    /// Versteht natürliche Befehle im Gruppenchat, z. B. „Tank, greif den Boss an.“, „Heiler, bitte Mana auffüllen.“,
    /// „Magier, kontrolliere den linken Gegner.“ oder „Alle zurückziehen.“ Die Anrede steht vor dem ersten Komma
    /// oder Doppelpunkt (bzw. ist das erste Wort); fehlt sie, gilt der Befehl allen.
    /// </summary>
    public static class BotCommandParser
    {
        private static readonly (BotCommand cmd, Regex re)[] Table =
        {
            (BotCommand.Dungeon, R(@"dungeon|instanz|krypta|verlies")),
            (BotCommand.Retreat, R(@"zurückzieh|zurueckzieh|rückzug|rueckzug|zieht euch zurück|lauft weg|weg hier|retreat")),
            (BotCommand.Control, R(@"kontrollier|verwandl|\bschaf\b|fessel|festhalten|\bcc\b|ausschalten")),
            (BotCommand.Interrupt, R(@"unterbrech|unterbrich|\bkick\b")),
            (BotCommand.Mana, R(@"mana auf|mana regener|mana tanken|trink|auffüll")),
            (BotCommand.Heal, R(@"\bheil(e|t|ung)?\b|\bheal\b|heilt mich|heil mich")),
            (BotCommand.Focus, R(@"fokus|konzentriert euch")),
            (BotCommand.AvoidAoe, R(@"flächen|flaechen|\baoe\b|verteilt euch|auseinander")),
            (BotCommand.Stay, R(@"\bwartet\b|\bwarte\b|\bbleibt\b|\bbleib\b|\bstopp?\b|\bhalt\b|position halten|\bstay\b")),
            (BotCommand.Follow, R(@"\bfolgt\b|\bfolge\b|\bfolg\b|\bkommt\b|follow|mir nach")),
            (BotCommand.Pull, R(@"\bzieh\b|\bpull")),
            (BotCommand.Attack, R(@"angriff|greift|greif\b|attack|auf ihn|attacke|\blos!")),
            (BotCommand.Rest, R(@"pause|\brast|esst|ausruhen")),
            (BotCommand.Passive, R(@"passiv")),
            (BotCommand.Aggressive, R(@"aggressiv|\baggro\b")),
            (BotCommand.Assist, R(@"verteidig|\bnormal\b|assist")),
            (BotCommand.Status, R(@"status|wie geht|bericht")),
            (BotCommand.Buff, R(@"\bbuff")),
        };

        private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static readonly Dictionary<string, Role> RoleWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["tank"] = Role.Tank, ["tanks"] = Role.Tank, ["heiler"] = Role.Healer, ["heilerin"] = Role.Healer, ["healer"] = Role.Healer,
            ["dds"] = Role.MeleeDps, ["schaden"] = Role.MeleeDps, ["nahkämpfer"] = Role.MeleeDps, ["fernkämpfer"] = Role.RangedDps,
        };

        public static readonly Dictionary<string, CharClass> ClassWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["krieger"] = CharClass.Warrior, ["paladin"] = CharClass.Paladin, ["jäger"] = CharClass.Hunter, ["jaeger"] = CharClass.Hunter,
            ["schurke"] = CharClass.Rogue, ["priester"] = CharClass.Priest, ["priesterin"] = CharClass.Priest, ["schamane"] = CharClass.Shaman,
            ["magier"] = CharClass.Mage, ["magierin"] = CharClass.Mage, ["hexer"] = CharClass.Warlock, ["hexenmeister"] = CharClass.Warlock,
            ["druide"] = CharClass.Druid, ["druidin"] = CharClass.Druid,
        };

        public static BotCommand ParseCommand(string text) => Parse(text, Array.Empty<string>()).Command;

        public static BotOrder Parse(string text, IEnumerable<string> botNames)
        {
            var order = new BotOrder { Text = text };
            var lower = " " + text.ToLowerInvariant() + " ";
            foreach (var (cmd, re) in Table)
                if (re.IsMatch(lower)) { order.Command = cmd; break; }
            order.MentionsBoss = lower.Contains("boss");
            if (lower.Contains("link")) order.Side = OrderSide.Left;
            else if (lower.Contains("recht")) order.Side = OrderSide.Right;

            // Anrede: alles vor dem ersten Komma/Doppelpunkt, sonst das erste Wort.
            int cut = text.IndexOfAny(new[] { ',', ':' });
            string head = cut > 0 ? text.Substring(0, cut) : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            foreach (var raw in head.Split(new[] { ' ', '&', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var w = raw.Trim('.', '!', '?');
                if (w.Equals("und", StringComparison.OrdinalIgnoreCase)) continue;
                if (w.Equals("alle", StringComparison.OrdinalIgnoreCase) || w.Equals("ihr", StringComparison.OrdinalIgnoreCase)) { order.Addressees.Clear(); break; }
                if (RoleWords.ContainsKey(w) || ClassWords.ContainsKey(w) || botNames.Any(n => n.Equals(w, StringComparison.OrdinalIgnoreCase)))
                    order.Addressees.Add(w);
            }
            return order;
        }
    }
}
