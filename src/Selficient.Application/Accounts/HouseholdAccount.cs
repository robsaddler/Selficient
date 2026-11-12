using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Selficient.Application.Accounts;

/// <summary>
/// The account a household may choose to make: an address and nothing else about a person. The
/// security stamp travels in the session, so deleting the account ends every session at the next
/// request.
/// </summary>
/// <param name="Id">The account's number.</param>
/// <param name="Email">The address, in lower case.</param>
/// <param name="SecurityStamp">Moves when every session should end.</param>
/// <param name="CreatedAt">When the account was made.</param>
/// <param name="LastSeenAt">When a session of this account was last used, to the day.</param>
public sealed record HouseholdAccount(long Id, string Email, Guid SecurityStamp, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);

/// <summary>
/// A sign-in that has been asked for and not yet proved: the address and the random value that
/// binds the code to the browser that asked. The browser holds this, encrypted; the database holds
/// only hashes of it.
/// </summary>
/// <param name="Email">The address the code went to.</param>
/// <param name="Nonce">Thirty-two random bytes that only the asking browser has.</param>
public sealed record PendingSignIn(string Email, byte[] Nonce);

/// <summary>One code as the store keeps it: hashes and times, nothing that can be read.</summary>
/// <param name="EmailHash">A hash of the address, to count codes sent to it.</param>
/// <param name="BindingHash">A hash of the browser's value, to find the row.</param>
/// <param name="CodeHash">A hash of the address and the code, keyed by the browser's value.</param>
/// <param name="CreatedAt">When the code was sent.</param>
/// <param name="ExpiresAt">When it stops working.</param>
public sealed record SignInCode(byte[] EmailHash, byte[] BindingHash, byte[] CodeHash, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>What the store answers when a code is tried: which row, how many tries it has now had, and the hash to compare.</summary>
/// <param name="Id">The row.</param>
/// <param name="Attempts">How many times the code has been tried, this one included.</param>
/// <param name="CodeHash">The keyed hash the right code produces.</param>
public sealed record CodeAttempt(long Id, int Attempts, byte[] CodeHash);

/// <summary>An email address as an account knows it: one mailbox, in lower case, with nothing a mail header could misread.</summary>
public static partial class EmailAddress
{
    /// <summary>The longest address the standards allow.</summary>
    public static int MaxLength => 254;

    /// <summary>The address trimmed and lower-cased, or nothing when it is not one mailbox.</summary>
    public static string? Normalise(string? typed)
    {
        string text = (typed ?? string.Empty).Trim();
        return text.Length <= MaxLength && Mailbox().IsMatch(text) ? text.ToLowerInvariant() : null;
    }

    /// <summary>The hash codes are counted against, so the codes table never holds an address.</summary>
    public static byte[] Hash(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return SHA256.HashData(Encoding.UTF8.GetBytes(email));
    }

    /// <summary>
    /// The shape browsers accept in an email box, with a dot required in the domain: a local part
    /// of up to 64 ordinary characters, then labels of letters, digits and hyphens. No spaces, no
    /// quotes, no comments, no angle brackets, so nothing here can start a second header.
    /// </summary>
    [GeneratedRegex(@"^[a-z0-9.!#$%&'*+/=?^_`{|}~-]{1,64}@[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Mailbox();
}
