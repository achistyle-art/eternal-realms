using System;
using EternalRealms.Shared.Net;

namespace EternalRealms.Shared.Client.Ui
{
    /// <summary>Chatreiter-Filter und Kampflogzeilen (Kapitel 28/31) – ohne Unity testbar.</summary>
    public static class ChatFormat
    {
        /// <summary>Zeigt ein Reiter diesen Kanal? Leere Kanalliste = alle; Systemmeldungen erscheinen immer.</summary>
        public static bool Accepts(ChatTab tab, ChatChannel channel) =>
            channel == ChatChannel.System || tab.Channels.Count == 0 || tab.Channels.Contains((int)channel);

        public static string ChannelPrefix(ChatChannel c) => c switch
        {
            ChatChannel.Party => "[Gruppe] ", ChatChannel.Raid => "[Schlachtzug] ", ChatChannel.Guild => "[Gilde] ", ChatChannel.General => "[Allgemein] ",
            ChatChannel.Whisper => "[Flüstern] ", ChatChannel.Yell => "[Schreien] ", _ => "",
        };

        public static string Timestamp(DateTime t) => t.ToString("HH:mm");

        /// <summary>Eine Kampflogzeile, z. B. „Wolf trifft Euch (Biss) für 12 Körperlich.“</summary>
        public static string CombatLine(S_CombatEvent e, Func<int, string> name, Func<string, string> ability)
        {
            string src = name(e.SourceId), dst = name(e.TargetId);
            string what = e.AbilityId != "" ? $" ({ability(e.AbilityId)})" : "";
            string school = ItemTooltip.SchoolName(e.School);
            string extra = (e.Absorbed > 0 ? $", {e.Absorbed} absorbiert" : "") + (e.Resisted > 0 ? $", {e.Resisted} widerstanden" : "") + (e.Overkill > 0 ? $", {e.Overkill} Überschuss" : "");
            string dot = e.Periodic ? " (regelmäßig)" : "";
            return e.Result switch
            {
                CombatResult.Heal => $"{src} heilt {dst}{what} um {e.Amount}{dot}.",
                CombatResult.CritHeal => $"{src} heilt {dst}{what} kritisch um {e.Amount}{dot}.",
                CombatResult.Crit => $"{src} trifft {dst}{what} kritisch für {e.Amount} {school}{extra}{dot}.",
                CombatResult.Miss => $"{src} verfehlt {dst}{what}.",
                CombatResult.Dodge => $"{dst} weicht {src}{what} aus.",
                CombatResult.Parry => $"{dst} pariert {src}{what}.",
                CombatResult.Block => $"{dst} blockt {src}{what}{(e.Amount > 0 ? $", {e.Amount} {school}" : "")}.",
                CombatResult.Resist => $"{dst} widersteht {src}{what}.",
                CombatResult.Absorb => $"{src}{what}: vollständig absorbiert von {dst}.",
                CombatResult.Immune => $"{dst} ist immun gegen {src}{what}.",
                CombatResult.Evade => $"{dst} entgeht {src}{what}.",
                _ => $"{src} trifft {dst}{what} für {e.Amount} {school}{extra}{dot}.",
            };
        }
    }
}
