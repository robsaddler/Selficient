using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Catalogue;
using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.TestKit;
using Selficient.Web.Formatting;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Pages;

/// <summary>
/// The page that lists what a household saved for later. The cases are the ones in
/// <c>docs/threat-models/shortlist.md</c>: nothing a poster sends is ever shown, only what the
/// catalogue holds under the numbers posted.
/// </summary>
public sealed partial class ShortlistPageTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private async Task<IReadOnlyList<Offer>> OffersAsync()
    {
        await _factory.Services.GetRequiredService<CatalogueOperations>().ReloadAsync(CancellationToken.None);
        return await _factory.Services.GetRequiredService<ICatalogueStore>().ShownAsync(CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string ids)
    {
        string token = await OperatorSignInHelper.TokenAsync(client, "/plan/shortlist");
        return await client.PostAsync(new Uri("/plan/shortlist", UriKind.Relative), OperatorSignInHelper.Form(token, ("Ids", ids)));
    }

    [Fact]
    public async Task With_nothing_posted_the_page_says_how_to_save_something_and_carries_the_form_the_script_posts()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/plan/shortlist", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        html.ShouldContain("<meta name=\"robots\" content=\"noindex, nofollow\">");
        html.ShouldContain("<section class=\"shortlist\" aria-labelledby=\"shortlist-heading\" data-shortlist-page data-shortlist-asked=\"\" data-shortlist-known=\"\">");
        html.ShouldContain("<h1 id=\"shortlist-heading\">Saved for later</h1>");
        html.ShouldContain("<p class=\"shortlist__lede\" data-shortlist-nothing>Nothing is saved for later yet. On your plan, open &quot;Where to look&quot; under an action and press &quot;Save for later&quot; beside anything you want to come back to.</p>");
        html.ShouldContain("<form method=\"post\" data-shortlist-form hidden>");
        html.ShouldContain("<input type=\"hidden\" name=\"Ids\" value=\"\" data-shortlist-ids>");
        html.ShouldContain("name=\"__RequestVerificationToken\"");
        html.ShouldNotContain("data-shortlist-gone");
        html.ShouldNotContain("shortlist__shop");
    }

    [Fact]
    public async Task The_shortlist_is_rendered_from_the_catalogue_and_never_from_what_was_posted()
    {
        IReadOnlyList<Offer> offers = await OffersAsync();
        Offer[] chosen = [.. offers.GroupBy(o => o.Retailer).Take(2).SelectMany(shop => shop.Take(2))];
        chosen.Select(o => o.Retailer).Distinct().Count().ShouldBe(2, "the shipped catalogue has more than one shop");
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostAsync(client, string.Join(',', chosen.Select(o => o.Id)) + ",<script>alert(1)</script>,https://evil.example,-5,1e3");
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("evil.example");
        html.ShouldNotContain("alert(1)");
        html.ShouldNotContain("data-shortlist-gone", customMessage: "what is not a number is passed over, not counted as something that went");
        html.ShouldContain($"data-shortlist-asked=\"{string.Join(',', chosen.Select(o => o.Id))}\"");
        foreach (Offer offer in chosen)
        {
            html.ShouldContain($"<a href=\"/out/{offer.Id}\" rel=\"sponsored noopener noreferrer\" target=\"_blank\">");
            html.ShouldContain($"data-shortlist-remove=\"{offer.Id}\"");
        }

        ShopLinks().Count(html).ShouldBe(chosen.Length, "every link to a shop goes out through the counter");
        string[] shops = [.. chosen.Select(o => o.Retailer).Distinct().Order(StringComparer.OrdinalIgnoreCase)];
        ShopHeadings().Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups["shop"].Value)).ShouldBe(shops, "one panel a shop, in alphabetical order");
        Money total = Money.FromPence(chosen.Sum(o => o.Price.Pence));
        html.ShouldContain($"<p class=\"shortlist__lede\">{chosen.Length} things saved from 2 shops, {MoneyFormat.Amount(total)} in all at the prices we saw. The list is kept with your plan.</p>");
        html.ShouldContain("Following a link to a shop takes you to the shop; some links earn Selficient a small commission at no cost to you.");
        html.ShouldContain("aria-label=\"Remove: ");
    }

    [Fact]
    public async Task An_offer_that_has_left_the_catalogue_is_counted_and_not_shown()
    {
        IReadOnlyList<Offer> offers = await OffersAsync();
        Offer kept = offers[0];
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage one = await PostAsync(client, $"{kept.Id},987654321");
        string oneGone = await one.Content.ReadAsStringAsync();
        using HttpResponseMessage two = await PostAsync(client, "987654321,987654322");
        string allGone = await two.Content.ReadAsStringAsync();

        oneGone.ShouldContain("<p class=\"callout\" role=\"status\" data-shortlist-gone>1 thing you saved is no longer in the catalogue, and has been taken off your list.</p>");
        oneGone.ShouldContain($"data-shortlist-asked=\"{kept.Id},987654321\" data-shortlist-known=\"{kept.Id}\"");
        oneGone.ShouldContain($"1 thing saved from 1 shop, {MoneyFormat.Amount(kept.Price)} in all at the prices we saw.");
        oneGone.ShouldContain($"<p class=\"shortlist__sum\">1 thing, {MoneyFormat.Amount(kept.Price)} at the prices we saw.</p>");
        allGone.ShouldContain("2 things you saved are no longer in the catalogue, and have been taken off your list.");
        allGone.ShouldContain("data-shortlist-nothing");
        allGone.ShouldContain("data-shortlist-known=\"\"");
    }

    [Fact]
    public async Task A_post_without_a_token_is_refused()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsync(new Uri("/plan/shortlist", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["Ids"] = "1" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void What_is_posted_is_cut_to_a_hundred_numbers_and_anything_else_is_ignored()
    {
        ShortlistView.Parse(null).ShouldBeEmpty();
        ShortlistView.Parse(string.Empty).ShouldBeEmpty();
        ShortlistView.Parse(" 3, 1 ,3,,2,").ShouldBe([3, 1, 2], "each once, in the order saved");
        ShortlistView.Parse("12,abc,<b>,1.5,-4,0x10,٣").ShouldBe([12]);
        ShortlistView.Parse("999999999999999999,1000000000000000000").ShouldBe([999_999_999_999_999_999], "eighteen digits at most, so nothing overflows");
        ShortlistView.Parse(string.Join(',', Enumerable.Range(1, 5000))).Count.ShouldBe(100);
        ShortlistView.Most.ShouldBe(100);
        ShortlistView.Empty.Count.ShouldBe(0);
        ShortlistView.Empty.Gone.ShouldBe(0);
        Should.Throw<ArgumentNullException>(() => ShortlistView.For(null!, _ => null));
        Should.Throw<ArgumentNullException>(() => ShortlistView.For([], null!));
    }

    [Fact]
    public void Picks_are_grouped_by_shop_cheapest_first_and_the_sentences_count_in_whole_words()
    {
        KitPick Pick(long id, string name, string shop, decimal pounds) =>
            KitPick.Create("headtorches", name, shop, pounds, new DateOnly(2026, 11, 1), new Uri("https://www.example.com/search?q=x"), null).Value.WithOffer(id);
        Dictionary<long, KitPick> catalogue = new()
        {
            [1] = Pick(1, "Torch B", "Screwfix", 12m),
            [2] = Pick(2, "Torch A", "Screwfix", 12m),
            [3] = Pick(3, "Radio", "amazon", 20m),
            [4] = Pick(4, "Bottles", "Amazon", 5.5m),
        };

        ShortlistView view = ShortlistView.For([1, 2, 3, 4, 9], id => catalogue.GetValueOrDefault(id));

        view.Shops.Select(s => s.Retailer).ShouldBe(["amazon", "Screwfix"], "a shop is one shop however its name is capitalised");
        view.Shops[0].Picks.Select(p => p.Name).ShouldBe(["Bottles", "Radio"]);
        view.Shops[1].Picks.Select(p => p.Name).ShouldBe(["Torch A", "Torch B"], "the same price reads in alphabetical order");
        view.Shops[0].Total.ShouldBe(Money.FromPence(2550));
        view.Total.ShouldBe(Money.FromPence(4950));
        view.Count.ShouldBe(4);
        view.Gone.ShouldBe(1);
        view.Known.ShouldBe("4,3,2,1");
        view.AskedFor.ShouldBe("1,2,3,4,9");
        ShortlistCopy.Lede(1, 1, Money.FromPence(550)).ShouldBe($"1 thing saved from 1 shop, {MoneyFormat.Amount(Money.FromPence(550))} in all at the prices we saw. The list is kept with your plan.");
        ShortlistCopy.Lede(4, 2, Money.FromPence(4950)).ShouldBe($"4 things saved from 2 shops, {MoneyFormat.Amount(Money.FromPence(4950))} in all at the prices we saw. The list is kept with your plan.");
        ShortlistCopy.Shop(view.Shops[1]).ShouldBe($"2 things, {MoneyFormat.Amount(Money.FromPence(2400))} at the prices we saw.");
        MoneyFormat.Amount(Money.FromPence(550)).ShouldStartWith("£");
        ShortlistCopy.Gone(1).ShouldBe("1 thing you saved is no longer in the catalogue, and has been taken off your list.");
        ShortlistCopy.Gone(3).ShouldBe("3 things you saved are no longer in the catalogue, and have been taken off your list.");
        Should.Throw<ArgumentNullException>(() => ShortlistCopy.Shop(null!));
        KitLinks.OfferOf(catalogue[1]).ShouldBe("1");
        KitLinks.OfferOf(KitPick.Create("headtorches", "Torch", "Screwfix", 12m, new DateOnly(2026, 11, 1), new Uri("https://www.example.com/"), null).Value).ShouldBeEmpty("a pick with no offer behind it cannot be saved, and its button is never shown");
        Should.Throw<ArgumentNullException>(() => KitLinks.OfferOf(null!));
        Should.Throw<ArgumentNullException>(() => new ShortlistModel(null!));
    }

    [Fact]
    public async Task Every_pick_on_the_plan_has_a_way_to_save_it_and_the_plan_has_a_way_to_what_was_saved()
    {
        await OffersAsync();
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage plan = await PlanPagesTests.PostRobsHouse(client);
        string html = await plan.Content.ReadAsStringAsync();

        html.ShouldContain("<p class=\"plan__saved\" data-shortlist-link hidden><a href=\"/plan/shortlist\">Saved for later: <span class=\"data\" data-shortlist-count>0</span></a></p>");
        int picks = Regex.Count(html, "<li class=\"pick\">");
        picks.ShouldBeGreaterThan(10);
        SaveButtons().Count(html).ShouldBe(picks, "one button a pick, by the offer's number, hidden until the script knows what is saved");
    }

    public void Dispose() => _factory.Dispose();

    [GeneratedRegex("<a href=\"/out/[0-9]+\" rel=\"sponsored noopener noreferrer\" target=\"_blank\">")]
    private static partial Regex ShopLinks();

    [GeneratedRegex("<section class=\"shortlist__shop\" data-shortlist-shop=\"[^\"]*\">\\s*<h2>(?<shop>[^<]+)</h2>")]
    private static partial Regex ShopHeadings();

    [GeneratedRegex("<button type=\"button\" class=\"button button--small button--quiet pick__save\" data-shortlist=\"[0-9]+\" aria-pressed=\"false\" hidden><span data-shortlist-says=\"off\">Save for later</span><span data-shortlist-says=\"on\" hidden>Saved for later</span></button>")]
    private static partial Regex SaveButtons();
}
