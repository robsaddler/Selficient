using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Selficient.Application.Security;

/// <summary>What a password has to be before it is accepted, and the sentence said when it is not.</summary>
public static class PasswordPolicy
{
    /// <summary>The shortest password accepted. Length is the one rule that reliably buys entropy.</summary>
    public const int MinimumLength = 14;

    /// <summary>The longest, so a pasted file cannot be spent as work in the hasher.</summary>
    public const int MaximumLength = 256;

    /// <summary>
    /// The reason a password is refused, or nothing when it is fine. One whole sentence, in the
    /// interface's voice, saying what to do rather than what was wrong with the person.
    /// </summary>
    public static string? Refusal(string? password, string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
        {
            return $"A password needs at least {MinimumLength} characters. A short sentence you can remember is stronger than a short word with symbols in it.";
        }

        if (password.Length > MaximumLength)
        {
            return $"A password can be at most {MaximumLength} characters.";
        }

        if (password.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            return "A password cannot contain the username.";
        }

        if (password.Distinct().Count() < 5)
        {
            return "A password needs more than four different characters in it.";
        }

        return null;
    }
}

/// <summary>
/// How long an account is shut after consecutive wrong passwords. The fifth failure buys a minute
/// and every failure after it doubles the wait, to a cap of an hour: slow enough that guessing is
/// pointless, short enough that a tired operator is not locked out of their own platform for a day.
/// </summary>
public static class LockoutPolicy
{
    /// <summary>Failures tolerated before the first lock.</summary>
    public const int Threshold = 5;

    /// <summary>The first lock.</summary>
    public static readonly TimeSpan FirstWait = TimeSpan.FromMinutes(1);

    /// <summary>The longest lock.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromHours(1);

    /// <summary>When an account with this many consecutive failures may try again, or nothing if it may now.</summary>
    public static DateTimeOffset? LockedUntil(int consecutiveFailures, DateTimeOffset now)
    {
        if (consecutiveFailures < Threshold)
        {
            return null;
        }

        int doublings = Math.Min(consecutiveFailures - Threshold, 10);
        TimeSpan wait = FirstWait * Math.Pow(2, doublings);
        return now + (wait < LongestWait ? wait : LongestWait);
    }
}

/// <summary>
/// The codes that get an operator back in when the phone with the authenticator on it is gone. Ten
/// of them, each 128 bits of randomness shown once; stored as SHA-256 because a code of that width
/// needs no stretching, and spent the moment it is used.
/// </summary>
public static class RecoveryCodes
{
    /// <summary>How many are issued at once.</summary>
    public const int Count = 10;

    /// <summary>Makes a set, in the form the operator reads them.</summary>
    public static IReadOnlyList<string> Issue() =>
        [.. Enumerable.Range(0, Count).Select(_ => Format(RandomNumberGenerator.GetBytes(16)))];

    /// <summary>The stored form of a code.</summary>
    public static string Fingerprint(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        string tidy = code.Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tidy)));
    }

    /// <summary>
    /// Whether a typed code is one of the ones left, and the set with it spent. A code never works
    /// twice, which is the whole of what a recovery code promises.
    /// </summary>
    public static bool Spend(string? typed, IReadOnlyList<string> remaining, out IReadOnlyList<string> left)
    {
        ArgumentNullException.ThrowIfNull(remaining);
        left = remaining;
        if (string.IsNullOrWhiteSpace(typed))
        {
            return false;
        }

        string fingerprint = Fingerprint(typed);
        // Every stored fingerprint is compared, in constant time, so the answer does not depend on
        // which one matched or on how far down the list it was.
        bool found = false;
        List<string> keep = [];
        foreach (string stored in remaining)
        {
            bool same = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(fingerprint));
            found |= same;
            if (!same)
            {
                keep.Add(stored);
            }
        }

        left = keep;
        return found;
    }

    private static string Format(byte[] bytes)
    {
        string text = Convert.ToHexStringLower(bytes)[..20].ToUpperInvariant();
        return string.Create(CultureInfo.InvariantCulture, $"{text[..5]}-{text[5..10]}-{text[10..15]}-{text[15..]}");
    }
}
