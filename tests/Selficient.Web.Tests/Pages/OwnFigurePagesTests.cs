using System.Net;
using System.Text.RegularExpressions;
using Selficient.Domain.Parameters;
using Selficient.Domain.Resilience;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Pages;

/// <summary>
/// A household's own figures for things it has: asked on the plan page only of a household that has
/// the thing, used for its plan and its board, and never able to stop its plan being built.
/// </summary>
public sealed partial class OwnFigurePagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new SelficientWebApplicationFactory().WithSetting("RateLimits:PlanPerMinute", "1000").WithSetting("RateLimits:BoardPerMinute", "1000");

    private static Dictionary<string, string> House(params (string Field, string Value)[] more)
    {
        Dictionary<string, string> form = new()
        {
            ["Form.Postcode"] = "NP12 1GW", ["Form.People"] = "2", ["Form.Children"] = "0", ["Form.HasVulnerablePerson"] = "true", ["Form.HasPets"] = "false",
            ["Form.RunsBusinessFromHome"] = "false", ["Form.DwellingType"] = "Terrace", ["Form.Storeys"] = "2", ["Form.Heating"] = "GasBoiler", ["Form.Hob"] = "Gas",
            ["Form.Car"] = "None", ["Form.HasGarden"] = "true", ["Form.HasDecking"] = "false", ["Form.HasTallTreeNearHouse"] = "false", ["Form.FloodRisk"] = "VeryLow",
            ["Form.Stairlift"] = "Battery",
        };
        foreach ((string field, string value) in more)
        {
            form[field] = value;
        }

        return form;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> form)
    {
        string wizard = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        Dictionary<string, string> body = new(form) { ["__RequestVerificationToken"] = Token().Match(wizard).Groups["token"].Value };
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(body));
    }

    private static async Task<string> PageAsync(HttpClient client, string path, Dictionary<string, string> form)
    {
        using HttpResponseMessage response = await PostAsync(client, path, form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static string Question(string html, string id)
    {
        int start = html.IndexOf($"<label for=\"own-{id.Replace('.', '-')}\">", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, $"the household is asked about {id}");
        return html[html.LastIndexOf("<div class=\"assumptions__item\" data-own-state", start, StringComparison.Ordinal)..html.IndexOf("data-own-says>", start, StringComparison.Ordinal)] + html[html.IndexOf("data-own-says>", start, StringComparison.Ordinal)..html.IndexOf("</p>", html.IndexOf("data-own-says>", start, StringComparison.Ordinal), StringComparison.Ordinal)];
    }

    [Fact]
    public async Task A_household_is_asked_only_about_what_it_has_and_the_questions_are_shut_until_one_is_answered()
    {
        using HttpClient client = _factory.CreateClient();

        string withLift = await PageAsync(client, "/plan", House());
        string without = await PageAsync(client, "/plan", House(("Form.Stairlift", "None"), ("Form.HasWoodStove", "true"), ("Form.Car", "Petrol")));

        withLift.ShouldContain("<details class=\"assumptions__own\" data-own-figures>", customMessage: "shut: a new plan is not met with a row of boxes");
        withLift.ShouldContain(" figures you can change to your own</summary>");
        withLift.ShouldContain("<div class=\"assumptions__item\" data-own-state=\"plans\" data-assume-cash-item>", customMessage: "the cash a person needs is asked the same way, in the same place, as the first of them");
        withLift.IndexOf("data-assume-cash-item", StringComparison.Ordinal).ShouldBeGreaterThan(withLift.IndexOf("data-own-figures>", StringComparison.Ordinal));
        withLift.ShouldContain(OwnFigureCopy.Lede);
        withLift.ShouldContain("<p class=\"callout\" role=\"status\" data-own-rebuilt hidden>Your plan has been worked out again from your figures.</p>");
        string lift = Question(withLift, "stairlift-batteries.stock");
        lift.ShouldContain("data-own-state=\"plans\"");
        lift.ShouldContain(">How many days of trips to bed and the bathroom does your stairlift make in a power cut?</label>");
        lift.ShouldContain("type=\"number\" inputmode=\"decimal\" min=\"0\" max=\"100\" step=\"any\" autocomplete=\"off\" value=\"2\" data-own-figure=\"stairlift-batteries.stock\" data-own-plans=\"2\"");
        lift.ShouldContain("<span>days</span>");
        lift.ShouldContain("data-own-reset=\"stairlift-batteries.stock\">Use the plan's 2</button>");
        lift.ShouldContain("data-own-says>This is the plan's own figure, from: Acorn Stairlifts");
        lift.ShouldContain("Change it if you know yours.");
        Question(withLift, "closed-freezer.stock").ShouldContain("How many days does your freezer keep its cold with the door shut?");
        Question(withLift, "water-butt.stock").ShouldContain("How many litres does your water butt hold?", customMessage: "the plan offers this house a water butt, so it is asked what one of its own holds");
        withLift.ShouldNotContain("own-wood-stove-stock");
        withLift.ShouldNotContain("own-petrol-car-stock");
        withLift.ShouldNotContain("own-assumed-cash", customMessage: "the cash a person needs is asked once");
        withLift.ShouldContain("<ul hidden data-own-asked>");
        withLift.ShouldNotContain("<li data-id=");
        withLift.ShouldNotContain("name=\"Own[");
        withLift.ShouldContain("<span class=\"board__own\" data-own-row></span>", customMessage: "no row says it was worked out from a figure nobody gave");

        without.ShouldNotContain("own-stairlift-batteries-stock");
        Question(without, "wood-stove.stock").ShouldContain("How many days of fires does a full log store give you?");
        Question(without, "petrol-car.stock").ShouldContain("How many days of your usual driving is half a tank?");
        Question(without, "petrol-car.stock").ShouldContain("data-own-says>This is the plan's own figure. Change it if you know yours.", customMessage: "a figure the plan chose for itself names no source");
    }

    [Fact]
    public async Task The_plan_and_the_board_are_worked_out_from_a_households_own_figures_and_say_so()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = House(
            ("Own[water-butt.stock]", "300"),
            ("Own[stairlift-batteries.stock]", " 4.5 "),
            ("Own[closed-freezer.stock]", "5000"),
            ("Own[a-figure-a-release-removed]", "1"));

        string html = await PageAsync(client, "/plan", form);
        string fragment = await PageAsync(client, "/plan/board", form);
        string plain = await PageAsync(client, "/plan/board", House());

        html.ShouldContain("A 300-litre water butt on a downpipe");
        html.ShouldNotContain("A 210-litre water butt");
        html.ShouldContain("data-figures-version=\"0\"", customMessage: "the set in force is still the set the plan names");
        html.ShouldContain("<details class=\"assumptions__own\" open=\"open\" data-own-figures>", customMessage: "open, so a household sees what it has answered");

        string butt = Question(html, "water-butt.stock");
        butt.ShouldContain("data-own-state=\"own\"");
        butt.ShouldContain("value=\"300\" data-own-figure=\"water-butt.stock\" data-own-plans=\"210\"");
        butt.ShouldContain("data-own-says>The plan is using your figure. Its own is 210 litres.");
        Question(html, "stairlift-batteries.stock").ShouldContain("value=\"4.5\"");
        string freezer = Question(html, "closed-freezer.stock");
        freezer.ShouldContain("data-own-state=\"declined\"");
        freezer.ShouldContain("data-own-plans=\"2\"");
        freezer.ShouldContain("data-own-says>The plan could not use the figure you gave before, so it is using its own. ");
        freezer.ShouldContain(" must be between 0 and 100 days.");
        freezer.ShouldNotContain("value=\"5000\"");

        // What the plan was built with, as it was asked, for the browser to hold against what it keeps.
        html.ShouldContain("<li data-id=\"water-butt.stock\" data-value=\"300\"></li>");
        html.ShouldContain("<li data-id=\"stairlift-batteries.stock\" data-value=\"4.5\"></li>");
        html.ShouldContain("<li data-id=\"closed-freezer.stock\" data-value=\"5000\"></li>");
        html.ShouldContain("<li data-id=\"a-figure-a-release-removed\" data-value=\"1\"></li>");
        // And on the two forms that post the profile again, so what they build is this plan.
        Regex.Count(html, Regex.Escape("<input type=\"hidden\" name=\"Own[water-butt.stock]\" value=\"300\">")).ShouldBe(2);

        html.ShouldContain("<span class=\"board__own\" data-own-row>Worked out from your own figure for the stairlift's own batteries.</span>");
        Regex.Count(html, "data-own-row>Worked out").ShouldBe(1, "only the row the figure carries says so");
        fragment.ShouldContain("data-own-row>Worked out from your own figure for the stairlift's own batteries.</span>", customMessage: "the board redrawn is the board of the same figures");
        plain.ShouldNotContain("data-own-row>Worked out");
    }

    [Fact]
    public async Task A_household_is_asked_what_the_set_in_force_says_it_may_give_in_the_words_the_set_gives()
    {
        ParameterSet set = ParameterSet.Create(4, new Dictionary<string, decimal>(), new Dictionary<string, string?>
        {
            ["headtorches.stock"] = "How many hours of light do your headtorches give on the cells you keep?",
            ["price.headtorches.high"] = "What is the most a headtorch costs where you shop?",
            ["price.wood-stove-ready.high"] = "What does your chimney sweep charge?",
            ["water-butt.stock"] = null,
            ["closed-freezer.stock"] = "How long does your freezer stay frozen with the door shut, in days?",
        }).Value;
        Selficient.Application.Figures.ICurrentFigures inForce = new InForce(set);
        using Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host = _factory.WithWebHostBuilder(builder => Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.ConfigureTestServices(builder, services => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, inForce)));
        using HttpClient client = host.CreateClient();

        string html = await PageAsync(client, "/plan", House(("Own[headtorches.stock]", "60"), ("Own[price.headtorches.high]", "20"), ("Own[water-butt.stock]", "300")));

        Question(html, "headtorches.stock").ShouldContain(">How many hours of light do your headtorches give on the cells you keep?</label>");
        Question(html, "headtorches.stock").ShouldContain("value=\"60\" data-own-figure=\"headtorches.stock\" data-own-plans=\"40\"");
        Question(html, "closed-freezer.stock").ShouldContain(">How long does your freezer stay frozen with the door shut, in days?</label>", customMessage: "a question an expert reworded");
        string price = Question(html, "price.headtorches.high");
        price.ShouldContain(">What is the most a headtorch costs where you shop?</label>");
        price.ShouldContain("<span>pounds</span>");
        price.ShouldContain("data-own-state=\"own\"");
        html.ShouldNotContain("own-price-wood-stove-ready-high", customMessage: "a price is asked only of a household whose plan has the thing to buy");
        html.ShouldNotContain("own-water-butt-stock", customMessage: "switched off, so it is not asked");
        html.ShouldContain("<li data-id=\"water-butt.stock\" data-value=\"300\"></li>", customMessage: "and a figure kept from before it was switched off is declined, not obeyed");
        html.ShouldContain("A 210-litre water butt on a downpipe");
        html.ShouldContain("£28 to £40", customMessage: "two headtorches at the household's own most");
    }

    [Fact]
    public async Task What_no_browser_of_ours_would_send_is_refused_with_a_sentence()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> words = House(("Own[water-butt.stock]", "lots"));
        Dictionary<string, string> many = House([.. Enumerable.Range(0, ParameterSet.MaxOwn + 1).Select(static i => ($"Own[figure-{i}.stock]", "1"))]);

        string page = await PageAsync(client, "/plan", words);
        string tooMany = await PageAsync(client, "/plan", many);
        using HttpResponseMessage fragment = await PostAsync(client, "/plan/board", words);
        string explained = await PageAsync(client, "/plan/explain", words);

        page.ShouldContain("One of your own figures is not a number.");
        page.ShouldNotContain("data-plan ", customMessage: "the form is shown again, not a plan built from a guess");
        tooMany.ShouldContain("That is more figures of your own than the plan asks for.");
        fragment.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fragment.Content.ReadAsStringAsync()).ShouldBe("One of your own figures is not a number.");
        explained.ShouldContain("The plan's answers did not arrive whole. Open your plan and try again from there.");
    }

    [Fact]
    public void The_words_and_the_questions_are_decided_in_one_place()
    {
        OwnFigureCopy.Summary(1).ShouldBe("One figure you can change to your own");
        OwnFigureCopy.Summary(4).ShouldBe("4 figures you can change to your own");
        OwnFigures.Plain(4.5000m).ShouldBe("4.5");
        OwnFigures.Read(new Dictionary<string, string>()).Value.ShouldBeEmpty();
        OwnFigures.Read(new Dictionary<string, string> { ["water-butt.stock"] = "300" }).Value.ShouldBe(new Dictionary<string, decimal> { ["water-butt.stock"] = 300m });
        OwnFigures.Read(new Dictionary<string, string> { ["water-butt.stock"] = "-3" }).Error.Message.ShouldBe("One of your own figures is not a number.", "a figure has no sign");
        OwnFigures.Read(new Dictionary<string, string> { ["water-butt.stock"] = null! }).IsFailure.ShouldBeTrue();
        ParameterDefinition whole = ParameterRegistry.Find(Assumptions.CashFigure)!;
        new OwnFigureQuestion(whole, 10m, 10m, "plans", "Says.").Step.ShouldBe("1");
        new OwnFigureQuestion(whole, 10m, 10m, "plans", "Says.").InputId.ShouldBe("own-assumed-cash-pounds-a-person-a-day");

        // A row carried by nothing a figure was given for says nothing, whatever else was given.
        ParameterSet own = ParameterSet.Default.WithOwn(new Dictionary<string, decimal> { ["water-butt.stock"] = 300m });
        BoardRow row = new("water", "Water to drink", Rung.Survive, true, [new DayState(CellState.Held, "mains-water", null), new DayState(CellState.Lost, null, "water-butt")], []) { Figures = own };
        OwnFigures.OnRow(row).ShouldBe("Worked out from your own figure for the water butt.");
        OwnFigures.OnRow(row with { Days = [new DayState(CellState.Held, "mains-water", null)] }).ShouldBeEmpty();
        OwnFigures.OnRow(row with { Figures = ParameterSet.Default }).ShouldBeEmpty();

        Should.Throw<ArgumentNullException>(() => OwnFigures.Read(null!));
        Should.Throw<ArgumentNullException>(() => OwnFigures.OnRow(null!));
        Should.Throw<ArgumentNullException>(() => OwnFigures.Questions(null!, ResilienceProfile.FromProfile(Profiles.RobsHouse())));
        Should.Throw<ArgumentNullException>(() => OwnFigures.Questions(new Selficient.Application.Planning.GeneratePlanHandler(new Selficient.Domain.Risks.RiskAssessmentPipeline(Selficient.Application.Composition.ApplicationServiceCollectionExtensions.Assessors()), new Selficient.Domain.Plans.PlanBuilder(Selficient.Application.Composition.ApplicationServiceCollectionExtensions.Rules())).Handle(new Selficient.Application.Planning.GeneratePlanCommand(2, 0, false, false, false, Selficient.Domain.Households.DwellingType.Terrace, 2, Selficient.Domain.Households.HeatingType.GasBoiler, Selficient.Domain.Households.HobType.Gas, false, false, false, false, 40m, Selficient.Domain.Households.FloodRisk.VeryLow)).Value, null!));
    }

    public void Dispose() => _factory.Dispose();

    private sealed class InForce(ParameterSet set) : Selficient.Application.Figures.ICurrentFigures
    {
        public ParameterSet Current => set;

        public DateTimeOffset? PublishedAt => new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();
}
