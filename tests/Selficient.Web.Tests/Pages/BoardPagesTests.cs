using System.Net;
using System.Text.RegularExpressions;
using Selficient.Application.Resilience;
using Selficient.Domain.Resilience;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Pages;

public sealed class BoardPagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new SelficientWebApplicationFactory().WithSetting("RateLimits:PlanPerMinute", "1000").WithSetting("RateLimits:BoardPerMinute", "1000");

    [Fact]
    public async Task The_plan_page_carries_the_board_for_the_storm_with_nothing_ticked()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await Post(client, "/plan", RobsHouse());

        html.ShouldContain("A storm takes the power out: what holds, what goes, and when");
        html.ShouldContain("<days-ready value=\"1\"");
        html.ShouldContain("data-source=\"house\" data-season=\"Winter\"");
        html.ShouldContain("<h3 class=\"board__rung-name\" id=\"rung-Survive\">Survive</h3>", customMessage: "each rung is its own band with a heading");
        html.ShouldContain("Stay alive: water, food, warmth, medicines, and the people and animals who depend on you.");
        html.ShouldContain("Held for the whole stretch: ", customMessage: "each band says how it stands");
        html.ShouldNotContain("data-source=\"rooftop-solar-and-battery\"", customMessage: "one sheet is drawn; a switch posts for another");
        html.ShouldContain("Held for the whole stretch by the tap.");
        html.ShouldContain("Water to drink, a day: held throughout.");
        html.ShouldContain("What you rely on");
        html.ShouldNotContain("A home battery needs a roof of your own", customMessage: "a detached house has one");
        Regex.Count(html, "data-scenario-card").ShouldBe(Scenarios.All.Count);
    }

    [Fact]
    public async Task The_fragment_redraws_for_ticks_and_a_chosen_scenario()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = RobsHouse();
        form["Form.Scenario"] = "snow";

        string bare = await Post(client, "/plan/board", RobsHouse());
        string snow = await Post(client, "/plan/board", form, "Ticks=water-bottled-three-days", "Ticks=food-no-cook-three-days", "Ticks=cash-in-small-notes", "Ticks=headtorches");

        bare.ShouldContain("data-days-ready=\"1\"");
        bare.ShouldNotContain("<html", customMessage: "a fragment, not a page");
        snow.ShouldContain("Snow closes the road: what holds, what goes, and when");
        snow.ShouldContain("data-scenario=\"snow\"");
        snow.ShouldContain("data-days-ready=\"3\"", customMessage: "three days of tins before the road is wanted");
        snow.ShouldContain("data-turns-action=\"food-one-month\"", customMessage: "the month of food would turn cells");
        snow.ShouldContain("<ul hidden data-board-cards>", customMessage: "the chips in the hero take their figures from the fragment");
        snow.ShouldContain("data-card-id=\"snow\" data-days=\"3\" data-selected=\"true\"");
        snow.ShouldContain("<ul hidden data-board-tiers>");
        snow.ShouldContain("data-tier=\"ThisWeek\" data-days=\"");
        snow.ShouldNotContain("data-scenario-card", customMessage: "the cards left the fragment for the hero");
    }

    [Fact]
    public async Task A_dependency_gets_its_own_row_and_decides_the_headline()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = RobsHouse();
        form["Form.Dependencies[0].Name"] = "Oxygen concentrator";
        form["Form.Dependencies[0].Kind"] = "PoweredDevice";
        form["Form.Dependencies[0].Watts"] = "300";
        form["Form.Dependencies[0].HoursPerDay"] = "24";
        form["Form.Dependencies[1].Name"] = "   ";

        string html = await Post(client, "/plan/board", form);
        string page = await Post(client, "/plan", form);

        html.ShouldContain("Oxygen concentrator");
        html.ShouldContain("data-days-ready=\"0.5\"", customMessage: "a concentrator with no source is lost on day one");
        html.ShouldContain("Lost, because the sources cannot carry it.");
        page.ShouldContain("name=\"Form.Dependencies[1].Name\" value=\"Oxygen concentrator\"", customMessage: "the plan page carries what was posted as hidden rows so a post without script keeps it");
        page.ShouldNotContain("name=\"Form.Dependencies[2].Name\"", customMessage: "the blank row is dropped");
    }

    [Theory]
    [InlineData("Form.Dependencies[0].Watts", "9000", "A device draws between 1 and 3,000 watts.")]
    [InlineData("Form.Dependencies[0].Name", "This name is far too long to be a sensible name for anything", "Give the thing you rely on a short name, up to forty characters.")]
    [InlineData("Form.Infants", "9", "The number of infants must be between zero and the number of children.")]
    [InlineData("Form.Floor", "99", "The floor must be between 0 for the ground floor and 40.")]
    public async Task A_bad_answer_in_the_newer_questions_comes_back_as_a_sentence(string field, string value, string sentence)
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = RobsHouse();
        form["Form.Dependencies[0].Name"] = "Pump";
        form["Form.Dependencies[0].Kind"] = "PoweredDevice";
        form["Form.Dependencies[0].Watts"] = "50";
        form["Form.Dependencies[0].HoursPerDay"] = "8";
        form[field] = value;

        using HttpResponseMessage fragment = await PostRaw(client, "/plan/board", form);
        string page = await Post(client, "/plan", form);

        fragment.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string refusal = await fragment.Content.ReadAsStringAsync();
        refusal.ShouldBe(sentence, customMessage: "the fragment answers with the same sentence the form would show");
        page.ShouldContain(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(sentence));
        page.ShouldNotContain("field is required.");
        page.ShouldNotContain("is invalid.");
    }

    [Fact]
    public async Task Ticks_are_checked_before_they_reach_the_engine()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage hostile = await PostRaw(client, "/plan/board", RobsHouse(), "Ticks=<script>");
        using HttpResponseMessage tooMany = await PostRaw(client, "/plan/board", RobsHouse(), [.. Enumerable.Range(0, IndexModel.MaxTicks + 1).Select(i => $"Ticks=tick-{i}")]);
        using HttpResponseMessage unknown = await PostRaw(client, "/plan/board", RobsHouse(), "Ticks=not-a-real-action");

        hostile.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await hostile.Content.ReadAsStringAsync()).ShouldBe("One of the ticks is not a plan action.");
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooMany.Content.ReadAsStringAsync()).ShouldBe("That is more ticks than the plan has actions.");
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK, "a well-formed id the engine does not know changes nothing");
    }

    [Fact]
    public async Task What_is_planned_and_ruled_out_is_checked_as_ticks_are_and_counted_together()
    {
        using HttpClient client = _factory.CreateClient();
        string[] spread = [.. Enumerable.Range(0, 67).SelectMany(i => new[] { $"Ticks=have-{i}", $"PlanTo=plan-{i}", $"NotForMe=skip-{i}" })];

        using HttpResponseMessage hostilePlan = await PostRaw(client, "/plan/board", RobsHouse(), "PlanTo=<script>");
        using HttpResponseMessage hostileSkip = await PostRaw(client, "/plan/board", RobsHouse(), "NotForMe=../etc");
        using HttpResponseMessage tooMany = await PostRaw(client, "/plan/board", RobsHouse(), spread);
        using HttpResponseMessage unknown = await PostRaw(client, "/plan/board", RobsHouse(), "PlanTo=not-a-real-action", "NotForMe=nor-this");
        using HttpResponseMessage page = await PostRaw(client, "/plan", RobsHouse(), "PlanTo=<script>");

        hostilePlan.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await hostilePlan.Content.ReadAsStringAsync()).ShouldBe("One of the ticks is not a plan action.");
        hostileSkip.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await hostileSkip.Content.ReadAsStringAsync()).ShouldBe("One of the ticks is not a plan action.");
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "201 ids across the three lists is over the cap one list had");
        (await tooMany.Content.ReadAsStringAsync()).ShouldBe("That is more ticks than the plan has actions.");
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK, "a well-formed id the plan does not have changes nothing");
        string html = await unknown.Content.ReadAsStringAsync();
        html.ShouldNotContain("not-a-real-action", customMessage: "an id is never echoed");
        (await page.Content.ReadAsStringAsync()).ShouldNotContain("id=\"plan-heading\"", customMessage: "the plan page refuses the same post and builds nothing from it");
    }

    [Fact]
    public async Task The_fragment_carries_the_tallies_and_where_the_plan_takes_the_house()
    {
        using HttpClient client = _factory.CreateClient();

        string bare = await Post(client, "/plan/board", RobsHouse());
        string chosen = await Post(client, "/plan/board", RobsHouse(), "Ticks=headtorches", "PlanTo=water-bottled-three-days", "PlanTo=food-no-cook-three-days", "PlanTo=cash-in-small-notes", "NotForMe=radio-dab-fm-with-crank", "NotForMe=headtorches");

        bare.ShouldContain("<div hidden data-board-tallies>");
        bare.ShouldContain("data-tally=\"plan\"");
        bare.ShouldContain("data-tally=\"ThisWeek\"");
        bare.ShouldContain("Today the house stands for 1 day. Mark an action as planned and this line says where your plan takes it.");
        bare.ShouldContain("aria-label=\"Clear what you have: The whole plan\" aria-disabled=\"true\"", customMessage: "nothing to clear, so the button says so and stays put");
        bare.ShouldContain("aria-label=\"Plan all of these not yet decided: This week\" aria-disabled=\"false\"");
        chosen.ShouldContain("data-days-ready=\"1\"", customMessage: "planning to buy tins does not move today's figure");
        chosen.ShouldMatch("Today the house stands for 1 day\\. With everything you plan to get, it stands for [0-9.]+ days\\.");
        chosen.ShouldContain("data-said=\"What you planned in this stretch is back to not decided.\"");
        chosen.ShouldContain("data-said=\"Everything not decided across the whole plan is now planned.\"");
        chosen.ShouldContain("aria-label=\"Clear what you plan to get: This week\" aria-disabled=\"false\"");
        chosen.ShouldContain("aria-label=\"Clear what is not for you: This week\" aria-disabled=\"false\"");
        chosen.ShouldContain("still to spend</strong>");
        chosen.ShouldNotContain("data-turns-action=\"radio-dab-fm-with-crank\"", customMessage: "ruled out, so not suggested");
        chosen.ShouldContain("With everything up to here except what is not for you, the house stands for");
        Regex.Count(chosen, "data-tally-figure>1</strong>").ShouldBe(2, "the radio is the one thing ruled out, in this week and in the plan; the headtorches are held, which wins");
    }

    [Fact]
    public async Task Ten_days_of_tins_count_as_ten_days_on_the_board_the_rows_and_the_tally()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> snow = RobsHouse();
        snow["Form.Scenario"] = "snow";

        string ticked = await Post(client, "/plan/board", snow, "Ticks=food-no-cook-three-days", "Ticks=cash-in-small-notes");
        string counted = await Post(client, "/plan/board", snow, "Ticks=cash-in-small-notes", "Ticks=food-one-month", "Amounts[food-days]=10");
        string page = await Post(client, "/plan", RobsHouse());

        ticked.ShouldContain("data-days-ready=\"3\"");
        ticked.ShouldContain("<ul hidden data-board-progress>");
        ticked.ShouldNotContain("data-progress-action", customMessage: "no figure was given, so nothing is said about one");
        counted.ShouldContain("data-days-ready=\"7\"", customMessage: "the tins now outlast the three days a tick stood for, and the figure runs to where the next need gives");
        counted.ShouldContain("by the food you have stored", customMessage: "the board names the shelf the household counted");
        counted.ShouldContain("<li data-progress-action=\"food-no-cook-three-days\">You have 10 days of food.</li>");
        counted.ShouldContain("<li data-progress-action=\"food-one-month\">You have 10 of 30 days of food. The rest would cost £93.33 to £160.</li>", customMessage: "two thirds of a month for four people, though the month was ticked");
        counted.ShouldContain("data-turns-action=\"food-one-month\"", customMessage: "the month is not held, so its row still says what topping up would do");
        page.ShouldContain("data-amount=\"food-days\" data-amount-target=\"3\" hidden");
        page.ShouldContain("data-amount=\"food-days\" data-amount-target=\"30\" hidden");
        page.ShouldContain("data-amount=\"bottled-water-litres\" data-amount-target=\"36\" hidden");
        page.ShouldContain("data-amount=\"cash-pounds\" data-amount-target=\"400\" hidden");
        page.ShouldContain("<label for=\"amount-food-one-month\">How much do you have?</label>");
        page.ShouldContain("max=\"365\" step=\"1\" autocomplete=\"off\" data-amount-input>");
        page.ShouldContain("<span class=\"amount__unit\">days of food for the 4 of you</span>");
        page.ShouldContain("<span class=\"amount__hint\">The plan suggests 30.</span>");
        page.ShouldContain("data-progress-for=\"food-one-month\" hidden");
        page.ShouldNotContain("data-amount-target=\"0\"");
        Regex.Count(page, "data-amount-input").ShouldBe(6, "food twice, bottled water, cash, the cans, and the stove a house with an electric hob is offered");
    }

    [Fact]
    public async Task A_figure_for_something_unmeasured_or_out_of_range_is_refused_with_a_sentence()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage unknown = await PostRaw(client, "/plan/board", RobsHouse(), "Amounts[headtorches]=4");
        using HttpResponseMessage hostile = await PostRaw(client, "/plan/board", RobsHouse(), "Amounts[<script>]=4");
        using HttpResponseMessage negative = await PostRaw(client, "/plan/board", RobsHouse(), "Amounts[food-days]=-3");
        using HttpResponseMessage huge = await PostRaw(client, "/plan/board", RobsHouse(), "Amounts[food-days]=100000");
        using HttpResponseMessage words = await PostRaw(client, "/plan/board", RobsHouse(), "Amounts[food-days]=lots");
        using HttpResponseMessage page = await PostRaw(client, "/plan", RobsHouse(), "Amounts[headtorches]=4");

        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).ShouldBe("One of the amounts is for something the plan does not measure.");
        hostile.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await hostile.Content.ReadAsStringAsync()).ShouldBe("One of the amounts is for something the plan does not measure.");
        negative.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await negative.Content.ReadAsStringAsync()).ShouldBe("One of the amounts is below zero or more than the plan can count.");
        huge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await huge.Content.ReadAsStringAsync()).ShouldBe("One of the amounts is below zero or more than the plan can count.");
        words.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await words.Content.ReadAsStringAsync()).ShouldNotContain("<");
        (await page.Content.ReadAsStringAsync()).ShouldNotContain("id=\"plan-heading\"", customMessage: "the plan page builds nothing from it either");
    }

    [Fact]
    public async Task The_cash_rate_is_the_households_to_change_and_nonsense_is_refused_with_a_sentence()
    {
        using HttpClient client = _factory.CreateClient();

        string page = await Post(client, "/plan", RobsHouse());
        string plans = await Post(client, "/plan/board", RobsHouse(), "Amounts[cash-pounds]=200");
        string cheap = await Post(client, "/plan/board", RobsHouse(), "Amounts[cash-pounds]=200", "CashRate=5");
        string blank = await Post(client, "/plan/board", RobsHouse(), "CashRate=");
        using HttpResponseMessage zero = await PostRaw(client, "/plan/board", RobsHouse(), "CashRate=0");
        using HttpResponseMessage huge = await PostRaw(client, "/plan/board", RobsHouse(), "CashRate=101");
        using HttpResponseMessage words = await PostRaw(client, "/plan/board", RobsHouse(), "CashRate=<script>");
        using HttpResponseMessage signed = await PostRaw(client, "/plan/board", RobsHouse(), "CashRate=-5");

        page.ShouldContain("<h3 id=\"assumptions-heading\">What the plan assumes</h3>");
        page.ShouldContain("<label for=\"assume-cash\">What a person needs in cash for a day, when only cash will do</label>");
        page.ShouldContain("min=\"1\" max=\"100\" step=\"1\" autocomplete=\"off\" value=\"10\" data-assume-cash data-assume-default=\"10\"");
        page.ShouldContain("data-assume-reset>Use the plan&#x27;s £10</button>");
        page.ShouldContain("At £10 a person a day, ten days of cash for the 4 of you is £400.");
        plans.ShouldContain("<li data-progress-action=\"cash-in-small-notes\">You have 200 of 400 pounds in cash.</li>");
        plans.ShouldContain("<li data-target-action=\"cash-in-small-notes\" data-target=\"400\">The plan suggests 400, at £10 a person a day.");
        cheap.ShouldContain("<li data-progress-action=\"cash-in-small-notes\">You have 200 pounds in cash.</li>", customMessage: "at five pounds a day £200 is the ten days");
        cheap.ShouldContain("<li data-target-action=\"cash-in-small-notes\" data-target=\"200\">The plan suggests 200, at £5 a person a day.");
        cheap.ShouldContain("data-cash-says=\"At £5 a person a day, ten days of cash for the 4 of you is £200.\"");
        blank.ShouldContain("data-target=\"400\"", customMessage: "an empty box is the plan's own rate");
        foreach (HttpResponseMessage refused in new[] { zero, huge, words, signed })
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.Content.ReadAsStringAsync()).ShouldBe("The cash a person needs for a day must be between 1 and 100 pounds.");
        }
    }

    [Fact]
    public async Task Everything_planned_is_a_pill_beside_the_house_and_draws_the_board_the_plan_leads_to()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> chosen = RobsHouse();
        chosen["Form.Source"] = "planned";
        string[] planned = ["PlanTo=water-bottled-three-days", "PlanTo=food-no-cook-three-days", "PlanTo=cash-in-small-notes"];

        string bare = await Post(client, "/plan/board", chosen);
        string offered = await Post(client, "/plan/board", RobsHouse(), planned);
        string drawn = await Post(client, "/plan/board", chosen, planned);

        bare.ShouldNotContain("id=\"source-planned\"", customMessage: "nothing is planned, so there is nothing to switch to");
        bare.ShouldContain("data-source=\"house\"");
        offered.ShouldContain("id=\"source-planned\" value=\"planned\" class=\"seg__input\">", customMessage: "offered and not chosen");
        offered.ShouldContain("Everything we plan to get</label>");
        drawn.ShouldContain("id=\"source-planned\" value=\"planned\" class=\"seg__input\" checked");
        drawn.ShouldContain("data-days-ready=\"1\"", customMessage: "the headline is still the house tonight");
        drawn.ShouldMatch("with everything we plan to get, this house stands on its own for <strong class=\"data\">[0-9.]+ days</strong>");
        drawn.ShouldContain("#source-planned\"");
        // The same table, read against today.
        bare.ShouldNotContain("data-board-comparison", customMessage: "the house's own sheet is today");
        offered.ShouldNotContain("data-board-comparison");
        offered.ShouldNotContain("data-paired=\"true\"", customMessage: "nothing is drawn twice on the house sheet");
        offered.ShouldNotContain("board__when");
        drawn.ShouldContain("<p class=\"board__pairs-note\">Each need that changes is drawn twice: as it stands today, and with the plan.</p>");
        drawn.ShouldContain("<input type=\"checkbox\" data-board-only-changes> <span>Show only what changes</span>");
        drawn.ShouldContain("data-season=\"Winter\" data-paired=\"true\">");
        drawn.ShouldMatch("data-board-comparison>Everything we plan to get turns [0-9]+ of [0-9]+ needs for the better\\. Today the house stands for 1 day; with the plan, [0-9.]+ days\\. What you plan has £[0-9.,]+ to £[0-9.,]+ still to spend\\.</p>");
        drawn.ShouldContain("data-turned=\"true\"");
        drawn.ShouldContain("data-turned=\"false\"");
        drawn.ShouldMatch("<span class=\"board__delta\">1 day today, [0-9.]+ days with the plan</span>");
        // A need that changes is drawn twice, each line with its words in front and on every cell's label.
        drawn.ShouldContain("data-turned=\"true\" data-paired=\"true\">");
        drawn.ShouldContain("data-turned=\"false\" data-paired=\"false\">");
        drawn.ShouldContain("<span class=\"board__when\" data-line=\"today\">Today</span>");
        drawn.ShouldContain("<span class=\"board__when\" data-line=\"with\">With the plan</span>");
        // Every position the board can offer has a name of its own, or the page would fail the moment its pill was chosen.
        string[] positions = [.. Regex.Matches(drawn, "name=\"board-source\" id=\"source-([a-z-]+)\"").Select(match => match.Groups[1].Value)];
        positions.Length.ShouldBe(7, "the house, the plan and the five things a house with its own roof can add");
        positions.ShouldAllBe(position => BoardCopy.With(position).StartsWith("with ", StringComparison.Ordinal));
        drawn.ShouldMatch("aria-label=\"Today\\. Food, [a-z ]+: [a-z0-9 ,]*lost[a-z ]*\\.\"");
        drawn.ShouldMatch("aria-label=\"With the plan\\. Food, [a-z ]+: held throughout\\.\"");
        drawn.ShouldContain("aria-label=\"Laundry, a day: ", customMessage: "a need that does not change is drawn once and labelled as before");
    }

    [Fact]
    public async Task Another_source_is_compared_with_today_in_the_same_table_and_says_so_when_it_changes_nothing()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> battery = RobsHouse();
        battery["Form.Source"] = "rooftop-solar-and-battery";
        Dictionary<string, string> banks = RobsHouse();
        banks["Form.Source"] = "power-banks";
        banks["Form.Scenario"] = "water";

        string withBattery = await Post(client, "/plan/board", battery);
        string withBanks = await Post(client, "/plan/board", banks);

        withBattery.ShouldMatch("data-board-comparison>A home battery and gateway turns [0-9]+ of [0-9]+ needs for the better\\. Today the house stands for 1 day; with the battery, [0-9.]+ days?\\.</p>");
        withBattery.ShouldContain("data-line=\"with\">With the battery</span>");
        withBattery.ShouldContain("with the battery</span>", customMessage: "the line under a need names the battery too");
        withBattery.ShouldNotContain("still to spend.</p>", customMessage: "only the plan has a cost to quote");
        withBattery.ShouldContain("data-board-only-changes");
        withBattery.ShouldContain("aria-label=\"Today. ");
        withBanks.ShouldMatch("data-board-comparison>Power banks turns none of the [0-9]+ needs on this board\\. Today the house stands for [a-z0-9. ]+; with power banks, [a-z0-9. ]+\\.</p>");
        withBanks.ShouldNotContain("aria-label=\"Today. ", customMessage: "nothing differs, so nothing is drawn twice");
        withBanks.ShouldNotContain("data-paired=\"true\"");
        withBanks.ShouldNotContain("data-board-only-changes", customMessage: "there is nothing to narrow the board to");
        withBanks.ShouldNotContain("board__pairs-note");
        withBanks.ShouldNotContain("board__delta\">");
    }

    [Fact]
    public void The_words_around_a_figure_are_whole_and_say_what_is_counted()
    {
        Selficient.Domain.Measures.MoneyRange month = Selficient.Domain.Measures.MoneyRange.Pounds(140m, 240m);
        Selficient.Domain.Measures.MoneyRange free = Selficient.Domain.Measures.MoneyRange.Free;

        AmountCopy.Question.ShouldBe("How much do you have?");
        AmountCopy.Unit(MeasuredStocks.FoodDays, 4).ShouldBe("days of food for the 4 of you");
        AmountCopy.Unit(MeasuredStocks.FoodDays, 1).ShouldBe("days of food for you");
        AmountCopy.Unit(MeasuredStocks.BottledWaterLitres, 1).ShouldBe("litres of sealed bottled water");
        AmountCopy.Unit(MeasuredStocks.WaterCansLitres, 4).ShouldBe("litres of water in cans");
        AmountCopy.Unit(MeasuredStocks.CashPounds, 4).ShouldBe("pounds in notes and coins");
        AmountCopy.Unit(MeasuredStocks.StoveCartridges, 4).ShouldBe("cartridges for the stove");
        AmountCopy.Suggests(MeasuredStocks.BottledWaterLitres, 36m, Assumptions.Default).ShouldBe("The plan suggests 36.");
        AmountCopy.Suggests(MeasuredStocks.CashPounds, 800m, Assumptions.Create(20).Value).ShouldBe("The plan suggests 800, at £20 a person a day. The rate is yours to change, where the plan lists what it assumes.");
        AmountCopy.CashRateLabel.ShouldBe("What a person needs in cash for a day, when only cash will do");
        AmountCopy.CashRateReset.ShouldBe("Use the plan's £10");
        AmountCopy.CashRateSays(Assumptions.Default, 4).ShouldBe("At £10 a person a day, ten days of cash for the 4 of you is £400.");
        AmountCopy.CashRateSays(Assumptions.Create(25).Value, 1).ShouldBe("At £25 a person a day, ten days of cash for you is £250.");
        AmountCopy.CashRateSays(Assumptions.Create(100).Value, 4).ShouldBe("At £100 a person a day, ten days of cash for the 4 of you is £4,000.");
        Should.Throw<ArgumentNullException>(() => AmountCopy.Suggests(MeasuredStocks.CashPounds, 1m, null!));
        Should.Throw<ArgumentNullException>(() => AmountCopy.CashRateSays(null!, 4));
        AmountCopy.Progress(new CountedAction(new Progress("food-one-month", MeasuredStocks.FoodDays, 45m, 30m, 0m), free)).ShouldBe("You have 45 days of food.");
        AmountCopy.Progress(new CountedAction(new Progress("food-one-month", MeasuredStocks.FoodDays, 10m, 30m, 2m / 3m), month.Share(2m / 3m))).ShouldBe("You have 10 of 30 days of food. The rest would cost £93.33 to £160.");
        AmountCopy.Progress(new CountedAction(new Progress("food-one-month", MeasuredStocks.FoodDays, 0m, 30m, 1m), month)).ShouldBe("You have none of the 30 days of food.");
        AmountCopy.Progress(new CountedAction(new Progress("cash-in-small-notes", MeasuredStocks.CashPounds, 200m, 400m, 1m), free)).ShouldBe("You have 200 of 400 pounds in cash.");
        AmountCopy.Progress(new CountedAction(new Progress("butane-cartridge-stove", MeasuredStocks.StoveCartridges, 4m, 10m, 1m), month)).ShouldBe("You have 4 of 10 cartridges.", "a stove part stocked is not costed by the cartridge");
        AmountCopy.Progress(new CountedAction(new Progress("water-cans", MeasuredStocks.WaterCansLitres, 50m, 100m, 0.5m), free)).ShouldBe("You have 50 of 100 litres in cans.", "nothing to cost when the rest is free");
        Should.Throw<ArgumentNullException>(() => AmountCopy.Progress(null!));
        Should.Throw<ArgumentNullException>(() => AmountView.For(null!, 4));
    }

    [Fact]
    public void The_tally_says_whole_things_about_every_count()
    {
        Selficient.Domain.Measures.MoneyRange some = Selficient.Domain.Measures.MoneyRange.Pounds(40m, 90m);
        Selficient.Domain.Measures.MoneyRange free = Selficient.Domain.Measures.MoneyRange.Free;
        Selficient.Domain.Plans.ChoiceTally busy = new(12, 2, 3, some, 5, some, 2);
        Selficient.Domain.Plans.ChoiceTally idle = new(4, 0, 0, free, 0, free, 0);
        Selficient.Domain.Plans.ChoiceTally costless = new(4, 0, 2, free, 2, free, 0);

        TallyCopy.HaveCount(busy).ShouldBe("2 of 12");
        TallyCopy.PlannedCount(busy).ShouldBe("3, £40 to £90 still to spend");
        TallyCopy.PlannedCount(idle).ShouldBe("None yet");
        TallyCopy.PlannedCount(costless).ShouldBe("2, nothing to spend");
        TallyCopy.UndecidedCount(busy).ShouldBe("5, £40 to £90");
        TallyCopy.UndecidedCount(idle).ShouldBe("None");
        TallyCopy.UndecidedCount(costless).ShouldBe("2, nothing to spend");
        TallyCopy.SkippedCount(busy).ShouldBe("2");
        TallyCopy.SkippedCount(idle).ShouldBe("None");
        TallyCopy.Standing(0.5m, 0.5m, 0).ShouldBe("Today the house stands for half a day. Mark an action as planned and this line says where your plan takes it.");
        TallyCopy.Standing(1m, 7m, 4).ShouldBe("Today the house stands for 1 day. With everything you plan to get, it stands for 7 days.");
        TallyCopy.Label(TallyCopy.Have, "This week").ShouldBe("Clear what you have: This week");
        TallyCopy.Label(TallyCopy.Plan, "This week").ShouldBe("Clear what you plan to get: This week");
        TallyCopy.Label(TallyCopy.Skip, "The whole plan").ShouldBe("Clear what is not for you: The whole plan");
        TallyCopy.Label(TallyCopy.PlanAll, "This month").ShouldBe("Plan all of these not yet decided: This month");
        TallyCopy.Said(TallyCopy.Have, false).ShouldBe("What you had in this stretch is back to not decided.");
        TallyCopy.Said(TallyCopy.Plan, true).ShouldBe("What you planned across the whole plan is back to not decided.");
        TallyCopy.Said(TallyCopy.Skip, false).ShouldBe("What you ruled out in this stretch is back to not decided.");
        TallyCopy.Said(TallyCopy.PlanAll, true).ShouldBe("Everything not decided across the whole plan is now planned.");
        TallyCopy.Flag(true).ShouldBe("true");
        TallyCopy.Flag(false).ShouldBe("false");
        Should.Throw<ArgumentNullException>(() => TallyCopy.HaveCount(null!));
        Should.Throw<ArgumentNullException>(() => TallyCopy.PlannedCount(null!));
        Should.Throw<ArgumentNullException>(() => TallyCopy.UndecidedCount(null!));
        Should.Throw<ArgumentNullException>(() => TallyCopy.SkippedCount(null!));
        Should.Throw<ArgumentNullException>(() => TallyView.ForPlan(null!));
        Should.Throw<ArgumentNullException>(() => TallyView.ForTier(null!));
        TallyView.ForTier(new TierOutcome(Selficient.Domain.Plans.Tier.ThisMonth, "This month", 3m, busy, false)).ShouldBe(new TallyView("ThisMonth", "This month", busy, ""));
        TallyView.ForTier(new TierOutcome(Selficient.Domain.Plans.Tier.ThisMonth, "This month", 3m, busy, false)).WholePlan.ShouldBeFalse();
    }

    [Fact]
    public async Task The_fragment_refuses_what_the_plan_page_would_and_redirects_a_get()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> badPostcode = RobsHouse();
        badPostcode["Form.Postcode"] = "ZE1 0ZZ";
        Dictionary<string, string> badPeople = RobsHouse();
        badPeople["Form.People"] = "99";

        using HttpResponseMessage notFound = await PostRaw(client, "/plan/board", badPostcode);
        using HttpResponseMessage invalid = await PostRaw(client, "/plan/board", badPeople);
        using HttpResponseMessage get = await client.GetAsync(new Uri("/plan/board", UriKind.Relative));

        notFound.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await notFound.Content.ReadAsStringAsync()).ShouldBe("We couldn't find that postcode. Check it and try again.");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalid.Headers.CacheControl!.NoStore.ShouldBeTrue("a fragment is never kept by a shared cache");
        (await invalid.Content.ReadAsStringAsync()).ShouldBe("A plan covers between one and twenty people.", customMessage: "the first of our own sentences, so a person sees why");
        get.StatusCode.ShouldBe(HttpStatusCode.OK, "redirected to the plan");
        get.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/plan");
    }

    [Fact]
    public async Task A_flat_is_told_why_the_battery_is_missing_and_its_water_is_pumped()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> flat = Flat();
        flat["Form.Floor"] = "6";

        string html = await Post(client, "/plan", flat);

        html.ShouldContain("A home battery needs a roof of your own, so it is not among the sources for a flat.");
        html.ShouldContain("<days-ready value=\"0.5\"", customMessage: "the sixth floor loses its water with the power");
        html.ShouldContain("because the tap cannot carry it.");
    }

    [Fact]
    public void The_copy_helpers_say_whole_sentences()
    {
        ResilienceProfile rob = ResilienceProfile.FromProfile(Profiles.RobsHouse());
        Board board = OutageBoard.Simulate(rob, new HashSet<string>(), Scenarios.Storm, Season.Winter);
        BoardRow food = board.Row("Food")!;
        BoardRow water = board.Row("DrinkingWater")!;
        BoardRow laundry = board.Row("Laundry")!;
        BoardRow warmth = board.Row("Warmth")!;
        BoardRow cooking = board.Row("Cooking")!;
        BoardRow summerWarmth = OutageBoard.Simulate(rob, new HashSet<string>(), Scenarios.Storm, Season.Summer).Row("Warmth")!;

        BoardCopy.Row(water).ShouldBe("Held for the whole stretch by the tap.");
        BoardCopy.Row(food).ShouldBe("Held for 1 day by the shops, then not enough: the shops gives some of it, because deliveries cannot carry it.");
        BoardCopy.Row(warmth).ShouldBe("Held in a reduced way by blankets, duvets and one room, because the gas boiler cannot carry it.");
        BoardCopy.Row(cooking).ShouldBe("Lost, because the electric hob cannot carry it.");
        BoardCopy.Row(laundry).ShouldBe("Held for 7 days by nothing in the house, then lost, because the washing machine cannot carry it.");
        BoardCopy.Row(summerWarmth).ShouldBe("Not something this house needs in this scenario.");
        BoardCopy.Cell(food, food.Cells[0]).ShouldBe("Food, a day: held throughout.");
        BoardCopy.Cell(food, food.Cells[1]).ShouldBe("Food, three days: held for 1 day, then lost.");
        BoardCopy.Cell(cooking, cooking.Cells[0]).ShouldBe("Cooking, a day: lost from the start.");
        BoardCopy.Name(null).ShouldBe("nothing in the house");
        BoardCopy.Name("the sources").ShouldBe("the sources", "a dependency's own prose passes through");
        BoardCopy.Days(1).ShouldBe("1 day");
        BoardCopy.Days(3).ShouldBe("3 days");
        BoardCopy.DaysReady(0.5m).ShouldBe("half a day");
        BoardCopy.DaysReady(1m).ShouldBe("1 day");
        BoardCopy.Unit(1m).ShouldBe("day");
        BoardCopy.Unit(0.5m).ShouldBe("days");
        BoardCopy.TierOutcome(4m).ShouldBe("With everything up to here ticked, the house stands for 4 days.");
        BoardCopy.TierOutcome(null).ShouldBe("");
        BoardCopy.Cell(food, food.Cells[1], BoardCopy.Today).ShouldBe("Today. Food, three days: held for 1 day, then lost.");
        BoardCopy.Cell(food, food.Cells[0], BoardCopy.WithLabel(BoardSet.PlannedSource)).ShouldBe("With the plan. Food, a day: held throughout.");
        BoardCopy.With("large-power-station").ShouldBe("with the station and panels");
        BoardCopy.WithLabel("petrol-generator").ShouldBe("With the generator");
        BoardCopy.PairsNote("portable-power-station").ShouldBe("Each need that changes is drawn twice: as it stands today, and with the power station.");
        Should.Throw<ArgumentNullException>(() => BoardCopy.With(null!));
        Should.Throw<ArgumentNullException>(() => BoardCopy.Pairs(null!));
        BoardCopy.Delta(new NeedShift("Food", 1m, 16m, true), BoardSet.PlannedSource).ShouldBe("1 day today, 16 days with the plan");
        BoardCopy.Delta(new NeedShift("Warmth", 30m, 30m, true), "rooftop-solar-and-battery").ShouldBe("eased with the battery, though not for longer");
        Should.Throw<ArgumentNullException>(() => BoardCopy.Cell(food, food.Cells[0], (string)null!));
        Should.Throw<ArgumentNullException>(() => BoardCopy.Delta(null!, BoardSet.PlannedSource));
        Should.Throw<ArgumentNullException>(() => BoardCopy.Comparison(null!));
        BoardCopy.TierOutcome(null, true).ShouldBe("");
        BoardCopy.TierOutcome(4m, true).ShouldBe("With everything up to here except what is not for you, the house stands for 4 days.");
        BoardCopy.Turns([new Selficient.Application.Resilience.NeedShift("Food", 1m, 7m, true)]).ShouldBe("With this: food from 1 day to 7 days.");
        BoardCopy.Turns([new Selficient.Application.Resilience.NeedShift("Warmth", 30m, 30m, true), new("Light", 0.5m, 30m, true), new("Cash", 1m, 3m, true), new("Food", 1m, 3m, true)]).ShouldBe("With this: warmth eased, though not for longer, light from half a day to 30 days, cash from 1 day to 3 days, and 1 more.");
        Should.Throw<ArgumentNullException>(() => BoardCopy.Turns(null!));
        BoardCopy.RungName(Rung.Function).ShouldBe("Keep going");
        BoardCopy.RungWhat(Rung.Survive).ShouldBe("Stay alive: water, food, warmth, medicines, and the people and animals who depend on you.");
        BoardCopy.RungMark(Rung.House).ShouldBe("source-house");
        Should.Throw<ArgumentNullException>(() => BoardCopy.RungSummary(null!));
        BoardCopy.DaysReady(14m).ShouldBe("14 days");
        BoardCopy.State(CellState.Rationed).ShouldBe("Not enough");
        Should.Throw<ArgumentNullException>(() => BoardCopy.Row(null!));
        Should.Throw<ArgumentNullException>(() => BoardCopy.Cell(null!, food.Cells[0]));
        Should.Throw<ArgumentNullException>(() => BoardCopy.Cell(food, null!));
    }

    [Fact]
    public void The_form_maps_the_newer_answers_with_their_defaults()
    {
        PlanForm form = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        PlanForm flat = new() { Postcode = "NP12 1GW", People = 1, HasCar = false, Car = CarKind.ElectricWithOutput, DwellingType = Selficient.Domain.Households.DwellingType.Flat, Floor = 5, Infants = 0 };
        form.Dependencies.Add(new DependencyForm { Name = " " });
        form.Dependencies.Add(new DependencyForm { Name = "Insulin", Kind = DependencyKind.Medicine, DaysOfStock = 28, MustStayCold = true });

        ResilienceProfile rob = form.ToResilienceProfile(Profiles.RobsHouse()).Value;
        ResilienceProfile flatProfile = flat.ToResilienceProfile(Profiles.Flat()).Value;

        rob.Car.ShouldBe(CarKind.Petrol, "a car with no kind is petrol");
        rob.Growing.ShouldBe(Growing.None);
        rob.MedicineDaysInStock.ShouldBe(7);
        rob.Floor.ShouldBe(0, "a house has no floor to speak of");
        rob.Dependencies.Count.ShouldBe(1, "the blank row is ignored");
        rob.Dependencies[0].MustStayCold.ShouldBeTrue();
        flatProfile.Car.ShouldBe(CarKind.None, "the plan said no car, whatever the kind field says");
        flatProfile.Floor.ShouldBe(5);
        flatProfile.WaterIsPumped.ShouldBeTrue();

        PlanForm electric = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, Car = CarKind.Electric, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        PlanForm noKind = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, Car = CarKind.None, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        PlanForm badRow = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        badRow.Dependencies.Add(new DependencyForm { Name = "Pump", Kind = DependencyKind.PoweredDevice, Watts = 0m, HoursPerDay = 8m });
        PlanForm nullName = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        nullName.Dependencies.Add(new DependencyForm { Name = null, Kind = DependencyKind.Supply, DaysOfStock = 3 });

        electric.ToResilienceProfile(Profiles.RobsHouse()).Value.Car.ShouldBe(CarKind.Electric);
        noKind.ToResilienceProfile(Profiles.RobsHouse()).Value.Car.ShouldBe(CarKind.Petrol, "a car of no stated kind is petrol");
        badRow.ToResilienceProfile(Profiles.RobsHouse()).Error.Code.ShouldBe("dependency.power", "a device that draws nothing is refused by the domain");
        nullName.ToResilienceProfile(Profiles.RobsHouse()).Value.Dependencies.ShouldBeEmpty("a row with no name is a blank row");
        new DependencyForm { Name = null }.ToDependency().Error.Code.ShouldBe("dependency.name");
        IndexModel.Echo(form).ShouldContain((nameof(PlanForm.Car), "None"));
        IndexModel.Echo(form).ShouldContain((nameof(PlanForm.MedicineDaysInStock), "7"));
        IndexModel.FormFields.ShouldContain(nameof(PlanForm.Floor));
    }

    internal static Dictionary<string, string> RobsHouse() => new()
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
    };

    private static Dictionary<string, string> Flat() => new()
    {
        ["Form.Postcode"] = "NP12 1GW",
        ["Form.People"] = "1",
        ["Form.Children"] = "0",
        ["Form.HasVulnerablePerson"] = "false",
        ["Form.HasPets"] = "false",
        ["Form.RunsBusinessFromHome"] = "false",
        ["Form.DwellingType"] = "Flat",
        ["Form.Storeys"] = "1",
        ["Form.Heating"] = "Electric",
        ["Form.Hob"] = "Electric",
        ["Form.HasCar"] = "false",
        ["Form.HasGarden"] = "false",
        ["Form.HasDecking"] = "false",
        ["Form.HasTallTreeNearHouse"] = "false",
        ["Form.FloodRisk"] = "Unknown",
    };

    internal static async Task<string> Post(HttpClient client, string path, Dictionary<string, string> form, params string[] extra)
    {
        using HttpResponseMessage response = await PostRaw(client, path, form, extra);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> PostRaw(HttpClient client, string path, Dictionary<string, string> form, params string[] extra)
    {
        string wizard = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        string token = Regex.Match(wizard, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"").Groups["token"].Value;
        List<KeyValuePair<string, string>> pairs = [new("__RequestVerificationToken", token), .. form];
        foreach (string pair in extra)
        {
            int at = pair.IndexOf('=', StringComparison.Ordinal);
            pairs.Add(new(pair[..at], pair[(at + 1)..]));
        }

        using FormUrlEncodedContent content = new(pairs);
        return await client.PostAsync(new Uri(path, UriKind.Relative), content);
    }

    public void Dispose() => _factory.Dispose();
}
