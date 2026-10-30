using System.Net;
using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;
using Selficient.Infrastructure.Geography;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Infrastructure.Tests.Geography;

public sealed class GeocoderTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private PostcodesIoGeocoder Geocoder(int timeoutSeconds = 30)
    {
        HttpClient client = new() { BaseAddress = new Uri(_server.Url! + "/"), Timeout = TimeSpan.FromSeconds(timeoutSeconds), MaxResponseContentBufferSize = 64 * 1024 };
        return new PostcodesIoGeocoder(client);
    }

    [Fact]
    public async Task Reads_coordinates_nation_region_and_outcode()
    {
        _server.Given(Request.Create().WithPath("/postcodes/NP12 1GW").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"status":200,"result":{"postcode":"NP12 1GW","latitude":51.665493,"longitude":-3.203145,"country":"Wales","region":"Wales","outcode":"NP12","extra":"ignored"}}"""));

        Result<Locality> result = await Geocoder().LocateAsync(Postcode.Create("np121gw").Value, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Coordinates.Latitude.ShouldBe(51.665493);
        result.Value.Nation.ShouldBe(Nation.Wales);
        result.Value.Region.ShouldBe("Wales");
        result.Value.Outcode.ShouldBe("NP12");
    }

    [Fact]
    public async Task The_geocoder_escapes_the_postcode_in_the_path()
    {
        _server.Given(Request.Create().WithPath("/postcodes/SW1A 1AA").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"result":{"latitude":51.5,"longitude":-0.14,"country":"England","region":null,"outcode":null}}"""));

        Result<Locality> result = await Geocoder().LocateAsync(Postcode.Create("SW1A 1AA").Value, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Region.ShouldBe(string.Empty);
        result.Value.Outcode.ShouldBe("SW1A");
        _server.LogEntries.Count.ShouldBe(1, "the stub only answers the correctly escaped path, so a successful result proves the escaping");
    }

    [Fact]
    public async Task A_404_is_postcode_not_found()
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(404).WithBody("""{"status":404,"error":"Postcode not found"}"""));

        Result<Locality> result = await Geocoder().LocateAsync(Postcode.Create("ZE1 0ZZ").Value, CancellationToken.None);

        result.Error.Code.ShouldBe("postcode.not-found");
    }

    [Theory]
    [InlineData(500, "oops")]
    [InlineData(200, "not json at all")]
    [InlineData(200, """{"result":{"latitude":null,"longitude":null}}""")]
    [InlineData(200, """{"result":null}""")]
    [InlineData(200, "null")]
    [InlineData(200, """{"result":{"latitude":1,"longitude":null}}""")]
    public async Task Malformed_upstream_json_is_a_fault(int status, string body)
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

        Result<Locality> result = await Geocoder().LocateAsync(Postcode.Create("NP12 1GW").Value, CancellationToken.None);

        result.Error.Code.ShouldBe("lookup.unavailable");
        result.Error.Message.ShouldBe("We couldn't reach the postcode service just now. Wait a moment and try again.");
    }

    [Fact]
    public async Task An_oversized_body_is_refused()
    {
        string huge = """{"result":{"latitude":1,"longitude":1,"country":"England","region":""" + "\"" + new string('x', 70_000) + "\"}}";
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(huge));

        Result<Locality> result = await Geocoder().LocateAsync(Postcode.Create("NP12 1GW").Value, CancellationToken.None);

        result.Error.Code.ShouldBe("lookup.unavailable");
    }

    [Fact]
    public async Task Geocode_timeout_is_a_fault()
    {
        _server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)).WithBody("{}"));

        Result<Locality> result = await Geocoder(timeoutSeconds: 1).LocateAsync(Postcode.Create("NP12 1GW").Value, CancellationToken.None);

        result.Error.Code.ShouldBe("lookup.unavailable");
    }

    [Theory]
    [InlineData("England", Nation.England)]
    [InlineData("Wales", Nation.Wales)]
    [InlineData("Scotland", Nation.Scotland)]
    [InlineData("Northern Ireland", Nation.NorthernIreland)]
    [InlineData("Channel Islands", Nation.Unknown)]
    [InlineData(null, Nation.Unknown)]
    public void Nations_parse_from_the_country_field(string? country, Nation expected)
    {
        PostcodesIoGeocoder.ParseNation(country).ShouldBe(expected);
    }

    [Fact]
    public void Rejects_a_missing_client()
    {
        Should.Throw<ArgumentNullException>(() => new PostcodesIoGeocoder(null!));
        Should.Throw<ArgumentNullException>(() => new OpenMeteoElevationProvider(null!));
    }

    public void Dispose() => _server.Dispose();
}
