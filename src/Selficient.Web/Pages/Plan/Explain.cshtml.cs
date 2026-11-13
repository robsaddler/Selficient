using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Ai;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// "Explain my plan in plain words." The plan page posts the same profile it was built from; the
/// plan is rebuilt here, so the narrative can never describe a plan other than the one shown, and
/// the narrator is told the plan as text. It explains; it does not compute.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.AiPolicy)]
public sealed class ExplainModel : PageModel
{
    private readonly ExplainPlanHandler _handler;

    /// <summary>Creates the page.</summary>
    public ExplainModel(ExplainPlanHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <summary>The profile, re-posted from the plan page.</summary>
    [BindProperty]
    public PlanForm Form { get; set; } = new();

    /// <summary>The household's own figures for things it has, re-posted with the profile so the plan explained is the plan on its page.</summary>
    [BindProperty(Name = nameof(Own))]
    public Dictionary<string, string> Own { get; init; } = [];

    /// <summary>The plan the narrative is about.</summary>
    public Domain.Plans.Plan? Plan { get; private set; }

    /// <summary>The narrative.</summary>
    public PlanNarrative? Narrative { get; private set; }

    /// <summary>A sentence when it could not run.</summary>
    public string? Notice { get; private set; }

    /// <summary>A GET has no profile to explain; send people to their plan.</summary>
    public IActionResult OnGet() => RedirectToPage("/Plan/Index");

    /// <summary>Explains the plan.</summary>
    public async Task<IActionResult> OnPostAsync(string? household, CancellationToken cancellationToken)
    {
        Result<IReadOnlyDictionary<string, decimal>> own = OwnFigures.Read(Own);
        if (!ModelState.IsValid || own.IsFailure)
        {
            Notice = "The plan's answers did not arrive whole. Open your plan and try again from there.";
            return Page();
        }

        Result<(Domain.Plans.Plan Plan, PlanNarrative Narrative)> result = await _handler.HandleAsync(Form.ToRequest(own.Value), HouseholdKey.From(household, HttpContext), cancellationToken);
        if (result.IsFailure)
        {
            Notice = result.Error.Message;
            return Page();
        }

        Plan = result.Value.Plan;
        Narrative = result.Value.Narrative;
        return Page();
    }
}
