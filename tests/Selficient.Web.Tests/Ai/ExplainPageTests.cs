using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Ai;
using Selficient.TestKit;
using Selficient.Web.Tests.Pages;

namespace Selficient.Web.Tests.Ai;

public sealed partial class ExplainPageTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task The_plan_page_carries_the_explain_form_and_the_photos_link()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage plan = await PlanPagesTests.PostRobsHouse(client);
        string html = await plan.Content.ReadAsStringAsync();

        html.ShouldContain("href=\"/plan/photos\"");
        html.ShouldContain("action=\"/plan/explain\"");
        html.ShouldContain("Explain my plan in plain words");
        html.ShouldContain("name=\"Form.Postcode\" value=\"NP12 1GW\"");
    }

    [Fact]
    public async Task The_narrative_cannot_change_the_plan()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await Explain(client, RobsProfile());
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Why your plan looks the way it does");
        html.ShouldContain("Your house sits where the weather arrives first");
        html.ShouldContain("It explains the plan; it never changes a figure, an action or a cost.");
        html.ShouldContain("href=\"/plan\"");
    }

    [Fact]
    public async Task A_get_goes_back_to_the_plan_and_a_broken_profile_is_a_sentence()
    {
        using HttpClient client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        using HttpResponseMessage get = await client.GetAsync(new Uri("/plan/explain", UriKind.Relative));
        Dictionary<string, string> broken = new(RobsProfile()) { ["Form.People"] = "abc" };
        string html = await (await Explain(client, broken)).Content.ReadAsStringAsync();
        Dictionary<string, string> missing = new(RobsProfile()) { ["Form.Postcode"] = "ZE1 0ZZ" };
        string notFound = await (await Explain(client, missing)).Content.ReadAsStringAsync();

        get.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        get.Headers.Location!.ToString().ShouldBe("/plan");
        html.ShouldContain("The plan&#x27;s answers did not arrive whole.");
        notFound.ShouldContain("We couldn&#x27;t find that postcode.");
    }

    [Fact]
    public async Task Without_a_narrator_the_page_says_so()
    {
        using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory().Configuring(b =>
            b.ConfigureTestServices(s => s.AddSingleton<IPlanNarrator, NoAssistant>()));
        using HttpClient client = factory.CreateClient();

        string html = await (await Explain(client, RobsProfile())).Content.ReadAsStringAsync();

        html.ShouldContain("Photo help is not available just now.");
    }

    [Fact]
    public async Task The_allowance_applies_to_narratives_too()
    {
        using SelficientWebApplicationFactory limited = new SelficientWebApplicationFactory().WithSetting("Ai:FreeAnalysesPerMonth", "1").WithSetting("RateLimits:AiPerMinute", "100");
        using HttpClient client = limited.CreateClient();

        string first = await (await Explain(client, RobsProfile())).Content.ReadAsStringAsync();
        string second = await (await Explain(client, RobsProfile())).Content.ReadAsStringAsync();

        first.ShouldContain("Why your plan looks the way it does");
        second.ShouldContain("You have used this month&#x27;s 1 free photo analyses.");
    }

    public void Dispose() => _factory.Dispose();

    private static Dictionary<string, string> RobsProfile() => new()
    {
        ["Form.Postcode"] = "NP12 1GW",
        ["Form.People"] = "4",
        ["Form.Children"] = "2",
        ["Form.HasVulnerablePerson"] = "false",
        ["Form.HasPets"] = "false",
        ["Form.RunsBusinessFromHome"] = "true",
        ["Form.DwellingType"] = "Detached",
        ["Form.Storeys"] = "3",
        ["Form.Heating"] = "GasBoiler",
        ["Form.Hob"] = "Electric",
        ["Form.HasCar"] = "true",
        ["Form.HasGarden"] = "true",
        ["Form.HasDecking"] = "true",
        ["Form.HasTallTreeNearHouse"] = "true",
        ["Form.FloodRisk"] = "VeryLow",
        ["household"] = "household-explain",
    };

    private static async Task<HttpResponseMessage> Explain(HttpClient client, Dictionary<string, string> form)
    {
        string wizard = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        string token = Token().Match(wizard).Groups["token"].Value;
        token.ShouldNotBeEmpty();
        Dictionary<string, string> body = new(form) { ["__RequestVerificationToken"] = token };
        return await client.PostAsync(new Uri("/plan/explain", UriKind.Relative), new FormUrlEncodedContent(body));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();
}
