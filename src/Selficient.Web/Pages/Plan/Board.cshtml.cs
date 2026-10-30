using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Planning;
using Selficient.Application.Resilience;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// The board as a fragment. The plan page's script posts the profile, the ticks and the chosen
/// scenario here and swaps the answer in, so a tick redraws the board without reloading the plan.
/// Without script the board on the plan page itself is the one that counts.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.BoardPolicy)]
public sealed class BoardModel : PageModel
{
    private readonly PlanHouseholdHandler _handler;

    public BoardModel(PlanHouseholdHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <summary>The household's answers.</summary>
    [BindProperty]
    public PlanForm Form { get; set; } = new();

    /// <summary>The actions ticked.</summary>
    [BindProperty]
    public List<string> Ticks { get; init; } = [];

    /// <summary>There is nothing to see here; the plan page is where the board lives.</summary>
    public IActionResult OnGet() => RedirectToPage("/Plan/Index");

    /// <summary>Renders the board fragment, or a sentence with the status that explains why not.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            // The first of our own sentences, so a person adding a device sees why it was refused.
            return Refused(ModelState.Values.SelectMany(entry => entry.Errors).First().ErrorMessage);
        }

        Result<PlannedHousehold> planned = await _handler.HandleAsync(Form.ToRequest(), cancellationToken);
        if (planned.IsFailure)
        {
            return Refused(planned.Error.Message);
        }

        Result<BoardSet> board = IndexModel.Compose(Form, Ticks, planned.Value);
        if (board.IsFailure)
        {
            return Refused(board.Error.Message);
        }

        return Partial("_Board", new BoardView(board.Value, planned.Value.Plan.Profile.Dwelling.HasOwnRoof));
    }

    private static ContentResult Refused(string sentence) => new() { StatusCode = StatusCodes.Status400BadRequest, ContentType = "text/plain; charset=utf-8", Content = sentence };
}
