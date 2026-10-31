using Selficient.Application.Kit;
using Selficient.Domain.Kit;
using Selficient.Domain.Plans;

namespace Selficient.Application.Catalogue;

/// <summary>
/// The catalogue the pages read, drawn from the store and held in memory, so a plan page costs no
/// query. Until the store has answered once the shipped picks stand in, so a page never shows nothing.
/// The store is seeded from the shipped picks the first time it is empty, and the snapshot
/// is reloaded after every verification run and every hand of the reviewer.
/// </summary>
public sealed class StoredCatalogue : IKitCatalogue, IDisposable
{
    private readonly ICatalogueStore _store;
    private readonly IKitCatalogue _shipped;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot _snapshot = new([], new Dictionary<ActionId, IReadOnlyList<KitPick>>());

    /// <summary>Creates the catalogue over the store, with the shipped picks as the seed.</summary>
    public StoredCatalogue(ICatalogueStore store, IKitCatalogue shipped)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(shipped);
        _store = store;
        _shipped = shipped;
    }

    /// <inheritdoc />
    public IReadOnlyList<KitPick> All => IsLoaded ? _snapshot.All : _shipped.All;

    /// <summary>True once the store has been read at least once.</summary>
    public bool IsLoaded { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<KitPick> PicksFor(ActionId action)
    {
        if (!IsLoaded)
        {
            return _shipped.PicksFor(action);
        }

        return _snapshot.ByAction.TryGetValue(action, out IReadOnlyList<KitPick>? picks) ? picks : [];
    }

    /// <summary>Seeds the store if it is empty, then takes the snapshot the pages read.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await _store.CountAsync(cancellationToken) == 0)
            {
                await _store.SeedAsync(_shipped.All, cancellationToken);
            }

            IReadOnlyList<Offer> shown = await _store.ShownAsync(cancellationToken);
            List<KitPick> all = [.. shown.Select(static offer => offer.ToPick())];
            _snapshot = new Snapshot(all, all.GroupBy(static p => p.Action).ToDictionary(static g => g.Key, static g => (IReadOnlyList<KitPick>)[.. g.OrderBy(static p => p.Price.Pence)]));
            IsLoaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private sealed record Snapshot(IReadOnlyList<KitPick> All, IReadOnlyDictionary<ActionId, IReadOnlyList<KitPick>> ByAction);
}
