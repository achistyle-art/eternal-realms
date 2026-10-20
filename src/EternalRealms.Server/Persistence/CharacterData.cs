using System.Text.Json;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Persistence
{
    /// <summary>
    /// Persistenter Zustand eines Charakters (Spieler oder KI-Mitspieler). Wird beim Einloggen geladen,
    /// während des Spiels von der Spielfigur gepflegt und regelmäßig als Kopie gespeichert.
    /// </summary>
    public sealed class CharacterData
    {
        public long Id { get; set; }
        public long? AccountId { get; set; }
        public string Name { get; set; } = "";
        public Race Race { get; set; }
        public CharClass Class { get; set; }
        public Gender Gender { get; set; }
        public AppearanceDto Appearance { get; set; } = new();
        public int Level { get; set; } = 1;
        public int Xp { get; set; }
        public int RestedXp { get; set; }
        public long Money { get; set; }
        public string ZoneId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Facing { get; set; }
        public int Health { get; set; }
        public int Mana { get; set; }
        public bool IsBot { get; set; }
        public BotData? Bot { get; set; }
        public Dictionary<string, int> Abilities { get; set; } = new();
        public Dictionary<string, int> Talents { get; set; } = new();
        /// <summary>Taschenplätze; null = leer.</summary>
        public List<ItemStack?> Bag { get; set; } = new();
        public Dictionary<EquipSlot, ItemStack> Equipment { get; set; } = new();
        /// <summary>Angelegte Taschen (Platz 0–3).</summary>
        public Dictionary<int, ItemStack> BagContainers { get; set; } = new();
        /// <summary>Bankfächer; null = leer.</summary>
        public List<ItemStack?> Bank { get; set; } = new();
        /// <summary>Reitfertigkeit: 0, 75 (Lehrling, +60 %) oder 150 (Experte, +100 %).</summary>
        public int RidingSkill { get; set; }
        /// <summary>Reittier-Sammlung.</summary>
        public HashSet<string> Mounts { get; set; } = new();
        public List<SocialEntry> Friends { get; set; } = new();
        public List<SocialEntry> Ignored { get; set; } = new();
        /// <summary>Freunde dürfen sehen, in welcher Region der Charakter ist.</summary>
        public bool ShareLocation { get; set; } = true;
        /// <summary>Instanzbindungen je Instanzzone (Kapitel 10/11).</summary>
        public Dictionary<string, InstanceBind> InstanceBinds { get; set; } = new();
        /// <summary>Instanz, in der der Charakter zuletzt war (Wiederverbindung).</summary>
        public long CurrentInstanceId { get; set; }
        /// <summary>Zeitpunkte neu betretener Instanzen (Begrenzung je Stunde).</summary>
        public List<DateTime> RecentInstances { get; set; } = new();
        /// <summary>Berufe (Beruf-ID → Fortschritt).</summary>
        public Dictionary<string, ProfessionProgress> Professions { get; set; } = new();
        /// <summary>Clienteinstellungen (Tasten, Leisten, Fenster, Barrierefreiheit) als JSON; der Server prüft nur die Größe.</summary>
        public string ClientSettings { get; set; } = "";
        /// <summary>Verlassene öffentliche Kanäle (ChatChannel als Zahl); standardmäßig ist man in allen.</summary>
        public HashSet<int> LeftChannels { get; set; } = new();
        /// <summary>Erfolge (Kapitel 38): Fortschritt je Erfolg und gewählter Titel.</summary>
        public Dictionary<string, AchievementProgress> Achievements { get; set; } = new();
        public string ActiveTitle { get; set; } = "";
        /// <summary>Tutorial (Kapitel 39); null = noch nie gestartet.</summary>
        public TutorialState? Tutorial { get; set; }
        public int TalentResets { get; set; }
        public DateTime LastTalentReset { get; set; }
        public Dictionary<string, QuestProgress> Quests { get; set; } = new();
        public HashSet<string> CompletedQuests { get; set; } = new();
        public Dictionary<string, int> Reputation { get; set; } = new();
        public HashSet<string> DiscoveredAreas { get; set; } = new();
        public HashSet<string> KnownTravelNodes { get; set; } = new();
        public DeathState DeathState { get; set; }
        public string CorpseZone { get; set; } = "";
        public Vec3 CorpsePosition { get; set; }
        public string BindZone { get; set; } = "";
        public Vec3 BindPosition { get; set; }
        public string? ReturnZone { get; set; }
        public Vec3 ReturnPosition { get; set; }
        public long PlayedSeconds { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastLogout { get; set; } = DateTime.UtcNow;
        public bool LoggedOutInInn { get; set; }

        private static readonly JsonSerializerOptions Json = ContentDatabase.CreateOptions();

        /// <summary>Tiefe Kopie für das Speichern im Hintergrund (der Spielthread arbeitet am Original weiter).</summary>
        public CharacterData Clone() => JsonSerializer.Deserialize<CharacterData>(JsonSerializer.Serialize(this, Json), Json)!;

        public CharacterSummary ToSummary() => new()
        {
            Id = Id, Name = Name, Race = Race, Class = Class, Gender = Gender, Level = Level, ZoneId = ZoneId, Appearance = Appearance.ToNet(),
        };
    }

    public sealed class AchievementProgress
    {
        public List<int> Counts { get; set; } = new();
        public DateTime? CompletedAt { get; set; }
    }

    public sealed class TutorialState
    {
        public int Step { get; set; }
        public int Counter { get; set; }
        public bool Done { get; set; }
        public bool Skipped { get; set; }
    }

    public sealed class AppearanceDto
    {
        public byte SkinColor { get; set; }
        public byte Face { get; set; }
        public byte HairStyle { get; set; }
        public byte HairColor { get; set; }
        public byte Beard { get; set; }
        public byte EyeColor { get; set; }
        public byte Scar { get; set; }
        public byte Tattoo { get; set; }
        public float Height { get; set; } = 1f;

        public AppearanceData ToNet() => new()
        {
            SkinColor = SkinColor, Face = Face, HairStyle = HairStyle, HairColor = HairColor, Beard = Beard, EyeColor = EyeColor, Scar = Scar, Tattoo = Tattoo, Height = Height,
        };

        public static AppearanceDto From(AppearanceData a) => new()
        {
            SkinColor = a.SkinColor, Face = a.Face, HairStyle = a.HairStyle, HairColor = a.HairColor, Beard = a.Beard, EyeColor = a.EyeColor, Scar = a.Scar, Tattoo = a.Tattoo, Height = a.Height,
        };
    }

    public sealed class ItemStack
    {
        public string ItemId { get; set; } = "";
        public int Count { get; set; } = 1;
        public int Durability { get; set; }
        /// <summary>Dauerhafte Verzauberung (Rezept-ID), leer = keine.</summary>
        public string Enchant { get; set; } = "";

        public ItemStackData ToNet() => new() { ItemId = ItemId, Count = Count, Durability = Durability, Enchant = Enchant };
    }

    /// <summary>Fortschritt in einem Beruf.</summary>
    public sealed class ProfessionProgress
    {
        public int Skill { get; set; } = 1;
        public int MaxSkill { get; set; } = 75;
        public HashSet<string> Recipes { get; set; } = new();
    }

    public sealed class QuestProgress
    {
        public string QuestId { get; set; } = "";
        public QuestState State { get; set; }
        public int[] Counts { get; set; } = Array.Empty<int>();
        public float TimeLeft { get; set; }

        public QuestProgressData ToNet() => new() { QuestId = QuestId, State = State, Counts = Counts.ToList() };
    }

    /// <summary>Bindung an eine Instanz: Schlachtzüge bis zum Reset, Dungeons solange die Instanz besteht.</summary>
    public sealed class InstanceBind
    {
        public long InstanceId { get; set; }
        /// <summary>Ende der Schlachtzugsperre (null = Dungeon ohne feste Sperre).</summary>
        public DateTime? ExpiresAt { get; set; }
        public List<string> KilledBosses { get; set; } = new();
    }

    /// <summary>Zusätzliche Daten eines KI-Mitspielers.</summary>
    public sealed class BotData
    {
        public Role Role { get; set; }
        public string Archetype { get; set; } = "freundlich";
        public float Aggression { get; set; } = 0.5f;
        public float Caution { get; set; } = 0.5f;
        public float Chattiness { get; set; } = 0.5f;
        public float Skill { get; set; } = 0.7f;
        /// <summary>Unabhängig in einer Zone unterwegs (statt als Begleiter).</summary>
        public bool Ambient { get; set; }
        public string HomeZone { get; set; } = "";
        /// <summary>Konten, mit denen der Bot schon in einer Gruppe war ("Bekannte").</summary>
        public HashSet<long> KnownAccounts { get; set; } = new();
        public BotDifficulty Difficulty { get; set; } = BotDifficulty.Normal;
        /// <summary>Konten, die diesen Bot als bevorzugten Begleiter gespeichert haben.</summary>
        public HashSet<long> FavoriteOf { get; set; } = new();
        /// <summary>Gemeinsam erlebte Kämpfe und Quests je Konto – verbessert die Zusammenarbeit.</summary>
        public Dictionary<long, int> Familiarity { get; set; } = new();
        /// <summary>Persönliche Erfolgshistorie (Bosssiege, Stufen) – Stoff für Erinnerungen im Gespräch.</summary>
        public List<string> History { get; set; } = new();
        public DateTime LastActive { get; set; } = DateTime.UtcNow;
    }

    /// <summary>KI-Schwierigkeit: Reaktionszeit, Taktik und Fehlerwahrscheinlichkeit.</summary>
    public enum BotDifficulty : byte { Anfaenger = 0, Normal = 1, Erfahren = 2, Veteran = 3 }

    public sealed class Account
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public bool IsBanned { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
