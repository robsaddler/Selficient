using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Selficient.TestKit;
using Selficient.Web.Endpoints;
using Selficient.Web.Tests.Pages;

namespace Selficient.Web.Tests.Maps;

public sealed partial class TilesEndpointsTests : IDisposable
{
    private static readonly byte[] Raw = [0x0a, 0x05, 0x68, 0x6f, 0x75, 0x73, 0x65];
    private static readonly byte[] Gz = TestMbTiles.Gzipped(Raw);

    private readonly string _archive = TestMbTiles.Create(TestMbTiles.ZoomstackMetadata, (14, 8_056, 5_337, Gz), (14, 8_057, 5_337, Raw));
    private readonly SelficientWebApplicationFactory _withBasemap;
    private readonly SelficientWebApplicationFactory _withoutBasemap = new SelficientWebApplicationFactory().WithSetting("Map:MbTilesPath", Path.Combine(Path.GetTempPath(), $"selficient-{Guid.NewGuid():N}.mbtiles"));

    public TilesEndpointsTests()
    {
        _withBasemap = new SelficientWebApplicationFactory().WithSetting("Map:MbTilesPath", _archive);
    }

    [Fact]
    public async Task Status_reports_the_basemap()
    {
        using HttpClient client = _withBasemap.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/tiles/status", UriKind.Relative));
        BasemapStatusResponse? status = await response.Content.ReadFromJsonAsync<BasemapStatusResponse>();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=60");
        status.ShouldBe(new BasemapStatusResponse(true, 0, 14, "Contains OS data © Crown copyright and database right 2026"));
        string json = await response.Content.ReadAsStringAsync();
        json.ShouldNotContain("mbtiles", Case.Insensitive, "no path or file detail leaves the server");
    }

    [Fact]
    public async Task Missing_basemap_degrades_to_the_panel()
    {
        using HttpClient client = _withoutBasemap.CreateClient();

        BasemapStatusResponse? status = await client.GetFromJsonAsync<BasemapStatusResponse>(new Uri("/api/tiles/status", UriKind.Relative));
        using HttpResponseMessage tile = await client.GetAsync(new Uri("/api/tiles/14/8056/5337.pbf", UriKind.Relative));
        using HttpResponseMessage plan = await PlanPagesTests.PostRobsHouse(client);
        string html = await plan.Content.ReadAsStringAsync();

        status!.Available.ShouldBeFalse();
        tile.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        html.ShouldContain("Where your house stands");
        html.ShouldContain("about 232 m above sea level");
        html.ShouldContain("The street map is not available on this server yet.");
        html.ShouldContain("data-house-map data-lat=\"51.66550\" data-lng=\"-3.20310\" data-covered=\"true\"");
    }

    [Fact]
    public async Task Northern_ireland_keeps_the_panel_because_the_basemap_stops_at_the_coast()
    {
        using HttpClient client = _withBasemap.CreateClient();

        using HttpResponseMessage plan = await PlanPagesTests.PostHouseAt(client, "BT1 1AA");
        string html = await plan.Content.ReadAsStringAsync();

        html.ShouldContain("data-covered=\"false\"");
        html.ShouldContain("Our street map covers Great Britain only, so there is nothing to draw here yet.");
    }

    [Fact]
    public async Task Tiles_carry_the_right_encoding_and_type_and_are_immutable()
    {
        using HttpClient client = _withBasemap.CreateClient();

        using HttpResponseMessage gz = await client.GetAsync(new Uri("/api/tiles/14/8056/5337.pbf", UriKind.Relative));
        using HttpResponseMessage raw = await client.GetAsync(new Uri("/api/tiles/14/8057/5337.pbf", UriKind.Relative));

        gz.StatusCode.ShouldBe(HttpStatusCode.OK);
        gz.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-protobuf");
        gz.Content.Headers.ContentEncoding.ShouldBe(["gzip"]);
        gz.Headers.CacheControl!.ToString().ShouldBe("public, max-age=31536000, immutable");
        // The handler must send the stored bytes untouched; HttpClient does not decode them here.
        (await gz.Content.ReadAsByteArrayAsync()).ShouldBe(Gz);
        raw.Content.Headers.ContentEncoding.ShouldBeEmpty();
        (await raw.Content.ReadAsByteArrayAsync()).ShouldBe(Raw);
    }

    [Fact]
    public async Task A_miss_is_204_and_nothing_else()
    {
        using HttpClient client = _withBasemap.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/tiles/14/0/0.pbf", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/api/tiles/23/0/0.pbf")]
    [InlineData("/api/tiles/1/2/0.pbf")]
    [InlineData("/api/tiles/1/0/-1.pbf")]
    [InlineData("/api/tiles/x/0/0.pbf")]
    [InlineData("/api/tiles/1/0/0.png")]
    [InlineData("/api/tiles/1/0/0.pbf/../../status")]
    public async Task Tile_coordinates_outside_the_pyramid_are_404(string path)
    {
        using HttpClient client = _withBasemap.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rate_limit_applies_to_tiles_separately_from_the_api()
    {
        using SelficientWebApplicationFactory limited = new SelficientWebApplicationFactory().WithSetting("Map:MbTilesPath", _archive).WithSetting("RateLimits:TilesPerMinute", "1");
        using HttpClient client = limited.CreateClient();

        using HttpResponseMessage first = await client.GetAsync(new Uri("/api/tiles/14/8056/5337.pbf", UriKind.Relative));
        using HttpResponseMessage second = await client.GetAsync(new Uri("/api/tiles/status", UriKind.Relative));
        using HttpResponseMessage calendar = await client.GetAsync(new Uri("/plan/calendar.ics", UriKind.Relative));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        calendar.StatusCode.ShouldBe(HttpStatusCode.OK, "the api policy is a different bucket");
    }

    [Fact]
    public async Task Glyphs_and_the_library_are_served_from_this_site_with_the_right_types()
    {
        using HttpClient client = _withBasemap.CreateClient();

        using HttpResponseMessage glyphs = await client.GetAsync(new Uri("/fonts/glyphs/Open%20Sans%20Semibold/0-255.pbf", UriKind.Relative));
        using HttpResponseMessage library = await client.GetAsync(new Uri("/lib/maplibre/maplibre-gl.mjs", UriKind.Relative));
        using HttpResponseMessage worker = await client.GetAsync(new Uri("/lib/maplibre/maplibre-gl-worker.mjs", UriKind.Relative));
        using HttpResponseMessage styles = await client.GetAsync(new Uri("/lib/maplibre/maplibre-gl.css", UriKind.Relative));

        glyphs.StatusCode.ShouldBe(HttpStatusCode.OK);
        glyphs.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-protobuf");
        glyphs.Headers.CacheControl!.ToString().ShouldBe("public, max-age=31536000, immutable");
        library.StatusCode.ShouldBe(HttpStatusCode.OK);
        library.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
        library.Headers.CacheControl!.ToString().ShouldBe("no-cache");
        worker.StatusCode.ShouldBe(HttpStatusCode.OK);
        styles.StatusCode.ShouldBe(HttpStatusCode.OK);
        styles.Content.Headers.ContentType!.MediaType.ShouldBe("text/css");
    }

    [Fact]
    public async Task No_page_references_a_cdn()
    {
        using HttpClient client = _withBasemap.CreateClient();
        List<string> pages = [];
        foreach (string path in new[] { "/", "/plan/start", "/plan", "/plan/solar", "/plan/photos", "/styleguide", "/offline", "/does-not-exist" })
        {
            using HttpResponseMessage page = await client.GetAsync(new Uri(path, UriKind.Relative));
            pages.Add(await page.Content.ReadAsStringAsync());
        }

        using HttpResponseMessage plan = await PlanPagesTests.PostRobsHouse(client);
        pages.Add(await plan.Content.ReadAsStringAsync());

        foreach (string html in pages)
        {
            RemoteAsset().IsMatch(html).ShouldBeFalse("every script, style and module comes from this site");
        }

        pages[^1].ShouldContain("\"maplibre-gl\": \"/lib/maplibre/maplibre-gl.mjs\"");
    }

    [Fact]
    public async Task The_map_never_posts()
    {
        using HttpClient client = _withBasemap.CreateClient();

        string houseMap = await client.GetStringAsync(new Uri("/js/house-map.js", UriKind.Relative));
        string style = await client.GetStringAsync(new Uri("/js/map/basemap-style.js", UriKind.Relative));

        houseMap.ShouldNotContain("fetch(");
        houseMap.ShouldNotContain("XMLHttpRequest");
        houseMap.ShouldNotContain("sendBeacon");
        houseMap.ShouldNotContain("WebSocket");
        Regex.Count(style, @"fetch\(").ShouldBe(1, "the status probe is the only request");
        style.ShouldContain("fetch(\"/api/tiles/status\"");
        style.ShouldNotContain("method:");
        style.ShouldNotContain("https://");
    }

    public void Dispose()
    {
        _withBasemap.Dispose();
        _withoutBasemap.Dispose();
        TestMbTiles.Delete(_archive);
    }

    [GeneratedRegex("<(script|link)[^>]+(src|href)=\"(https?:)?//", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteAsset();
}
