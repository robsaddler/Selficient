using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Selficient.Application.Accounts;

namespace Selficient.Web.Security;

/// <summary>
/// The names a household's sign-in is known by, and the claims a signed-in household carries. It
/// shares nothing with the operator's: another scheme, another cookie, another table. A household
/// is never an operator and an operator is never a household.
/// </summary>
public static class HouseholdAuth
{
    /// <summary>The authentication scheme a household's session sits behind.</summary>
    public const string Scheme = "Selficient.Household";

    /// <summary>The policy every account page and account endpoint requires.</summary>
    public const string Policy = "Household";

    /// <summary>The session cookie: HttpOnly, and the only thing that says who a browser is.</summary>
    public const string CookieName = "selficient.household";

    /// <summary>
    /// A cookie script can read, holding only "1". It tells the page to ask who is signed in, so a
    /// guest's browser never asks. It grants nothing: the session cookie is what the server trusts.
    /// </summary>
    public const string HintCookieName = "selficient.signed-in";

    /// <summary>The cookie that binds a code to the browser that asked for it.</summary>
    public const string PendingCookieName = "selficient.signin";

    /// <summary>The claim carrying the account's security stamp.</summary>
    public const string StampClaim = "selficient:household-stamp";

    /// <summary>Where a visitor who is not signed in is sent.</summary>
    public const string SignInPath = "/account/sign-in";

    /// <summary>How long a household stays signed in on a device without using it. Decision 60: a household made to sign in again during a power cut has been failed by the product.</summary>
    public static TimeSpan SessionLength => TimeSpan.FromDays(90);

    /// <summary>The claims a signed-in household carries.</summary>
    public static ClaimsPrincipal Principal(HouseholdAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Email, account.Email),
                new Claim(StampClaim, account.SecurityStamp.ToString()),
            ],
            Scheme));
    }

    /// <summary>The account number on a principal, or nothing.</summary>
    public static long? IdOf(ClaimsPrincipal? principal) =>
        long.TryParse(ClaimOf(principal, ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out long id) ? id : null;

    /// <summary>The address on a principal, or an empty string.</summary>
    public static string EmailOf(ClaimsPrincipal? principal) => ClaimOf(principal, ClaimTypes.Email) ?? string.Empty;

    /// <summary>A claim from the household's own identity, never from an operator's that happens to be on the same request.</summary>
    private static string? ClaimOf(ClaimsPrincipal? principal, string type) =>
        principal?.Identities.FirstOrDefault(static i => i.AuthenticationType == Scheme)?.FindFirst(type)?.Value;

    /// <summary>Signs a household in on this browser: a fresh session, the hint beside it, and the pending sign-in gone.</summary>
    public static async Task SignInAsync(HttpContext context, HouseholdAccount account)
    {
        ArgumentNullException.ThrowIfNull(context);
        await context.SignInAsync(Scheme, Principal(account), new AuthenticationProperties { IsPersistent = true }).ConfigureAwait(false);
        SetHint(context.Response);
        context.Response.Cookies.Delete(PendingCookieName, PendingSignInCookie.Options(TimeSpan.Zero));
    }

    /// <summary>Signs this browser out and takes the hint away.</summary>
    public static async Task SignOutAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await context.SignOutAsync(Scheme).ConfigureAwait(false);
        context.Response.Cookies.Delete(HintCookieName, HintOptions());
    }

    /// <summary>Sets the hint for as long as the session can last. Renewed whenever the page asks who is signed in.</summary>
    public static void SetHint(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        CookieOptions options = HintOptions();
        options.MaxAge = SessionLength;
        response.Cookies.Append(HintCookieName, "1", options);
    }

    private static CookieOptions HintOptions() => new() { Path = "/", Secure = true, HttpOnly = false, SameSite = SameSiteMode.Lax, IsEssential = true };
}

/// <summary>
/// Checks on every request that uses a household's session that the account is still there and its
/// stamp has not moved, so deleting an account ends every session on every device at the next
/// request. An API call that is not signed in is answered 401 rather than sent to a page.
/// </summary>
public sealed class HouseholdCookieEvents : CookieAuthenticationEvents
{
    private readonly IAccountStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Creates the events over the store.</summary>
    public HouseholdCookieEvents(IAccountStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _clock = clock;
    }

    /// <inheritdoc />
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        long? id = HouseholdAuth.IdOf(context.Principal);
        HouseholdAccount? account = id is null ? null : await _store.FindAsync(id.Value, context.HttpContext.RequestAborted).ConfigureAwait(false);
        // An account only comes back when the principal carried a number, so the principal is there by the time the stamp is read.
        if (account is null || !context.Principal!.HasClaim(HouseholdAuth.StampClaim, account.SecurityStamp.ToString()))
        {
            context.RejectPrincipal();
            await HouseholdAuth.SignOutAsync(context.HttpContext).ConfigureAwait(false);
            return;
        }

        // "Last seen" is to the day: enough to know an account is in use, and one write a day at most.
        DateTimeOffset now = _clock.GetUtcNow();
        if (now - account.LastSeenAt >= TimeSpan.FromDays(1))
        {
            await _store.TouchAsync(account.Id, now, context.HttpContext.RequestAborted).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        }
        else
        {
            // No return address is carried, so there is nothing to redirect a visitor through.
            context.Response.Redirect(HouseholdAuth.SignInPath);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The cookie that binds a sign-in code to the browser that asked for it: the address and a random
/// value, encrypted and signed by the key ring, readable by nothing but this server. Without it a
/// code is useless, so a code read over a shoulder or from a forwarded email opens nothing.
/// </summary>
public sealed class PendingSignInCookie
{
    private const string Purpose = "Selficient.Household.PendingSignIn.v1";
    private const int NonceBytes = 32;

    private readonly IDataProtector _protector;

    /// <summary>Creates the cookie's reader and writer over the key ring.</summary>
    public PendingSignInCookie(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>The cookie's attributes: this site's account pages only, never to script, never cross-site.</summary>
    public static CookieOptions Options(TimeSpan lifetime) => new()
    {
        Path = "/account",
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        MaxAge = lifetime,
    };

    /// <summary>Puts a pending sign-in in the browser for as long as its code works.</summary>
    public void Write(HttpResponse response, PendingSignIn pending)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(pending);
        byte[] plain = [.. pending.Nonce, .. Encoding.UTF8.GetBytes(pending.Email)];
        response.Cookies.Append(HouseholdAuth.PendingCookieName, Base64Url.EncodeToString(_protector.Protect(plain)), Options(AccountSignIn.CodeLifetime));
    }

    /// <summary>The pending sign-in this browser holds, or nothing when it holds none, or one this key ring did not write.</summary>
    public PendingSignIn? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? cookie = request.Cookies[HouseholdAuth.PendingCookieName];
        if (string.IsNullOrEmpty(cookie) || !Base64Url.IsValid(cookie))
        {
            return null;
        }

        PendingSignIn? pending = null;
        try
        {
            byte[] plain = _protector.Unprotect(Base64Url.DecodeFromChars(cookie));
            pending = new PendingSignIn(Encoding.UTF8.GetString(plain.AsSpan(NonceBytes)), plain[..NonceBytes]);
        }
        catch (CryptographicException)
        {
            // Tampered with, or written before the key ring changed.
        }

        return pending;
    }

    /// <summary>Takes the pending sign-in out of the browser.</summary>
    public static void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(HouseholdAuth.PendingCookieName, Options(TimeSpan.Zero));
    }
}
