using Selficient.Application.Kit;
using Selficient.Domain.Kit;
using Selficient.Domain.Plans;

namespace Selficient.Web.Pages.Plan;

/// <summary>A tier with the catalogue picks for each of its actions.</summary>
public sealed class PlanTierView
{
    /// <summary>Builds the view.</summary>
    public PlanTierView(PlanTier tier, IKitCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(tier);
        ArgumentNullException.ThrowIfNull(catalogue);
        Tier = tier;
        Picks = tier.Actions.ToDictionary(static a => a.Id, a => catalogue.PicksFor(a.Id));
    }

    /// <summary>The tier.</summary>
    public PlanTier Tier { get; }

    /// <summary>Picks by action id.</summary>
    public IReadOnlyDictionary<ActionId, IReadOnlyList<KitPick>> Picks { get; }

    /// <summary>True when any action in the tier has a pick, so the disclosure is shown.</summary>
    public bool HasPicks => Picks.Values.Any(static p => p.Count > 0);
}
