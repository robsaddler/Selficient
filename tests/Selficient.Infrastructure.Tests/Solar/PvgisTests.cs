using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Selficient.Application.Geography;
using Selficient.Application.Solar;
using Selficient.Domain.Primitives;
using Selficient.Domain.Solar;
using Selficient.Infrastructure.Solar;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Infrastructure.Tests.Solar;

public sealed class PvgisTests : IDisposable
{
    private const string RealAnswer = """
        {"inputs":{"location":{"latitude":51.6655,"longitude":-3.2031}},"outputs":{"monthly":{"fixed":[
        {"month":12,"E_d":0.87,"E_m":27.12},{"month":1,"E_d":0.95,"E_m":29.35},{"month":2,"E_m":45.67},{"month":3,"E_m":79.6},
        {"month":4,"E_m":108.6},{"month":5,"E_m":122.41},{"month":6,"E_m":120.59},{"month":7,"E_m":121.84},{"month":8,"E_m":104.49},
        {"month":9,"E_m":85.78},{"month":10,"E_m":54.37},{"month":11,"E_m":35.88}]},"totals":{"fixed":{"E_y":935.68}}}}
        """;

    private static readonly Coordinates Blackwood = new(51.66549, -3.20314);
    private static readonly RoofPitch Pitch = RoofPitch.Create(35).Value;

    private readonly WireMockServer _server = WireMockServer.Start();

    private PvgisYieldProvider Provider(int timeoutSeconds = 30)
    {
        HttpClient client = new() { BaseAddress = new Uri(_server.Url! + "/"), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        return new PvgisYieldProvider(client);
    }

    [Fact]
    public void The_pvgis_url_carries_only_validated_numbers()
    {
        Uri path = PvgisYieldProvider.Path(Blackwood, RoofAspect.SouthWest, Pitch);

        path.ToString().ShouldBe("api/v5_2/PVcalc?lat=51.6655&lon=-3.2031&peakpower=1&loss=14&angle=35&aspect=45&outputformat=json");
        PvgisYieldProvider.Path(Blackwood, RoofAspect.East, RoofPitch.Create(10).Value).ToString().ShouldContain("angle=10&aspect=-90");
    }

    [Fact]
    public async Task Reads_the_twelve_months_by_their_month_field_not_their_order()
    {
        _server.Given(Request.Create().WithPath("/api/v5_2/PVcalc").WithParam("lat", "51.6655").WithParam("aspect", "45").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(RealAnswer));

        Result<SolarYield> result = await Provider().YieldAsync(Blackwood, RoofAspect.SouthWest, Pitch, CancellationToken.None);

        result.Value.MonthlyKwhPerKwp[0].ShouldBe(29.35m);
        result.Value.MonthlyKwhPerKwp[11].ShouldBe(27.12m);
        result.Value.AnnualKwhPerKwp.ShouldBe(935.7m);
    }

    [Theory]
    [InlineData(503, "down")]
    [InlineData(200, "garbage")]
    [InlineData(200, "null")]
    [InlineData(200, """{"outputs":null}""")]
    [InlineData(200, """{"outputs":{"monthly":null}}""")]
    [InlineData(200, """{"outputs":{"monthly":{"fixed":[{"month":1,"E_m":10}]}}}""")]
    [InlineData(200, """{"outputs":{"monthly":{"fixed":[{"month":13,"E_m":10},{"month":1,"E_m":1},{"month":2,"E_m":1},{"month":3,"E_m":1},{"month":4,"E_m":1},{"month":5,"E_m":1},{"month":6,"E_m":1},{"month":7,"E_m":1},{"month":8,"E_m":1},{"month":9,"E_m":1},{"month":10,"E_m":1},{"month":11,"E_m":1}]}}}""")]
    [InlineData(200, """{"outputs":{"monthly":{"fixed":[{"month":1},{"month":2,"E_m":1},{"month":3,"E_m":1},{"month":4,"E_m":1},{"month":5,"E_m":1},{"month":6,"E_m":1},{"month":7,"E_m":1},{"month":8,"E_m":1},{"month":9,"E_m":1},{"month":10,"E_m":1},{"month":11,"E_m":1},{"month":12,"E_m":1}]}}}""")]
    [InlineData(200, """{"outputs":{"monthly":{"fixed":[{"month":1,"E_m":1},{"month":2,"E_m":1},{"month":3,"E_m":1},{"month":4,"E_m":1},{"month":5,"E_m":1},{"month":6,"E_m":1},{"month":7,"E_m":1},{"month":8,"E_m":1},{"month":9,"E_m":1},{"month":10,"E_m":1},{"month":11,"E_m":1},{"month":12,"E_m":1}]}}}""")]
    public async Task Pvgis_failures_are_a_sentence_not_a_stack_trace(int status, string body)
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

        Result<SolarYield> result = await Provider().YieldAsync(Blackwood, RoofAspect.South, Pitch, CancellationToken.None);

        result.Error.Code.ShouldBe("solar.yield-unavailable");
        result.Error.Message.ShouldBe("We couldn't get the sunshine figures for your roof just now. Wait a moment and try again.");
    }

    [Fact]
    public async Task A_timeout_is_a_fault()
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)).WithBody("{}"));

        Result<SolarYield> result = await Provider(timeoutSeconds: 1).YieldAsync(Blackwood, RoofAspect.South, Pitch, CancellationToken.None);

        result.Error.Code.ShouldBe("solar.yield-unavailable");
    }

    [Fact]
    public async Task Yields_are_cached_by_rounded_coordinates_and_failures_are_not()
    {
        ISolarYieldProvider inner = Substitute.For<ISolarYieldProvider>();
        inner.YieldAsync(Arg.Any<Coordinates>(), Arg.Any<RoofAspect>(), Arg.Any<RoofPitch>(), Arg.Any<CancellationToken>())
            .Returns(SolarYield.Create(FakeSolarYieldProvider.SouthWestAt35));
        using ServiceProvider provider = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        CachedSolarYieldProvider cached = new(inner, provider.GetRequiredService<HybridCache>());

        Result<SolarYield> first = await cached.YieldAsync(new Coordinates(51.66549, -3.20314), RoofAspect.SouthWest, Pitch, CancellationToken.None);
        Result<SolarYield> nearby = await cached.YieldAsync(new Coordinates(51.66541, -3.20318), RoofAspect.SouthWest, Pitch, CancellationToken.None);
        Result<SolarYield> otherAspect = await cached.YieldAsync(new Coordinates(51.66549, -3.20314), RoofAspect.South, Pitch, CancellationToken.None);

        first.Value.AnnualKwhPerKwp.ShouldBe(nearby.Value.AnnualKwhPerKwp);
        otherAspect.IsSuccess.ShouldBeTrue();
        await inner.Received(2).YieldAsync(Arg.Any<Coordinates>(), Arg.Any<RoofAspect>(), Arg.Any<RoofPitch>(), Arg.Any<CancellationToken>());
        CachedSolarYieldProvider.Key(new Coordinates(51.66549, -3.20314), RoofAspect.SouthWest, Pitch).ShouldBe("pv:51.665:-3.203:5:35");

        inner.YieldAsync(Arg.Any<Coordinates>(), Arg.Any<RoofAspect>(), Arg.Any<RoofPitch>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SolarYield>(DomainErrors.YieldUnavailable()));
        Result<SolarYield> failed = await cached.YieldAsync(new Coordinates(55, -2), RoofAspect.South, Pitch, CancellationToken.None);
        failed.IsFailure.ShouldBeTrue();
        await inner.Received(4).YieldAsync(Arg.Any<Coordinates>(), Arg.Any<RoofAspect>(), Arg.Any<RoofPitch>(), Arg.Any<CancellationToken>());
        Should.Throw<ArgumentNullException>(() => new CachedSolarYieldProvider(null!, provider.GetRequiredService<HybridCache>()));
        Should.Throw<ArgumentNullException>(() => new CachedSolarYieldProvider(inner, null!));
        Should.Throw<ArgumentNullException>(() => new PvgisYieldProvider(null!));
    }

    [Fact]
    public async Task The_fake_yield_follows_aspect_and_pitch_and_can_be_down()
    {
        FakeSolarYieldProvider fake = new();

        decimal southWest = (await fake.YieldAsync(Blackwood, RoofAspect.SouthWest, Pitch, CancellationToken.None)).Value.AnnualKwhPerKwp;
        decimal south = (await fake.YieldAsync(Blackwood, RoofAspect.South, Pitch, CancellationToken.None)).Value.AnnualKwhPerKwp;
        decimal east = (await fake.YieldAsync(Blackwood, RoofAspect.East, Pitch, CancellationToken.None)).Value.AnnualKwhPerKwp;
        decimal northEast = (await fake.YieldAsync(Blackwood, RoofAspect.NorthEast, Pitch, CancellationToken.None)).Value.AnnualKwhPerKwp;
        decimal north = (await fake.YieldAsync(Blackwood, RoofAspect.North, Pitch, CancellationToken.None)).Value.AnnualKwhPerKwp;
        decimal steep = (await fake.YieldAsync(Blackwood, RoofAspect.SouthWest, RoofPitch.Create(60).Value, CancellationToken.None)).Value.AnnualKwhPerKwp;

        southWest.ShouldBe(935.7m);
        south.ShouldBeGreaterThan(southWest);
        east.ShouldBeLessThan(southWest);
        northEast.ShouldBeLessThan(east);
        north.ShouldBeLessThan(northEast);
        steep.ShouldBeLessThan(southWest);
        (await fake.YieldAsync(FakeSolarYieldProvider.Unreachable, RoofAspect.South, Pitch, CancellationToken.None)).Error.Code.ShouldBe("solar.yield-unavailable");
    }

    public void Dispose() => _server.Dispose();
}
