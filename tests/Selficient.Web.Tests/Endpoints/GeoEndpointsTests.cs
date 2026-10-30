using System.Net;
using System.Net.Http.Json;
using System.Text;
using Selficient.TestKit;
using Selficient.Web.Endpoints;

namespace Selficient.Web.Tests.Endpoints;

public sealed class GeoEndpointsTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task Nearest_is_a_post_with_no_store_that_answers_the_postcode()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/geo/nearest", new { lat = 51.6655, lng = -3.2031 });
        NearestResponse? answer = await response.Content.ReadFromJsonAsync<NearestResponse>();
        using HttpResponseMessage get = await client.GetAsync(new Uri("/api/geo/nearest?lat=51.6655&lng=-3.2031", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        answer!.Postcode.ShouldBe("NP12 1GW");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue("a location answer is never kept by any cache");
        get.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed, "coordinates never travel in a query string, where the proxy would log them");
    }

    [Theory]
    [InlineData("""{"lat": 48.8, "lng": 2.3}""")]
    [InlineData("""{"lat": 51.5, "lng": 10.0}""")]
    [InlineData("""{"lat": "fifty", "lng": -3}""")]
    [InlineData("""{"lat": 1e400, "lng": -3}""")]
    [InlineData("""{}""")]
    [InlineData("""not json""")]
    public async Task A_point_outside_the_united_kingdom_is_refused_before_the_upstream(string body)
    {
        using HttpClient client = _factory.CreateClient();
        using StringContent content = new(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/geo/nearest", UriKind.Relative), content);
        string text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        text.ShouldContain("That is not a place in the United Kingdom. Type the postcode instead.", customMessage: text);
    }

    [Fact]
    public async Task No_postcode_nearby_is_said_as_a_sentence_and_an_outage_is_a_503()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage sea = await client.PostAsJsonAsync("/api/geo/nearest", new { lat = 60.0, lng = -1.0 });
        using HttpResponseMessage outage = await client.PostAsJsonAsync("/api/geo/nearest", new { lat = 58.0, lng = -8.0 });
        Sentence? sentence = await sea.Content.ReadFromJsonAsync<Sentence>();

        sea.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        sentence!.Message.ShouldBe("We couldn't find a postcode where you are. Type it in instead.");
        outage.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(49.5, -9.0, true)]
    [InlineData(61.5, 2.5, true)]
    [InlineData(49.4, -3.0, false)]
    [InlineData(61.6, -3.0, false)]
    [InlineData(55.0, -9.1, false)]
    [InlineData(55.0, 2.6, false)]
    [InlineData(double.NaN, -3.0, false)]
    [InlineData(55.0, double.PositiveInfinity, false)]
    public void The_bounding_box_is_the_united_kingdom_and_a_little_sea(double lat, double lng, bool plausible) => GeoEndpoints.IsPlausible(lat, lng).ShouldBe(plausible);

    [Fact]
    public void Mapping_refuses_a_null_builder() => Should.Throw<ArgumentNullException>(() => GeoEndpoints.MapGeoEndpoints(null!));

    public void Dispose() => _factory.Dispose();
}
