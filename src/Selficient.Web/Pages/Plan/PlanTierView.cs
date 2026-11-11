using Selficient.Application.Kit;
using Selficient.Application.Resilience;
using Selficient.Domain.Kit;
using Selficient.Domain.Plans;

namespace Selficient.Web.Pages.Plan;

/// <summary>A tier with the catalogue picks for each of its actions.</summary>
public sealed class PlanTierView
{
    /// <summary>Builds the view.</summary>
    public PlanTierView(PlanTier tier, IKitCatalogue catalogue, TierOutcome? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(tier);
        ArgumentNullException.ThrowIfNull(catalogue);
        Tier = tier;
        Reached = outcome ?? new TierOutcome(tier.Tier, tier.Title, 0m, ChoiceTally.Of(tier.Actions, PlanChoices.None), false);
        Outcome = outcome?.DaysReady;
        Picks = tier.Actions.ToDictionary(static a => a.Id, a => catalogue.PicksFor(a.Id));
    }

    /// <summary>The tier.</summary>
    public PlanTier Tier { get; }

    /// <summary>Where this tier takes the house in the chosen scenario, with everything up to it ticked; null when the board has not said.</summary>
    public decimal? Outcome { get; }

    /// <summary>The tier as the board reached it, with its tally; with nothing said about any action when the board has not said.</summary>
    public TierOutcome Reached { get; }

    /// <summary>Picks by action id.</summary>
    public IReadOnlyDictionary<ActionId, IReadOnlyList<KitPick>> Picks { get; }

    /// <summary>True when any action in the tier has a pick, so the disclosure is shown.</summary>
    public bool HasPicks => Picks.Values.Any(static p => p.Count > 0);
}
