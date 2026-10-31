using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Share;
using Selficient.Web.Endpoints;
using Selficient.Web.Security;

namespace Selficient.Web.Pages;

/// <summary>
/// Where a shared figure lands: the number, the scenario, and the invitation to work out your own.
/// It carries nothing about the household that shared it. Not indexed, because there would be one
/// for every half day; the card it points to is the point.
/// </summary>
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class ShareModel : PageModel
{
    /// <summary>The card the page is for.</summary>
    public ShareCard Card { get; private set; } = new(1m, "storm", "A storm takes the power out");

    /// <summary>The card's image, at the public origin, for the chat apps.</summary>
    public string Image => $"{SeoEndpoints.Origin}/share/{Card.ScenarioId}/{Card.Figure}.png";

    /// <summary>The page's title, which is also what the chat card says.</summary>
    public string Title => $"Our house could stand on its own for {Plan.BoardCopy.DaysReady(Card.Days)} {Card.Clause}. How about yours?";

    /// <summary>Renders the page, or 404 for a scenario or a figure the card does not draw.</summary>
    public IActionResult OnGet(string scenario, string days)
    {
        ShareCard? card = ShareEndpoints.Resolve(scenario, days);
        if (card is null)
        {
            return NotFound();
        }

        Card = card;
        return Page();
    }
}
