using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Selficient.Application.Geography;
using Selficient.Domain.Households;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Geography;

/// <summary>One nation's flood map, read for the ground round a point.</summary>
public interface INationalFloodMap
{
    /// <summary>The nation whose map this is.</summary>
    Nation Nation { get; }

    /// <summary>The highest band the map gives within the reach of the point, or why it could not be read.</summary>
    Task<Result<FloodFinding>> AtAsync(Coordinates point, CancellationToken cancellationToken);
}

/// <summary>
/// The box asked about: the point and <see cref="ReachMetres"/> on every side of it. The point is the
/// middle of a postcode, which the Office for National Statistics places on a real address, so the
/// box is that property and its near neighbours. It is deliberately small: surface water is mapped
/// in patches a few metres across, and a wider box would call most streets in Britain high risk.
/// </summary>
public readonly record struct FloodBox(double South, double West, double North, double East)
{
    /// <summary>How far the box reaches from the point, in metres.</summary>
    public const double ReachMetres = 25;

    private const double MetresPerDegree = 111_320;

    /// <summary>The box round a point.</summary>
    public static FloodBox Around(Coordinates point) => Around(point, ReachMetres);

    /// <summary>A box reaching so many metres from a point on every side.</summary>
    public static FloodBox Around(Coordinates point, double metres)
    {
        double north = metres / MetresPerDegree;
        double east = metres / (MetresPerDegree * Math.Cos(point.Latitude * Math.PI / 180));
        return new FloodBox(point.Latitude - north, point.Longitude - east, point.Latitude + north, point.Longitude + east);
    }

    /// <summary>A coordinate as a service wants it: six decimals, a point for the decimal mark.</summary>
    public static string Text(double degrees) => degrees.ToString("0.000000", CultureInfo.InvariantCulture);
}

/// <summary>The highest band seen so far, and which water it was about. Rivers and the sea are read first, and keep a tie.</summary>
internal sealed class WorstBand
{
    private static readonly Dictionary<string, FloodRisk> Bands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Very low"] = FloodRisk.VeryLow,
        ["Low"] = FloodRisk.Low,
        ["Medium"] = FloodRisk.Medium,
        ["High"] = FloodRisk.High,
    };

    private FloodRisk _band = FloodRisk.Unknown;
    private FloodWater _from = FloodWater.RiversOrSea;
    private bool _unread;

    /// <summary>Takes a band as a map words it. Anything that is not a band ("Unavailable") is remembered as not read.</summary>
    internal void See(string? band, FloodWater from)
    {
        if (band is not null && Bands.TryGetValue(band.Trim(), out FloodRisk known))
        {
            See(known, from);
        }
        else
        {
            _unread = true;
        }
    }

    /// <summary>Takes a band.</summary>
    internal void See(FloodRisk band, FloodWater from)
    {
        if (band > _band)
        {
            _band = band;
            _from = from;
        }
    }

    /// <summary>
    /// What it comes to. A band seen is the answer. Nothing seen at all is very low: every map here
    /// covers its whole nation, so ground outside every polygon is below the lowest band drawn. Only
    /// words that were not a band is no answer, and is never passed off as a low risk.
    /// </summary>
    internal Result<FloodFinding> Finding(string source)
    {
        if (_band != FloodRisk.Unknown)
        {
            return Result.Success(new FloodFinding(_band, _band == FloodRisk.VeryLow ? FloodWater.NotSaid : _from, source));
        }

        return _unread
            ? Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable())
            : Result.Success(new FloodFinding(FloodRisk.VeryLow, FloodWater.NotSaid, source));
    }
}

/// <summary>
/// England: the Environment Agency's national flood risk assessment (NaFRA2), read through the
/// feature-info request of its published map service. Two layers, rivers and the sea and then
/// surface water, each one GET. The service answers "too many requests" after a handful in a few
/// seconds, so requests take turns with a gap between them; a lookup that cannot get a turn in time
/// is a failure, which the plan answers by asking the household to check for itself.
/// </summary>
public sealed class EnvironmentAgencyFloodMap : INationalFloodMap
{
    /// <summary>Whose map, as a sentence names it.</summary>
    public const string Source = "the Environment Agency's flood map";

    private static readonly (string Path, string Layer, FloodWater From)[] Layers =
    [
        ("nafra2-risk-of-flooding-from-rivers-and-sea", "rofrs_4band", FloodWater.RiversOrSea),
        ("nafra2-risk-of-flooding-from-surface-water", "rofsw", FloodWater.SurfaceWater),
    ];

    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _gap;
    private readonly TimeSpan _longestWait;
    private readonly Lock _turns = new();
    private DateTimeOffset _nextTurn = DateTimeOffset.MinValue;

    /// <summary>Creates the adapter over a typed client whose base address is fixed by options: 400 milliseconds between requests, and no more than four seconds waiting for a turn.</summary>
    public EnvironmentAgencyFloodMap(HttpClient http, TimeProvider clock)
        : this(http, clock, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(4))
    {
    }

    internal EnvironmentAgencyFloodMap(HttpClient http, TimeProvider clock, TimeSpan gap, TimeSpan longestWait)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(clock);
        _http = http;
        _clock = clock;
        _gap = gap;
        _longestWait = longestWait;
    }

    /// <inheritdoc />
    public Nation Nation => Nation.England;

    /// <inheritdoc />
    public async Task<Result<FloodFinding>> AtAsync(Coordinates point, CancellationToken cancellationToken)
    {
        // The map is drawn a metre to the pixel over a hundred metres, and the buffer is the reach in pixels.
        FloodBox box = FloodBox.Around(point, 50);
        string bbox = string.Join(',', FloodBox.Text(box.South), FloodBox.Text(box.West), FloodBox.Text(box.North), FloodBox.Text(box.East));
        string buffer = FloodBox.ReachMetres.ToString("0", CultureInfo.InvariantCulture);
        WorstBand worst = new();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach ((string path, string layer, FloodWater from) in Layers)
            {
                if (Turn() is not { } wait)
                {
                    return Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable());
                }

                await Task.Delay(wait, _clock, cancellationToken);
                Uri uri = new($"spatialdata/{path}/wms?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetFeatureInfo&LAYERS={layer}&QUERY_LAYERS={layer}&STYLES=&CRS=EPSG:4326&BBOX={bbox}&WIDTH=101&HEIGHT=101&I=50&J=50&INFO_FORMAT=application/json&FEATURE_COUNT=50&FORMAT=image/png&BUFFER={buffer}&PROPERTYNAME=risk_band", UriKind.Relative);
                FloodFeatures? features = await FloodReader.ReadAsync(_http, uri, cancellationToken);
                if (features?.Features is not { } found)
                {
                    return Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable());
                }

                foreach (FloodFeature feature in found)
                {
                    worst.See(feature.Properties?.RiskBand, from);
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable());
        }

        return worst.Finding(Source);
    }

    // The next free turn: how long to wait for it, or null when the queue is already longer than anyone should wait.
    private TimeSpan? Turn()
    {
        lock (_turns)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            DateTimeOffset start = _nextTurn > now ? _nextTurn : now;
            if (start - now > _longestWait)
            {
                return null;
            }

            _nextTurn = start + _gap;
            return start - now;
        }
    }
}

/// <summary>
/// Wales: Natural Resources Wales's flood risk assessment (FRAW), read from DataMapWales's feature
/// service. Three layers, rivers, the sea, and surface water with small watercourses, asked together.
/// It draws high, medium and low and nothing below, so ground in none of them is very low.
/// </summary>
public sealed class NaturalResourcesWalesFloodMap : INationalFloodMap
{
    /// <summary>Whose map, as a sentence names it.</summary>
    public const string Source = "Natural Resources Wales's flood map";

    private static readonly (string Layer, string Geometry, FloodWater From)[] Layers =
    [
        ("NRW_FLOOD_RISK_FROM_RIVERS", "geom", FloodWater.RiversOrSea),
        ("NRW_FLOOD_RISK_FROM_SEA", "geom", FloodWater.RiversOrSea),
        ("NRW_FLOOD_RISK_FROM_SURFACE_WATER_SMALL_WATERCOURSES", "fme_geometry", FloodWater.SurfaceWater),
    ];

    private readonly HttpClient _http;

    /// <summary>Creates the adapter over a typed client whose base address is fixed by options.</summary>
    public NaturalResourcesWalesFloodMap(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc />
    public Nation Nation => Nation.Wales;

    /// <inheritdoc />
    public async Task<Result<FloodFinding>> AtAsync(Coordinates point, CancellationToken cancellationToken)
    {
        FloodBox box = FloodBox.Around(point);
        string corners = string.Join(',', FloodBox.Text(box.West), FloodBox.Text(box.South), FloodBox.Text(box.East), FloodBox.Text(box.North));
        WorstBand worst = new();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            FloodFeatures?[] answers = await Task.WhenAll(Layers.Select(layer =>
            {
                string filter = Uri.EscapeDataString($"BBOX({layer.Geometry},{corners},'EPSG:4326')");
                return FloodReader.ReadAsync(_http, new Uri($"geoserver/ows?service=WFS&version=2.0.0&request=GetFeature&typeNames=inspire-nrw:{layer.Layer}&outputFormat=application/json&count=200&propertyName=risk&CQL_FILTER={filter}", UriKind.Relative), cancellationToken);
            }));

            for (int i = 0; i < answers.Length; i++)
            {
                if (answers[i]?.Features is not { } found)
                {
                    return Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable());
                }

                foreach (FloodFeature feature in found)
                {
                    worst.See(feature.Properties?.Risk, Layers[i].From);
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable());
        }

        return worst.Finding(Source);
    }
}

/// <summary>
/// Scotland: SEPA's flood maps, read from its open map service. There is no band on a feature here:
/// each likelihood is a layer of its own, for rivers, for surface water and for the coast, and the
/// band is the most likely layer the box touches. The low layers are asked first, because ground in
/// none of them is in none of the others and most of Scotland stops there after three requests; then
/// the medium and the high layers, for the waters that were found.
/// </summary>
public sealed class SepaFloodMap : INationalFloodMap
{
    /// <summary>Whose map, as a sentence names it.</summary>
    public const string Source = "SEPA's flood map";

    // By water: the layer for low, for medium and for high likelihood.
    private static readonly (FloodWater From, int Low, int Medium, int High)[] Waters =
    [
        (FloodWater.RiversOrSea, 2, 1, 0),
        (FloodWater.RiversOrSea, 8, 7, 6),
        (FloodWater.SurfaceWater, 5, 4, 3),
    ];

    private readonly HttpClient _http;

    /// <summary>Creates the adapter over a typed client whose base address is fixed by options.</summary>
    public SepaFloodMap(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc />
    public Nation Nation => Nation.Scotland;

    /// <inheritdoc />
    public async Task<Result<FloodFinding>> AtAsync(Coordinates point, CancellationToken cancellationToken)
    {
        FloodBox box = FloodBox.Around(point);
        string envelope = string.Join(',', FloodBox.Text(box.West), FloodBox.Text(box.South), FloodBox.Text(box.East), FloodBox.Text(box.North));
        WorstBand worst = new();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach ((FloodWater from, int[] layers) in Waters.Select(static water => (water.From, new[] { water.Low, water.Medium, water.High })))
            {
                // Low, then medium, then high, stopping at the first the box does not touch.
                FloodRisk band = FloodRisk.Low;
                foreach (int layer in layers)
                {
                    Uri uri = new($"server/rest/services/Open/Flood_Maps/MapServer/{layer.ToString(CultureInfo.InvariantCulture)}/query?f=json&geometry={envelope}&geometryType=esriGeometryEnvelope&inSR=4326&spatialRel=esriSpatialRelIntersects&returnCountOnly=true", UriKind.Relative);
                    using HttpResponseMessage response = await _http.GetAsync(uri, cancellationToken);
                    FeatureCount? counted = response.IsSuccessStatusCode
                        ? await response.Content.ReadFromJsonAsync(UpstreamJson.Default.FeatureCount, cancellationToken)
                        : null;
                    if (counted?.Count is not { } count)
                    {
                        // The service answers a refusal as a success with an error inside; neither is a count.
                        return Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable());
                    }

                    if (count == 0)
                    {
                        break;
                    }

                    worst.See(band++, from);
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable());
        }

        return worst.Finding(Source);
    }
}

/// <summary>
/// The flood map for whichever nation a point is in. Northern Ireland publishes a map to look at but
/// no service to ask and no licence to reuse, so it has none here, and neither does a place whose
/// nation is not known: both are a failure, which the plan answers by sending the household to look.
/// </summary>
public sealed class NationalFloodMaps : IFloodRiskProvider
{
    private readonly Dictionary<Nation, INationalFloodMap> _maps;

    /// <summary>Creates the provider over the maps there are.</summary>
    public NationalFloodMaps(IEnumerable<INationalFloodMap> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = maps.ToDictionary(static map => map.Nation);
    }

    /// <inheritdoc />
    public Task<Result<FloodFinding>> AtAsync(Coordinates point, Nation nation, CancellationToken cancellationToken) =>
        _maps.TryGetValue(nation, out INationalFloodMap? map)
            ? map.AtAsync(point, cancellationToken)
            : Task.FromResult(Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable()));
}

/// <summary>One GET that answers a collection of features, or null for anything else.</summary>
internal static class FloodReader
{
    internal static async Task<FloodFeatures?> ReadAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync(UpstreamJson.Default.FloodFeatures, cancellationToken)
            : null;
    }
}

/// <summary>The slice of a GeoJSON feature collection that is used.</summary>
public sealed record FloodFeatures([property: JsonPropertyName("features")] IReadOnlyList<FloodFeature>? Features);

/// <summary>One feature: only its properties.</summary>
public sealed record FloodFeature([property: JsonPropertyName("properties")] FloodProperties? Properties);

/// <summary>The band, as England names it and as Wales does.</summary>
public sealed record FloodProperties(
    [property: JsonPropertyName("risk_band")] string? RiskBand,
    [property: JsonPropertyName("risk")] string? Risk);

/// <summary>An ArcGIS count: how many features a query touched. Absent when the service answered with an error.</summary>
public sealed record FeatureCount([property: JsonPropertyName("count")] int? Count);
