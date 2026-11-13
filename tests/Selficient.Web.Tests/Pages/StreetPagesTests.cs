using System.Net;
using System.Text.RegularExpressions;
using Selficient.Domain.Households;
using Selficient.Domain.Plans.Rules;
using Selficient.Domain.Resilience;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;
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
        html.ShouldContain("<li data-offer=\"warm-room\" data-between=\"with a warm room\" data-held=\"portable-gas-heater\" data-answers=\"HasWoodStove=true HasGasHeater=true Heating=Wood\">A warm room</li>");
        html.ShouldContain("<li data-offer=\"lift\" data-between=\"with a car that has fuel or charge\" data-held=\"\" data-answers=\"Car=Petrol Car=Electric Car=ElectricWithOutput HasCar=true\">A car with fuel or charge</li>");
        html.ShouldContain("<label for=\"offer-phone-charging\">Somewhere to charge a phone</label>");
        html.ShouldContain("<p class=\"field__hint\" id=\"offer-phone-charging-plan\" data-offer-plan=\"phone-charging\" hidden>Your plan says you have this.</p>", customMessage: "said only when the browser's own plan says so");
        html.ShouldContain("data-street-offer=\"phone-charging\" aria-describedby=\"offer-phone-charging-plan\"");
        html.ShouldContain("<option value=\"\">Nobody</option>");
        html.ShouldContain("<option value=\"close\">My closer circle</option>");
        html.ShouldContain("<option value=\"street\">Everyone in the group</option>");
        html.ShouldContain("Nobody is told who you ticked, or who you did not.");

        // Where "Your street" lands for a guest: the claim, how it goes, and both ways to begin.
        html.ShouldContain("<h1 tabindex=\"-1\">A street copes better than a house</h1>");
        foreach ((string heading, string sentence) in StreetCopy.Steps)
        {
            html.ShouldContain($"<h2>{heading}</h2>");
            html.ShouldContain($"<p>{sentence}</p>");
        }

        html.ShouldContain("<a class=\"button button--quiet\" href=\"/plan/start\">Start with your plan</a>");

        // What a member with nothing to show for it yet is told to do next, one thing at a time.
        html.ShouldContain("<p>Make your plan, and this page will say what you could offer your neighbours. It takes about a minute.</p>");
        html.ShouldContain("data-says-next-offer-many=\"Your plan says you could offer {n} things. Choose who sees each under \"What you share, and with whom\". Nothing is shared until you save.\"");
        html.ShouldContain("data-says-gaps=\"Nobody has offered you these yet: {list}.\"");
        html.ShouldContain("data-says-seen=\"You are offline. This is the street as this device last saw it, on {date}. Nothing can be changed until you are back online.\"");

        // The invitation goes out three ways, and the slips say what a group is to somebody who has never heard of the site.
        html.ShouldContain("<button type=\"button\" class=\"button button--primary\" data-street-share hidden>Send the invitation</button>");
        html.ShouldContain("<button type=\"button\" class=\"button\" data-street-print-slips>Print slips to post through doors</button>");
        html.ShouldContain("data-says-slip-from=\"Your neighbour at {house} is inviting you to our street's group on Selficient.\"");
        System.Text.RegularExpressions.Regex.Count(html, "<div class=\"street__slip\">").ShouldBe(4, "four slips to a sheet");
        html.ShouldContain($"<p>{StreetCopy.SlipWhat}</p>");
        html.ShouldContain("<p>Point your phone's camera at this code to join.</p>");
    }

    [Fact]
    public async Task The_landing_page_makes_the_case_for_a_street_and_the_header_leads_to_it()
    {
        using HttpClient client = _factory.CreateClient();

        string html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/", UriKind.Relative)));

        html.ShouldContain("<a href=\"/street\">Your street</a>", customMessage: "in the header, beside the plan");
        html.ShouldContain("<section class=\"streets\" id=\"street\" aria-labelledby=\"streets-heading\">");
        html.ShouldContain("<h2 id=\"streets-heading\">A street copes better than a house</h2>");
        html.ShouldContain($"<p class=\"streets__lede\">{StreetCopy.Scene}</p>");
        foreach ((string heading, string sentence) in StreetCopy.Points)
        {
            html.ShouldContain($"<h3>{heading}</h3>");
            html.ShouldContain($"<p>{sentence}</p>");
        }

        StreetCopy.Points.Select(point => point.Heading).ShouldBe(["Offers, never needs", "You choose who sees what", "We cannot read it", "On paper for the day itself"]);
        html.ShouldContain("<a class=\"button button--primary\" href=\"/street\">Start a group for your street</a>");
        html.ShouldContain("<a class=\"button button--quiet\" href=\"/plan/start\">Start with your plan</a>");
        html.ShouldContain("<p class=\"streets__note\">Your plan needs no account. A street group does, because the group has to live somewhere other than one phone: an email address, and nothing else.</p>");
        html.IndexOf("id=\"how-heading\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"streets-heading\"", StringComparison.Ordinal), "the street comes after the house");
        html.IndexOf("id=\"streets-heading\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"ways-heading\"", StringComparison.Ordinal), "and before where it is all kept");

        // The example is drawn with the group page's own cards, and every offer in it is one a home can make.
        html.ShouldContain("<li class=\"street__home\" data-example-mine=\"True\">");
        Regex.Count(html, "<li class=\"street__home\" data-example-mine=\"False\">").ShouldBe(3);
        html.ShouldContain("<span class=\"street__name\">Doris</span>");
        html.ShouldContain("<figcaption>An example street, as number 27 sees it. A first name means that home has put number 27 in its closer circle.</figcaption>");
        string[] offers = [.. StreetCopy.Offers.Select(offer => offer.Words)];
        StreetCopy.Example.SelectMany(home => home.Offers).ShouldAllBe(offer => offers.Contains(offer));
        StreetCopy.Example.Count(home => home.Mine).ShouldBe(1);

        html.ShouldContain($"<li><h3>Your street</h3><p>{StreetCopy.Covers}</p></li>");
        html.ShouldContain("<summary>Can my neighbours see my plan?</summary>");
        html.ShouldContain("Neighbours in a group see your house number and whatever you choose to offer them, and nothing else");

        // The install button is one of the footer's links, not a paragraph of its own.
        html.ShouldContain("<button type=\"button\" class=\"site-footer__install\" data-install hidden>Install the app</button>");
        html.IndexOf("class=\"site-footer__links\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("data-install hidden", StringComparison.Ordinal));
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

    [Fact]
    public void What_in_a_plan_says_a_home_could_make_an_offer_names_only_real_actions_and_real_answers()
    {
        HashSet<string> actions = [.. RuleFigures.Definitions.Where(d => d.Id.StartsWith("price.", StringComparison.Ordinal)).Select(d => d.Id.Split('.')[1])];
        HashSet<string> fields = [.. Selficient.Web.Pages.Plan.IndexModel.Echo(new PlanForm()).Select(echoed => echoed.Field)];

        StreetCopy.PlanSigns.Keys.Order().ShouldBe(StreetCopy.Offers.Select(offer => offer.Id).Order(), "every offer says what to look for, and nothing else does");
        foreach ((string offer, PlanSign sign) in StreetCopy.PlanSigns)
        {
            (sign.Held.Length + sign.Answers.Length).ShouldBeGreaterThan(0, offer);
            foreach (string action in sign.Held.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                actions.ShouldContain(action, $"{offer} looks for an action the plan has");
            }

            foreach (string answer in sign.Answers.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = answer.Split('=');
                parts.Length.ShouldBe(2, answer);
                fields.ShouldContain(parts[0], $"{offer} looks for an answer the plan keeps");
            }
        }

        StreetCopy.PlanSigns["hot-food"].Answers.ShouldContain(nameof(HobType.Gas));
        StreetCopy.PlanSigns["warm-room"].Answers.ShouldContain(nameof(HeatingType.Wood));
        StreetCopy.PlanSigns["phone-charging"].Answers.ShouldContain(nameof(CarKind.ElectricWithOutput));
    }

    [Fact]
    public async Task A_server_that_offers_no_accounts_offers_no_street_groups_and_the_plan_does_not_point_to_them()
    {
        using SelficientWebApplicationFactory without = new SelficientWebApplicationFactory().WithoutMail();
        using HttpClient client = without.CreateClient(new() { AllowAutoRedirect = false });
        using HttpClient signedOut = _factory.CreateClient();

        using HttpResponseMessage street = await client.GetAsync(new Uri("/street", UriKind.Relative));
        using HttpResponseMessage planWithout = await PlanPagesTests.PostRobsHouse(client);
        using HttpResponseMessage planWith = await PlanPagesTests.PostRobsHouse(signedOut);
        string with = WebUtility.HtmlDecode(await planWith.Content.ReadAsStringAsync());

        street.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        street.Headers.Location!.ToString().ShouldBe("/account/unavailable");
        (await planWithout.Content.ReadAsStringAsync()).ShouldNotContain("/street");
        with.ShouldContain("<section class=\"street-card\" aria-labelledby=\"street-card-heading\" data-street-card=\"nothing\">");
        with.ShouldContain("<h3 id=\"street-card-heading\">A street copes better than a house</h3>");
        with.ShouldContain("<p>Neighbours in a group tell each other what each could offer when the power is off. You choose what to share and with whom, and Selficient cannot read any of it.</p>");
        with.ShouldContain("<p data-street-card-says=\"could\" hidden>From what you have ticked, you could offer your street:</p>");
        with.ShouldContain("<p data-street-card-says=\"nothing\">Tick what you have above, and this will say what you could offer your street.</p>");
        with.ShouldContain("<a class=\"button button--primary\" href=\"/street\" data-street-way=\"start\">Start a group for your street</a>");
        with.ShouldContain("<a class=\"button button--primary\" href=\"/street\" data-street-way=\"open\" hidden>Open your street group</a>");
        with.ShouldContain("data-answers=\"Car=Petrol Car=Electric Car=ElectricWithOutput HasCar=true\">A car with fuel or charge</li>", customMessage: "the card carries what to look for, as the group's page does");
        with.ShouldContain("<a class=\"button button--quiet\" href=\"/street\">Your street groups</a>");
    }

    public void Dispose() => _factory.Dispose();
}
