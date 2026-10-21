using System.Security.Cryptography;
using System.Text;

namespace EternalRealms.Server.Security
{
    /// <summary>
    /// Zeitbasierte Einmalcodes nach RFC 6238 (TOTP, HMAC-SHA1, 30 Sekunden, 6 Ziffern) – kompatibel mit üblichen
    /// Authenticator-Apps. Ein Code gilt im aktuellen sowie im vorigen und nächsten Zeitschritt (Uhrabweichung),
    /// aber nur einmal: <see cref="Verify"/> liefert den verwendeten Schritt, der am Konto gespeichert wird.
    /// </summary>
    public static class Totp
    {
        public const int StepSeconds = 30, Digits = 6;

        public static string NewSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

        public static long StepAt(DateTime utc) => (long)Math.Floor((utc - DateTime.UnixEpoch).TotalSeconds / StepSeconds);

        public static string Code(string secret, long step)
        {
            var key = Base32.Decode(secret);
            Span<byte> counter = stackalloc byte[8];
            for (int i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xff); step >>= 8; }
            var hash = HMACSHA1.HashData(key, counter);
            int offset = hash[^1] & 0x0f;
            int bin = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            return (bin % 1_000_000).ToString("D6");
        }

        /// <summary>Prüft einen Code. Liefert den Zeitschritt bei Erfolg, sonst null (auch bei bereits verwendetem Schritt).</summary>
        public static long? Verify(string secret, string? code, DateTime utc, long lastUsedStep)
        {
            code = (code ?? "").Replace(" ", "");
            if (code.Length != Digits || !code.All(char.IsDigit)) return null;
            long now = StepAt(utc);
            for (long s = now - 1; s <= now + 1; s++)
            {
                if (s <= lastUsedStep) continue;
                if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, s)), Encoding.ASCII.GetBytes(code))) return s;
            }
            return null;
        }

        /// <summary>otpauth-Adresse für QR-Codes in Authenticator-Apps.</summary>
        public static string Uri(string account, string secret) =>
            $"otpauth://totp/Eternal%20Realms:{System.Uri.EscapeDataString(account)}?secret={secret}&issuer=Eternal%20Realms&digits={Digits}&period={StepSeconds}";
    }

    /// <summary>Base32 nach RFC 4648 ohne Auffüllzeichen.</summary>
    public static class Base32
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string Encode(byte[] data)
        {
            var sb = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0, bits = 0;
            foreach (var b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
            }
            if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            return sb.ToString();
        }

        public static byte[] Decode(string text)
        {
            var bytes = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var ch in text.ToUpperInvariant())
            {
                if (ch == '=' || ch == ' ') continue;
                int v = Alphabet.IndexOf(ch);
                if (v < 0) throw new FormatException("Ungültiges Base32-Zeichen.");
                buffer = (buffer << 5) | v;
                bits += 5;
                if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xff)); bits -= 8; }
            }
            return bytes.ToArray();
        }
    }

    /// <summary>Passwortregeln für neue Konten und Passwortänderungen (bestehende Konten melden sich weiter an).</summary>
    public static class PasswordPolicy
    {
        public const int MinLength = 8, MaxLength = 128;

        /// <summary>Häufige Passwörter, die trotz erfüllter Regeln abgelehnt werden.</summary>
        private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
        {
            "passwort1", "password1", "passwort123", "password123", "12345678a", "qwertz123", "qwerty123", "hallo1234", "abc12345", "test1234",
        };

        public static string? Check(string password, string username)
        {
            if (password.Length < MinLength) return $"Das Passwort muss mindestens {MinLength} Zeichen haben.";
            if (password.Length > MaxLength) return "Das Passwort ist zu lang.";
            if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "Das Passwort braucht Buchstaben und Ziffern.";
            if (password.Contains(username, StringComparison.OrdinalIgnoreCase)) return "Das Passwort darf den Kontonamen nicht enthalten.";
            if (Common.Contains(password)) return "Dieses Passwort ist zu verbreitet.";
            return null;
        }
    }

    /// <summary>Wiederverbindungstokens: 256 Bit Zufall für den Client, gespeichert wird nur der SHA-256-Hash.</summary>
    public static class SessionTokens
    {
        public static string Create() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>
    /// Begrenzung von Versuchen je IP-Adresse (im Speicher): nach <see cref="MaxPerIp"/> Ereignissen im Zeitfenster wird die
    /// Adresse abgewiesen, bis das älteste aus dem Fenster fällt. Die Sperre je Konto liegt am Konto selbst (übersteht Neustarts).
    /// </summary>
    public sealed class LoginLimiter
    {
        /// <summary>Fehlversuche, nach denen ein Konto für <see cref="AccountLock"/> gesperrt wird.</summary>
        public const int MaxPerAccount = 5;
        public static readonly TimeSpan AccountLock = TimeSpan.FromMinutes(15);

        public int MaxPerIp { get; }
        public TimeSpan Window { get; }

        public LoginLimiter(int maxPerIp = 20, TimeSpan? window = null)
        {
            MaxPerIp = maxPerIp;
            Window = window ?? TimeSpan.FromMinutes(15);
        }

        private readonly Dictionary<string, List<DateTime>> _failures = new();
        private readonly object _lock = new();

        public bool IsBlocked(string ip, DateTime now)
        {
            lock (_lock)
            {
                if (!_failures.TryGetValue(ip, out var list)) return false;
                list.RemoveAll(t => now - t > Window);
                if (list.Count == 0) _failures.Remove(ip);
                return list.Count >= MaxPerIp;
            }
        }

        public void Fail(string ip, DateTime now)
        {
            lock (_lock)
            {
                if (!_failures.TryGetValue(ip, out var list)) _failures[ip] = list = new List<DateTime>();
                list.Add(now);
                // Speicher begrenzen: Adressen ohne frische Fehlversuche verwerfen
                if (_failures.Count > 50_000)
                    foreach (var k in _failures.Where(kv => kv.Value.All(t => now - t > Window)).Select(kv => kv.Key).ToList()) _failures.Remove(k);
            }
        }

        public void Success(string ip) { lock (_lock) _failures.Remove(ip); }
    }
}
