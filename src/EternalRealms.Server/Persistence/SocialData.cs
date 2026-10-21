namespace EternalRealms.Server.Persistence
{
    /// <summary>Eintrag in Freundes- oder Ignorierliste (Name/Stufe/Klasse als zuletzt bekannter Stand).</summary>
    public sealed class SocialEntry
    {
        public long CharacterId { get; set; }
        public string Name { get; set; } = "";
        public string Note { get; set; } = "";
    }

    public sealed class GuildMemberData
    {
        public long CharacterId { get; set; }
        public string Name { get; set; } = "";
        /// <summary>0 = Gildenmeister, 1 = Offizier, 2 = Veteran, 3 = Mitglied, 4 = Rekrut.</summary>
        public int Rank { get; set; } = 4;
        public string Note { get; set; } = "";
        public int Level { get; set; }
        public Shared.CharClass Class { get; set; }
        public bool IsBot { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class GuildData
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public Shared.Faction Faction { get; set; }
        public long LeaderId { get; set; }
        public string Motd { get; set; } = "";
        public string Info { get; set; } = "";
        public List<string> RankNames { get; set; } = new() { "Gildenmeister", "Offizier", "Veteran", "Mitglied", "Rekrut" };
        public List<GuildMemberData> Members { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public GuildData Clone() => new()
        {
            Id = Id, Name = Name, Faction = Faction, LeaderId = LeaderId, Motd = Motd, Info = Info, RankNames = RankNames.ToList(), CreatedAt = CreatedAt,
            Members = Members.Select(m => new GuildMemberData { CharacterId = m.CharacterId, Name = m.Name, Rank = m.Rank, Note = m.Note, Level = m.Level, Class = m.Class, IsBot = m.IsBot, JoinedAt = m.JoinedAt }).ToList(),
        };
    }

    /// <summary>Spielermeldung (Kapitel 31/32/49): wird gespeichert und von Game-Mastern ausgewertet.</summary>
    public sealed class PlayerReport
    {
        public long Id { get; set; }
        public bool Handled { get; set; }
        public long ReporterId { get; set; }
        public string ReporterName { get; set; } = "";
        public long? TargetId { get; set; }
        public string TargetName { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Text { get; set; } = "";
        /// <summary>Letzte Chatzeilen des Gemeldeten als Beleg.</summary>
        public List<string> Evidence { get; set; } = new();
        public DateTime At { get; set; } = DateTime.UtcNow;
    }

    public interface ISocialRepository
    {
        Task<List<GuildData>> LoadGuildsAsync();
        /// <summary>Gilde samt Mitgliedern vollständig schreiben (anlegen oder ersetzen).</summary>
        Task SaveGuildAsync(GuildData guild);
        Task DeleteGuildAsync(long guildId);
        Task SaveReportAsync(PlayerReport report);
        /// <summary>Unbearbeitete Meldungen, neueste zuerst (Spielleitung, Kapitel 50).</summary>
        Task<List<PlayerReport>> ListOpenReportsAsync(int limit);
        Task<bool> MarkReportHandledAsync(long id);
    }
}
