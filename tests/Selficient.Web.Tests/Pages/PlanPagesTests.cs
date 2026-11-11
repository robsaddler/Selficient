using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Selficient.TestKit;
using Selficient.Web.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Web.Tests.Pages;

public sealed partial class PlanPagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private static Dictionary<string, string> RobsHouse(string postcode = "NP12 1GW") => new()
    {
        ["Form.Postcode"] = postcode,
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
    };

    [Fact]
    public async Task The_wizard_renders_every_question_and_a_token()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));

        html.ShouldContain("data-locate-for=\"#Form_Postcode\"", customMessage: "the wizard offers the location beside its postcode box");
        html.ShouldContain("id=\"Form_Postcode\"", customMessage: "and the box the button names is on the page");

        html.ShouldContain("How many people live in your home?");
        html.ShouldContain("Is there a tree tall enough to reach the house if it fell?");
        html.ShouldContain("name=\"Form.FloodRisk\"");
        html.ShouldContain("__RequestVerificationToken");
        html.ShouldContain("Build my plan");
    }

    [Theory]
    [InlineData("?postcode=np12%201gw", "NP12 1GW")]
    [InlineData("?postcode=%20%20", "")]
    [InlineData("?postcode=this-is-far-too-long-to-be-a-postcode", "")]
    [InlineData("", "")]
    public async Task The_wizard_takes_the_postcode_typed_on_the_landing_page(string query, string expected)
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/plan/start" + query, UriKind.Relative));

        html.ShouldContain($"id=\"Form_Postcode\" name=\"Form.Postcode\" value=\"{expected}\"");
    }

    [Fact]
    public async Task Robs_house_renders_the_plan_with_its_place_and_height()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostPlan(client, RobsHouse());
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        html.ShouldContain("NP12, Wales, 232 m above sea level");
        html.ShouldContain("<days-ready value=\"1\"", customMessage: "the board's storm figure: the fridge and the shops feed one day");
        html.ShouldContain("A storm takes the power out: what holds, what goes, and when");
        html.ShouldContain("<summary>Clear my plan from this browser</summary>");
        html.ShouldContain("This removes your answers, what you said about each action, the pin on the map and what you rely on from this browser. Export a file first if you want them back. Your theme choice stays.");
        html.ShouldContain("data-clear-plan>Clear it</button>");
        // The page in job order: the verdict, the plan, the board, what you rely on, the risks, the map, upkeep, the tools.
        int[] order = new[] { "id=\"plan-heading\"", "data-verdict", "id=\"actions-heading\"", "id=\"board-heading\"", "id=\"rely-heading\"", "id=\"risks-heading\"", "id=\"map-heading\"", "id=\"tools-heading\"" }.Select(marker => html.IndexOf(marker, StringComparison.Ordinal)).ToArray();
        order.ShouldAllBe(at => at >= 0, "every section is on the page");
        order.ShouldBeInOrder(customMessage: "the sections come in the order the questions come");
        html.ShouldContain("With everything up to here ticked, the house stands for", customMessage: "each tier header says where it takes the house");
        html.ShouldContain("more, lower down the list</summary>", customMessage: "the risks beyond the top three fold away");
        html.ShouldContain("data-scenario-card");
        html.ShouldContain("data-people=\"4\"");
        html.ShouldContain("data-action-id=\"water-bottled-three-days\"");
        html.ShouldContain("data-action-row=\"water-bottled-three-days\"");
        html.ShouldContain("name=\"plan\" value=\"water-bottled-three-days\" data-choice=\"plan\"", customMessage: "each action can be planned");
        html.ShouldContain("name=\"skip\" value=\"water-bottled-three-days\" data-choice=\"skip\"", customMessage: "or ruled out");
        html.ShouldContain("<span>Have it</span>");
        html.ShouldContain("<span>Plan to</span>");
        html.ShouldContain("<span>Not for me</span>");
        html.ShouldContain("Where your plan stands");
        html.ShouldContain("Say which of these you have, which you plan to get and which are not for you.");
        html.ShouldContain("Today the house stands for 1 day. Mark an action as planned and this line says where your plan takes it.");
        html.ShouldContain("data-undo hidden", customMessage: "the way back waits out of sight until a button is pressed");
        html.ShouldContain("data-action-id=\"arborist-survey\"");
        html.ShouldContain("data-action-id=\"rooftop-solar-and-battery\"");
        html.ShouldContain("Within a year");
        html.ShouldContain("Your house sits high and exposed");
        html.ShouldContain("name=\"Postcode\" value=\"NP12 1GW\"");
        html.ShouldContain("name=\"DwellingType\" value=\"Detached\"");
        html.ShouldContain("Everything together:");
        html.ShouldContain("Keep it going");
        html.ShouldContain("href=\"/plan/calendar.ics?decking=true&amp;garden=true\"");
        html.ShouldContain("Clear the leaf litter from under the decking");
    }

    [Fact]
    public async Task A_flat_gets_an_empty_final_stretch_and_a_bare_place_line_when_lookups_degrade()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> flat = RobsHouse("ZE2 9ZZ");
        flat["Form.People"] = "1";
        flat["Form.Children"] = "0";
        flat["Form.DwellingType"] = "Flat";
        flat["Form.Storeys"] = "1";
        flat["Form.Heating"] = "Electric";
        flat["Form.HasGarden"] = "false";
        flat["Form.HasCar"] = "false";

        using HttpResponseMessage response = await PostPlan(client, flat);
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<p class=\"eyebrow\">ZE2</p>");
        html.ShouldNotContain("above sea level");
        html.ShouldContain("Nothing extra for your home in this stretch.");
        html.ShouldNotContain("data-action-id=\"rooftop-solar-and-battery\"");
        html.ShouldContain("data-action-id=\"heating-fallback\"");
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_is_rejected()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsync(new Uri("/plan", UriKind.Relative), new FormUrlEncodedContent(RobsHouse()));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("nope", "That does not look like a UK postcode. Check it and try again.")]
    [InlineData("ZE1 0ZZ", "We couldn't find that postcode. Check it and try again.")]
    public async Task Postcode_problems_come_back_as_sentences_on_the_form(string postcode, string sentence)
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostPlan(client, RobsHouse(postcode));
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Check your answers");
        html.ShouldContain(HtmlEncoder.Default.Encode(sentence));
        html.ShouldNotContain("<days-ready");
    }

    [Theory]
    [InlineData("Form.FloodRisk", "99", "That answer is not one of the options offered. Choose one from the list.")]
    [InlineData("Form.Heating", "Nuclear", "That answer is not one of the options offered. Choose one from the list.")]
    [InlineData("Form.People", "lots", "That answer is not one of the options offered. Choose one from the list.")]
    [InlineData("Form.People", "99", "A plan covers between one and twenty people.")]
    [InlineData("Form.Storeys", "0", "A home has between one and six storeys.")]
    [InlineData("Form.Children", "5", "The number of children must be between zero and the number of people.")]
    public async Task Tampered_profile_values_are_rejected_with_sentences(string field, string value, string sentence)
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = RobsHouse();
        form[field] = value;

        using HttpResponseMessage response = await PostPlan(client, form);
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain(HtmlEncoder.Default.Encode(sentence));
        html.ShouldNotContain("<days-ready");
        html.ShouldNotContain("field is required.", customMessage: "no framework fragment may reach the page");
        html.ShouldNotContain("is invalid.", customMessage: "no framework fragment may reach the page");
    }

    [Fact]
    public async Task Upstream_strings_are_encoded_on_the_page()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostPlan(client, RobsHouse("SW1A 1AA"));
        string html = await response.Content.ReadAsStringAsync();

        html.ShouldContain("Fake region &lt;for testing&gt;");
        html.ShouldNotContain("<for testing>");
    }

    [Fact]
    public async Task The_empty_state_offers_the_wizard_and_a_hidden_resubmit_form()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/plan", UriKind.Relative));

        html.ShouldContain("No plan on this device yet");
        html.ShouldContain("data-resubmit");
        html.ShouldContain("data-field=\"Postcode\"");
        html.ShouldContain("data-field=\"FloodRisk\"");
        html.ShouldContain("__RequestVerificationToken");
    }

    [Fact]
    public async Task Rate_limit_returns_the_polite_429()
    {
        using SelficientWebApplicationFactory limited = new SelficientWebApplicationFactory().WithSetting("RateLimits:PlanPerMinute", "2");
        using HttpClient client = limited.CreateClient();

        using HttpResponseMessage first = await PostPlan(client, RobsHouse());
        using HttpResponseMessage second = await PostPlan(client, RobsHouse());
        using HttpResponseMessage third = await PostPlan(client, RobsHouse());
        string html = await third.Content.ReadAsStringAsync();

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        html.ShouldContain("That was a little too fast.", customMessage: $"Body: {html[Math.Max(0, html.IndexOf("<h1", StringComparison.Ordinal))..Math.Min(html.Length, Math.Max(0, html.IndexOf("<h1", StringComparison.Ordinal)) + 400)]}");
    }

    [Fact]
    public async Task The_full_postcode_is_never_logged()
    {
        // Real adapters against a local stub, because the HttpClient factory's own handlers log
        // request URLs and the fakes would never exercise them (found by the M2 security review).
        using WireMockServer upstream = WireMockServer.Start();
        upstream.Given(Request.Create().WithPath("/postcodes/NP12 1GW").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"result":{"latitude":51.6655,"longitude":-3.2031,"country":"Wales","region":"Wales","outcode":"NP12"}}"""));
        upstream.Given(Request.Create().WithPath("/v1/elevation").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("""{"elevation":[232.0]}"""));

        CapturingSink sink = new();
        LogTap.Attach(sink);
        try
        {
            using SelficientWebApplicationFactory logging = new SelficientWebApplicationFactory()
                .WithRealUpstreams()
                .WithSetting("Upstreams:PostcodesBaseUrl", upstream.Url! + "/")
                .WithSetting("Upstreams:ElevationBaseUrl", upstream.Url! + "/");
            using HttpClient client = logging.CreateClient();

            using HttpResponseMessage response = await PostPlan(client, RobsHouse());
            string html = await response.Content.ReadAsStringAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            html.ShouldContain("232 m above sea level");
            upstream.LogEntries.Count.ShouldBe(2, "both real adapters must have been used; saw: " + string.Join(" | ", upstream.LogEntries.Select(e => (e.RequestMessage?.Path ?? "?") + " -> " + (e.ResponseMessage?.StatusCode ?? "?"))));
            // The tap is process-wide, so other tests' hosts contribute too; every message still has to be clean.
            sink.Messages.ShouldContain(m => m.Contains("\"POST\"", StringComparison.Ordinal) && m.Contains("/plan", StringComparison.Ordinal), "request logging must record the plan POST for this test to mean anything");
            sink.Messages.ShouldAllBe(m =>
                !m.Contains("NP12 1GW", StringComparison.OrdinalIgnoreCase)
                && !m.Contains("NP121GW", StringComparison.OrdinalIgnoreCase)
                && !m.Contains("NP12%201GW", StringComparison.OrdinalIgnoreCase)
                && !Coordinate().IsMatch(m));
        }
        finally
        {
            LogTap.Detach(sink);
        }
    }

    [Fact]
    public void The_log_tap_rejects_null_and_ignores_events_with_nobody_attached()
    {
        Should.Throw<ArgumentNullException>(() => LogTap.Attach(null!));
        Should.Throw<ArgumentNullException>(() => LogTap.Detach(null!));
        LogTap.Sink.Emit(new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, new MessageTemplate("x", []), []));
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>Posts Rob's house through the wizard, for other test classes.</summary>
    internal static Task<HttpResponseMessage> PostRobsHouse(HttpClient client) => PostPlan(client, RobsHouse());

    internal static Task<HttpResponseMessage> PostHouseAt(HttpClient client, string postcode) => PostPlan(client, RobsHouse(postcode));

    private static async Task<HttpResponseMessage> PostPlan(HttpClient client, Dictionary<string, string> form)
    {
        string wizard = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        string token = Token().Match(wizard).Groups["token"].Value;
        token.ShouldNotBeEmpty();
        Dictionary<string, string> body = new(form) { ["__RequestVerificationToken"] = token };
        return await client.PostAsync(new Uri("/plan", UriKind.Relative), new FormUrlEncodedContent(body));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();

    // The latitude as a number, not as a count of milliseconds: a request that happens to take 51.6655 ms is not a leak.
    [GeneratedRegex(@"51\.6655(?!\d*\s?ms)")]
    private static partial Regex Coordinate();

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_messages)
            {
                _messages.Add(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
