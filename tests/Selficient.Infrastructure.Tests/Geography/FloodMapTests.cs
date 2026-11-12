using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Selficient.Application.Geography;
using Selficient.Domain.Households;
using Selficient.Domain.Primitives;
using Selficient.Infrastructure.Geography;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Infrastructure.Tests.Geography;

/// <summary>
/// The three flood maps against stand-ins that answer as the real services were seen to on
/// 12 November 2026, and the rule they share: the highest band near the point, and never a low
/// risk made out of a failure.
/// </summary>
public sealed class FloodMapTests : IDisposable
{
    private static readonly Coordinates York = new(53.9577, -1.0850);
    private static readonly Coordinates Llanrwst = new(53.137425, -3.794465);
    private static readonly Coordinates Perth = new(56.3960, -3.4245);

    private readonly WireMockServer _server = WireMockServer.Start();

    private HttpClient Client(int timeoutSeconds = 30) => new() { BaseAddress = new Uri(_server.Url! + "/"), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

    private EnvironmentAgencyFloodMap England() => new(Client(), TimeProvider.System, TimeSpan.Zero, TimeSpan.FromSeconds(4));

    private static string Features(string property, params string[] bands) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(',', bands.Select(band => $"{{\"type\":\"Feature\",\"properties\":{{\"{property}\":\"{band}\"}}}}")) + "]}";

    private void Answer(string path, string? param, string? value, string body, int status = 200)
    {
        IRequestBuilder request = Request.Create().WithPath(path).UsingGet();
        if (param is not null)
        {
            request = request.WithParam(param, value!);
        }

        _server.Given(request).RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));
    }

    private const string RiversPath = "/spatialdata/nafra2-risk-of-flooding-from-rivers-and-sea/wms";
    private const string SurfacePath = "/spatialdata/nafra2-risk-of-flooding-from-surface-water/wms";

    [Fact]
    public async Task England_is_the_highest_band_of_the_rivers_and_the_surface_water_within_the_reach()
    {
        Answer(RiversPath, null, null, Features("risk_band", "Low", "Medium", "High", "Very low"));
        Answer(SurfacePath, null, null, Features("risk_band", "Low", "Medium"));

        Result<FloodFinding> result = await England().AtAsync(York, CancellationToken.None);

        result.Value.ShouldBe(new FloodFinding(FloodRisk.High, FloodWater.RiversOrSea, "the Environment Agency's flood map"));
        IDictionary<string, WireMock.Types.WireMockList<string>> asked = _server.LogEntries.First(entry => entry.RequestMessage!.Path == RiversPath).RequestMessage!.Query!;
        asked["REQUEST"].Single().ShouldBe("GetFeatureInfo");
        asked["LAYERS"].Single().ShouldBe("rofrs_4band");
        asked["CRS"].Single().ShouldBe("EPSG:4326");
        string.Join(',', asked["BBOX"]).ShouldBe("53.957251,-1.085763,53.958149,-1.084237", "fifty metres on every side, latitude first as the service's version of the standard wants it");
        asked["BUFFER"].Single().ShouldBe("25", "the reach, at a metre to the pixel");
        asked["INFO_FORMAT"].Single().ShouldBe("application/json");
        _server.LogEntries.Single(entry => entry.RequestMessage!.Path == SurfacePath).RequestMessage!.Query!["LAYERS"].Single().ShouldBe("rofsw");
    }

    [Fact]
    public async Task England_names_surface_water_when_that_is_the_higher_band_and_keeps_rivers_on_a_tie()
    {
        Answer(RiversPath, null, null, Features("risk_band", "Very Low"));
        Answer(SurfacePath, null, null, Features("risk_band", "medium"));

        (await England().AtAsync(York, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.Medium, FloodWater.SurfaceWater, EnvironmentAgencyFloodMap.Source));

        _server.Reset();
        Answer(RiversPath, null, null, Features("risk_band", "Low"));
        Answer(SurfacePath, null, null, Features("risk_band", "Low"));

        (await England().AtAsync(York, CancellationToken.None)).Value.From.ShouldBe(FloodWater.RiversOrSea);
    }

    [Fact]
    public async Task Ground_in_no_polygon_is_very_low_and_a_band_that_is_not_a_band_is_no_answer()
    {
        Answer(RiversPath, null, null, Features("risk_band"));
        Answer(SurfacePath, null, null, Features("risk_band"));
        (await England().AtAsync(York, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.VeryLow, FloodWater.NotSaid, EnvironmentAgencyFloodMap.Source));

        _server.Reset();
        Answer(RiversPath, null, null, Features("risk_band", "Very low"));
        Answer(SurfacePath, null, null, Features("risk_band"));
        (await England().AtAsync(York, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.VeryLow, FloodWater.NotSaid, EnvironmentAgencyFloodMap.Source));

        // "Unavailable" is the agency saying it has no figure there. That is never passed off as very low.
        _server.Reset();
        Answer(RiversPath, null, null, Features("risk_band", "Unavailable"));
        Answer(SurfacePath, null, null, Features("risk_band"));
        (await England().AtAsync(York, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");

        // But a band beside it is still a band.
        _server.Reset();
        Answer(RiversPath, null, null, Features("risk_band", "Unavailable"));
        Answer(SurfacePath, null, null, Features("risk_band", "Low"));
        (await England().AtAsync(York, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.Low, FloodWater.SurfaceWater, EnvironmentAgencyFloodMap.Source));

        // A feature with no properties, or no band among them, is not read either.
        _server.Reset();
        Answer(RiversPath, null, null, """{"features":[{"properties":null},{"properties":{}}]}""");
        Answer(SurfacePath, null, null, Features("risk_band"));
        (await England().AtAsync(York, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
    }

    [Theory]
    [InlineData(429, "<html><body><p>You are making too many requests. Please try again later.</p></body></html>")]
    [InlineData(503, "down")]
    [InlineData(200, "garbage")]
    [InlineData(200, "null")]
    [InlineData(200, """{"features":null}""")]
    public async Task Englands_failures_are_faults_and_never_a_band(int status, string body)
    {
        Answer(RiversPath, null, null, Features("risk_band", "Low"));
        Answer(SurfacePath, null, null, body, status);

        Result<FloodFinding> result = await England().AtAsync(York, CancellationToken.None);

        result.Error.Code.ShouldBe("lookup.unavailable", customMessage: "a band from one layer is not the answer when the other could not be read");
    }

    [Fact]
    public async Task England_takes_turns_and_gives_up_when_the_queue_is_longer_than_anyone_should_wait()
    {
        Answer(RiversPath, null, null, Features("risk_band"));
        Answer(SurfacePath, null, null, Features("risk_band"));
        FakeTimeProvider clock = new();

        // A gap of ten seconds and no patience at all: the first request goes, the second cannot have a turn.
        EnvironmentAgencyFloodMap impatient = new(Client(), clock, TimeSpan.FromSeconds(10), TimeSpan.Zero);
        (await impatient.AtAsync(York, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        _server.LogEntries.Count.ShouldBe(1, "the second layer was never asked for");

        // With patience, the second waits its gap and both are asked.
        EnvironmentAgencyFloodMap patient = new(Client(), clock, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        Task<Result<FloodFinding>> waiting = patient.AtAsync(York, CancellationToken.None);
        for (int i = 0; i < 200 && !waiting.IsCompleted; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        (await waiting).Value.Band.ShouldBe(FloodRisk.VeryLow);
        new EnvironmentAgencyFloodMap(Client(), TimeProvider.System).Nation.ShouldBe(Nation.England);
    }

    [Fact]
    public async Task Wales_asks_three_layers_for_the_box_and_takes_the_highest()
    {
        Answer("/geoserver/ows", "typeNames", "inspire-nrw:NRW_FLOOD_RISK_FROM_RIVERS", Features("risk", "Low", "Medium"));
        Answer("/geoserver/ows", "typeNames", "inspire-nrw:NRW_FLOOD_RISK_FROM_SEA", Features("risk"));
        Answer("/geoserver/ows", "typeNames", "inspire-nrw:NRW_FLOOD_RISK_FROM_SURFACE_WATER_SMALL_WATERCOURSES", Features("risk", "Medium", "Low", "High"));
        NaturalResourcesWalesFloodMap wales = new(Client());

        Result<FloodFinding> result = await wales.AtAsync(Llanrwst, CancellationToken.None);

        result.Value.ShouldBe(new FloodFinding(FloodRisk.High, FloodWater.SurfaceWater, "Natural Resources Wales's flood map"));
        wales.Nation.ShouldBe(Nation.Wales);
        _server.LogEntries.Count.ShouldBe(3);
        IDictionary<string, WireMock.Types.WireMockList<string>> rivers = _server.LogEntries.Single(entry => entry.RequestMessage!.Query!["typeNames"].Single().EndsWith("RIVERS", StringComparison.Ordinal)).RequestMessage!.Query!;
        rivers["CQL_FILTER"].Single().ShouldBe("BBOX(geom,-3.794839,53.137200,-3.794091,53.137650,'EPSG:4326')", "twenty-five metres on every side, longitude first");
        rivers["propertyName"].Single().ShouldBe("risk");
        _server.LogEntries.Single(entry => entry.RequestMessage!.Query!["typeNames"].Single().Contains("SURFACE", StringComparison.Ordinal)).RequestMessage!.Query!["CQL_FILTER"].Single().ShouldStartWith("BBOX(fme_geometry,");
    }

    [Fact]
    public async Task Wales_in_no_polygon_is_very_low_and_a_layer_that_fails_is_no_answer()
    {
        Answer("/geoserver/ows", null, null, Features("risk"));
        (await new NaturalResourcesWalesFloodMap(Client()).AtAsync(Llanrwst, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.VeryLow, FloodWater.NotSaid, NaturalResourcesWalesFloodMap.Source));

        _server.Reset();
        Answer("/geoserver/ows", "typeNames", "inspire-nrw:NRW_FLOOD_RISK_FROM_RIVERS", Features("risk", "High"));
        Answer("/geoserver/ows", "typeNames", "inspire-nrw:NRW_FLOOD_RISK_FROM_SEA", "down", 503);
        Answer("/geoserver/ows", "typeNames", "inspire-nrw:NRW_FLOOD_RISK_FROM_SURFACE_WATER_SMALL_WATERCOURSES", Features("risk"));
        (await new NaturalResourcesWalesFloodMap(Client()).AtAsync(Llanrwst, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");

        // A feature with nothing in it is not a band, and is not passed off as very low.
        _server.Reset();
        Answer("/geoserver/ows", null, null, """{"features":[{"properties":null}]}""");
        (await new NaturalResourcesWalesFloodMap(Client()).AtAsync(Llanrwst, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");

        _server.Reset();
        Answer("/geoserver/ows", null, null, "<ows:ExceptionReport/>");
        (await new NaturalResourcesWalesFloodMap(Client()).AtAsync(Llanrwst, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
    }

    private void Scotland(int layer, int count) =>
        Answer($"/server/rest/services/Open/Flood_Maps/MapServer/{layer}/query", null, null, $"{{\"count\":{count}}}");

    [Fact]
    public async Task Scotland_reads_the_most_likely_layer_the_box_touches_low_first()
    {
        // The river: low and medium, not high. The coast: nothing. Surface water: low, medium and high.
        Scotland(2, 1);
        Scotland(1, 3);
        Scotland(0, 0);
        Scotland(8, 0);
        Scotland(5, 18);
        Scotland(4, 13);
        Scotland(3, 2);
        SepaFloodMap scotland = new(Client());

        Result<FloodFinding> result = await scotland.AtAsync(Perth, CancellationToken.None);

        result.Value.ShouldBe(new FloodFinding(FloodRisk.High, FloodWater.SurfaceWater, "SEPA's flood map"));
        scotland.Nation.ShouldBe(Nation.Scotland);
        _server.LogEntries.Select(entry => entry.RequestMessage!.Path).ShouldBe(new[] { 2, 1, 0, 8, 5, 4, 3 }.Select(layer => $"/server/rest/services/Open/Flood_Maps/MapServer/{layer}/query"), "the coast's medium and high layers are never asked for once its low layer is empty");
        IDictionary<string, WireMock.Types.WireMockList<string>> asked = _server.LogEntries[0].RequestMessage!.Query!;
        string.Join(',', asked["geometry"]).ShouldBe("-3.424906,56.395775,-3.424094,56.396225");
        asked["geometryType"].Single().ShouldBe("esriGeometryEnvelope");
        asked["inSR"].Single().ShouldBe("4326");
        asked["returnCountOnly"].Single().ShouldBe("true");
    }

    [Fact]
    public async Task Scotland_in_no_layer_is_very_low_after_three_requests_and_a_river_keeps_a_tie()
    {
        Scotland(2, 0);
        Scotland(8, 0);
        Scotland(5, 0);
        (await new SepaFloodMap(Client()).AtAsync(Perth, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.VeryLow, FloodWater.NotSaid, SepaFloodMap.Source));
        _server.LogEntries.Count.ShouldBe(3);

        _server.Reset();
        Scotland(2, 1);
        Scotland(1, 0);
        Scotland(8, 0);
        Scotland(5, 4);
        Scotland(4, 0);
        (await new SepaFloodMap(Client()).AtAsync(Perth, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.Low, FloodWater.RiversOrSea, SepaFloodMap.Source));
    }

    [Theory]
    [InlineData(200, """{"error":{"code":499,"message":"Token Required"}}""")]
    [InlineData(500, "down")]
    [InlineData(200, "garbage")]
    [InlineData(200, "null")]
    public async Task Scotlands_failures_are_faults(int status, string body)
    {
        Scotland(2, 1);
        Answer("/server/rest/services/Open/Flood_Maps/MapServer/1/query", null, null, body, status);

        (await new SepaFloodMap(Client()).AtAsync(Perth, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
    }

    [Fact]
    public async Task A_timeout_is_a_fault_and_a_caller_who_has_gone_is_not_answered()
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)).WithBody("{}"));
        using CancellationTokenSource gone = new();
        await gone.CancelAsync();

        (await new EnvironmentAgencyFloodMap(Client(1), TimeProvider.System, TimeSpan.Zero, TimeSpan.FromSeconds(4)).AtAsync(York, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await new NaturalResourcesWalesFloodMap(Client(1)).AtAsync(Llanrwst, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await new SepaFloodMap(Client(1)).AtAsync(Perth, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        await Should.ThrowAsync<OperationCanceledException>(() => England().AtAsync(York, gone.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new NaturalResourcesWalesFloodMap(Client()).AtAsync(Llanrwst, gone.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new SepaFloodMap(Client()).AtAsync(Perth, gone.Token));
    }

    [Fact]
    public async Task Each_nation_is_asked_of_its_own_map_and_northern_ireland_has_none()
    {
        INationalFloodMap england = Substitute.For<INationalFloodMap>();
        england.Nation.Returns(Nation.England);
        england.AtAsync(York, Arg.Any<CancellationToken>()).Returns(Result.Success(new FloodFinding(FloodRisk.Medium, FloodWater.RiversOrSea, "England")));
        INationalFloodMap scotland = Substitute.For<INationalFloodMap>();
        scotland.Nation.Returns(Nation.Scotland);
        NationalFloodMaps maps = new([england, scotland]);

        (await maps.AtAsync(York, Nation.England, CancellationToken.None)).Value.Source.ShouldBe("England");
        (await maps.AtAsync(York, Nation.NorthernIreland, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await maps.AtAsync(York, Nation.Unknown, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await maps.AtAsync(York, Nation.Wales, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable", customMessage: "a nation whose map is not registered is not answered from another's");
        await scotland.DidNotReceiveWithAnyArgs().AtAsync(default, default);
    }

    [Fact]
    public void The_box_reaches_twenty_five_metres_and_narrows_its_degrees_of_longitude_with_latitude()
    {
        FloodBox box = FloodBox.Around(new Coordinates(60, -1));

        FloodBox.ReachMetres.ShouldBe(25);
        (box.North - box.South).ShouldBe(2 * 25 / 111_320d, 1e-9);
        (box.East - box.West).ShouldBe(2 * 25 / (111_320d * 0.5), 1e-9, "at sixty degrees north a degree of longitude is half as long");
        FloodBox.Text(-3.2).ShouldBe("-3.200000");
    }

    [Fact]
    public async Task What_a_map_said_is_kept_for_a_month_and_a_map_that_did_not_answer_for_ten_minutes()
    {
        IFloodRiskProvider inner = Substitute.For<IFloodRiskProvider>();
        FloodFinding high = new(FloodRisk.High, FloodWater.SurfaceWater, "the Environment Agency's flood map");
        inner.AtAsync(Arg.Any<Coordinates>(), Arg.Any<Nation>(), Arg.Any<CancellationToken>()).Returns(Result.Success(high));
        CachedFloodRiskProvider cached = new(inner, Cache());

        (await cached.AtAsync(York, Nation.England, CancellationToken.None)).Value.ShouldBe(high);
        (await cached.AtAsync(new Coordinates(53.95771, -1.08502), Nation.England, CancellationToken.None)).Value.ShouldBe(high, "eleven metres away is the same entry");
        await inner.Received(1).AtAsync(Arg.Any<Coordinates>(), Arg.Any<Nation>(), Arg.Any<CancellationToken>());
        await cached.AtAsync(York, Nation.Scotland, CancellationToken.None);
        await inner.Received(2).AtAsync(Arg.Any<Coordinates>(), Arg.Any<Nation>(), Arg.Any<CancellationToken>());

        // A failure is remembered as a failure, so a page that redraws does not wait on the service again each time.
        IFloodRiskProvider failing = Substitute.For<IFloodRiskProvider>();
        failing.AtAsync(Arg.Any<Coordinates>(), Arg.Any<Nation>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable()));
        CachedFloodRiskProvider remembering = new(failing, Cache());
        (await remembering.AtAsync(York, Nation.England, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await remembering.AtAsync(York, Nation.England, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        await failing.Received(1).AtAsync(Arg.Any<Coordinates>(), Arg.Any<Nation>(), Arg.Any<CancellationToken>());

        Should.Throw<ArgumentNullException>(() => new CachedFloodRiskProvider(null!, Cache()));
        Should.Throw<ArgumentNullException>(() => new CachedFloodRiskProvider(inner, null!));
        Should.Throw<ArgumentNullException>(() => new NationalFloodMaps(null!));
        Should.Throw<ArgumentNullException>(() => new EnvironmentAgencyFloodMap(null!, TimeProvider.System));
        Should.Throw<ArgumentNullException>(() => new EnvironmentAgencyFloodMap(Client(), null!));
        Should.Throw<ArgumentNullException>(() => new NaturalResourcesWalesFloodMap(null!));
        Should.Throw<ArgumentNullException>(() => new SepaFloodMap(null!));
    }

    [Fact]
    public async Task The_stand_in_map_gives_every_band_from_a_postcodes_coordinates_and_none_where_there_is_no_map()
    {
        FakeFloodRiskProvider fake = new();

        (await fake.AtAsync(new Coordinates(51.6655, -3.2031), Nation.Wales, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.VeryLow, FloodWater.NotSaid, NaturalResourcesWalesFloodMap.Source));
        (await fake.AtAsync(new Coordinates(54.6, -5.9), Nation.NorthernIreland, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await fake.AtAsync(new Coordinates(60.0, -1.0), Nation.Scotland, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await fake.AtAsync(new Coordinates(52.0, 0.0005), Nation.England, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.Low, FloodWater.SurfaceWater, EnvironmentAgencyFloodMap.Source));
        (await fake.AtAsync(new Coordinates(52.0, 0.0006), Nation.England, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.Medium, FloodWater.RiversOrSea, EnvironmentAgencyFloodMap.Source));
        (await fake.AtAsync(new Coordinates(56.0, 0.0007), Nation.Scotland, CancellationToken.None)).Value.ShouldBe(new FloodFinding(FloodRisk.High, FloodWater.RiversOrSea, SepaFloodMap.Source));
        (await fake.AtAsync(new Coordinates(52.0, 0.0), Nation.England, CancellationToken.None)).Value.Band.ShouldBe(FloodRisk.VeryLow);
    }

    private static HybridCache Cache()
    {
        ServiceCollection services = new();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    public void Dispose() => _server.Dispose();
}
