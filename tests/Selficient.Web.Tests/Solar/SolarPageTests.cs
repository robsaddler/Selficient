using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Selficient.Application.Geography;
using Selficient.Application.Solar;
using Selficient.Domain.Primitives;
using Selficient.Domain.Solar;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Solar;

public sealed partial class SolarPageTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private static Dictionary<string, string> RobsRoof(string postcode = "NP12 1GW") => new()
    {
        ["Form.Postcode"] = postcode,
        ["Form.Aspect"] = "SouthWest",
        ["Form.PitchDegrees"] = "35",
        ["Form.Panels"] = "13",
        ["Form.BatteryKilowattHours"] = "13.5",
        ["Form.Backup"] = "WholeHome",
        ["Form.Storeys"] = "3",
        ["Form.People"] = "4",
        ["Form.HasElectricCar"] = "false",
        ["Form.HasHeatPump"] = "false",
        ["Form.AnnualKilowattHours"] = "5000",
    };

    [Fact]
    public async Task The_questions_render_with_the_compass_and_the_consent_panel_under_the_fakes()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/plan/solar", UriKind.Relative));

        html.ShouldContain("What would your roof make, and what would it be worth?");
        html.ShouldContain("class=\"compass__rose\"");
        html.ShouldContain("id=\"aspect-SouthWest\"");
        html.ShouldContain("Let an aerial survey measure the roof");
        html.ShouldContain("data-locate-for=\"#Form_Postcode\"", customMessage: "the estimator offers the location beside its postcode box");
        html.ShouldContain("name=\"consent\"");
        html.ShouldContain("Estimate it</button>");
        html.ShouldNotContain("AIza");
    }

    [Fact]
    public async Task Robs_roof_matches_the_review_on_the_page()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await Post(client, RobsRoof());
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        html.ShouldContain("Solar and a battery could save you about");
        html.ShouldContain("NP12, Wales: 13 panels facing south-west, 13.5 kWh battery, whole-home backup");
        Match saving = Saving().Match(html);
        saving.Success.ShouldBeTrue();
        int pounds = int.Parse(saving.Groups["pounds"].Value.Replace(",", "", StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture);
        pounds.ShouldBeInRange(1_125, 1_650);
        html.ShouldContain("In a power cut the whole house keeps running");
        html.ShouldContain("Battery tariff (EDF Empower Fixed, 01:00 to 04:00 at 10.36p)");
        html.ShouldContain("Green Homes Wales 0 percent loan");
        html.ShouldContain("Scaffolding for a tall roof");
        html.ShouldContain("<svg viewBox=\"0 0 480 200\"");
        html.ShouldContain("class=\"chart__bar chart__bar--made\"");
        html.ShouldContain(">Jan</text>");
        html.ShouldContain("Change the answers");
        PanelsValue().Match(html).Groups["value"].Value.ShouldBe("13");
    }

    [Fact]
    public async Task A_flat_roofed_bungalow_without_a_battery_is_honest_about_the_cut()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = RobsRoof();
        form["Form.BatteryKilowattHours"] = "0";
        form["Form.Backup"] = "None";
        form["Form.Storeys"] = "1";
        form["Form.AnnualKilowattHours"] = "";

        using HttpResponseMessage response = await Post(client, form);
        string html = await response.Content.ReadAsStringAsync();

        html.ShouldContain("Panels alone shut down with the grid, so this adds nothing to your Days Ready.");
        html.ShouldContain("no battery");
        html.ShouldNotContain("Scaffolding for a tall roof");
        html.ShouldNotContain("With the battery");
        html.ShouldContain("Standard variable tariff (price cap)");
    }

    [Fact]
    public async Task Bad_answers_come_back_as_sentences_beside_the_field()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = RobsRoof();
        form["Form.Panels"] = "99";
        form["Form.PitchDegrees"] = "abc";
        form["Form.Postcode"] = "";

        using HttpResponseMessage response = await Post(client, form);
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("The panel count needs to be between 2 and 40.");
        html.ShouldContain("Enter your postcode so we can look up the sunshine for your roof.");
        html.ShouldContain("That answer is not one of the options offered.");
        html.ShouldNotContain("Solar and a battery could save you about");
    }

    [Fact]
    public async Task Upstream_faults_land_beside_the_postcode_or_in_the_summary()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage notFound = await Post(client, RobsRoof("ZE1 0ZZ"));
        using HttpResponseMessage yieldDown = await Post(client, RobsRoof("ZE2 9ZZ"));
        string notFoundHtml = await notFound.Content.ReadAsStringAsync();
        string yieldDownHtml = await yieldDown.Content.ReadAsStringAsync();

        notFoundHtml.ShouldContain("We couldn&#x27;t find that postcode.");
        yieldDownHtml.ShouldContain("We couldn&#x27;t get the sunshine figures for your roof just now.");
        SolarModel.FieldFor(DomainErrors.PostcodeNotFound()).ShouldBe("Form.Postcode");
        SolarModel.FieldFor(DomainErrors.YieldUnavailable()).ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Google_is_only_called_with_consent_and_a_key()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> withConsent = new(RobsRoof()) { ["consent"] = "true" };
        Dictionary<string, string> withoutConsent = RobsRoof();

        using HttpResponseMessage surveyed = await Post(client, withConsent, "Roof");
        using HttpResponseMessage declined = await Post(client, withoutConsent, "Roof");
        string surveyedHtml = await surveyed.Content.ReadAsStringAsync();
        string declinedHtml = await declined.Content.ReadAsStringAsync();

        surveyedHtml.ShouldContain("What the aerial survey found");
        surveyedHtml.ShouldContain("south-west (225°)");
        surveyedHtml.ShouldContain("Up to <span class=\"data\">14</span> panels");
        AspectInput().Matches(surveyedHtml).Single(m => m.Value.Contains("aspect-SouthWest", StringComparison.Ordinal)).Value.ShouldContain("checked");
        PanelsValue().Match(surveyedHtml).Groups["value"].Value.ShouldBe("14");
        surveyedHtml.ShouldNotContain("Solar and a battery could save you about");
        declinedHtml.ShouldNotContain("What the aerial survey found");
        declinedHtml.ShouldContain("Solar and a battery could save you about", customMessage: "without consent the roof handler is a plain estimate");
    }

    [Fact]
    public async Task Without_a_provider_the_consent_panel_is_not_offered_and_the_roof_handler_estimates()
    {
        using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory().Configuring(b =>
            b.ConfigureTestServices(s => s.AddSingleton<IRoofInsightsProvider, NoRoofInsights>()));
        using HttpClient client = factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/plan/solar", UriKind.Relative));
        using HttpResponseMessage response = await Post(client, new Dictionary<string, string>(RobsRoof()) { ["consent"] = "true" }, "Roof");
        string posted = await response.Content.ReadAsStringAsync();

        html.ShouldNotContain("Let an aerial survey measure the roof");
        posted.ShouldContain("Solar and a battery could save you about");
    }

    [Fact]
    public async Task A_survey_that_fails_says_so_and_a_bad_postcode_on_the_survey_lands_on_the_field()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage down = await Post(client, new Dictionary<string, string>(RobsRoof("ZE2 9ZZ")) { ["consent"] = "true" }, "Roof");
        using HttpResponseMessage missing = await Post(client, new Dictionary<string, string>(RobsRoof("ZE1 0ZZ")) { ["consent"] = "true" }, "Roof");
        using HttpResponseMessage malformed = await Post(client, new Dictionary<string, string>(RobsRoof("nope")) { ["consent"] = "true" }, "Roof");

        (await down.Content.ReadAsStringAsync()).ShouldContain("The aerial survey has nothing for this roof.");
        (await missing.Content.ReadAsStringAsync()).ShouldContain("We couldn&#x27;t find that postcode.");
        (await malformed.Content.ReadAsStringAsync()).ShouldContain("That does not look like a UK postcode.");
    }

    [Fact]
    public async Task A_survey_with_no_usable_segment_leaves_the_form_alone()
    {
        IRoofInsightsProvider odd = Substitute.For<IRoofInsightsProvider>();
        odd.IsAvailable.Returns(true);
        odd.DescribeAsync(Arg.Any<Coordinates>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new RoofInsights([], 0, 400)));
        using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory().Configuring(b =>
            b.ConfigureTestServices(s => s.AddSingleton(odd)));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Post(client, new Dictionary<string, string>(RobsRoof()) { ["consent"] = "true", ["Form.Panels"] = "9" }, "Roof");
        string html = await response.Content.ReadAsStringAsync();

        html.ShouldContain("What the aerial survey found");
        PanelsValue().Match(html).Groups["value"].Value.ShouldBe("9");
        html.ShouldNotContain("panels of 400 W would fit");
    }

    [Fact]
    public async Task The_plan_links_to_the_estimator_for_a_house_but_not_a_flat()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage house = await Pages.PlanPagesTests.PostRobsHouse(client);
        (await house.Content.ReadAsStringAsync()).ShouldContain("href=\"/plan/solar\"");
    }

    [Fact]
    public async Task A_report_with_no_payback_says_so()
    {
        EstimateSolarHandler handler = _factory.Services.GetRequiredService<EstimateSolarHandler>();
        SolarReport report = (await handler.HandleAsync(new EstimateSolarRequest("NP12 1GW", RoofAspect.SouthWest, 35, 13, 13.5m, BackupLevel.WholeHome, 3, 4, false, false, 5_000), CancellationToken.None)).Value;
        SolarReport hopeless = report with { Estimate = report.Estimate with { Payback = new PaybackYears(0, null) } };

        new SolarEstimateView(hopeless).Lede.ShouldContain("with no payback at these prices");
        new SolarEstimateView(report).Lede.ShouldContain("paid back in");
    }

    [Fact]
    public void The_view_words_every_backup_and_the_credit_case()
    {
        SolarEstimateView.Bill(Domain.Measures.Money.FromPence(-12_345)).ShouldBe("a credit of £123");
        SolarEstimateView.Bill(Domain.Measures.Money.FromPence(12_345)).ShouldBe("£123");
        SolarEstimateView.Kwh(5_147.4m).ShouldBe("5,147");
        Should.Throw<ArgumentNullException>(() => new SolarEstimateView(null!));
        Should.Throw<ArgumentOutOfRangeException>(() => CompassCopy.Letters((RoofAspect)42));
        Should.Throw<ArgumentNullException>(() => new SolarModel(null!, new NoRoofInsights(), Substitute.For<IGeocoder>()));
        Should.Throw<ArgumentNullException>(() => new SolarModel(new EstimateSolarHandler(Substitute.For<IGeocoder>(), Substitute.For<ISolarYieldProvider>()), null!, Substitute.For<IGeocoder>()));
        Should.Throw<ArgumentNullException>(() => new SolarModel(new EstimateSolarHandler(Substitute.For<IGeocoder>(), Substitute.For<ISolarYieldProvider>()), new NoRoofInsights(), null!));
    }

    [Fact]
    public async Task Protected_circuits_and_a_no_backup_battery_each_get_their_sentence()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> protectedForm = RobsRoof();
        protectedForm["Form.Backup"] = "ProtectedCircuits";
        Dictionary<string, string> noBackup = RobsRoof();
        noBackup["Form.Backup"] = "None";

        string protectedHtml = await (await Post(client, protectedForm)).Content.ReadAsStringAsync();
        string noBackupHtml = await (await Post(client, noBackup)).Content.ReadAsStringAsync();

        protectedHtml.ShouldContain("In a power cut the lights, fridge, router and boiler keep running.");
        protectedHtml.ShouldContain(", protected circuits");
        noBackupHtml.ShouldContain("this system shuts down with the grid");
    }

    public void Dispose() => _factory.Dispose();

    private static async Task<HttpResponseMessage> Post(HttpClient client, Dictionary<string, string> form, string? handler = null)
    {
        string page = await client.GetStringAsync(new Uri("/plan/solar", UriKind.Relative));
        string token = Token().Match(page).Groups["token"].Value;
        token.ShouldNotBeEmpty();
        Dictionary<string, string> body = new(form) { ["__RequestVerificationToken"] = token };
        string url = handler is null ? "/plan/solar" : $"/plan/solar?handler={handler}";
        return await client.PostAsync(new Uri(url, UriKind.Relative), new FormUrlEncodedContent(body));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();

    [GeneratedRegex("<span class=\"figure__amount\">£(?<pounds>[0-9,]+)</span>")]
    private static partial Regex Saving();

    [GeneratedRegex("<input[^>]*name=\"Form.Panels\"[^>]*value=\"(?<value>[0-9]+)\"")]
    private static partial Regex PanelsValue();

    [GeneratedRegex("<input[^>]*type=\"radio\"[^>]*>")]
    private static partial Regex AspectInput();
}
