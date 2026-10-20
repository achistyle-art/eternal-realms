using System.Collections.Generic;
using System.Text;
using EternalRealms.Shared.Content;

namespace EternalRealms.Shared.Client.Ui
{
    public enum ChatLinkKind : byte { Item, Quest, Ability }

    public struct ChatLink
    {
        public ChatLinkKind Kind;
        public string Id;
    }

    /// <summary>
    /// Verlinkungen im Chat (Kapitel 31): im Text als <c>|item:ID|</c>, <c>|quest:ID|</c> oder <c>|ability:ID|</c>.
    /// Der Server entfernt unbekannte oder zu viele Links, damit niemand gefälschte Gegenstände „zeigen“ kann;
    /// der Client stellt sie als farbige [Namen] dar. Links tragen nur Inhalts-IDs, nie Werte.
    /// </summary>
    public static class ChatLinks
    {
        public const int MaxLinks = 5;

        public static string Item(string id) => $"|item:{id}|";
        public static string Quest(string id) => $"|quest:{id}|";
        public static string Ability(string id) => $"|ability:{id}|";

        private static bool TryKind(string s, out ChatLinkKind kind)
        {
            switch (s)
            {
                case "item": kind = ChatLinkKind.Item; return true;
                case "quest": kind = ChatLinkKind.Quest; return true;
                case "ability": kind = ChatLinkKind.Ability; return true;
                default: kind = ChatLinkKind.Item; return false;
            }
        }

        /// <summary>Zerlegt den Text in Textstücke und Links (Link = Stück mit Wert).</summary>
        public static List<(string text, ChatLink? link)> Split(string text)
        {
            var parts = new List<(string, ChatLink?)>();
            int i = 0;
            var plain = new StringBuilder();
            while (i < text.Length)
            {
                if (text[i] == '|')
                {
                    int colon = text.IndexOf(':', i + 1), end = text.IndexOf('|', i + 1);
                    if (colon > i && end > colon && TryKind(text.Substring(i + 1, colon - i - 1), out var kind))
                    {
                        var id = text.Substring(colon + 1, end - colon - 1);
                        if (id.Length > 0 && id.Length <= 64 && IsIdChars(id))
                        {
                            if (plain.Length > 0) { parts.Add((plain.ToString(), null)); plain.Clear(); }
                            parts.Add(("", new ChatLink { Kind = kind, Id = id }));
                            i = end + 1;
                            continue;
                        }
                    }
                }
                plain.Append(text[i]);
                i++;
            }
            if (plain.Length > 0) parts.Add((plain.ToString(), null));
            return parts;
        }

        private static bool IsIdChars(string id)
        {
            foreach (var c in id) if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        public static bool Exists(ChatLink l, ContentDatabase content) => l.Kind switch
        {
            ChatLinkKind.Item => content.Items.ContainsKey(l.Id),
            ChatLinkKind.Quest => content.Quests.ContainsKey(l.Id),
            _ => content.AbilityRanks.ContainsKey(l.Id),
        };

        /// <summary>Server: gültige Links behalten (höchstens <see cref="MaxLinks"/>), alle anderen und lose Trennzeichen entfernen.</summary>
        public static string Sanitize(string text, ContentDatabase content)
        {
            var sb = new StringBuilder();
            int links = 0;
            foreach (var (t, link) in Split(text))
            {
                if (link == null) { sb.Append(t.Replace("|", "")); continue; }
                if (links >= MaxLinks || !Exists(link.Value, content)) continue;
                sb.Append('|').Append(link.Value.Kind.ToString().ToLowerInvariant()).Append(':').Append(link.Value.Id).Append('|');
                links++;
            }
            return sb.ToString().Trim();
        }

        public static string Name(ChatLink l, ContentDatabase content) => l.Kind switch
        {
            ChatLinkKind.Item => content.Items.TryGetValue(l.Id, out var i) ? i.Name : l.Id,
            ChatLinkKind.Quest => content.Quests.TryGetValue(l.Id, out var q) ? q.Title : l.Id,
            _ => content.AbilityRanks.TryGetValue(l.Id, out var r) && r.Count > 0 ? r[0].Name : l.Id,
        };

        /// <summary>Text ohne Steuerzeichen, Links als [Name] (für Bots, Protokolle und einfache Anzeigen).</summary>
        public static string Plain(string text, ContentDatabase content)
        {
            var sb = new StringBuilder();
            foreach (var (t, link) in Split(text)) sb.Append(link == null ? t : "[" + Name(link.Value, content) + "]");
            return sb.ToString();
        }
    }
}
