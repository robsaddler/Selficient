using System.Net;
using Selficient.Application.Share;
using Selficient.TestKit;
using Selficient.Web.Endpoints;

namespace Selficient.Web.Tests.Pages;

public sealed class SharePageTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Theory]
    [InlineData("storm", "1", true)]
    [InlineData("storm", "0", true)]
    [InlineData("water", "7.5", true)]
    [InlineData("storm", "60", true)]
    [InlineData("storm", "60.5", false)]
    [InlineData("storm", "1.25", false)]
    [InlineData("storm", "-1", false)]
    [InlineData("storm", "one", false)]
    [InlineData("storm", "1e3", false)]
    [InlineData("storm", null, false)]
    [InlineData("tsunami", "1", false)]
    [InlineData(null, "1", false)]
    public void Only_a_scenario_the_engine_knows_and_a_count_of_half_days_make_a_card(string? scenario, string? days, bool drawn)
    {
        ShareCard? card = ShareEndpoints.Resolve(scenario, days);

        (card is not null).ShouldBe(drawn);
        if (card is not null)
        {
            card.ScenarioId.ShouldBe(scenario);
            card.ScenarioName.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public async Task The_card_image_is_a_cached_public_png_and_anything_else_is_not_found()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage first = await client.GetAsync(new Uri("/share/storm/1.png", UriKind.Relative));
        byte[] png = await first.Content.ReadAsByteArrayAsync();
        using HttpResponseMessage again = await client.GetAsync(new Uri("/share/storm/1.png", UriKind.Relative));
        byte[] cached = await again.Content.ReadAsByteArrayAsync();
        using HttpResponseMessage unknown = await client.GetAsync(new Uri("/share/tsunami/1.png", UriKind.Relative));
        using HttpResponseMessage quarter = await client.GetAsync(new Uri("/share/storm/1.25.png", UriKind.Relative));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        first.Headers.CacheControl!.Public.ShouldBeTrue();
        first.Headers.CacheControl.MaxAge.ShouldBe(TimeSpan.FromDays(1));
        png[..4].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        cached.ShouldBe(png, "the second request is the kept card");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        quarter.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_share_landing_carries_the_card_for_the_chat_apps_and_nothing_about_the_house()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/share/water/1.5", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();
        using HttpResponseMessage unknown = await client.GetAsync(new Uri("/share/tsunami/1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<meta name=\"robots\" content=\"noindex, nofollow\">");
        html.ShouldContain("<meta property=\"og:image\" content=\"https://selficient.app/share/water/1.5.png\">");
        html.ShouldContain("<meta name=\"twitter:image\" content=\"https://selficient.app/share/water/1.5.png\">");
        html.ShouldContain("<meta property=\"og:title\" content=\"Our house could stand on its own for 1.5 days when the water stops. How about yours?");
        html.ShouldNotContain("rel=\"canonical\"");
        html.ShouldContain("<days-ready value=\"1.5\"");
        html.ShouldContain("That is when the water stops.");
        html.ShouldContain("Work out yours");
        html.ShouldNotContain("NP12");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_plan_offers_the_share_button_and_the_other_pages_keep_the_sites_own_card()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage plan = await PlanPagesTests.PostRobsHouse(client);
        string planHtml = await plan.Content.ReadAsStringAsync();
        string home = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        string start = await client.GetStringAsync(new Uri("/plan", UriKind.Relative));

        planHtml.ShouldContain("data-share");
        planHtml.ShouldContain("Share this figure");
        home.ShouldContain("<meta property=\"og:image\" content=\"https://selficient.app/img/og.png\">");
        home.ShouldContain("<link rel=\"canonical\" href=\"https://selficient.app/\">");
        start.ShouldNotContain("og:image", customMessage: "a page kept out of search without a card of its own carries no card");
    }

    public void Dispose() => _factory.Dispose();
}
