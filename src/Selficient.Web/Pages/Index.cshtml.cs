using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Selficient.Domain.Households;
using Selficient.Domain.Resilience;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Pages;

/// <summary>The landing page: the Days Ready figure, what the app does, and the questions people ask.</summary>
public sealed class IndexModel : PageModel
{
    /// <summary>
    /// The board for a typical house with nothing put by, in the most likely scenario, so the figure
    /// and the strip on the landing page are the engine's and not a number someone typed.
    /// </summary>
    public static Board TypicalBoard { get; } = OutageBoard.Simulate(ResilienceProfile.FromProfile(TypicalHouse()), new HashSet<string>(), Scenarios.Storm, Season.Winter);

    /// <summary>The figure shown before a plan exists: the typical house's Days Ready.</summary>
    public decimal DaysReady => TypicalBoard.DaysReady;

    /// <summary>How long each need on the strip holds for the typical house.</summary>
    public IReadOnlyList<StripBar> Bars { get; } = Strip.From(TypicalBoard);

    /// <summary>The questions on the page, which the structured data repeats word for word.</summary>
    public static IReadOnlyList<Faq> Questions { get; } =
    [
        new("Is this the government's three-day checklist?",
            "It starts there. The Be Ready campaign of 22 October 2026 asks every household for three days of water, food that needs no cooking, light, a radio, a charged power bank, some cash and a few other things. Selficient turns that list into a plan sized to your house, your hill and your heating, and then goes further for those who want to."),
        new("Do you store my address or my answers?",
            "Not unless you ask us to. Your postcode is sent once to find your area and your height above sea level, and is not kept. Everything else, including your plan and your ticks, lives in your own browser. You do not need an account and nothing tracks you. If you choose to make an account, so that your plan follows you between devices, we keep your email address and your plan, and you can delete both whenever you like."),
        new("How much does it cost?",
            "Nothing. Where we point to a shop, some links earn us a small commission at no cost to you, and we say so beside them. Photo readings use a small free monthly allowance, or your own key if you prefer."),
        new("Is this professional advice?",
            "No. It is careful guidance built from public sources and a named household's own planning, and it never replaces an electrician, an arborist, an MCS installer or the instructions of the emergency services."),
        new("Does it work without signal?",
            "Yes. Install it to your phone and your plan opens without a connection, which is rather the point of a plan for a power cut."),
    ];

    /// <summary>Renders the page.</summary>
    public IActionResult OnGet() => Page();

    /// <summary>
    /// A typical British household: four people, two of them children, a semi-detached house of two
    /// storeys with a gas boiler, an electric hob, a car and a garden, nowhere in particular.
    /// </summary>
    internal static HouseholdProfile TypicalHouse()
    {
        Household household = Household.Create(people: 4, children: 2, hasVulnerablePerson: false, hasPets: false, runsBusinessFromHome: false).Value;
        Dwelling dwelling = Dwelling.Create(DwellingType.SemiDetached, storeys: 2, HeatingType.GasBoiler, HobType.Electric, hasCar: true, hasGarden: true, hasDecking: false, hasTallTreeNearHouse: false, elevation: null, floodRisk: FloodRisk.Unknown).Value;
        return new HouseholdProfile(household, dwelling);
    }

    /// <summary>One question and its answer.</summary>
    /// <param name="Question">The question as asked.</param>
    /// <param name="Answer">The answer, in whole sentences.</param>
    public sealed record Faq(string Question, string Answer);
}
