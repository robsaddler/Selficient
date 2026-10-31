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
/// The second step: the authenticator's code, or one of the recovery codes. Only an operator who
/// has passed the password step can reach it, and passing it is the only way to a reviewer page.
/// </summary>
[Authorize(Policy = OperatorAuth.PendingPolicy)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class CodeModel : PageModel
{
    private readonly OperatorSignIn _signIn;
    private readonly IOptions<OperatorOptions> _options;

    /// <summary>Creates the page.</summary>
    public CodeModel(OperatorSignIn signIn, IOptions<OperatorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(options);
        _signIn = signIn;
        _options = options;
    }

    /// <summary>The code, as typed.</summary>
    [BindProperty]
    public string? Code { get; set; }

    /// <summary>Why the last code was refused, or nothing.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Shows the form.</summary>
    public IActionResult OnGet() => Page();

    /// <summary>Takes the code.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        long id = OperatorAuth.IdOf(User)!.Value;
        OperatorSignInResult result = await _signIn.SecondFactorAsync(id, Code, OperatorAuth.OriginOf(HttpContext), cancellationToken);
        Code = null;
        if (result.Outcome is SignInOutcome.Refused)
        {
            Refusal = OperatorSignIn.CodeRefusedSentence;
            return Page();
        }

        await HttpContext.SignInAsync(
            OperatorAuth.Scheme,
            OperatorAuth.Principal(result.Account!, pending: false),
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(_options.Value.SessionHours),
            });
        return RedirectToPage(result.Outcome == SignInOutcome.NeedsNewPassword ? "/Admin/Password" : "/Admin/Catalogue");
    }
}
