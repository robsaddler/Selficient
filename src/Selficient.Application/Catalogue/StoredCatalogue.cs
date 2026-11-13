using Selficient.Application.Kit;
using Selficient.Domain.Kit;
using Selficient.Domain.Plans;

namespace Selficient.Application.Catalogue;

/// <summary>
/// The catalogue the pages read, drawn from the store and held in memory, so a plan page costs no
/// query. Until the store has answered once the shipped picks stand in, so a page never shows nothing.
/// Every shipped pick the store has never held is added on each reload, so a release that ships a
/// new action reaches a store seeded before it; a shipped pick that has been corrected at the same
/// link rewrites the offer the store holds; and a pick the release has withdrawn is hidden there.
/// The snapshot is reloaded after every verification run and every hand of the reviewer.
/// </summary>
public sealed class StoredCatalogue : IKitCatalogue, IDisposable
{
    private readonly ICatalogueStore _store;
    private readonly IKitCatalogue _shipped;
    private readonly HashSet<(string Action, string Link)> _retired;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot _snapshot = new([], new Dictionary<ActionId, IReadOnlyList<KitPick>>(), new Dictionary<long, KitPick>());

    /// <summary>Creates the catalogue over the store, with the shipped picks as the seed and nothing withdrawn.</summary>
    public StoredCatalogue(ICatalogueStore store, IKitCatalogue shipped)
        : this(store, shipped, [])
    {
    }

    /// <summary>Creates the catalogue over the store, with the shipped picks as the seed and the picks the release has withdrawn.</summary>
    public StoredCatalogue(ICatalogueStore store, IKitCatalogue shipped, IReadOnlyList<RetiredPick> retired)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(shipped);
        ArgumentNullException.ThrowIfNull(retired);
        _store = store;
        _shipped = shipped;
        _retired = [.. retired.Select(static pick => (pick.Action.Value, pick.Link.ToString()))];
    }

    /// <inheritdoc />
    public IReadOnlyList<KitPick> All => IsLoaded ? _snapshot.All : _shipped.All;

    /// <summary>True once the store has been read at least once.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Goes up by one on every reload, so a cache keyed on it empties when the catalogue changes.</summary>
    public int Version { get; private set; }

    /// <summary>The shown pick for an offer, or null when the offer is not shown; no query, the snapshot answers.</summary>
    public KitPick? Find(long offerId) => _snapshot.ByOffer.TryGetValue(offerId, out KitPick? pick) ? pick : null;

    /// <inheritdoc />
    public IReadOnlyList<KitPick> PicksFor(ActionId action)
    {
        if (!IsLoaded)
        {
            return _shipped.PicksFor(action);
        }

        return _snapshot.ByAction.TryGetValue(action, out IReadOnlyList<KitPick>? picks) ? picks : [];
    }

    /// <summary>Adds the shipped picks the store has never held, then takes the snapshot the pages read.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Every shipped pick the store has never held is added, not only when the store is empty:
            // a release that ships a new action must reach a database seeded before that action
            // existed. A pick an operator has hidden is still held, so it is not brought back.
            IReadOnlyList<Offer> held = await _store.AllAsync(cancellationToken);
            Dictionary<(string Action, string Link), Offer> known = [];
            foreach (Offer offer in held)
            {
                known.TryAdd((offer.Action.Value, offer.Link.ToString()), offer);
            }

            List<KitPick> fresh = [];
            foreach (KitPick pick in _shipped.All)
            {
                if (!known.TryGetValue((pick.Action.Value, pick.Link.ToString()), out Offer? offer))
                {
                    fresh.Add(pick);
                }
                else if (Corrects(pick, offer))
                {
                    // The store keeps what it was seeded with for ever otherwise: a pick whose name or price was wrong
                    // when it shipped would stay wrong in every database that had already taken it.
                    await _store.ReviseAsync(offer.Id, pick, cancellationToken);
                }
            }

            if (fresh.Count > 0)
            {
                await _store.SeedAsync(fresh, cancellationToken);
            }

            foreach (Offer offer in held)
            {
                if (offer.Status != OfferStatus.Hidden && _retired.Contains((offer.Action.Value, offer.Link.ToString())))
                {
                    await _store.SetStatusAsync(offer.Id, OfferStatus.Hidden, cancellationToken);
                }
            }

            IReadOnlyList<Offer> shown = await _store.ShownAsync(cancellationToken);
            List<KitPick> all = [.. shown.Select(static offer => offer.ToPick())];
            _snapshot = new Snapshot(
                all,
                all.GroupBy(static p => p.Action).ToDictionary(static g => g.Key, static g => (IReadOnlyList<KitPick>)[.. g.OrderBy(static p => p.Price.Pence)]),
                all.ToDictionary(static p => p.OfferId!.Value));
            IsLoaded = true;
            Version++;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Whether a shipped pick corrects the offer held at its link. Only a release names an offer, so a
    /// different name, shop or note is a correction and its price comes with it. The nightly check
    /// keeps the price of an offer whose words have not changed, so there the shipped price only
    /// stands when it was seen later.
    /// </summary>
    internal static bool Corrects(KitPick pick, Offer offer)
    {
        bool reworded = pick.Name != offer.Name || pick.Retailer != offer.Retailer || pick.Note != offer.Note;
        return reworded || (pick.Seen > offer.Seen && pick.Price != offer.Price);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private sealed record Snapshot(IReadOnlyList<KitPick> All, IReadOnlyDictionary<ActionId, IReadOnlyList<KitPick>> ByAction, IReadOnlyDictionary<long, KitPick> ByOffer);
}
