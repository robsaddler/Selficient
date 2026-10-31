using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Catalogue;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// "Found it cheaper?" A reader pastes a shop's link under an action and nothing else. The link is
/// kept as a proposal for the worker to open and the reviewer to decide; nothing about the reader
/// is kept, and nothing a reader sends is shown to anyone without a person approving it.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.ApiPolicy)]
public sealed class TipModel : PageModel
{
    private readonly TipBox _tips;

    /// <summary>Creates the page over the tip box.</summary>
    public TipModel(TipBox tips)
    {
        ArgumentNullException.ThrowIfNull(tips);
        _tips = tips;
    }

    /// <summary>The action the tip is for.</summary>
    [BindProperty]
    public string? Action { get; set; }

    /// <summary>The shop's link.</summary>
    [BindProperty]
    public string? Link { get; set; }

    /// <summary>What became of it.</summary>
    public TipOutcome Outcome { get; private set; }

    /// <summary>The sentence for the outcome.</summary>
    public string Sentence => TipCopy.Sentence(Outcome);

    /// <summary>A GET has nothing to take; send people to their plan.</summary>
    public IActionResult OnGet() => RedirectToPage("/Plan/Index");

    /// <summary>Takes the tip.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Outcome = await _tips.SuggestAsync(Action, Link, cancellationToken);
        return Page();
    }
}
