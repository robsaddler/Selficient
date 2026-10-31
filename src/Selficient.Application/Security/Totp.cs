using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Selficient.Application.Security;

/// <summary>
/// The time-based one-time password of RFC 6238: HMAC-SHA1 over the number of thirty-second steps
/// since the epoch, truncated to six digits. SHA-1 here is the RFC's own construction inside an
/// HMAC, where a collision is not the question, and it is what every authenticator app implements.
/// The published test vectors are pinned in the tests, so this is the standard and not an
/// impression of it.
/// </summary>
public static class Totp
{
    /// <summary>Seconds in a step.</summary>
    public const int StepSeconds = 30;

    /// <summary>Digits in a code.</summary>
    public const int Digits = 6;

    /// <summary>How many steps either side of the present one are accepted, for clock drift.</summary>
    public const int Drift = 1;

    /// <summary>Bytes in a new secret: 160 bits, the RFC's own width for SHA-1.</summary>
    public const int SecretBytes = 20;

    /// <summary>A new secret from the system's cryptographic source.</summary>
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    /// <summary>The code for one step, which is what the published vectors name.</summary>
    public static string CodeAtStep(byte[] secret, long step)
    {
        ArgumentNullException.ThrowIfNull(secret);
        byte[] counter = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        // CA5350 reads HMACSHA1 as a weak algorithm. RFC 6238 specifies exactly this construction,
        // every authenticator app implements it, and the property it needs is HMAC's keyed
        // pseudorandomness rather than SHA-1's collision resistance, which is what the rule is
        // about. Swapping it would mean no app could be enrolled.
#pragma warning disable CA5350
        byte[] mac = HMACSHA1.HashData(secret, counter);
#pragma warning restore CA5350
        int offset = mac[^1] & 0x0F;
        int binary = ((mac[offset] & 0x7F) << 24) | ((mac[offset + 1] & 0xFF) << 16) | ((mac[offset + 2] & 0xFF) << 8) | (mac[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>The step a moment falls in.</summary>
    public static long StepAt(DateTimeOffset moment) => moment.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>The code for a moment.</summary>
    public static string CodeAt(byte[] secret, DateTimeOffset moment) => CodeAtStep(secret, StepAt(moment));

    /// <summary>
    /// Whether a typed code is right for this moment, allowing one step either side. The step it
    /// matched comes back so the caller can refuse a code that has already been spent: a code is
    /// valid for ninety seconds, which is ninety seconds in which a shoulder-surfed code would
    /// otherwise work twice.
    /// </summary>
    public static bool Matches(byte[]? secret, string? typed, DateTimeOffset moment, long lastUsedStep, out long step)
    {
        step = 0;
        if (secret is null || secret.Length == 0 || string.IsNullOrWhiteSpace(typed))
        {
            return false;
        }

        string trimmed = typed.Replace(" ", string.Empty, StringComparison.Ordinal);
        long now = StepAt(moment);
        for (long candidate = now - Drift; candidate <= now + Drift; candidate++)
        {
            if (candidate <= lastUsedStep)
            {
                continue;
            }

            byte[] expected = Encoding.UTF8.GetBytes(CodeAtStep(secret, candidate));
            byte[] given = Encoding.UTF8.GetBytes(trimmed);
            if (expected.Length == given.Length && CryptographicOperations.FixedTimeEquals(expected, given))
            {
                step = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The <c>otpauth://</c> address an authenticator app reads, with the secret in base32 as the
    /// scheme requires. The issuer and the account name are what the operator sees in their app.
    /// </summary>
    public static string Enrolment(byte[] secret, string issuer, string account)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        string label = Uri.EscapeDataString($"{issuer}:{account}");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"otpauth://totp/{label}?secret={Base32(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}");
    }

    /// <summary>The secret in RFC 4648 base32 without padding, which is what the apps accept.</summary>
    public static string Base32(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        StringBuilder text = new();
        int buffer = 0;
        int bits = 0;
        foreach (byte value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                text.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            text.Append(alphabet[(buffer << (5 - bits)) & 31]);
        }

        return text.ToString();
    }
}
