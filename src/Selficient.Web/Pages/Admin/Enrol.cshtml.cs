using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Selficient.Application.Security;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Admin;

/// <summary>
/// Setting up the authenticator. The seed is made here, shown once as a setup key, and only
/// written to the database when a code made from it is typed back, so an enrolment that was never
/// completed leaves no half-armed second factor behind. The recovery codes are issued at the same
/// moment and shown once.
/// </summary>
[Authorize(Policy = OperatorAuth.PendingPolicy)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class EnrolModel : PageModel
{
    private readonly IOperatorStore _store;
    private readonly OperatorSignIn _signIn;
    private readonly IOptions<OperatorOptions> _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the page.</summary>
    public EnrolModel(IOperatorStore store, OperatorSignIn signIn, IOptions<OperatorOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _signIn = signIn;
        _options = options;
        _clock = clock;
    }

    /// <summary>The seed, in base32, carried across the post so it is written only once it is proved.</summary>
    [BindProperty]
    public string? Secret { get; set; }

    /// <summary>The code typed back from the app.</summary>
    [BindProperty]
    public string? Code { get; set; }

    /// <summary>The seed as the operator types it into their app, in readable groups.</summary>
    public string SecretText { get; private set; } = string.Empty;

    /// <summary>The name the app will show.</summary>
    public string AccountName { get; private set; } = string.Empty;

    /// <summary>Why the last code was refused, or nothing.</summary>
    public string? Refusal { get; private set; }

    /// <summary>The recovery codes, shown once immediately after enrolment.</summary>
    public IReadOnlyList<string> Codes { get; private set; } = [];

    /// <summary>Whether this operator still has to replace a starting password.</summary>
    public bool OwesAPassword { get; private set; }

    /// <summary>The enrolment address as a QR code, drawn inline so it is never fetched from anywhere.</summary>
    public Selficient.Web.Formatting.QrCode Qr { get; private set; }

    /// <summary>Makes a seed and shows it.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        OperatorAccount? account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return RedirectToPage("/Admin/SignIn");
        }

        Secret = Totp.Base32(Totp.NewSecret());
        Describe(account);
        return Page();
    }

    /// <summary>Takes the code and, if it is right, arms the second factor and issues the recovery codes.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        OperatorAccount? account = await AccountAsync(cancellationToken);
        if (account is null || string.IsNullOrWhiteSpace(Secret))
        {
            return RedirectToPage("/Admin/SignIn");
        }

        Describe(account);
        byte[] secret = Base32Bytes(Secret);
        if (!Totp.Matches(secret, Code, _clock.GetUtcNow(), lastUsedStep: -1, out _))
        {
            Refusal = "That code is not right. Check the setup key went in exactly, and that your phone's clock is set automatically.";
            Code = null;
            return Page();
        }

        IReadOnlyList<string> codes = RecoveryCodes.Issue();
        await _store.SetSecondFactorAsync(account.Id, _signIn.ProtectSecret(secret), _clock.GetUtcNow(), _signIn.ProtectCodes(codes), Guid.NewGuid(), cancellationToken);

        // The enrolment completes the sign-in: the account is re-read so the cookie carries the new
        // stamp, or the very next request would be rejected for carrying the old one.
        OperatorAccount armed = (await _store.FindAsync(account.Id, cancellationToken))!;
        await HttpContext.SignInAsync(
            OperatorAuth.Scheme,
            OperatorAuth.Principal(armed, pending: false),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(_options.Value.SessionHours) });
        Codes = codes;
        return Page();
    }

    /// <summary>The seed in the groups of four the page shows, which is how a person types it without losing their place.</summary>
    public static string Readable(string base32)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base32);
        return string.Join(' ', Enumerable.Range(0, (base32.Length + 3) / 4).Select(i => base32.Substring(i * 4, Math.Min(4, base32.Length - (i * 4)))));
    }

    /// <summary>Reads an RFC 4648 base32 string back to bytes, ignoring spaces and case.</summary>
    public static byte[] Base32Bytes(string base32)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base32);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = [];
        int buffer = 0;
        int bits = 0;
        foreach (char letter in base32.ToUpperInvariant())
        {
            int value = alphabet.IndexOf(letter, StringComparison.Ordinal);
            if (value < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. bytes];
    }

    private void Describe(OperatorAccount account)
    {
        AccountName = account.Username;
        OwesAPassword = account.MustChangePassword;
        SecretText = Readable(Secret!);
        Qr = Selficient.Web.Formatting.QrCode.For(Totp.Enrolment(Base32Bytes(Secret!), "Selficient", account.Username));
    }

    private async Task<OperatorAccount?> AccountAsync(CancellationToken cancellationToken)
    {
        long? id = OperatorAuth.IdOf(User);
        return id is null ? null : await _store.FindAsync(id.Value, cancellationToken);
    }
}
