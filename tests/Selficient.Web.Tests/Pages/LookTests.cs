using System.Xml.Linq;
using Selficient.Application.Composition;
using Selficient.Application.Resilience;
using Selficient.Domain.Households;
using Selficient.Domain.Plans;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Pages;

/// <summary>The illustration system and the strip: the drawings exist for everything that asks for one, and the strip says what the board says.</summary>
public sealed class LookTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private static XDocument Sprite() => XDocument.Load(Path.Combine(SolutionRoot(), "src", "Selficient.Web", "wwwroot", "img", "marks.svg"));

    private static string SolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Selficient.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The solution root was not found above the test directory.");
    }

    private static HashSet<string> SymbolIds(XDocument sprite)
    {
        XNamespace svg = "http://www.w3.org/2000/svg";
        return sprite.Descendants(svg + "symbol").Select(symbol => (string)symbol.Attribute("id")!).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void The_sprite_has_a_mark_for_every_need_every_source_and_the_two_generic_ones()
    {
        HashSet<string> ids = SymbolIds(Sprite());

        foreach (NeedId need in Enum.GetValues<NeedId>())
        {
            ids.ShouldContain(Marks.ForNeed(need), customMessage: $"{need} has no mark");
        }

        foreach (string source in Marks.SourceIds)
        {
            ids.ShouldContain(Marks.ForSource(source), customMessage: $"{source} has no mark");
        }

        foreach (Scenario scenario in Scenarios.All)
        {
            ids.ShouldContain(Marks.ForScenario(scenario.Id), customMessage: $"{scenario.Id} has no pictogram");
        }

        ids.ShouldContain(Marks.Dependency);
        ids.ShouldContain(Marks.House);
        ids.ShouldContain(Marks.ActionGeneral);
        ids.ShouldContain("mark-locate");
        ids.Count.ShouldBe(Enum.GetValues<NeedId>().Length + Marks.SourceIds.Count + Scenarios.All.Count + 4, "no mark is drawn for nothing");
    }

    [Fact]
    public void Every_source_the_composer_can_offer_has_a_mark()
    {
        HashSet<string> ids = SymbolIds(Sprite());
        RiskAssessmentPipeline risks = new(ApplicationServiceCollectionExtensions.Assessors());
        PlanBuilder builder = new(ApplicationServiceCollectionExtensions.Rules());
        HouseholdProfile rob = Profiles.RobsHouse();
        Plan plan = builder.Build(rob, risks.Run(rob)).Value;
        ResilienceProfile ev = ResilienceProfile.Create(rob, false, false, false, Growing.None, CarKind.ElectricWithOutput, 0, 7, 0, []).Value;

        BoardSet set = BoardComposer.Compose(new BoardRequest(ev, new HashSet<string>(), plan, null, null));

        foreach (SourceOption source in set.Sources)
        {
            ids.ShouldContain(Marks.ForSource(source.Id), customMessage: $"{source.Id} has no mark");
        }
    }

    [Theory]
    [InlineData("DrinkingWater", "need-drinkingwater")]
    [InlineData("ColdStorage", "need-coldstorage")]
    [InlineData("dependency-0", "need-dependency")]
    [InlineData("not-a-need", "need-dependency")]
    [InlineData("99", "need-dependency")]
    public void A_row_key_finds_its_mark_or_the_generic_one(string key, string id) => Marks.ForRow(key).ShouldBe(id);

    [Theory]
    [InlineData("water-bottled-three-days", "need-drinkingwater")]
    [InlineData("headtorches", "need-light")]
    [InlineData("portable-power-station", "source-portable-power-station")]
    [InlineData("rooftop-solar-and-battery", "source-rooftop-solar-and-battery")]
    [InlineData("arborist-survey", "action-general")]
    public void An_action_carries_its_source_its_first_need_or_the_list(string action, string id) => Marks.ForAction(action).ShouldBe(id);

    [Fact]
    public void The_helpers_refuse_null()
    {
        Should.Throw<ArgumentNullException>(() => Marks.ForRow(null!));
        Should.Throw<ArgumentNullException>(() => Marks.ForSource(null!));
        Should.Throw<ArgumentNullException>(() => Marks.ForScenario(null!));
        Should.Throw<ArgumentNullException>(() => Marks.ForAction(null!));
        Should.Throw<ArgumentNullException>(() => Strip.From(null!));
    }

    [Fact]
    public void The_strip_reads_the_board_a_bar_per_need_with_the_figure_and_a_sentence()
    {
        Board typical = Selficient.Web.Pages.IndexModel.TypicalBoard;
        ResilienceProfile rob = ResilienceProfile.FromProfile(Profiles.RobsHouse());
        Board stocked = OutageBoard.Simulate(rob, new HashSet<string> { "water-bottled-three-days", "food-one-month", "cash-in-small-notes", "headtorches", "rooftop-solar-and-battery" }, Scenarios.Storm, Season.Winter);

        IReadOnlyList<StripBar> bars = Strip.From(typical);
        IReadOnlyList<StripBar> after = Strip.From(stocked);

        bars.Select(bar => bar.Key).ShouldBe(["DrinkingWater", "Food", "Warmth", "Light"]);
        StripBar water = bars.Single(bar => bar.Key == "DrinkingWater");
        water.HeldThroughout.ShouldBeTrue("the tap keeps running when the power goes");
        water.Figure.ShouldBe("30");
        water.Sentence.ShouldBe("Water to drink holds for the whole month.");
        StripBar food = bars.Single(bar => bar.Key == "Food");
        food.Days.ShouldBe(typical.DaysReady, "the shortest Survive bar is the figure");
        food.Sentence.ShouldBe($"Food holds for {BoardCopy.DaysReady(food.Days)}, then not enough.");
        StripBar warmth = bars.Single(bar => bar.Key == "Warmth");
        warmth.Reduced.ShouldBeTrue("blankets keep a house alive but not warm");
        warmth.Sentence.ShouldStartWith("Warmth is kept in a reduced way");
        after.Single(bar => bar.Key == "Food").Days.ShouldBeGreaterThan(food.Days);
        after.Single(bar => bar.Key == "Light").Days.ShouldBeGreaterThan(bars.Single(bar => bar.Key == "Light").Days);
        new StripBar("Food", "Food", 0.5m, false, 30).Sentence.ShouldBe("Food holds for half a day, then not enough.");
    }

    [Fact]
    public async Task The_landing_page_carries_the_night_hero_the_strip_and_the_three_pictures()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        html.ShouldContain("class=\"hero hero--night night\"");
        html.ShouldContain("<p class=\"hero__brand\">Selficient</p>");
        html.ShouldContain("data-locate data-locate-for=\"#hero-postcode\" data-locate-status=\"#hero-locate-status\" data-locate-submit>", customMessage: "always a button, before the box, that opens the questions");
        html.IndexOf("Use my location</span>", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"hero-postcode\"", StringComparison.Ordinal), "the location comes first, then or, then the postcode");
        html.ShouldContain("<span class=\"locate-row__or\" aria-hidden=\"true\">or</span>");
        html.ShouldContain("#mark-locate\"/></svg><span data-locate-label>Use my location</span></button>");
        html.ShouldContain("<strong>Self-sufficient, efficiently.</strong> How long your house could stand on its own when the power, the water or the roads stop, and the shortest list that closes the gap.");
        html.ShouldNotContain("placeholder=\"NP12 1GW\"", customMessage: "a new household must not see Rob's postcode as if it were preloaded");
        html.ShouldContain("placeholder=\"Enter postcode\"");
        html.ShouldContain("<div class=\"glow\"");
        html.ShouldContain("data-strip data-scale=\"30\"");
        html.ShouldContain("data-need=\"DrinkingWater\"");
        html.ShouldContain("data-need=\"Light\"");
        html.ShouldContain("still-life--kitchen");
        html.ShouldContain("still-life--larder");
        html.ShouldContain("still-life--hill");
        html.ShouldNotContain("<h3>Three photos</h3>", customMessage: "the photos are a tool on the plan, not a headline on the landing page");
        html.ShouldContain("A kitchen table at night with a lit storm lantern");
        html.ShouldNotContain("contour-map", customMessage: "the card gave way to the still life");
    }

    [Fact]
    public async Task The_plan_and_its_fragment_carry_the_marks_and_the_strip()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = BoardPagesTests.RobsHouse();

        string page = await BoardPagesTests.Post(client, "/plan", form);
        string fragment = await BoardPagesTests.Post(client, "/plan/board", form, "Ticks=water-bottled-three-days", "Ticks=food-one-month", "Ticks=cash-in-small-notes");

        page.ShouldContain("class=\"hero hero--plan night\"");
        page.ShouldContain("data-strip data-scale=\"30\"");
        page.ShouldContain("<use href=\"/img/marks.svg#need-drinkingwater\"/>");
        page.ShouldContain("<use href=\"/img/marks.svg#source-house\"/>");
        page.ShouldContain("<use href=\"/img/marks.svg#source-power-banks\"/>");
        page.ShouldContain("<use href=\"/img/marks.svg#scenario-storm\"/>", customMessage: "every scenario card carries its pictogram");
        page.ShouldContain("<use href=\"/img/marks.svg#action-general\"/>", customMessage: "an action that provides nothing on the board carries the list");
        fragment.ShouldContain("<ul hidden data-board-strip>");
        fragment.ShouldContain("data-strip-need=\"Food\"");
        fragment.ShouldNotContain("data-strip-need=\"Food\" data-days=\"1\"", customMessage: "a month of tins moves the food bar");
    }

    public void Dispose() => _factory.Dispose();
}
