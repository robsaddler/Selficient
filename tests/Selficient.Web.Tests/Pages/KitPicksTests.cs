using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Selficient.Application.Kit;
using Selficient.Domain.Plans;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Pages;

public sealed partial class KitPicksTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task Picks_render_under_their_actions_with_safe_links_and_the_disclosure()
    {
        using HttpClient client = _factory.CreateClient();
        await _factory.Services.GetRequiredService<Selficient.Application.Catalogue.CatalogueOperations>().ReloadAsync(CancellationToken.None);

        using HttpResponseMessage response = await PlanPagesTests.PostRobsHouse(client);
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Where to look");
        html.ShouldContain("Roberts SolarDAB 99 with crank, solar and USB-C");
        html.ShouldContain("seen 29 Oct 2026");
        html.ShouldContain("some links earn Selficient a small commission at no cost to you");
        html.ShouldContain("Cheapest first. Each does what the action asks; the dearer ones last longer or do more.");
        html.ShouldContain("<span class=\"pick__value\">Lowest price that does the job</span>");
        html.ShouldNotContain("href=\"https://www.amazon.co.uk", customMessage: "the shop address is behind the counter, not on the page");
        MatchCollection links = OutboundLinks().Matches(html);
        links.Count.ShouldBeGreaterThan(20);
        foreach (Match link in links)
        {
            link.Value.ShouldContain("rel=\"sponsored noopener noreferrer\"");
            link.Value.ShouldContain("href=\"/out/", customMessage: "every shop link goes out through the click counter");
        }

        html.ShouldNotContain("href=\"http://");
    }

    [Fact]
    public async Task Catalogue_text_is_encoded_on_the_page()
    {
        IKitCatalogue hostile = Substitute.For<IKitCatalogue>();
        hostile.PicksFor(Arg.Any<ActionId>()).Returns(call =>
            call.Arg<ActionId>().Value == "headtorches"
                ? [Selficient.Domain.Kit.KitPick.Create("headtorches", "<script>alert(1)</script>", "Shop & Co", 1m, new DateOnly(2026, 10, 29), new Uri("https://www.argos.co.uk/?a=1&b=2"), "\"quoted\" note").Value]
                : []);
        using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory().Configuring(b =>
            b.ConfigureTestServices(s => s.AddSingleton(hostile)));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await PlanPagesTests.PostRobsHouse(client);
        string html = await response.Content.ReadAsStringAsync();

        html.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.ShouldNotContain("<script>alert(1)</script>");
        html.ShouldContain("Shop &amp; Co");
        html.ShouldContain("href=\"https://www.argos.co.uk/?a=1&amp;b=2\"");
    }

    [Fact]
    public void The_tier_view_rejects_nulls_and_reports_whether_it_has_picks()
    {
        IKitCatalogue empty = Substitute.For<IKitCatalogue>();
        empty.PicksFor(Arg.Any<ActionId>()).Returns([]);
        PlanTier tier = new(Tier.ThisWeek, "This week", []);

        new PlanTierView(tier, empty).HasPicks.ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => new PlanTierView(null!, empty));
        Should.Throw<ArgumentNullException>(() => new PlanTierView(tier, null!));
        Should.Throw<ArgumentNullException>(() => new IndexModel(null!, empty));
    }

    public void Dispose() => _factory.Dispose();

    [GeneratedRegex("<a href=\"/out/[^>]*>")]
    private static partial Regex OutboundLinks();
}
