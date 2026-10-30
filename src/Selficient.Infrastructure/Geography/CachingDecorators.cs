using System.Globalization;
using Microsoft.Extensions.Caching.Hybrid;
using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Geography;

/// <summary>
/// Caches successful lookups for thirty days, keyed on the normalised postcode so case and spacing
/// variants share one entry. Failures are never cached, so a blip does not stick.
/// </summary>
public sealed class CachedGeocoder : IGeocoder
{
    private static readonly HybridCacheEntryOptions Month = new() { Expiration = TimeSpan.FromDays(30), LocalCacheExpiration = TimeSpan.FromDays(30) };

    private readonly IGeocoder _inner;
    private readonly HybridCache _cache;

    /// <summary>Wraps a geocoder.</summary>
    public CachedGeocoder(IGeocoder inner, HybridCache cache)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        _inner = inner;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<Result<Locality>> LocateAsync(Postcode postcode, CancellationToken cancellationToken)
    {
        string key = "geo:" + postcode.Value;
        Locality? hit = await _cache.GetOrCreateAsync(
            key,
            (_inner, postcode),
            static async (state, ct) =>
            {
                Result<Locality> result = await state._inner.LocateAsync(state.postcode, ct);
                return result.IsSuccess ? result.Value : null;
            },
            Month,
            cancellationToken: cancellationToken);

        if (hit is not null)
        {
            return Result.Success(hit);
        }

        // A null landed in the cache from a failure; evict it and surface the real fault.
        await _cache.RemoveAsync(key, cancellationToken);
        return await _inner.LocateAsync(postcode, cancellationToken);
    }
}

/// <summary>Caches elevations by coordinates rounded to four decimals (about eleven metres).</summary>
public sealed class CachedElevationProvider : IElevationProvider
{
    private static readonly HybridCacheEntryOptions Month = new() { Expiration = TimeSpan.FromDays(30), LocalCacheExpiration = TimeSpan.FromDays(30) };

    private readonly IElevationProvider _inner;
    private readonly HybridCache _cache;

    /// <summary>Wraps a provider.</summary>
    public CachedElevationProvider(IElevationProvider inner, HybridCache cache)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        _inner = inner;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<Result<Elevation>> ElevationAtAsync(Coordinates coordinates, CancellationToken cancellationToken)
    {
        string key = string.Create(CultureInfo.InvariantCulture, $"elev:{coordinates.Latitude:0.0000}:{coordinates.Longitude:0.0000}");
        decimal? metres = await _cache.GetOrCreateAsync(
            key,
            (_inner, coordinates),
            static async (state, ct) =>
            {
                Result<Elevation> result = await state._inner.ElevationAtAsync(state.coordinates, ct);
                return result.IsSuccess ? result.Value.Metres : (decimal?)null;
            },
            Month,
            cancellationToken: cancellationToken);

        if (metres is decimal value)
        {
            return Elevation.Create(value);
        }

        await _cache.RemoveAsync(key, cancellationToken);
        return await _inner.ElevationAtAsync(coordinates, cancellationToken);
    }
}
