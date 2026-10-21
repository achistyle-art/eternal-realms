using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace EternalRealms.Server.Persistence
{
    public interface IAccountRepository
    {
        Task<Account?> FindByNameAsync(string username);
        Task<Account> CreateAsync(string username, string passwordHash);
        Task TouchLoginAsync(long accountId);
        Task<Account?> GetAccountAsync(long accountId);
        /// <summary>Schreibt Passwort, Fehlversuche, Sperren, Zwei-Faktor und Spielleiter-Stufe.</summary>
        Task UpdateSecurityAsync(Account account);
        /// <summary>Speichert den Hash eines Wiederverbindungstokens.</summary>
        Task SaveSessionTokenAsync(string tokenHash, long accountId, DateTime expiresAt);
        /// <summary>Löst ein Token ein (einmalig): liefert das Konto, wenn es gültig war, und löscht es.</summary>
        Task<long?> ConsumeSessionTokenAsync(string tokenHash, DateTime now);
        Task RevokeSessionTokensAsync(long accountId);
        Task AuditAsync(AuditEntry entry);
        Task<List<AuditEntry>> ListAuditAsync(string? actionPrefix, string? target, int limit);
    }

    public interface ICharacterRepository
    {
        Task<List<CharacterData>> ListForAccountAsync(long accountId);
        Task<CharacterData?> GetAsync(long id);
        Task<bool> NameExistsAsync(string name);
        Task<long> CreateAsync(CharacterData data);
        Task SaveAsync(CharacterData data);
        Task DeleteAsync(long id);
        Task<List<CharacterData>> ListBotsAsync();
    }

    /// <summary>PBKDF2-SHA256 mit zufälligem Salz. Format: iterationen.salz.hash (Base64).</summary>
    public static class PasswordHasher
    {
        private const int Iterations = 100_000;

        public static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string stored)
        {
            var parts = stored.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }

    /// <summary>Speicher im Arbeitsspeicher – für Tests und Einzelspieler ohne Datenbank.</summary>
    public sealed class InMemoryStore : IAccountRepository, ICharacterRepository, IEconomyRepository, ISocialRepository, IWorldRepository
    {
        private readonly ConcurrentDictionary<long, Account> _accounts = new();
        private readonly ConcurrentDictionary<long, CharacterData> _chars = new();
        private long _nextAccount = 1, _nextChar = 1;

        public Task<Account?> FindByNameAsync(string username) =>
            Task.FromResult(_accounts.Values.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase))?.Clone());

        public Task<Account> CreateAsync(string username, string passwordHash)
        {
            var a = new Account { Id = Interlocked.Increment(ref _nextAccount) - 1, Username = username, PasswordHash = passwordHash };
            _accounts[a.Id] = a;
            return Task.FromResult(a.Clone());
        }

        public Task TouchLoginAsync(long accountId) => Task.CompletedTask;

        // Konten werden als Kopie herausgegeben – wie bei einer Datenbank wirkt eine Änderung erst nach UpdateSecurityAsync.
        public Task<Account?> GetAccountAsync(long accountId) => Task.FromResult(_accounts.TryGetValue(accountId, out var a) ? a.Clone() : null);

        public Task UpdateSecurityAsync(Account account)
        {
            if (_accounts.ContainsKey(account.Id)) _accounts[account.Id] = account.Clone();
            return Task.CompletedTask;
        }

        private readonly ConcurrentDictionary<string, (long Account, DateTime Expires)> _tokens = new();
        private readonly List<AuditEntry> _audit = new();
        public IReadOnlyList<AuditEntry> Audit { get { lock (_audit) return _audit.ToList(); } }

        public Task SaveSessionTokenAsync(string tokenHash, long accountId, DateTime expiresAt)
        {
            _tokens[tokenHash] = (accountId, expiresAt);
            return Task.CompletedTask;
        }

        public Task<long?> ConsumeSessionTokenAsync(string tokenHash, DateTime now) =>
            Task.FromResult(_tokens.TryRemove(tokenHash, out var t) && t.Expires > now ? t.Account : (long?)null);

        public Task RevokeSessionTokensAsync(long accountId)
        {
            foreach (var kv in _tokens.Where(kv => kv.Value.Account == accountId).ToList()) _tokens.TryRemove(kv.Key, out _);
            return Task.CompletedTask;
        }

        public Task AuditAsync(AuditEntry entry)
        {
            lock (_audit) { entry.Id = _audit.Count + 1; _audit.Add(entry); }
            return Task.CompletedTask;
        }

        public Task<List<AuditEntry>> ListAuditAsync(string? actionPrefix, string? target, int limit)
        {
            lock (_audit)
                return Task.FromResult(_audit.Where(e => (actionPrefix == null || e.Action.StartsWith(actionPrefix)) &&
                                                         (target == null || string.Equals(e.Target, target, StringComparison.OrdinalIgnoreCase)))
                    .Reverse().Take(limit).ToList());
        }

        public Task<List<CharacterData>> ListForAccountAsync(long accountId) =>
            Task.FromResult(_chars.Values.Where(c => c.AccountId == accountId && !c.IsBot).OrderBy(c => c.Id).Select(c => c.Clone()).ToList());

        public Task<CharacterData?> GetAsync(long id) => Task.FromResult(_chars.TryGetValue(id, out var c) ? c.Clone() : null);

        public Task<bool> NameExistsAsync(string name) =>
            Task.FromResult(_chars.Values.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));

        public Task<long> CreateAsync(CharacterData data)
        {
            data.Id = Interlocked.Increment(ref _nextChar) - 1;
            _chars[data.Id] = data.Clone();
            return Task.FromResult(data.Id);
        }

        public Task SaveAsync(CharacterData data)
        {
            _chars[data.Id] = data.Clone();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(long id)
        {
            _chars.TryRemove(id, out _);
            return Task.CompletedTask;
        }

        public Task<List<CharacterData>> ListBotsAsync() => Task.FromResult(_chars.Values.Where(c => c.IsBot).Select(c => c.Clone()).ToList());

        // ------------------------------------------------------------ Wirtschaft

        private readonly object _economyLock = new();
        private readonly Dictionary<long, MailData> _mail = new();
        private readonly Dictionary<long, AuctionData> _auctions = new();
        private readonly List<LedgerEntry> _ledger = new();

        /// <summary>Alle Einträge des Geldprotokolls (für Tests und Auswertungen).</summary>
        public IReadOnlyList<LedgerEntry> Ledger { get { lock (_economyLock) return _ledger.ToList(); } }

        public Task CommitAsync(EconomyCommit commit)
        {
            lock (_economyLock)
            {
                foreach (var c in commit.Characters) if (c.Id > 0) _chars[c.Id] = c.Clone();
                foreach (var m in commit.UpsertMail) _mail[m.Id] = m.Clone();
                foreach (var id in commit.DeleteMail) _mail.Remove(id);
                foreach (var a in commit.UpsertAuctions) _auctions[a.Id] = a.Clone();
                foreach (var id in commit.DeleteAuctions) _auctions.Remove(id);
                _ledger.AddRange(commit.Ledger);
            }
            return Task.CompletedTask;
        }

        public Task<List<MailData>> LoadMailAsync()
        {
            lock (_economyLock) return Task.FromResult(_mail.Values.Where(m => _chars.ContainsKey(m.RecipientId)).Select(m => m.Clone()).ToList());
        }

        public Task<List<AuctionData>> LoadAuctionsAsync()
        {
            lock (_economyLock) return Task.FromResult(_auctions.Values.Select(a => a.Clone()).ToList());
        }

        public Task<CharacterRef?> FindCharacterAsync(string name)
        {
            var c = _chars.Values.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(c == null ? null : new CharacterRef { Id = c.Id, AccountId = c.AccountId, Name = c.Name, Race = c.Race });
        }

        public Task<List<LedgerEntry>> LedgerForAsync(long characterId, int limit)
        {
            lock (_economyLock) return Task.FromResult(_ledger.Where(l => l.CharacterId == characterId).Reverse().Take(limit).ToList());
        }

        // ------------------------------------------------------------ Soziales

        private readonly Dictionary<long, GuildData> _guilds = new();
        private readonly List<PlayerReport> _reports = new();
        public IReadOnlyList<PlayerReport> Reports { get { lock (_economyLock) return _reports.ToList(); } }

        public Task<List<GuildData>> LoadGuildsAsync() { lock (_economyLock) return Task.FromResult(_guilds.Values.Select(g => g.Clone()).ToList()); }
        public Task SaveGuildAsync(GuildData guild) { lock (_economyLock) _guilds[guild.Id] = guild.Clone(); return Task.CompletedTask; }
        public Task DeleteGuildAsync(long guildId) { lock (_economyLock) _guilds.Remove(guildId); return Task.CompletedTask; }
        public Task SaveReportAsync(PlayerReport report) { lock (_economyLock) { report.Id = _reports.Count + 1; _reports.Add(report); } return Task.CompletedTask; }
        public Task<List<PlayerReport>> ListOpenReportsAsync(int limit) { lock (_economyLock) return Task.FromResult(_reports.Where(r => !r.Handled).Reverse().Take(limit).ToList()); }
        public Task<bool> MarkReportHandledAsync(long id)
        {
            lock (_economyLock)
            {
                var r = _reports.FirstOrDefault(x => x.Id == id && !x.Handled);
                if (r != null) r.Handled = true;
                return Task.FromResult(r != null);
            }
        }

        private readonly Dictionary<string, DateTime> _spawnTimers = new();
        public Task<Dictionary<string, DateTime>> LoadSpawnTimersAsync() { lock (_spawnTimers) return Task.FromResult(new Dictionary<string, DateTime>(_spawnTimers)); }
        public Task SaveSpawnTimerAsync(string key, DateTime? respawnAt)
        {
            lock (_spawnTimers) { if (respawnAt == null) _spawnTimers.Remove(key); else _spawnTimers[key] = respawnAt.Value; }
            return Task.CompletedTask;
        }
    }
}