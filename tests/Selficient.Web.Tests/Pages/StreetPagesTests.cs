using System.Net;
using Selficient.TestKit;
using Selficient.Web.Pages.Street;

namespace Selficient.Web.Tests.Pages;

/// <summary>
/// The street group page as the server draws it: the same for everyone, with every sentence the
/// script will say carried on it, and nothing about any street, because the server knows nothing.
/// </summary>
public sealed class StreetPagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Theory]
    [InlineData("/street")]
    [InlineData("/street/join")]
    [InlineData("/street/group")]
    public async Task The_page_is_the_same_for_everyone_says_what_a_street_group_is_and_tells_no_other_site_its_address(string path)
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"], "the address of a group is not handed to another site");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        html.ShouldContain("<meta name=\"robots\" content=\"noindex");
        html.ShouldContain("<section class=\"street\" data-street=\"what\"");
        html.ShouldContain($"<p class=\"street__lede\">{StreetCopy.What}</p>");
        html.ShouldContain("It is offers and never needs. Nobody says who in their home is frail, what cash they keep or when they are away, and there is no setting that could.");
        html.ShouldContain("Selficient keeps that you are in the group and cannot read anything in it: not the street's name, not your house number, not what you offer.");
        html.ShouldContain("<div class=\"street__state\" data-street-state=\"group\" hidden>", customMessage: "every state but the first waits for the script");
        html.ShouldContain("data-says-bad-invitation=\"That invitation does not open a group. Ask your neighbour for a new one.\"");
        html.ShouldContain("data-says-homes-many=\"{n} homes in this group.\"");
        html.ShouldContain("data-says-between-one=\"One home {what}.\"");
        html.ShouldContain("<li data-offer=\"warm-room\" data-between=\"with a warm room\">A warm room</li>");
        html.ShouldContain("<label for=\"offer-phone-charging\">Somewhere to charge a phone</label>");
        html.ShouldContain("<option value=\"\">Nobody</option>");
        html.ShouldContain("<option value=\"close\">My closer circle</option>");
        html.ShouldContain("<option value=\"street\">Everyone in the group</option>");
        html.ShouldContain("Nobody is told who you ticked, or who you did not.");
    }

    [Fact]
    public async Task There_is_no_other_street_page_and_the_offers_are_a_closed_list_the_script_shares()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage other = await client.GetAsync(new Uri("/street/maple-road", UriKind.Relative));
        string script = await client.GetStringAsync(new Uri("/js/street-crypto.js", UriKind.Relative));

        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreach ((string id, string words, string between) in StreetCopy.Offers)
        {
            script.ShouldContain($"\"{id}\"", customMessage: "an offer the page words is one the script knows");
            words.ShouldNotBeNullOrWhiteSpace();
            between.ShouldNotEndWith(".");
        }

        StreetCopy.Offers.Select(offer => offer.Id).ShouldBeUnique();
        StreetCopy.Offers.Count.ShouldBe(9);
        StreetCopy.Circles.Select(circle => circle.Value).ShouldBe(["", "close", "street"]);
        string list = script[script.IndexOf("export const OFFERS = [", StringComparison.Ordinal)..];
        list = list[..list.IndexOf(']', StringComparison.Ordinal)];
        list.Split(',').Length.ShouldBe(StreetCopy.Offers.Count, "and the script knows no offer the page does not word");
    }

    public void Dispose() => _factory.Dispose();
}
