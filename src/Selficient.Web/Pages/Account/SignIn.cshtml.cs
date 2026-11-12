using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Accounts;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Account;

/// <summary>
/// The first step of a household signing in: the address. The same page makes an account and
/// signs in to one, and says the same thing whichever it turns out to be, so it tells nobody which
/// addresses have accounts.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.SignInPolicy)]
public sealed class SignInModel : PageModel
{
    private readonly AccountSignIn _signIn;
    private readonly PendingSignInCookie _pending;

    /// <summary>Creates the page.</summary>
    public SignInModel(AccountSignIn signIn, PendingSignInCookie pending)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(pending);
        _signIn = signIn;
        _pending = pending;
    }

    /// <summary>The address, as typed.</summary>
    [BindProperty]
    public string? Email { get; set; }

    /// <summary>Why the last attempt was refused, or nothing.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Shows the form, or sends a household that is already signed in to its plan. A server that cannot send a code says so on a page of its own.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        if (!_signIn.IsAvailable)
        {
            return RedirectToPage("/Account/Unavailable");
        }

        AuthenticateResult session = await HttpContext.AuthenticateAsync(HouseholdAuth.Scheme);
        return session.Succeeded ? Redirect("/plan") : Page();
    }

    /// <summary>Sends the code and moves on to the page that takes it.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_signIn.IsAvailable)
        {
            return RedirectToPage("/Account/Unavailable");
        }

        Result<PendingSignIn> asked = await _signIn.RequestCodeAsync(Email, cancellationToken);
        if (asked.IsFailure)
        {
            Refusal = asked.Error.Message;
            return Page();
        }

        _pending.Write(Response, asked.Value);
        return RedirectToPage("/Account/Code");
    }
}

/// <summary>
/// The second step: the code. It is only ever right in the browser that asked for it, five tries
/// at most, for ten minutes.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.SignInPolicy)]
public sealed class CodeModel : PageModel
{
    private readonly AccountSignIn _signIn;
    private readonly PendingSignInCookie _pending;

    /// <summary>Creates the page.</summary>
    public CodeModel(AccountSignIn signIn, PendingSignInCookie pending)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(pending);
        _signIn = signIn;
        _pending = pending;
    }

    /// <summary>The code, as typed.</summary>
    [BindProperty]
    public string? Code { get; set; }

    /// <summary>The address the code went to, read back from this browser's own pending sign-in.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Why the last attempt was refused, or nothing.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Said when a new code has been sent, or nothing.</summary>
    public string? Notice { get; private set; }

    /// <summary>Shows the form to a browser that asked for a code; any other is sent to ask.</summary>
    public IActionResult OnGet()
    {
        PendingSignIn? pending = _pending.Read(Request);
        if (pending is null)
        {
            return RedirectToPage("/Account/SignIn");
        }

        Email = pending.Email;
        return Page();
    }

    /// <summary>Tries the code.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        PendingSignIn? pending = _pending.Read(Request);
        if (pending is null)
        {
            return RedirectToPage("/Account/SignIn");
        }

        Result<HouseholdAccount> proved = await _signIn.VerifyAsync(pending, Code, cancellationToken);
        // What was typed is not written back into the page: a wrong code is retyped, not edited.
        Code = null;
        ModelState.Remove(nameof(Code));
        if (proved.IsFailure)
        {
            Email = pending.Email;
            Refusal = proved.Error.Message;
            return Page();
        }

        // A new session from the server; nothing the browser arrived with is promoted.
        await HouseholdAuth.SignInAsync(HttpContext, proved.Value);
        return Redirect("/plan");
    }

    /// <summary>Sends another code to the same address.</summary>
    public async Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken)
    {
        PendingSignIn? pending = _pending.Read(Request);
        if (pending is null)
        {
            return RedirectToPage("/Account/SignIn");
        }

        Email = pending.Email;
        Code = null;
        ModelState.Remove(nameof(Code));
        Result<PendingSignIn> asked = await _signIn.RequestCodeAsync(pending.Email, cancellationToken);
        if (asked.IsFailure)
        {
            Refusal = asked.Error.Message;
            return Page();
        }

        _pending.Write(Response, asked.Value);
        Notice = AccountCopy.Resent;
        return Page();
    }
}

/// <summary>
/// Signing out of this device. A post, never a link, so no other site can end a session for you;
/// a visit by address shows the one question and changes nothing.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Authorize(Policy = HouseholdAuth.Policy)]
public sealed class SignOutModel : PageModel
{
    /// <summary>The address signed in.</summary>
    public string Email => HouseholdAuth.EmailOf(User);

    /// <summary>Shows the question.</summary>
    public IActionResult OnGet() => Page();

    /// <summary>Signs this browser out. Whether the plan stays in it is the browser's doing, before the post.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        await HouseholdAuth.SignOutAsync(HttpContext);
        return Redirect("/");
    }
}

/// <summary>
/// Deleting the account: the address, the plan, the passkeys and every sign-in, at once. One
/// confirmation, which is this page.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Authorize(Policy = HouseholdAuth.Policy)]
public sealed class DeleteModel : PageModel
{
    private readonly IAccountStore _store;

    /// <summary>Creates the page.</summary>
    public DeleteModel(IAccountStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>The address signed in.</summary>
    public string Email => HouseholdAuth.EmailOf(User);

    /// <summary>Shows the confirmation.</summary>
    public IActionResult OnGet() => Page();

    /// <summary>Deletes the account and signs this browser out. Every other session ends at its next request, because the account it names is gone.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // The policy let this request in, so the principal carries the account's number.
        await _store.DeleteAsync(HouseholdAuth.IdOf(User)!.Value, cancellationToken);
        await HouseholdAuth.SignOutAsync(HttpContext);
        return RedirectToPage("/Account/Deleted");
    }
}

/// <summary>What a server with no way to send a sign-in code says in place of the sign-in page.</summary>
public sealed class UnavailableModel : PageModel
{
    /// <summary>Shows the page.</summary>
    public IActionResult OnGet() => Page();
}

/// <summary>What is said once an account is gone. Open to anyone, because by then nobody is signed in.</summary>
public sealed class DeletedModel : PageModel
{
    /// <summary>Shows the page.</summary>
    public IActionResult OnGet() => Page();
}
