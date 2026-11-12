using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Selficient.Web.Security;

/// <summary>
/// What a passkey ceremony in progress is remembered by: the options this browser was given,
/// challenge included, in a cookie only this server can read. The answer must be to these options,
/// from this browser, within five minutes, and a ceremony's cookie is taken away when it is
/// answered, right or wrong, so a challenge is used once.
/// </summary>
public sealed class PasskeyCeremonyCookie
{
    /// <summary>The cookie's name.</summary>
    public const string Name = "selficient.passkey";

    /// <summary>A ceremony that adds a passkey to the account signed in.</summary>
    public const string Adding = "add";

    /// <summary>A ceremony that signs in with one.</summary>
    public const string SigningIn = "sign-in";

    private const string Purpose = "Selficient.Household.PasskeyCeremony.v1";

    private readonly IDataProtector _protector;

    /// <summary>Creates the cookie's reader and writer over the key ring.</summary>
    public PasskeyCeremonyCookie(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>How long a ceremony may take.</summary>
    public static TimeSpan Lifetime => TimeSpan.FromMinutes(5);

    /// <summary>The cookie's attributes: the API only, never to script, never cross-site.</summary>
    public static CookieOptions Options(TimeSpan lifetime) => new()
    {
        Path = "/api",
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        MaxAge = lifetime,
    };

    /// <summary>Remembers the options a ceremony of one kind began with.</summary>
    public void Write(HttpResponse response, string kind, string optionsJson)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(optionsJson);
        byte[] sealed_ = _protector.Protect(Encoding.UTF8.GetBytes($"{kind}\n{optionsJson}"));
        response.Cookies.Append(Name, Base64Url.EncodeToString(sealed_), Options(Lifetime));
    }

    /// <summary>
    /// The options a ceremony of this kind began with, and the cookie gone whatever is found, so
    /// the challenge in it cannot be answered twice. Nothing when there is no ceremony in progress,
    /// or one of the other kind, or a cookie this key ring did not write.
    /// </summary>
    public string? Take(HttpContext context, string kind)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        string? cookie = context.Request.Cookies[Name];
        context.Response.Cookies.Delete(Name, Options(TimeSpan.Zero));
        if (string.IsNullOrEmpty(cookie) || !Base64Url.IsValid(cookie))
        {
            return null;
        }

        string? options = null;
        try
        {
            string plain = Encoding.UTF8.GetString(_protector.Unprotect(Base64Url.DecodeFromChars(cookie)));
            string prefix = $"{kind}\n";
            options = plain.StartsWith(prefix, StringComparison.Ordinal) ? plain[prefix.Length..] : null;
        }
        catch (CryptographicException)
        {
            // Tampered with, or written before the key ring changed.
        }

        return options;
    }
}

/// <summary>
/// What a passkey is called in the panel: the kind of device it was made on, chosen here from a
/// fixed list by the browser's own description of itself. Nothing typed is ever stored, so a label
/// cannot carry anything but one of these.
/// </summary>
public static class DeviceLabel
{
    /// <summary>What a device that names itself as nothing we know is called.</summary>
    public const string Unknown = "Passkey";

    private static readonly (string Needle, string Label)[] Kinds =
    [
        ("iPhone", "iPhone"),
        ("iPad", "iPad"),
        ("Android", "Android device"),
        ("CrOS", "Chromebook"),
        ("Windows", "Windows computer"),
        ("Macintosh", "Mac"),
        ("Linux", "Linux computer"),
    ];

    /// <summary>The label for a user agent.</summary>
    public static string From(string? userAgent)
    {
        string agent = userAgent ?? string.Empty;
        return Kinds.FirstOrDefault(kind => agent.Contains(kind.Needle, StringComparison.Ordinal)).Label ?? Unknown;
    }
}
