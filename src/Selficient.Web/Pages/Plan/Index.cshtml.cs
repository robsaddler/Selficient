using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Kit;
using Selficient.Application.Planning;
using Selficient.Application.Resilience;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Plan;

/// <summary>The three things the plan page can show.</summary>
public enum PlanView
{
    /// <summary>No plan on this device: the invitation and the hidden re-submit form.</summary>
    Empty,

    /// <summary>The form again, with what needs correcting.</summary>
    Form,

    /// <summary>The plan.</summary>
    Plan,
}

/// <summary>
/// The plan page. GET shows the empty state (the browser re-posts a stored profile if it has one);
/// POST validates the form, runs the journey and renders the plan. Nothing is stored server-side.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class IndexModel : PageModel
{
    private readonly PlanHouseholdHandler _handler;

    /// <summary>Creates the page.</summary>
    public IndexModel(PlanHouseholdHandler handler, IKitCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(catalogue);
        _handler = handler;
        Catalogue = catalogue;
    }

    /// <summary>The product picks, for the tier partials.</summary>
    public IKitCatalogue Catalogue { get; }

    /// <summary>The form field names, for the hidden re-submit form the browser fills in.</summary>
    public static IReadOnlyList<string> FormFields { get; } =
    [
        nameof(PlanForm.Postcode), nameof(PlanForm.People), nameof(PlanForm.Children), nameof(PlanForm.HasVulnerablePerson),
        nameof(PlanForm.HasPets), nameof(PlanForm.RunsBusinessFromHome), nameof(PlanForm.DwellingType), nameof(PlanForm.Storeys),
        nameof(PlanForm.Heating), nameof(PlanForm.Hob), nameof(PlanForm.HasCar), nameof(PlanForm.HasGarden),
        nameof(PlanForm.HasDecking), nameof(PlanForm.HasTallTreeNearHouse), nameof(PlanForm.FloodRisk),
        nameof(PlanForm.HasChestFreezer), nameof(PlanForm.HasWoodStove), nameof(PlanForm.HasWaterButt), nameof(PlanForm.Growing),
        nameof(PlanForm.Car), nameof(PlanForm.Infants), nameof(PlanForm.MedicineDaysInStock), nameof(PlanForm.Floor),
    ];

    /// <summary>The most ticks a request may carry.</summary>
    public const int MaxTicks = 200;

    /// <summary>The submitted form.</summary>
    [BindProperty]
    public PlanForm Form { get; set; } = new();

    /// <summary>The actions the household has ticked, sent by the page's script so the board reflects them.</summary>
    [BindProperty]
    public List<string> Ticks { get; init; } = [];

    /// <summary>The plan, once built.</summary>
    public PlannedHousehold? Planned { get; private set; }

    /// <summary>The boards for the plan, or null when the form is showing.</summary>
    public BoardSet? Board { get; private set; }

    /// <summary>True after a failed POST, so the form is shown with its errors.</summary>
    public bool ShowForm { get; private set; }

    /// <summary>Which of the three states the page is in.</summary>
    public PlanView View => Planned is not null ? PlanView.Plan : ShowForm ? PlanView.Form : PlanView.Empty;

    /// <summary>The submitted values, echoed for the browser to store. Enums as names, bools as true/false.</summary>
    public IReadOnlyList<(string Field, string Value)> EchoedProfile { get; private set; } = [];

    /// <summary>The empty state.</summary>
    public IActionResult OnGet() => Page();

    /// <summary>Builds the plan.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ShowForm = true;
            return Page();
        }

        Result<PlannedHousehold> result = await _handler.HandleAsync(Form.ToRequest(), cancellationToken);
        if (result.IsFailure)
        {
            ShowForm = true;
            string field = result.Error.Code.StartsWith("postcode", StringComparison.Ordinal) || result.Error.Code == "lookup.unavailable"
                ? $"{nameof(Form)}.{nameof(PlanForm.Postcode)}"
                : string.Empty;
            ModelState.AddModelError(field, result.Error.Message);
            return Page();
        }

        Result<BoardSet> board = Compose(Form, Ticks, result.Value);
        if (board.IsFailure)
        {
            ShowForm = true;
            ModelState.AddModelError(string.Empty, board.Error.Message);
            return Page();
        }

        Planned = result.Value;
        Board = board.Value;
        EchoedProfile = Echo(Form);
        return Page();
    }

    /// <summary>The boards for a planned household, or the sentence that stops them: a bad dependency, or too many ticks.</summary>
    internal static Result<BoardSet> Compose(PlanForm form, IReadOnlyList<string> ticks, PlannedHousehold planned)
    {
        if (ticks.Count > MaxTicks)
        {
            return Result.Failure<BoardSet>(new Fault("plan.ticks", "That is more ticks than the plan has actions."));
        }

        HashSet<string> held = new(StringComparer.Ordinal);
        foreach (string tick in ticks)
        {
            if (!ActionId.IsValid(tick))
            {
                return Result.Failure<BoardSet>(new Fault("plan.ticks", "One of the ticks is not a plan action."));
            }

            held.Add(tick);
        }

        return form.ToResilienceProfile(planned.Plan.Profile)
            .Map(profile => BoardComposer.Compose(new BoardRequest(profile, held, planned.Plan, form.Scenario, form.Season)));
    }

    internal static IReadOnlyList<(string Field, string Value)> Echo(PlanForm form) =>
    [
        (nameof(PlanForm.Postcode), form.Postcode),
        (nameof(PlanForm.People), form.People.ToString(CultureInfo.InvariantCulture)),
        (nameof(PlanForm.Children), form.Children.ToString(CultureInfo.InvariantCulture)),
        (nameof(PlanForm.HasVulnerablePerson), Flag(form.HasVulnerablePerson)),
        (nameof(PlanForm.HasPets), Flag(form.HasPets)),
        (nameof(PlanForm.RunsBusinessFromHome), Flag(form.RunsBusinessFromHome)),
        (nameof(PlanForm.DwellingType), form.DwellingType.ToString()),
        (nameof(PlanForm.Storeys), form.Storeys.ToString(CultureInfo.InvariantCulture)),
        (nameof(PlanForm.Heating), form.Heating.ToString()),
        (nameof(PlanForm.Hob), form.Hob.ToString()),
        (nameof(PlanForm.HasCar), Flag(form.HasCar)),
        (nameof(PlanForm.HasGarden), Flag(form.HasGarden)),
        (nameof(PlanForm.HasDecking), Flag(form.HasDecking)),
        (nameof(PlanForm.HasTallTreeNearHouse), Flag(form.HasTallTreeNearHouse)),
        (nameof(PlanForm.FloodRisk), form.FloodRisk.ToString()),
        (nameof(PlanForm.HasChestFreezer), Flag(form.HasChestFreezer)),
        (nameof(PlanForm.HasWoodStove), Flag(form.HasWoodStove)),
        (nameof(PlanForm.HasWaterButt), Flag(form.HasWaterButt)),
        (nameof(PlanForm.Growing), (form.Growing ?? Selficient.Domain.Resilience.Growing.None).ToString()),
        (nameof(PlanForm.Car), (form.Car ?? Selficient.Domain.Resilience.CarKind.None).ToString()),
        (nameof(PlanForm.Infants), (form.Infants ?? 0).ToString(CultureInfo.InvariantCulture)),
        (nameof(PlanForm.MedicineDaysInStock), (form.MedicineDaysInStock ?? 7).ToString(CultureInfo.InvariantCulture)),
        (nameof(PlanForm.Floor), (form.Floor ?? 0).ToString(CultureInfo.InvariantCulture)),
    ];

    private static string Flag(bool value) => value ? "true" : "false";
}
