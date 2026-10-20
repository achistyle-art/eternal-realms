using System.Collections.Generic;

namespace EternalRealms.Shared.Content
{
    /// <summary>
    /// Fortschrittsereignisse des Spielers (Kapitel 38/39). Erfolge und Tutorial hören auf dieselben Ereignisse.
    /// Ziel "" = beliebig. „Wert“-Ereignisse (Stufe, Fertigkeit, Ruf, Reittiere) melden den aktuellen Stand statt einer Anzahl.
    /// </summary>
    public enum ProgressEvent : byte
    {
        ReachLevel = 0, Kill, KillFamily, KillRare, KillBoss, CompleteQuest, AcceptQuest, Discover, VisitZone,
        ProfessionSkill, LearnProfession, Craft, Gather, Loot, WorldEvent, Emote, Trade, JoinGuild, AddFriend,
        GroupWithBot, LearnRiding, MountsOwned, Die, Reputation, EnterDungeon, Move, Target, AttackStart, CastAbility,
        Equip, LearnAbility, Bind, Flight, TutorialDone,
    }

    public static class ProgressEvents
    {
        /// <summary>Ereignisse, bei denen der höchste gemeldete Wert zählt (nicht die Summe).</summary>
        public static bool IsValue(ProgressEvent e) => e is ProgressEvent.ReachLevel or ProgressEvent.ProfessionSkill or ProgressEvent.Reputation or ProgressEvent.MountsOwned;
    }

    public sealed class CriterionDef
    {
        public ProgressEvent Event { get; set; }
        public string Target { get; set; } = "";
        public int Count { get; set; } = 1;
        /// <summary>Anzeige im Erfolgsfenster, z. B. „Graumähne besiegt“.</summary>
        public string Description { get; set; } = "";
    }

    /// <summary>Erfolg (Kapitel 38): Punkte und ggf. ein Titel – nie ein Spielvorteil.</summary>
    public sealed class AchievementDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "Allgemein";
        public int Points { get; set; } = 10;
        /// <summary>Nur für diese Fraktion (z. B. Hauptstädte besuchen).</summary>
        public Faction? Faction { get; set; }
        /// <summary>Bis zum Abschluss verborgen (Überraschungserfolge).</summary>
        public bool Hidden { get; set; }
        public string? Title { get; set; }
        public List<CriterionDef> Criteria { get; set; } = new List<CriterionDef>();
    }

    /// <summary>Schritt des Tutorials (Kapitel 39). Texte dürfen {key:aktion} enthalten – der Client setzt die eigene Belegung ein.</summary>
    public sealed class TutorialStepDef
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        /// <summary>Zusätzlicher Tipp, wenn der Spieler eine Weile nicht weiterkommt.</summary>
        public string Hint { get; set; } = "";
        /// <summary>Was den Schritt abschließt; null = nach AutoSeconds weiter.</summary>
        public ProgressEvent? Trigger { get; set; }
        public string Target { get; set; } = "";
        public int Count { get; set; } = 1;
        public float AutoSeconds { get; set; }
        /// <summary>Worauf die KI-Erklärerin zeigt: "QuestGiver", "Hostile", "Trainer", "Innkeeper", "Corpse" oder leer.</summary>
        public string Focus { get; set; } = "";
        /// <summary>Emote der Erklärerin zu Beginn des Schritts.</summary>
        public string Emote { get; set; } = "";
    }
}
