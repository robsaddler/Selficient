using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Selficient.Web.Security;

/// <summary>
/// Who may use the reviewer pages. The key is compared in constant time; a match sets one signed,
/// HttpOnly, Secure, SameSite=Strict cookie carrying only when it was issued, which Data Protection
/// signs and encrypts. There is no account, no name, and nothing a person could be identified by.
/// </summary>
public sealed class OperatorSession
{
    /// <summary>The cookie's name.</summary>
    public const string CookieName = "selficient.operator";

    private const string Purpose = "Selficient.Operator.v1";

    private readonly IDataProtector _protector;
    private readonly IOptions<OperatorOptions> _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the session helper.</summary>
    public OperatorSession(IDataProtectionProvider protection, IOptions<OperatorOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _protector = protection.CreateProtector(Purpose);
        _options = options;
        _clock = clock;
    }

    /// <summary>Whether an operator key is configured at all.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_options.Value.Key);

    /// <summary>Whether the typed key is the operator's, compared in constant time.</summary>
    public bool Matches(string? typed)
    {
        string? key = _options.Value.Key;
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(typed))
        {
            return false;
        }

        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        byte[] given = SHA256.HashData(Encoding.UTF8.GetBytes(typed));
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    /// <summary>Issues the signed cookie value: the moment, protected.</summary>
    public string Issue() => _protector.Protect(_clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));

    /// <summary>Whether a cookie value is a session issued here that has not run out.</summary>
    public bool IsValid(string? cookie)
    {
        if (!IsConfigured || string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        try
        {
            string issued = _protector.Unprotect(cookie);
            DateTimeOffset at = DateTimeOffset.Parse(issued, CultureInfo.InvariantCulture);
            return _clock.GetUtcNow() - at < TimeSpan.FromHours(_options.Value.SessionHours);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The cookie's options: HttpOnly, Secure, strict, and as long as the session.</summary>
    public CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/admin",
        MaxAge = TimeSpan.FromHours(_options.Value.SessionHours),
        IsEssential = true,
    };
}
