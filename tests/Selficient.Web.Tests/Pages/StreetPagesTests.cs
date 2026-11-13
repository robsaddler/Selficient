using System.Net;
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
        with.ShouldContain("<p class=\"plan__street\">A street copes better than a house. <a href=\"/street\">Start a group for your street</a>, and neighbours can tell each other what each could offer when the power is off. Selficient cannot read what a group shares.</p>");
        with.ShouldContain("<a class=\"button button--quiet\" href=\"/street\">Your street groups</a>");
    }

    public void Dispose() => _factory.Dispose();
}
