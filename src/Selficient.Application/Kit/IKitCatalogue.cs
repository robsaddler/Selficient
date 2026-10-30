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
