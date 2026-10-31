using Selficient.Application.Ai;
using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;

namespace Selficient.Application.Catalogue;

/// <summary>The cheapest pick for an action, and the highest price the checks saw for it when that is more.</summary>
/// <param name="Action">The action.</param>
/// <param name="Title">The action's title, as the plan says it.</param>
/// <param name="Cheapest">The cheapest shown pick.</param>
/// <param name="WasHigh">The highest price a check read for that pick in the window, when it is above today's; null otherwise.</param>
/// <param name="Others">How many more picks the action has.</param>
public sealed record Deal(ActionId Action, string Title, KitPick Cheapest, Money? WasHigh, int Others)
{
    /// <summary>Whether the price has come down inside the window.</summary>
    public bool IsDrop => WasHigh is not null;
}

/// <summary>
/// What is cheapest right now for every action the plan buys for, with the drops first. Read from
/// the catalogue the pages already hold and the prices the nightly checks read; nothing here is
/// a person's.
/// </summary>
public sealed class DealsReader
{
    /// <summary>How far back a price counts as recent.</summary>
    public const int WindowDays = 90;

    private readonly StoredCatalogue _catalogue;
    private readonly ICatalogueStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Creates the reader.</summary>
    public DealsReader(StoredCatalogue catalogue, ICatalogueStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        _catalogue = catalogue;
        _store = store;
        _clock = clock;
    }

    /// <summary>The deals: price drops first, then the rest cheapest first.</summary>
    public async Task<IReadOnlyList<Deal>> ReadAsync(CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        IReadOnlyDictionary<long, PriceSpan> spans = await _store.PriceSpansAsync(WindowDays, today, cancellationToken);
        List<Deal> deals = [];
        foreach (KitCandidate candidate in KitCandidates.All)
        {
            IReadOnlyList<KitPick> picks = _catalogue.PicksFor(candidate.Id);
            if (picks.Count == 0)
            {
                continue;
            }

            KitPick cheapest = picks[0];
            deals.Add(new Deal(candidate.Id, candidate.Title, cheapest, WasHigh(cheapest, spans), picks.Count - 1));
        }

        return deals.OrderByDescending(static deal => deal.IsDrop).ThenBy(static deal => deal.Cheapest.Price.Pence).ToList();
    }

    /// <summary>The window's high for a pick when it is above today's price.</summary>
    internal static Money? WasHigh(KitPick pick, IReadOnlyDictionary<long, PriceSpan> spans)
    {
        if (pick.OfferId is not { } id || !spans.TryGetValue(id, out PriceSpan? span))
        {
            return null;
        }

        return span.High.Pence > pick.Price.Pence ? span.High : null;
    }
}
