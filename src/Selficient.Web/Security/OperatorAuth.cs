using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Selficient.Application.Security;

namespace Selficient.Web.Security;

/// <summary>
/// The names the operator's sign-in is known by, and the claims a signed-in operator carries. The
/// security stamp travels in the cookie so that a password change, an enrolment or a disabled
/// account ends every live session on every device at the next request rather than at the next
/// expiry.
/// </summary>
public static class OperatorAuth
{
    /// <summary>The authentication scheme the reviewer sits behind.</summary>
    public const string Scheme = "Selficient.Operator";

    /// <summary>The policy every reviewer page requires: signed in, second factor done, password not owed.</summary>
    public const string Policy = "Operator";

    /// <summary>
    /// Signed in with the second factor done, whether or not a password change is owed. The
    /// password page alone uses this, so an account that must change its password can reach the one
    /// page that lets it and nothing else.
    /// </summary>
    public const string SignedInPolicy = "OperatorSignedIn";

    /// <summary>The policy a half-signed-in operator holds: enough to finish signing in, nothing else.</summary>
    public const string PendingPolicy = "OperatorPending";

    /// <summary>The claim carrying the account's security stamp.</summary>
    public const string StampClaim = "selficient:stamp";

    /// <summary>The claim that says the password step is done and the second factor is not.</summary>
    public const string PendingClaim = "selficient:pending";

    /// <summary>The claim that says this operator must replace their password before anything else.</summary>
    public const string MustChangeClaim = "selficient:must-change-password";

    /// <summary>The cookie's name.</summary>
    public const string CookieName = "selficient.operator";

    /// <summary>The claims a fully signed-in operator carries.</summary>
    public static ClaimsPrincipal Principal(OperatorAccount account, bool pending)
    {
        ArgumentNullException.ThrowIfNull(account);
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, account.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, account.Username),
            new(StampClaim, account.SecurityStamp.ToString()),
        ];
        if (pending)
        {
            claims.Add(new Claim(PendingClaim, "true"));
        }

        if (account.MustChangePassword)
        {
            claims.Add(new Claim(MustChangeClaim, "true"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }

    /// <summary>The account number on a principal, or nothing.</summary>
    public static long? IdOf(ClaimsPrincipal? principal) =>
        long.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out long id) ? id : null;

    /// <summary>The operator's name on a principal, or nothing. Every hand is recorded against it.</summary>
    public static string NameOf(ClaimsPrincipal? principal) => principal?.Identity?.Name ?? string.Empty;

    /// <summary>Whether a principal has only finished the password step.</summary>
    public static bool IsPending(ClaimsPrincipal? principal) => principal?.HasClaim(PendingClaim, "true") == true;

    /// <summary>Whether a principal must replace their password before using the reviewer.</summary>
    public static bool MustChangePassword(ClaimsPrincipal? principal) => principal?.HasClaim(MustChangeClaim, "true") == true;
}

/// <summary>
/// Checks on every request that the cookie's security stamp is still the account's. This is what
/// makes "change your password and every other session ends" true rather than aspirational.
/// </summary>
public sealed class OperatorCookieEvents : CookieAuthenticationEvents
{
    private readonly IOperatorStore _store;

    /// <summary>Creates the events over the store.</summary>
    public OperatorCookieEvents(IOperatorStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ClaimsPrincipal? principal = context.Principal;
        long? id = OperatorAuth.IdOf(principal);
        OperatorAccount? account = id is null ? null : await _store.FindAsync(id.Value, context.HttpContext.RequestAborted).ConfigureAwait(false);
        // An account only comes back when the principal carried a number, so the principal is there
        // by the time the stamp is read; the short circuit is what makes that true.
        if (account is null || !account.IsEnabled || !principal!.HasClaim(OperatorAuth.StampClaim, account.SecurityStamp.ToString()))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(OperatorAuth.Scheme).ConfigureAwait(false);
        }
    }
}

/// <summary>The Data Protection key ring, as the secret protector the application layer asks for.</summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "Selficient.Operator.Secrets.v1";

    private readonly IDataProtector _protector;

    /// <summary>Creates the protector.</summary>
    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    /// <inheritdoc />
    public byte[]? Unprotect(byte[]? ciphertext)
    {
        if (ciphertext is null || ciphertext.Length == 0)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // A value this key ring did not write, or one written before the ring was replaced.
            return null;
        }
    }
}

/// <summary>
/// Makes the first operator on a server that has none, from the two bootstrap settings, and marks
/// it so that it must replace its password and enrol an authenticator before it can review
/// anything. Does nothing at all once one account exists, so the settings can stay on the server
/// without being a second way in.
/// </summary>
public sealed partial class OperatorBootstrap : IHostedService
{
    private readonly IOperatorStore _store;
    private readonly IOptions<OperatorOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<OperatorBootstrap> _logger;

    /// <summary>Creates the start-up step.</summary>
    public OperatorBootstrap(IOperatorStore store, IOptions<OperatorOptions> options, TimeProvider clock, ILogger<OperatorBootstrap> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        OperatorOptions options = _options.Value;
        if (!options.Enabled || !options.HasBootstrap || await _store.CountAsync(cancellationToken).ConfigureAwait(false) > 0)
        {
            return;
        }

        await _store.CreateAsync(
            options.BootstrapUsername!.Trim(),
            options.BootstrapUsername!.Trim(),
            PasswordHasher.Hash(options.BootstrapPassword!),
            mustChangePassword: true,
            _clock.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        Log.Created(_logger, options.BootstrapUsername!.Trim());
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "The first operator account, {Username}, was created from the bootstrap settings. It must change its password and enrol an authenticator before it can review anything, and the bootstrap settings should then be removed.")]
        public static partial void Created(ILogger logger, string username);
    }
}
