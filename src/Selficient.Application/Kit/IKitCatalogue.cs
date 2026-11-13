using Selficient.Domain.Kit;
using Selficient.Domain.Plans;

namespace Selficient.Application.Kit;

/// <summary>Where the product picks come from. The Infrastructure adapter embeds and validates them.</summary>
public interface IKitCatalogue
{
    /// <summary>Every pick, in catalogue order.</summary>
    IReadOnlyList<KitPick> All { get; }

    /// <summary>The picks for one action, cheapest first; empty when there are none.</summary>
    IReadOnlyList<KitPick> PicksFor(ActionId action);
}

/// <summary>
/// A pick the shipped catalogue used to carry and has withdrawn: the shop stopped selling it, or the
/// link never showed what the pick named. A store seeded before the withdrawal still holds it, so it
/// is named here to be hidden there.
/// </summary>
/// <param name="Action">The action it was a pick for.</param>
/// <param name="Link">Its link, as the store holds it.</param>
public sealed record RetiredPick(ActionId Action, Uri Link);
