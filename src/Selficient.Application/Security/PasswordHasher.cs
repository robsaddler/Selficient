using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Selficient.Application.Security;

/// <summary>
/// How an operator's password is stored. PBKDF2-HMAC-SHA512 from the base class library, with the
/// algorithm and its cost written into the stored value, so the cost can be raised or the algorithm
/// replaced later and every password is brought up to the new policy the next time its owner signs
/// in correctly. Nothing here ever sees a password that is not about to be hashed, and no method
/// returns one.
/// </summary>
public static class PasswordHasher
{
    /// <summary>The current format's marker. A stored value starting with anything else is not ours.</summary>
    public const string Version = "v1";

    /// <summary>Iterations for a new hash. Above ASP.NET Core Identity's own default of 100,000.</summary>
    public const int Iterations = 210_000;

    /// <summary>Bytes of salt, from the system's cryptographic source.</summary>
    public const int SaltBytes = 16;

    /// <summary>Bytes of derived key, the full width of SHA-512.</summary>
    public const int KeyBytes = 64;

    private const char Separator = '$';

    /// <summary>Hashes a password for storage. The result carries its own parameters.</summary>
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] key = Derive(password, salt, Iterations);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Version}{Separator}{Iterations}{Separator}{Convert.ToBase64String(salt)}{Separator}{Convert.ToBase64String(key)}");
    }

    /// <summary>
    /// Whether the password matches the stored value, and whether that value was made to a weaker
    /// policy than the current one and should be written again. A stored value we cannot read is a
    /// mismatch, never an exception: a row corrupted by hand must not be a way in.
    /// </summary>
    public static (bool Matches, bool NeedsRehash) Verify(string? password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || !TryRead(stored, out int iterations, out byte[] salt, out byte[] expected))
        {
            return (false, false);
        }

        byte[] actual = Derive(password, salt, iterations);
        bool matches = CryptographicOperations.FixedTimeEquals(expected, actual);
        return (matches, matches && iterations < Iterations);
    }

    /// <summary>
    /// Spends the same work as a real verification against a name nobody holds, so that an unknown
    /// username and a wrong password take the same time and the sign-in page tells an attacker
    /// nothing about which names exist.
    /// </summary>
    public static void SpendTheSameTime(string? password)
    {
        byte[] salt = new byte[SaltBytes];
        byte[] key = Derive(password ?? string.Empty, salt, Iterations);
        CryptographicOperations.ZeroMemory(key);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, KeyBytes);

    private static bool TryRead(string? stored, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = [];
        key = [];
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        string[] parts = stored.Split(Separator);
        if (parts.Length != 4 || parts[0] != Version || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out iterations) || iterations < 1)
        {
            return false;
        }

        if (!TryBase64(parts[2], out salt) || !TryBase64(parts[3], out key) || key.Length != KeyBytes)
        {
            return false;
        }

        return true;
    }

    private static bool TryBase64(string text, out byte[] bytes)
    {
        bytes = new byte[((text.Length * 3) + 3) / 4];
        if (Convert.TryFromBase64String(text, bytes, out int written))
        {
            bytes = bytes[..written];
            return true;
        }

        bytes = [];
        return false;
    }
}
