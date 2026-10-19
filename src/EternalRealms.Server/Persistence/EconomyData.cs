namespace EternalRealms.Server.Persistence
{
    /// <summary>Ein Brief im Postfach (mit bis zu 12 Anhängen, Geld und optionaler Nachnahme).</summary>
    public sealed class MailData
    {
        public long Id { get; set; }
        public long RecipientId { get; set; }
        public long? SenderId { get; set; }
        public string SenderName { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public long Money { get; set; }
        /// <summary>Nachnahme: Der Empfänger zahlt diesen Betrag, um die Anhänge zu erhalten.</summary>
        public long Cod { get; set; }
        public List<ItemStack> Items { get; set; } = new();
        public DateTime DeliverAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(30);
        public bool Read { get; set; }
        /// <summary>Bereits an den Absender zurückgeschickt (wird nicht erneut zurückgeschickt, sondern gelöscht).</summary>
        public bool Returned { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public MailData Clone() => new()
        {
            Id = Id, RecipientId = RecipientId, SenderId = SenderId, SenderName = SenderName, Subject = Subject, Body = Body, Money = Money, Cod = Cod,
            Items = Items.Select(i => new ItemStack { ItemId = i.ItemId, Count = i.Count, Durability = i.Durability }).ToList(),
            DeliverAt = DeliverAt, ExpiresAt = ExpiresAt, Read = Read, Returned = Returned, CreatedAt = CreatedAt,
        };
    }

    /// <summary>Ein Angebot im Auktionshaus. Das Gebot des Höchstbietenden ist bereits von dessen Geld abgezogen (treuhänderisch).</summary>
    public sealed class AuctionData
    {
        public long Id { get; set; }
        /// <summary>Auktionshaus: Fraktion (1 Sonnenbündnis, 2 Schattenpakt) – Häuser sind getrennt.</summary>
        public byte House { get; set; }
        public long SellerId { get; set; }
        public string SellerName { get; set; } = "";
        public bool SellerIsBot { get; set; }
        public string ItemId { get; set; } = "";
        public int Count { get; set; } = 1;
        public int Durability { get; set; }
        public long StartBid { get; set; }
        public long Buyout { get; set; }
        public long CurrentBid { get; set; }
        public long? BidderId { get; set; }
        public string BidderName { get; set; } = "";
        public long Deposit { get; set; }
        public int DurationHours { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }

        public AuctionData Clone() => (AuctionData)MemberwiseClone();
    }

    /// <summary>Eintrag im Geldprotokoll: jede Geldbewegung auf dem Server wird mit Grund festgehalten.</summary>
    public sealed class LedgerEntry
    {
        public DateTime At { get; set; } = DateTime.UtcNow;
        public long CharacterId { get; set; }
        public string CharacterName { get; set; } = "";
        public long Delta { get; set; }
        public long Balance { get; set; }
        public string Reason { get; set; } = "";
        public string Reference { get; set; } = "";
    }

    /// <summary>Alles, was atomar gemeinsam geschrieben werden muss.</summary>
    public sealed class EconomyCommit
    {
        public List<CharacterData> Characters { get; } = new();
        public List<MailData> UpsertMail { get; } = new();
        public List<long> DeleteMail { get; } = new();
        public List<AuctionData> UpsertAuctions { get; } = new();
        public List<long> DeleteAuctions { get; } = new();
        public List<LedgerEntry> Ledger { get; } = new();
        public bool IsEmpty => Characters.Count + UpsertMail.Count + DeleteMail.Count + UpsertAuctions.Count + DeleteAuctions.Count + Ledger.Count == 0;
    }

    /// <summary>Kurzinfo zu einem (auch nicht eingeloggten) Charakter, z. B. als Briefempfänger.</summary>
    public sealed class CharacterRef
    {
        public long Id { get; set; }
        public long? AccountId { get; set; }
        public string Name { get; set; } = "";
        public Shared.Race Race { get; set; }
    }

    public interface IEconomyRepository
    {
        Task CommitAsync(EconomyCommit commit);
        Task<List<MailData>> LoadMailAsync();
        Task<List<AuctionData>> LoadAuctionsAsync();
        Task<CharacterRef?> FindCharacterAsync(string name);
        Task<List<LedgerEntry>> LedgerForAsync(long characterId, int limit);
    }
}
