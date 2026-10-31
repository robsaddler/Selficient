using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Catalogue;
using Selficient.Domain.Measures;
using Selficient.TestKit;
using Selficient.Web.Pages;

namespace Selficient.Web.Tests.Pages;

public sealed partial class TipAndDealsPagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private Task ReadyAsync() => _factory.Services.GetRequiredService<CatalogueOperations>().ReloadAsync(CancellationToken.None);

    private static async Task<HttpResponseMessage> PostTipAsync(HttpClient client, string action, string? link)
    {
        string plan = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        string token = Token().Match(plan).Groups["token"].Value;
        Dictionary<string, string> body = new() { ["__RequestVerificationToken"] = token, ["Action"] = action };
        if (link is not null)
        {
            body["Link"] = link;
        }

        return await client.PostAsync(new Uri("/plan/tip", UriKind.Relative), new FormUrlEncodedContent(body));
    }

    [Fact]
    public async Task Every_action_with_picks_offers_the_tip_box_and_a_good_tip_becomes_a_proposal()
    {
        await ReadyAsync();
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage plan = await PlanPagesTests.PostRobsHouse(client);
        string planHtml = await plan.Content.ReadAsStringAsync();
        using HttpResponseMessage taken = await PostTipAsync(client, "headtorches", "https://www.argos.co.uk/product/123#reviews");
        string takenHtml = await taken.Content.ReadAsStringAsync();
        IReadOnlyList<Offer> proposed = await _factory.Services.GetRequiredService<ICatalogueStore>().ProposedAsync(CancellationToken.None);

        planHtml.ShouldContain("Found it cheaper?");
        planHtml.ShouldContain("<input type=\"hidden\" name=\"Action\" value=\"headtorches\">");
        planHtml.ShouldContain("Paste the shop's link to the item and we will check it overnight. Nothing about you is sent.");
        taken.StatusCode.ShouldBe(HttpStatusCode.OK);
        taken.Headers.CacheControl!.NoStore.ShouldBeTrue();
        takenHtml.ShouldContain("Thank you. We will open it tonight and, if it is the item at a better price, it joins the list.");
        takenHtml.ShouldContain("data-tip-outcome=\"taken\"");
        takenHtml.ShouldContain("name=\"robots\" content=\"noindex, nofollow\"");
        proposed.Count.ShouldBe(1);
        proposed[0].Link.ShouldBe(new Uri("https://www.argos.co.uk/product/123"));
        proposed[0].Action.Value.ShouldBe("headtorches");
    }

    [Theory]
    [InlineData("headtorches", "http://www.argos.co.uk/product/1", "notalink", "That is not a web address we can open.")]
    [InlineData("headtorches", "https://evil.example/product/1", "notashop", "That shop is not one we link to yet, so we cannot open it.")]
    [InlineData("not-on-the-plan", "https://www.argos.co.uk/product/1", "notanaction", "That is not an action on the plan, so we have nothing to compare it with.")]
    [InlineData("headtorches", null, "notalink", "That is not a web address we can open.")]
    public async Task A_tip_we_cannot_take_is_told_why_in_a_sentence(string action, string? link, string outcome, string sentence)
    {
        await ReadyAsync();
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostTipAsync(client, action, link);
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("We could not take that one");
        html.ShouldContain($"data-tip-outcome=\"{outcome}\"");
        html.ShouldContain(sentence);
        (await _factory.Services.GetRequiredService<ICatalogueStore>().ProposedAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_get_of_the_tip_page_goes_to_the_plan_and_a_post_without_a_token_is_refused()
    {
        using HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        using HttpResponseMessage get = await client.GetAsync(new Uri("/plan/tip", UriKind.Relative));
        using HttpResponseMessage bare = await client.PostAsync(new Uri("/plan/tip", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["Action"] = "headtorches", ["Link"] = "https://www.argos.co.uk/x" }));

        get.StatusCode.ShouldBe(HttpStatusCode.Found);
        get.Headers.Location!.ToString().ShouldBe("/plan");
        bare.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_deals_page_lists_the_cheapest_pick_per_action_with_the_drops_first()
    {
        await ReadyAsync();
        ICatalogueStore store = _factory.Services.GetRequiredService<ICatalogueStore>();
        Offer torch = (await store.ShownAsync(CancellationToken.None)).First(o => o.Action.Value == "headtorches");
        await store.RecordCheckAsync(torch.Id, new OfferCheck(DateTimeOffset.UtcNow.AddDays(-20), Verdict.Landed, 200, "Then.", Money.FromPence(torch.Price.Pence + 700), "model"), CancellationToken.None);
        await store.RecordCheckAsync(torch.Id, new OfferCheck(DateTimeOffset.UtcNow.AddDays(-1), Verdict.Landed, 200, "Now.", torch.Price, "model"), CancellationToken.None);
        await ReadyAsync();
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/deals", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromMinutes(10));
        html.ShouldContain("The cheapest kit for a three-day plan");
        html.ShouldContain("One has come down in price this quarter; it is first.");
        html.ShouldContain("<link rel=\"canonical\" href=\"https://selficient.app/deals\">");
        html.ShouldContain("some links earn Selficient a small commission at no cost to you");
        Regex.Count(html, "<li class=\"deal\"").ShouldBeGreaterThan(15);
        int firstDeal = html.IndexOf("<li class=\"deal\"", StringComparison.Ordinal);
        html.IndexOf("data-deal=\"headtorches\" data-drop=\"true\"", StringComparison.Ordinal).ShouldBe(html.IndexOf("data-deal=", firstDeal, StringComparison.Ordinal), "the drop is the first card");
        html.ShouldContain("down from");
        html.ShouldContain($"href=\"/out/{torch.Id}\"");
        html.ShouldNotContain("href=\"https://www.amazon.co.uk");
        html.ShouldContain("Work out your own plan");
    }

    [Fact]
    public void The_deals_pages_phrases_cover_every_count()
    {
        DealsModel.Lede(20, 0).ShouldBe("The cheapest we know for each of 20 things a plan buys, every link opened by our checker. No price has come down this quarter.");
        DealsModel.Lede(20, 1).ShouldEndWith("One has come down in price this quarter; it is first.");
        DealsModel.Lede(20, 3).ShouldEndWith("3 have come down in price this quarter; they are first.");
        DealCopy.Others(0).ShouldBe("The only pick we show for this.");
        DealCopy.Others(1).ShouldBe("One more pick on the plan, which lasts longer or does more.");
        DealCopy.Others(2).ShouldBe("2 more picks on the plan, which last longer or do more.");
        Should.Throw<ArgumentNullException>(() => new DealsModel(null!));
        Should.Throw<ArgumentNullException>(() => new Selficient.Web.Pages.Plan.TipModel(null!));
    }

    [Fact]
    public async Task The_footer_and_the_privacy_page_know_about_the_deals_and_the_tips()
    {
        using HttpClient client = _factory.CreateClient();

        string home = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        string privacy = await client.GetStringAsync(new Uri("/privacy", UriKind.Relative));

        home.ShouldContain("<a href=\"/deals\">Cheapest kit today</a>");
        privacy.ShouldContain("If you send us a shop link under \"Found it cheaper?\", we keep the link and nothing about you.");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();

    public void Dispose() => _factory.Dispose();
}
