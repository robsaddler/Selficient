using Selficient.Application.Catalogue;

namespace Selficient.Infrastructure.Catalogue;

/// <summary>
/// The deals, computed once per catalogue version and day and kept in process: the page is public
/// and shareable, and the grouped read of every check must not run once per visitor. A reload of
/// the catalogue (a hand on the reviewer, the night's run) moves the version and so the key. Kept
/// as the objects themselves rather than through <c>HybridCache</c>, which serialises what it keeps
/// and a pick is deliberately not a thing that deserialises.
/// </summary>
public sealed class CachedDealsReader : IDealsReader
{
    private readonly DealsReader _inner;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private (string Key, Task<IReadOnlyList<Deal>> Deals)? _kept;

    /// <summary>Wraps the reader.</summary>
    public CachedDealsReader(DealsReader inner, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(clock);
        _inner = inner;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Deal>> ReadAsync(CancellationToken cancellationToken)
    {
        string key = $"{_inner.CatalogueVersion}:{_clock.GetUtcNow():yyyy-MM-dd}";
        lock (_gate)
        {
            if (_kept is { } kept && kept.Key == key && !kept.Deals.IsFaulted)
            {
                return kept.Deals;
            }

            // The read is shared by everyone who asks while it runs, so a burst is one query.
            Task<IReadOnlyList<Deal>> deals = _inner.ReadAsync(CancellationToken.None);
            _kept = (key, deals);
            return deals;
        }
    }
}
