using System.Collections.Concurrent;

namespace Selficient.Application.Catalogue;

/// <summary>Clicks on one offer on one day, as the store takes them.</summary>
public sealed record ClickBatch(long OfferId, DateOnly Day, long Count);

/// <summary>
/// The click counter the hop writes to: a number in memory per offer per day, flushed to the store
/// once a second by the flusher. The hop itself touches no database, so a viral afternoon of a
/// thousand clicks a second on one pick is a thousand increments and one row update, not a
/// thousand row locks. A crash loses at most the second not yet flushed, which is the trade the
/// counter makes on purpose.
/// </summary>
public sealed class ClickCounter
{
    private readonly ConcurrentDictionary<(long OfferId, DateOnly Day), long> _pending = new();

    /// <summary>Counts one click.</summary>
    public void Record(long offerId, DateOnly day) => _pending.AddOrUpdate((offerId, day), 1, static (_, count) => count + 1);

    /// <summary>How many clicks wait to be written.</summary>
    public long Pending => _pending.Values.Sum();

    /// <summary>Takes everything counted so far, leaving the counter empty for the next second.</summary>
    public IReadOnlyList<ClickBatch> Drain()
    {
        List<ClickBatch> batches = [];
        foreach (KeyValuePair<(long OfferId, DateOnly Day), long> entry in _pending)
        {
            if (_pending.TryRemove(entry.Key, out long count) && count > 0)
            {
                batches.Add(new ClickBatch(entry.Key.OfferId, entry.Key.Day, count));
            }
        }

        return batches;
    }

    /// <summary>Writes everything counted so far to the store; what the store refuses goes back on the counter.</summary>
    public async Task FlushAsync(ICatalogueStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        IReadOnlyList<ClickBatch> batches = Drain();
        if (batches.Count == 0)
        {
            return;
        }

        bool written = false;
        try
        {
            await store.RecordClicksAsync(batches, cancellationToken);
            written = true;
        }
        finally
        {
            // Whatever stopped the write, the store refusing or the host stopping, the clicks go back for the next flush.
            if (!written)
            {
                foreach (ClickBatch batch in batches)
                {
                    _pending.AddOrUpdate((batch.OfferId, batch.Day), batch.Count, (_, count) => count + batch.Count);
                }
            }
        }
    }
}
