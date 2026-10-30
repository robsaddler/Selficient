using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;
using Selficient.Infrastructure.Geography;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Infrastructure.Tests.Geography;

public sealed class ElevationTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private OpenMeteoElevationProvider Provider(int timeoutSeconds = 30)
    {
        HttpClient client = new() { BaseAddress = new Uri(_server.Url! + "/"), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        return new OpenMeteoElevationProvider(client);
    }

    [Fact]
    public async Task Reads_the_first_elevation_rounded_to_a_decimetre()
    {
        _server.Given(Request.Create().WithPath("/v1/elevation").WithParam("latitude", "51.6655").WithParam("longitude", "-3.2031").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("""{"elevation":[232.37]}"""));

        Result<Elevation> result = await Provider().ElevationAtAsync(new Coordinates(51.66549, -3.20314), CancellationToken.None);

        result.Value.Metres.ShouldBe(232.4m);
    }

    [Theory]
    [InlineData(503, "down")]
    [InlineData(200, "garbage")]
    [InlineData(200, """{"elevation":[]}""")]
    [InlineData(200, """{"elevation":null}""")]
    [InlineData(200, "null")]
    public async Task Failures_are_faults(int status, string body)
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

        Result<Elevation> result = await Provider().ElevationAtAsync(new Coordinates(1, 1), CancellationToken.None);

        result.Error.Code.ShouldBe("lookup.unavailable");
    }

    [Fact]
    public async Task An_elevation_off_the_british_isles_is_a_fault_too()
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("""{"elevation":[8848.0]}"""));

        Result<Elevation> result = await Provider().ElevationAtAsync(new Coordinates(1, 1), CancellationToken.None);

        result.Error.Code.ShouldBe("dwelling.elevation");
    }

    [Fact]
    public async Task A_timeout_is_a_fault()
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)).WithBody("{}"));

        Result<Elevation> result = await Provider(timeoutSeconds: 1).ElevationAtAsync(new Coordinates(1, 1), CancellationToken.None);

        result.Error.Code.ShouldBe("lookup.unavailable");
    }

    public void Dispose() => _server.Dispose();
}
