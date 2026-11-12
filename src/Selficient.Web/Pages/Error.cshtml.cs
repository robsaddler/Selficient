using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages;

/// <summary>
/// The page shown for any error status. It explains what happened and what to do next, in whole
/// sentences, and never exposes a stack trace or an internal name.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    /// <summary>The HTTP status being explained.</summary>
    public int Status { get; private set; } = StatusCodes.Status500InternalServerError;

    /// <summary>The heading.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>What happened and what to do.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Renders the explanation for <paramref name="code"/>.</summary>
    public IActionResult OnGet([FromRoute] int? code) => Explain(code);

    /// <summary>
    /// A rejected POST (rate limit, bad token) is re-executed here as a POST. Without this handler
    /// Razor Pages renders the page with no model and an empty heading.
    /// </summary>
    /// <remarks>
    /// The status is read from the address and nowhere else. A rejected post still carries its
    /// form, and a field in it called "code" (the sign-in pages have one) was once read as the
    /// status, so a six-digit code became a six-digit status and the page threw.
    /// </remarks>
    public IActionResult OnPost([FromRoute] int? code) => Explain(code);

    private PageResult Explain(int? code)
    {
        // Only an error status is explained here; anything else in the address is an error of its own.
        Status = code is >= 400 and <= 599 ? code.Value : StatusCodes.Status500InternalServerError;
        (Title, Message) = Describe(Status);
        Response.StatusCode = Status;
        return Page();
    }

    internal static (string Title, string Message) Describe(int statusCode) => statusCode switch
    {
        StatusCodes.Status404NotFound => (
            "We couldn't find that page.",
            "The address may have been typed incorrectly, or the page may have moved. Your plan is still safe in your browser."),
        StatusCodes.Status429TooManyRequests => (
            "That was a little too fast.",
            "You have made more requests than we can serve at once. Wait a moment and try again."),
        _ => (
            "Something went wrong on our side.",
            "We have recorded the problem. Your plan is still safe in your browser, so go back and carry on."),
    };
}
