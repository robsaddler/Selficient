using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Hybrid;
using Selficient.Application.Geography;
using Selficient.Application.Solar;
using Selficient.Domain.Primitives;
using Selficient.Domain.Solar;

namespace Selficient.Infrastructure.Solar;

/// <summary>
/// PVGIS 5.2 (the European Commission's Joint Research Centre), free and keyless. One GET per
/// roof: a kilowatt-peak of crystalline silicon at the given pitch and azimuth with 14 percent
/// system losses, the figures the household review was built on.
/// </summary>
public sealed class PvgisYieldProvider : ISolarYieldProvider
{
    /// <summary>System losses PVGIS is asked to assume, in percent.</summary>
    public const int SystemLossPercent = 14;

    private readonly HttpClient _http;

    /// <summary>Creates the adapter over a typed client whose base address is fixed by options.</summary>
    public PvgisYieldProvider(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>The request path. Every variable part is a validated number; nothing a person typed reaches it.</summary>
    internal static Uri Path(Coordinates coordinates, RoofAspect aspect, RoofPitch pitch)
    {
        string lat = coordinates.Latitude.ToString("0.0000", CultureInfo.InvariantCulture);
        string lon = coordinates.Longitude.ToString("0.0000", CultureInfo.InvariantCulture);
        return new Uri($"api/v5_2/PVcalc?lat={lat}&lon={lon}&peakpower=1&loss={SystemLossPercent}&angle={pitch.Degrees}&aspect={aspect.PvgisAzimuth()}&outputformat=json", UriKind.Relative);
    }

    /// <inheritdoc />
    public async Task<Result<SolarYield>> YieldAsync(Coordinates coordinates, RoofAspect aspect, RoofPitch pitch, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(Path(coordinates, aspect, pitch), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<SolarYield>(DomainErrors.YieldUnavailable());
            }

            PvgisEnvelope? envelope = await response.Content.ReadFromJsonAsync(SolarJson.Default.PvgisEnvelope, cancellationToken);
            IReadOnlyList<PvgisMonth>? months = envelope?.Outputs?.Monthly?.Fixed;
            if (months is not { Count: 12 })
            {
                return Result.Failure<SolarYield>(DomainErrors.YieldUnavailable());
            }

            // Ordered by the month field rather than trusting the array order.
            decimal[] perMonth = new decimal[12];
            foreach (PvgisMonth month in months)
            {
                if (month.Month is < 1 or > 12 || month.EnergyMonth is null)
                {
                    return Result.Failure<SolarYield>(DomainErrors.YieldUnavailable());
                }

                perMonth[month.Month - 1] = Math.Round((decimal)month.EnergyMonth.Value, 2);
            }

            return SolarYield.Create(perMonth);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<SolarYield>(DomainErrors.YieldUnavailable());
        }
    }
}

/// <summary>Caches yields for thirty days by coordinates to three decimals (about a hundred metres), aspect and pitch.</summary>
public sealed class CachedSolarYieldProvider : ISolarYieldProvider
{
    private static readonly HybridCacheEntryOptions Month = new() { Expiration = TimeSpan.FromDays(30), LocalCacheExpiration = TimeSpan.FromDays(30) };

    private readonly ISolarYieldProvider _inner;
    private readonly HybridCache _cache;

    /// <summary>Wraps a provider.</summary>
    public CachedSolarYieldProvider(ISolarYieldProvider inner, HybridCache cache)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        _inner = inner;
        _cache = cache;
    }

    /// <summary>The cache key, exposed for the test that proves the rounding.</summary>
    internal static string Key(Coordinates coordinates, RoofAspect aspect, RoofPitch pitch) =>
        string.Create(CultureInfo.InvariantCulture, $"pv:{Math.Round(coordinates.Latitude, 3):0.000}:{Math.Round(coordinates.Longitude, 3):0.000}:{(int)aspect}:{pitch.Degrees}");

    /// <inheritdoc />
    public async Task<Result<SolarYield>> YieldAsync(Coordinates coordinates, RoofAspect aspect, RoofPitch pitch, CancellationToken cancellationToken)
    {
        string key = Key(coordinates, aspect, pitch);
        decimal[]? hit = await _cache.GetOrCreateAsync(
            key,
            (_inner, coordinates, aspect, pitch),
            static async (state, ct) =>
            {
                Result<SolarYield> result = await state._inner.YieldAsync(state.coordinates, state.aspect, state.pitch, ct);
                return result.IsSuccess ? result.Value.MonthlyKwhPerKwp.ToArray() : null;
            },
            Month,
            cancellationToken: cancellationToken);

        if (hit is not null)
        {
            return SolarYield.Create(hit);
        }

        await _cache.RemoveAsync(key, cancellationToken);
        return await _inner.YieldAsync(coordinates, aspect, pitch, cancellationToken);
    }
}

/// <summary>The part of PVGIS's answer the adapter reads.</summary>
public sealed record PvgisEnvelope([property: JsonPropertyName("outputs")] PvgisOutputs? Outputs);

/// <summary>PVGIS outputs.</summary>
public sealed record PvgisOutputs([property: JsonPropertyName("monthly")] PvgisMonthly? Monthly);

/// <summary>PVGIS monthly block.</summary>
public sealed record PvgisMonthly([property: JsonPropertyName("fixed")] IReadOnlyList<PvgisMonth>? Fixed);

/// <summary>One PVGIS month.</summary>
public sealed record PvgisMonth([property: JsonPropertyName("month")] int Month, [property: JsonPropertyName("E_m")] double? EnergyMonth);

/// <summary>Source-generated serializer context for the solar upstreams.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PvgisEnvelope))]
[JsonSerializable(typeof(GoogleBuildingInsights))]
public sealed partial class SolarJson : JsonSerializerContext;
