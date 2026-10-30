using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Selficient.Application.Geography;
using Selficient.Application.Solar;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Solar;

/// <summary>The Google Solar API key. Absent means the feature is absent, never an error.</summary>
public sealed class GoogleOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Google";

    /// <summary>The API key, restricted in the Google console to the Solar API and this server's address.</summary>
    [RegularExpression("^[A-Za-z0-9_-]{20,80}$", ErrorMessage = "Google:SolarApiKey does not look like an API key.")]
    public string? SolarApiKey { get; init; }

    /// <summary>True when a key is set.</summary>
    public bool HasSolarKey => !string.IsNullOrEmpty(SolarApiKey);
}

/// <summary>
/// Google Solar API, Building Insights. Called only from the consent handler, only with a key, and
/// the key travels in a header rather than the URL so it is never in a log or a referrer.
/// </summary>
public sealed class GoogleSolarRoofInsights : IRoofInsightsProvider
{
    /// <summary>Most segments read from an answer; a roof with more is not a house.</summary>
    public const int MaxSegments = 20;

    private readonly HttpClient _http;

    /// <summary>Creates the adapter over a typed client carrying the key header.</summary>
    public GoogleSolarRoofInsights(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <summary>The request path; coordinates to five decimals, nothing else variable.</summary>
    internal static Uri Path(Coordinates coordinates)
    {
        string lat = coordinates.Latitude.ToString("0.00000", CultureInfo.InvariantCulture);
        string lon = coordinates.Longitude.ToString("0.00000", CultureInfo.InvariantCulture);
        return new Uri($"v1/buildingInsights:findClosest?location.latitude={lat}&location.longitude={lon}&requiredQuality=MEDIUM", UriKind.Relative);
    }

    /// <inheritdoc />
    public async Task<Result<RoofInsights>> DescribeAsync(Coordinates coordinates, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(Path(coordinates), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<RoofInsights>(DomainErrors.LookupUnavailable());
            }

            GoogleBuildingInsights? insights = await response.Content.ReadFromJsonAsync(SolarJson.Default.GoogleBuildingInsights, cancellationToken);
            GoogleSolarPotential? potential = insights?.SolarPotential;
            if (potential?.RoofSegmentStats is not { Count: > 0 } raw)
            {
                return Result.Failure<RoofInsights>(DomainErrors.LookupUnavailable());
            }

            List<RoofSegment> segments = [];
            foreach (GoogleRoofSegment segment in raw.Take(MaxSegments))
            {
                double? area = segment.Stats?.AreaMeters2;
                if (segment.AzimuthDegrees is not (>= 0 and <= 360) || segment.PitchDegrees is not (>= 0 and <= 90) || area is not (> 0 and < 10_000))
                {
                    continue;
                }

                segments.Add(new RoofSegment(segment.AzimuthDegrees.Value, segment.PitchDegrees.Value, area.Value));
            }

            if (segments.Count == 0)
            {
                return Result.Failure<RoofInsights>(DomainErrors.LookupUnavailable());
            }

            segments.Sort(static (a, b) => b.AreaSquareMetres.CompareTo(a.AreaSquareMetres));
            int maxPanels = potential.MaxArrayPanelsCount is (> 0 and <= 200) ? potential.MaxArrayPanelsCount.Value : 0;
            int watts = potential.PanelCapacityWatts is (>= 100 and <= 1000) ? potential.PanelCapacityWatts.Value : 400;
            return Result.Success(new RoofInsights(segments, maxPanels, watts));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<RoofInsights>(DomainErrors.LookupUnavailable());
        }
    }
}

/// <summary>The part of a Building Insights answer the adapter reads.</summary>
public sealed record GoogleBuildingInsights([property: JsonPropertyName("solarPotential")] GoogleSolarPotential? SolarPotential);

/// <summary>Google's solar potential block.</summary>
public sealed record GoogleSolarPotential(
    [property: JsonPropertyName("maxArrayPanelsCount")] int? MaxArrayPanelsCount,
    [property: JsonPropertyName("panelCapacityWatts")] int? PanelCapacityWatts,
    [property: JsonPropertyName("roofSegmentStats")] IReadOnlyList<GoogleRoofSegment>? RoofSegmentStats);

/// <summary>One roof segment.</summary>
public sealed record GoogleRoofSegment(
    [property: JsonPropertyName("pitchDegrees")] double? PitchDegrees,
    [property: JsonPropertyName("azimuthDegrees")] double? AzimuthDegrees,
    [property: JsonPropertyName("stats")] GoogleSegmentStats? Stats);

/// <summary>A segment's size.</summary>
public sealed record GoogleSegmentStats([property: JsonPropertyName("areaMeters2")] double? AreaMeters2);
