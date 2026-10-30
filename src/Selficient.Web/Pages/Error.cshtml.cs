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
    public IActionResult OnGet(int? code) => Explain(code);

    /// <summary>
    /// A rejected POST (rate limit, bad token) is re-executed here as a POST. Without this handler
    /// Razor Pages renders the page with no model and an empty heading.
    /// </summary>
    public IActionResult OnPost(int? code) => Explain(code);

    private PageResult Explain(int? code)
    {
        Status = code ?? StatusCodes.Status500InternalServerError;
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
