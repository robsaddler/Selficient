using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Selficient.Application.Geography;
using Selficient.Application.Solar;
using Selficient.Domain.Primitives;
using Selficient.Domain.Solar;
using Selficient.Infrastructure.Composition;
using Selficient.Infrastructure.Solar;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Infrastructure.Tests.Solar;

public sealed class GoogleSolarTests : IDisposable
{
    private const string Answer = """
        {"name":"buildings/abc","solarPotential":{"maxArrayPanelsCount":14,"panelCapacityWatts":430,"roofSegmentStats":[
        {"pitchDegrees":35.2,"azimuthDegrees":44.9,"stats":{"areaMeters2":41.0}},
        {"pitchDegrees":34.8,"azimuthDegrees":224.6,"stats":{"areaMeters2":48.5}},
        {"pitchDegrees":500,"azimuthDegrees":10,"stats":{"areaMeters2":5}},
        {"pitchDegrees":30,"azimuthDegrees":-5,"stats":{"areaMeters2":5}},
        {"pitchDegrees":30,"azimuthDegrees":135,"stats":{"areaMeters2":0}},
        {"pitchDegrees":30,"azimuthDegrees":135,"stats":null},
        {"pitchDegrees":30,"azimuthDegrees":135,"stats":{"areaMeters2":9.2}}]}}
        """;

    private static readonly Coordinates Blackwood = new(51.66549, -3.20314);
    private readonly WireMockServer _server = WireMockServer.Start();

    private GoogleSolarRoofInsights Provider(int timeoutSeconds = 30)
    {
        HttpClient client = new() { BaseAddress = new Uri(_server.Url! + "/"), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        return new GoogleSolarRoofInsights(client);
    }

    [Fact]
    public async Task Reads_the_segments_largest_first_and_drops_the_impossible_ones()
    {
        _server.Given(Request.Create().WithPath("/v1/buildingInsights:findClosest").WithParam("location.latitude", "51.66549").WithParam("requiredQuality", "MEDIUM").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Answer));

        Result<RoofInsights> result = await Provider().DescribeAsync(Blackwood, CancellationToken.None);

        result.Value.Segments.Count.ShouldBe(3);
        result.Value.Segments[0].Bearing.ShouldBe(224.6);
        result.Value.Segments[0].Aspect.ShouldBe(RoofAspect.SouthWest);
        result.Value.Segments[2].AreaSquareMetres.ShouldBe(9.2);
        result.Value.MaxPanels.ShouldBe(14);
        result.Value.PanelWatts.ShouldBe(430);
        result.Value.Best!.Bearing.ShouldBe(224.6);
        Provider().IsAvailable.ShouldBeTrue();
        GoogleSolarRoofInsights.Path(Blackwood).ToString().ShouldNotContain("key");
    }

    [Theory]
    [InlineData(403, "denied")]
    [InlineData(200, "garbage")]
    [InlineData(200, "null")]
    [InlineData(200, """{"solarPotential":null}""")]
    [InlineData(200, """{"solarPotential":{"roofSegmentStats":[]}}""")]
    [InlineData(200, """{"solarPotential":{"roofSegmentStats":[{"pitchDegrees":30,"azimuthDegrees":400,"stats":{"areaMeters2":5}}]}}""")]
    public async Task Hostile_roof_data_is_ignored(int status, string body)
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

        Result<RoofInsights> result = await Provider().DescribeAsync(Blackwood, CancellationToken.None);

        result.Error.Code.ShouldBe("lookup.unavailable");
    }

    [Fact]
    public async Task Odd_panel_figures_fall_back_and_a_timeout_is_a_fault()
    {
        _server.Given(Request.Create().WithPath("/v1/buildingInsights:findClosest").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"solarPotential":{"maxArrayPanelsCount":5000,"panelCapacityWatts":5,"roofSegmentStats":[{"pitchDegrees":30,"azimuthDegrees":180,"stats":{"areaMeters2":20}}]}}"""));

        Result<RoofInsights> result = await Provider().DescribeAsync(Blackwood, CancellationToken.None);
        result.Value.MaxPanels.ShouldBe(0);
        result.Value.PanelWatts.ShouldBe(400);

        _server.Reset();
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)).WithBody("{}"));
        (await Provider(timeoutSeconds: 1).DescribeAsync(Blackwood, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        Should.Throw<ArgumentNullException>(() => new GoogleSolarRoofInsights(null!));
    }

    [Fact]
    public async Task More_than_twenty_segments_are_cut_at_twenty()
    {
        string segments = string.Join(",", Enumerable.Range(0, 30).Select(i => "{\"pitchDegrees\":30,\"azimuthDegrees\":" + (i * 10) + ",\"stats\":{\"areaMeters2\":" + (i + 1) + "}}"));
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
            .WithBody("{\"solarPotential\":{\"roofSegmentStats\":[" + segments + "]}}"));

        Result<RoofInsights> result = await Provider().DescribeAsync(Blackwood, CancellationToken.None);

        result.Value.Segments.Count.ShouldBe(GoogleSolarRoofInsights.MaxSegments);
    }

    [Fact]
    public async Task The_fake_describes_robs_roof_and_can_be_down()
    {
        FakeRoofInsights fake = new();

        Result<RoofInsights> robs = await fake.DescribeAsync(Blackwood, CancellationToken.None);

        fake.IsAvailable.ShouldBeTrue();
        robs.Value.Best!.Aspect.ShouldBe(RoofAspect.SouthWest);
        robs.Value.MaxPanels.ShouldBe(14);
        (await fake.DescribeAsync(FakeSolarYieldProvider.Unreachable, CancellationToken.None)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Google_is_only_wired_with_a_key_and_the_key_travels_as_a_header()
    {
        ServiceCollection without = new();
        without.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        without.AddSelficientInfrastructure(Config(), Environment("Development"));
        using ServiceProvider noKey = without.BuildServiceProvider();

        ServiceCollection with = new();
        with.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        with.AddSelficientInfrastructure(Config(("Google:SolarApiKey", "AIzaSyTESTKEY-0123456789abcdefghijk")), Environment("Development"));
        using ServiceProvider withKey = with.BuildServiceProvider();

        noKey.GetRequiredService<IRoofInsightsProvider>().ShouldBeOfType<NoRoofInsights>();
        noKey.GetRequiredService<ISolarYieldProvider>().ShouldBeOfType<CachedSolarYieldProvider>();
        noKey.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PvgisYieldProvider)).BaseAddress.ShouldBe(new Uri("https://re.jrc.ec.europa.eu/"));
        withKey.GetRequiredService<IRoofInsightsProvider>().ShouldBeOfType<GoogleSolarRoofInsights>();
        HttpClient google = withKey.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleSolarRoofInsights));
        google.BaseAddress.ShouldBe(new Uri("https://solar.googleapis.com/"));
        google.DefaultRequestHeaders.GetValues("X-Goog-Api-Key").ShouldBe(["AIzaSyTESTKEY-0123456789abcdefghijk"]);
        withKey.GetRequiredService<IOptions<GoogleOptions>>().Value.HasSolarKey.ShouldBeTrue();
        noKey.GetRequiredService<IOptions<GoogleOptions>>().Value.HasSolarKey.ShouldBeFalse();
    }

    [Fact]
    public void A_key_that_does_not_look_like_one_is_refused_at_startup()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSelficientInfrastructure(Config(("Google:SolarApiKey", "short")), Environment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GoogleOptions>>().Value);
    }

    [Fact]
    public void Fakes_cover_the_solar_ports_too()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSelficientInfrastructure(Config(("Upstreams:UseFakes", "true")), Environment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISolarYieldProvider>().ShouldBeOfType<FakeSolarYieldProvider>();
        provider.GetRequiredService<IRoofInsightsProvider>().ShouldBeOfType<FakeRoofInsights>();
    }

    public void Dispose() => _server.Dispose();

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    private static IHostEnvironment Environment(string name)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }
}
