using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;
using Selficient.Infrastructure.Geography;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Infrastructure.Tests.Geography;

public sealed class ReverseGeocoderTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private PostcodesIoReverseGeocoder Geocoder()
    {
        HttpClient client = new() { BaseAddress = new Uri(_server.Url! + "/"), Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 64 * 1024 };
        return new PostcodesIoReverseGeocoder(client);
    }

    [Fact]
    public async Task Reads_the_nearest_postcode_from_a_rounded_point()
    {
        _server.Given(Request.Create().WithPath("/postcodes").WithParam("lon", "-3.2031").WithParam("lat", "51.6655").WithParam("limit", "1").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"status":200,"result":[{"postcode":"NP12 1GW","distance":12.4,"extra":"ignored"},{"postcode":"NP12 1GX"}]}"""));

        Result<Postcode> result = await Geocoder().NearestAsync(new Coordinates(51.66549312, -3.20314499), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Value.ShouldBe("NP12 1GW");
        _server.LogEntries.Count.ShouldBe(1);
        string query = _server.LogEntries[0].RequestMessage?.RawQuery ?? string.Empty;
        query.ShouldContain("lat=51.6655", customMessage: "the point goes out rounded to about ten metres");
        query.ShouldContain("lon=-3.2031");
    }

    [Fact]
    public async Task No_result_is_no_postcode_here()
    {
        _server.Given(Request.Create().WithPath("/postcodes").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("""{"status":200,"result":null}"""));

        Result<Postcode> result = await Geocoder().NearestAsync(new Coordinates(60.0, -1.0), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("postcode.none-here");
        result.Error.Message.ShouldBe("We couldn't find a postcode where you are. Type it in instead.");
    }

    [Theory]
    [InlineData(200, """{"status":200,"result":[{"postcode":"not a postcode"}]}""", "postcode.none-here")]
    [InlineData(200, """{"status":200,"result":[{"postcode":null}]}""", "postcode.none-here")]
    [InlineData(200, """{"status":200,"result":[]}""", "postcode.none-here")]
    [InlineData(200, """null""", "postcode.none-here")]
    [InlineData(500, """{"status":500}""", "lookup.unavailable")]
    [InlineData(200, """not json""", "lookup.unavailable")]
    public async Task Nearest_failures_are_faults(int status, string body, string code)
    {
        _server.Given(Request.Create().WithPath("/postcodes").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

        Result<Postcode> result = await Geocoder().NearestAsync(new Coordinates(51.5, -0.1), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    public async Task A_dead_upstream_is_unavailable_and_the_adapter_refuses_null()
    {
        HttpClient client = new() { BaseAddress = new Uri("http://127.0.0.1:1/"), Timeout = TimeSpan.FromMilliseconds(500) };
        PostcodesIoReverseGeocoder geocoder = new(client);

        Result<Postcode> result = await geocoder.NearestAsync(new Coordinates(51.5, -0.1), CancellationToken.None);

        result.Error.Code.ShouldBe("lookup.unavailable");
        Should.Throw<ArgumentNullException>(() => new PostcodesIoReverseGeocoder(null!));
    }

    [Fact]
    public async Task The_fake_knows_robs_house_the_sea_and_an_outage()
    {
        FakeReverseGeocoder fake = new();

        (await fake.NearestAsync(new Coordinates(51.66, -3.2), CancellationToken.None)).Value.Value.ShouldBe("NP12 1GW");
        (await fake.NearestAsync(new Coordinates(60.0, -1.0), CancellationToken.None)).Error.Code.ShouldBe("postcode.none-here");
        (await fake.NearestAsync(new Coordinates(58.0, -8.0), CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
    }

    public void Dispose() => _server.Dispose();
}
