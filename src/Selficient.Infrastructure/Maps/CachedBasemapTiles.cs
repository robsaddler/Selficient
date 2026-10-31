using Microsoft.Extensions.Caching.Hybrid;
using Selficient.Application.Maps;

namespace Selficient.Infrastructure.Maps;

/// <summary>
/// Keeps the hottest tiles, and the hottest misses, in memory for ten minutes. A street that one
/// household is looking at is read from disk once however many times they pan across it; the
/// browser's own immutable cache does the rest.
/// </summary>
public sealed class CachedBasemapTiles : IBasemapTiles
{
    // In process only: a tile is up to two megabytes and the archive is on the same disk, so the database would be a slower cache than the file.
    private static readonly HybridCacheEntryOptions TenMinutes = new() { Expiration = TimeSpan.FromMinutes(10), LocalCacheExpiration = TimeSpan.FromMinutes(10), Flags = HybridCacheEntryFlags.DisableDistributedCache };

    private readonly IBasemapTiles _inner;
    private readonly HybridCache _cache;

    /// <summary>Wraps a tile source.</summary>
    public CachedBasemapTiles(IBasemapTiles inner, HybridCache cache)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        _inner = inner;
        _cache = cache;
    }

    /// <inheritdoc />
    public BasemapStatus Status() => _inner.Status();

    /// <inheritdoc />
    public async ValueTask<MapTile?> GetTileAsync(TileAddress address, CancellationToken cancellationToken)
    {
        // An empty array stands for a miss, so sea tiles are not re-read either.
        byte[] data = await _cache.GetOrCreateAsync(
            $"tile:{address.Z}/{address.X}/{address.Y}",
            (_inner, address),
            static async (state, ct) =>
            {
                MapTile? tile = await state._inner.GetTileAsync(state.address, ct);
                return tile is { } found ? found.Data.ToArray() : [];
            },
            TenMinutes,
            cancellationToken: cancellationToken);

        return data.Length > 0 ? new MapTile(data) : null;
    }
}
