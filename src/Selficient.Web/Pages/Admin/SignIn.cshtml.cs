using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Selficient.Application.Security;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Admin;

/// <summary>
/// The first step of signing in: the username and the password. Every refusal says one sentence
/// and costs the same work, whether the name exists or not, so the page tells nobody which
/// operators there are.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class SignInModel : PageModel
{
    private readonly OperatorSignIn _signIn;
    private readonly IOptions<OperatorOptions> _options;

    /// <summary>Creates the page.</summary>
    public SignInModel(OperatorSignIn signIn, IOptions<OperatorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(options);
        _signIn = signIn;
        _options = options;
    }

    /// <summary>The username, as typed.</summary>
    [BindProperty]
    public string? Username { get; set; }

    /// <summary>The password, which is never written anywhere.</summary>
    [BindProperty]
    public string? Password { get; set; }

    /// <summary>Why the last attempt was refused, or nothing.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Shows the form, or sends an operator who is already signed in on.</summary>
    public IActionResult OnGet()
    {
        if (!_options.Value.Enabled)
        {
            return NotFound();
        }

        if (User.Identity?.IsAuthenticated != true || OperatorAuth.IsPending(User))
        {
            return Page();
        }

        // An operator who still owes a password is sent to the one page that lets them pay it.
        // Sending them to the reviewer instead would bounce them back here and round again.
        return RedirectToPage(OperatorAuth.MustChangePassword(User) ? "/Admin/Password" : "/Admin/Catalogue");
    }

    /// <summary>Takes the password step.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled)
        {
            return NotFound();
        }

        OperatorSignInResult result = await _signIn.PasswordAsync(Username, Password, cancellationToken);
        Password = null;
        switch (result.Outcome)
        {
            case SignInOutcome.LockedOut:
                Refusal = OperatorSignIn.LockedSentence;
                return Page();
            case SignInOutcome.Disabled:
            case SignInOutcome.Refused:
                Refusal = OperatorSignIn.RefusedSentence;
                return Page();
            default:
                break;
        }

        // The password is right and the second factor is not done: a short-lived half-signed-in
        // cookie carries only enough to finish, and reaches no reviewer page.
        await HttpContext.SignInAsync(
            OperatorAuth.Scheme,
            OperatorAuth.Principal(result.Account!, pending: true),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(_options.Value.SecondFactorMinutes) });
        return RedirectToPage(result.Outcome == SignInOutcome.NeedsEnrolment ? "/Admin/Enrol" : "/Admin/Code");
    }
}

/// <summary>Signing out, from any page that offers it.</summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class SignOutModel : PageModel
{
    /// <summary>A sign-out is a post, never a link, so no other site can end a session for you.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(OperatorAuth.Scheme);
        return RedirectToPage("/Admin/SignIn");
    }

    /// <summary>A visit by address signs nobody out; it goes to the sign-in.</summary>
    public IActionResult OnGet() => RedirectToPage("/Admin/SignIn");
}
