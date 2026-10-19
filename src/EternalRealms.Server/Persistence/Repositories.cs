using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace EternalRealms.Server.Persistence
{
    public interface IAccountRepository
    {
        Task<Account?> FindByNameAsync(string username);
        Task<Account> CreateAsync(string username, string passwordHash);
        Task TouchLoginAsync(long accountId);
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
    public sealed class InMemoryStore : IAccountRepository, ICharacterRepository
    {
        private readonly ConcurrentDictionary<long, Account> _accounts = new();
        private readonly ConcurrentDictionary<long, CharacterData> _chars = new();
        private long _nextAccount = 1, _nextChar = 1;

        public Task<Account?> FindByNameAsync(string username) =>
            Task.FromResult(_accounts.Values.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)));

        public Task<Account> CreateAsync(string username, string passwordHash)
        {
            var a = new Account { Id = Interlocked.Increment(ref _nextAccount) - 1, Username = username, PasswordHash = passwordHash };
            _accounts[a.Id] = a;
            return Task.FromResult(a);
        }

        public Task TouchLoginAsync(long accountId) => Task.CompletedTask;

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
    }
}
