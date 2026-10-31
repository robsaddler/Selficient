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
/// Changing the password. The current one is asked for, the new one has to pass the policy, and
/// writing it moves the account's security stamp, which ends every other session on every other
/// device at its next request. This device is signed in again on the way out, so the person doing
/// the changing is not signed out by their own change.
/// </summary>
[Authorize(Policy = OperatorAuth.SignedInPolicy)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class PasswordModel : PageModel
{
    private readonly IOperatorStore _store;
    private readonly IOptions<OperatorOptions> _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the page.</summary>
    public PasswordModel(IOperatorStore store, IOptions<OperatorOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _options = options;
        _clock = clock;
    }

    /// <summary>The password in force.</summary>
    [BindProperty]
    public string? Current { get; set; }

    /// <summary>The new password.</summary>
    [BindProperty]
    public string? Fresh { get; set; }

    /// <summary>The new password, typed twice.</summary>
    [BindProperty]
    public string? Again { get; set; }

    /// <summary>Whether this change is being insisted on rather than chosen.</summary>
    public bool Forced { get; private set; }

    /// <summary>Why the last attempt was refused, or nothing.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Shows the form.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        OperatorAccount? account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return RedirectToPage("/Admin/SignIn");
        }

        Forced = account.MustChangePassword;
        return Page();
    }

    /// <summary>Takes the change.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        OperatorAccount? account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return RedirectToPage("/Admin/SignIn");
        }

        Forced = account.MustChangePassword;
        Refusal = Check(account);
        string chosen = Fresh ?? string.Empty;
        // The typed values leave the model before anything renders, so no password is ever written
        // back into the page, even on the refusal path.
        Current = null;
        Fresh = null;
        Again = null;
        if (Refusal is not null)
        {
            return Page();
        }

        await _store.SetPasswordAsync(account.Id, PasswordHasher.Hash(chosen), Guid.NewGuid(), _clock.GetUtcNow(), cancellationToken);
        OperatorAccount changed = (await _store.FindAsync(account.Id, cancellationToken))!;
        await HttpContext.SignInAsync(
            OperatorAuth.Scheme,
            OperatorAuth.Principal(changed, pending: false),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(_options.Value.SessionHours) });
        TempData["Notice"] = "Your password is changed, and every other device is signed out.";
        return RedirectToPage("/Admin/Catalogue");
    }

    private string? Check(OperatorAccount account)
    {
        if (!PasswordHasher.Verify(Current, account.PasswordHash).Matches)
        {
            return "That is not your password now.";
        }

        if (!string.Equals(Fresh, Again, StringComparison.Ordinal))
        {
            return "The two new passwords are not the same.";
        }

        if (string.Equals(Fresh, Current, StringComparison.Ordinal))
        {
            return "The new password has to be different from the old one.";
        }

        return PasswordPolicy.Refusal(Fresh, account.Username);
    }

    private async Task<OperatorAccount?> AccountAsync(CancellationToken cancellationToken)
    {
        long? id = OperatorAuth.IdOf(User);
        return id is null ? null : await _store.FindAsync(id.Value, cancellationToken);
    }
}
