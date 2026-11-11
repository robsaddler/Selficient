using System.Globalization;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Selficient.E2E.Tests;

[Collection(BrowserCollection.Name)]
public sealed class Journeys
{
    private readonly Browser _browser;

    public Journeys(Browser browser) => _browser = browser;

    [Fact]
    public async Task Postcode_to_plan_on_a_phone_in_under_a_minute_with_nothing_scrolling_sideways()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/plan/start");
        await FillRobsHouseAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();

        await page.GetByRole(AriaRole.Heading, new() { Name = "Your house could stand on its own for" }).WaitForAsync();
        (await page.Locator("days-ready").GetAttributeAsync("value")).ShouldBe("1", "the board's storm figure for a house with nothing ticked");
        await Expect(page.Locator("[data-board]")).ToBeVisibleAsync();
        await ShouldNotScrollSidewaysAsync(page);
        (await page.Locator("[data-action-id=\"water-bottled-three-days\"]").CountAsync()).ShouldBe(1);
        await page.GetByText("NP12, Wales, 232 m above sea level").WaitForAsync();
    }

    [Fact]
    public async Task Ticking_moves_days_ready_and_survives_a_reload()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);

        // In a storm the shops want cash and the tins fill the gap: the board redraws and the figure follows it.
        await page.Locator("[data-action-id=\"water-bottled-three-days\"]").CheckAsync();
        await page.Locator("[data-action-id=\"food-no-cook-three-days\"]").CheckAsync();
        await page.Locator("[data-action-id=\"cash-in-small-notes\"]").CheckAsync();
        await Expect(page.Locator("days-ready")).ToHaveAttributeAsync("value", new System.Text.RegularExpressions.Regex("^(?!1$).+"));
        decimal after = decimal.Parse((await page.Locator("days-ready").GetAttributeAsync("value"))!, CultureInfo.InvariantCulture);
        after.ShouldBeGreaterThan(1m);
        await Expect(page.Locator("[data-turns-for=\"headtorches\"]")).ToBeVisibleAsync();
        // The strip under the figure follows the board: the food bar grows with the tins.
        await Expect(page.Locator(".hero--plan [data-strip] [data-need=\"Food\"]")).ToHaveAttributeAsync("data-days", new System.Text.RegularExpressions.Regex("^(?!1$).+"));
        // The verdict bar at the top of the viewport says the same figure as the hero.
        await Expect(page.Locator("[data-verdict-days]")).ToHaveTextAsync(after.ToString(CultureInfo.InvariantCulture));
        await Expect(page.Locator("[data-tier-outcome=\"ThisWeek\"]")).ToContainTextAsync("With everything up to here ticked, the house stands for");

        await page.ReloadAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Your house could stand on its own for" }).WaitForAsync();
        await Expect(page.Locator("[data-action-id=\"water-bottled-three-days\"]")).ToBeCheckedAsync();
        await Expect(page.Locator("days-ready")).ToHaveAttributeAsync("value", after.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Planning_counts_the_cost_ruling_out_quietens_the_row_and_a_cleared_stretch_comes_back()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        ILocator water = page.Locator("[data-action-row=\"water-bottled-three-days\"]");
        ILocator radio = page.Locator("[data-action-row=\"radio-dab-fm-with-crank\"]");
        ILocator whole = page.Locator(".plan__tally");
        ILocator week = page.Locator("[data-tier=\"ThisWeek\"] > .tally");

        // Plan to: counted, costed, and today's figure stays where it was.
        await water.Locator("[data-choice=\"plan\"]").CheckAsync();
        await page.Locator("[data-action-row=\"food-no-cook-three-days\"] [data-choice=\"plan\"]").CheckAsync();
        await page.Locator("[data-action-row=\"cash-in-small-notes\"] [data-choice=\"plan\"]").CheckAsync();
        await Expect(whole.Locator("[data-tally-kind=\"plan\"] [data-tally-figure]")).ToContainTextAsync("3, ");
        await Expect(whole.Locator("[data-tally-kind=\"plan\"] [data-tally-figure]")).ToContainTextAsync("still to spend");
        await Expect(week.Locator("[data-tally-kind=\"plan\"] [data-tally-figure]")).ToContainTextAsync("3, ");
        await Expect(whole.Locator("[data-tally-standing]")).ToContainTextAsync("Today the house stands for 1 day. With everything you plan to get, it stands for");
        (await page.Locator("days-ready").GetAttributeAsync("value")).ShouldBe("1", "meaning to buy something does not move the figure");
        await Expect(whole.Locator("[data-tally-kind=\"plan\"] button")).ToHaveAttributeAsync("aria-disabled", "false");

        // One at most: having it lifts the plan.
        await water.Locator("[data-choice=\"have\"]").CheckAsync();
        await Expect(water.Locator("[data-choice=\"plan\"]")).Not.ToBeCheckedAsync();
        await Expect(whole.Locator("[data-tally-kind=\"have\"] [data-tally-figure]")).ToContainTextAsync("1 of ");
        await Expect(whole.Locator("[data-tally-kind=\"plan\"] [data-tally-figure]")).ToContainTextAsync("2, ");

        // Not for me: the row keeps its name and its choice and drops the rest, and the board stops suggesting it.
        await Expect(page.Locator("[data-turns-for=\"radio-dab-fm-with-crank\"]")).ToBeVisibleAsync();
        await radio.Locator("[data-choice=\"skip\"]").CheckAsync();
        await Expect(radio.Locator(".action__why")).ToBeHiddenAsync();
        await Expect(radio.Locator(".action__title")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-turns-for=\"radio-dab-fm-with-crank\"]")).ToHaveCountAsync(0);
        await Expect(week.Locator("[data-tally-kind=\"skip\"] [data-tally-figure]")).ToHaveTextAsync("1");
        await Expect(page.Locator("[data-tier-outcome=\"ThisWeek\"]")).ToContainTextAsync("except what is not for you");
        // Pressing the pressed pill clears it: back to not decided.
        await radio.Locator("[data-choice=\"skip\"]").UncheckAsync();
        await Expect(week.Locator("[data-tally-kind=\"skip\"] [data-tally-figure]")).ToHaveTextAsync("None");
        await Expect(radio.Locator(".action__why")).ToBeVisibleAsync();

        // Plan everything not decided in this stretch, then take it back.
        await week.GetByRole(AriaRole.Button, new() { Name = "Plan all of these not yet decided: This week" }).ClickAsync();
        await Expect(page.Locator("[data-undo]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-undo-said]")).ToHaveTextAsync("Everything not decided in this stretch is now planned.");
        await Expect(week.Locator("[data-tally-kind=\"none\"] [data-tally-figure]")).ToHaveTextAsync("None");
        await Expect(radio.Locator("[data-choice=\"plan\"]")).ToBeCheckedAsync();
        await Expect(week.Locator("[data-tally-kind=\"none\"] button")).ToHaveAttributeAsync("aria-disabled", "true");
        await page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-undo]")).ToBeHiddenAsync();
        await Expect(radio.Locator("[data-choice=\"plan\"]")).Not.ToBeCheckedAsync();
        await Expect(week.Locator("[data-tally-kind=\"plan\"] [data-tally-figure]")).ToContainTextAsync("2, ");

        // Clear what is planned in the stretch; what the house has is left alone.
        await week.GetByRole(AriaRole.Button, new() { Name = "Clear what you plan to get: This week" }).ClickAsync();
        await Expect(page.Locator("[data-undo-said]")).ToHaveTextAsync("What you planned in this stretch is back to not decided.");
        await Expect(week.Locator("[data-tally-kind=\"plan\"] [data-tally-figure]")).ToHaveTextAsync("None yet");
        await Expect(water.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(week.Locator("[data-tally-kind=\"plan\"] [data-tally-figure]")).ToContainTextAsync("2, ");
        await ShouldNotScrollSidewaysAsync(page);

        // All of it survives a reload, and an export carries the three lists.
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(water.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync();
        await Expect(page.Locator("[data-action-row=\"cash-in-small-notes\"] [data-choice=\"plan\"]")).ToBeCheckedAsync();
        await Expect(whole.Locator("[data-tally-kind=\"plan\"] [data-tally-figure]")).ToContainTextAsync("2, ");
        await radio.Locator("[data-choice=\"skip\"]").CheckAsync();
        IDownload download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Export as a file" }).ClickAsync());
        string path = Path.Combine(Path.GetTempPath(), $"selficient-e2e-{Guid.NewGuid():N}.json");
        await download.SaveAsAsync(path);
        string json = await File.ReadAllTextAsync(path);
        File.Delete(path);
        System.Text.RegularExpressions.Regex.IsMatch(json, "\"ticks\": \\[\\s*\"water-bottled-three-days\"\\s*\\]").ShouldBeTrue(json);
        System.Text.RegularExpressions.Regex.IsMatch(json, "\"skipped\": \\[\\s*\"radio-dab-fm-with-crank\"\\s*\\]").ShouldBeTrue(json);
        json.ShouldContain("\"planned\": [");
        json.ShouldContain("cash-in-small-notes");

        AxeResult axe = await RunAxeWhenSettledAsync(page);
        axe.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ten_days_of_tins_are_counted_as_ten_days_across_both_food_rows()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        ILocator shelf = page.Locator("[data-action-row=\"food-no-cook-three-days\"]");
        ILocator month = page.Locator("[data-action-row=\"food-one-month\"]");
        ILocator shelfBox = page.Locator("#amount-food-no-cook-three-days");
        ILocator monthBox = page.Locator("#amount-food-one-month");
        ILocator whole = page.Locator(".plan__tally");

        // The question is out of sight until the action is held, then offers what the plan asks for.
        await Expect(shelfBox).ToBeHiddenAsync();
        await shelf.Locator("[data-choice=\"have\"]").CheckAsync();
        await Expect(shelfBox).ToBeVisibleAsync();
        await Expect(shelfBox).ToHaveValueAsync("3");
        await Expect(shelf.GetByText("days of food for the 4 of you")).ToBeVisibleAsync();
        await Expect(shelf.GetByText("The plan suggests 3.")).ToBeVisibleAsync();

        // Ten days: the shelf is held, the month is a third met and costed for the rest, and both rows show the one figure.
        await shelfBox.FillAsync("10");
        await shelfBox.PressAsync("Tab");
        await Expect(shelf.Locator("[data-progress-for]")).ToHaveTextAsync("You have 10 days of food.");
        await Expect(month.Locator("[data-progress-for]")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^You have 10 of 30 days of food\\. The rest would cost £[0-9.,]+ to £[0-9.,]+\\.$"));
        await Expect(monthBox).ToBeVisibleAsync();
        await Expect(monthBox).ToHaveValueAsync("10");
        await Expect(shelf.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync();
        await Expect(month.Locator("[data-choice=\"have\"]")).Not.ToBeCheckedAsync();
        await Expect(whole.Locator("[data-tally-kind=\"have\"] [data-tally-figure]")).ToContainTextAsync("1 of ");

        // A month from either box holds both rows.
        await monthBox.FillAsync("30");
        await monthBox.PressAsync("Tab");
        await Expect(month.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync();
        await Expect(shelfBox).ToHaveValueAsync("30");
        await Expect(month.Locator("[data-progress-for]")).ToHaveTextAsync("You have 30 days of food.");
        await Expect(whole.Locator("[data-tally-kind=\"have\"] [data-tally-figure]")).ToContainTextAsync("2 of ");

        // Taking "Have it" off the month drops the figure to the shelf below it; off the shelf, the figure goes.
        await month.Locator("[data-choice=\"have\"]").UncheckAsync();
        await Expect(shelfBox).ToHaveValueAsync("3");
        await Expect(shelf.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync();
        await Expect(month.Locator("[data-progress-for]")).ToContainTextAsync("You have 3 of 30 days of food.");
        await shelf.Locator("[data-choice=\"have\"]").UncheckAsync();
        await Expect(shelfBox).ToBeHiddenAsync();
        await Expect(monthBox).ToBeHiddenAsync();
        await Expect(month.Locator("[data-progress-for]")).ToBeHiddenAsync();

        // A figure too big to be true is held to what the plan can count, and nonsense clears it.
        await shelf.Locator("[data-choice=\"have\"]").CheckAsync();
        await shelfBox.FillAsync("99999");
        await shelfBox.PressAsync("Tab");
        await Expect(month.Locator("[data-progress-for]")).ToHaveTextAsync("You have 365 days of food.");
        await shelfBox.FillAsync("10");
        await shelfBox.PressAsync("Tab");
        await Expect(month.Locator("[data-progress-for]")).ToContainTextAsync("You have 10 of 30 days of food.");

        // It survives a reload, clearing what is held clears the count, and Undo brings both back.
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(shelfBox).ToHaveValueAsync("10");
        await Expect(month.Locator("[data-progress-for]")).ToContainTextAsync("You have 10 of 30 days of food.");
        await whole.GetByRole(AriaRole.Button, new() { Name = "Clear what you have: The whole plan" }).ClickAsync();
        await Expect(shelfBox).ToBeHiddenAsync();
        await Expect(month.Locator("[data-progress-for]")).ToBeHiddenAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(shelfBox).ToHaveValueAsync("10");
        await Expect(shelf.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync();
        await ShouldNotScrollSidewaysAsync(page);

        IDownload download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Export as a file" }).ClickAsync());
        string path = Path.Combine(Path.GetTempPath(), $"selficient-e2e-{Guid.NewGuid():N}.json");
        await download.SaveAsAsync(path);
        string json = await File.ReadAllTextAsync(path);
        File.Delete(path);
        json.ShouldContain("\"food-days\": 10");

        AxeResult axe = await RunAxeWhenSettledAsync(page);
        axe.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_then_start_again_then_import_brings_the_plan_back()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        await page.Locator("[data-action-id=\"headtorches\"]").CheckAsync();

        IDownload download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Export as a file" }).ClickAsync());
        string path = Path.Combine(Path.GetTempPath(), $"selficient-e2e-{Guid.NewGuid():N}.json");
        await download.SaveAsAsync(path);
        string json = await File.ReadAllTextAsync(path);
        json.ShouldContain("\"selficient\": 3");
        json.ShouldContain("headtorches");

        await page.EvaluateAsync("() => indexedDB.deleteDatabase('selficient')");
        await page.GotoAsync("/plan");
        await page.GetByRole(AriaRole.Heading, new() { Name = "No plan on this device yet" }).WaitForAsync();

        await page.Locator("[data-import-plan]").SetInputFilesAsync(path);
        await page.GetByRole(AriaRole.Heading, new() { Name = "Your house could stand on its own for" }).WaitForAsync();
        await Expect(page.Locator("[data-action-id=\"headtorches\"]")).ToBeCheckedAsync();
        File.Delete(path);
    }

    [Fact]
    public async Task The_saved_plan_opens_offline_with_the_honest_banner()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);

        // Wait for the worker to take control and for the plan to be kept under its address.
        await page.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null", null, new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync("async () => !!(await caches.match('/plan'))", null, new() { Timeout = 20_000 });

        await context.SetOfflineAsync(true);
        await page.GotoAsync("/plan");
        await page.GetByRole(AriaRole.Heading, new() { Name = "Your house could stand on its own for" }).WaitForAsync();
        await Expect(page.Locator("[data-offline-banner]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-offline-banner]")).ToContainTextAsync("You are offline.");

        await page.GotoAsync("/plan/solar");
        await page.GetByRole(AriaRole.Heading, new() { Name = "You are offline, which is rather the point" }).WaitForAsync();
        await context.SetOfflineAsync(false);
    }

    [Fact]
    public async Task The_solar_estimate_reads_on_a_phone()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/plan/solar");
        await page.GetByLabel("Postcode", new() { Exact = true }).FillAsync("NP12 1GW");
        // The radio itself is visually hidden; a person taps the compass point.
        await page.Locator("label[for=\"aspect-SouthWest\"]").ClickAsync();
        await Expect(page.Locator("#aspect-SouthWest")).ToBeCheckedAsync();
        await page.GetByLabel("How many panels would fit?").FillAsync("13");
        await page.GetByLabel("How many storeys, counting rooms in the roof?").FillAsync("3");
        await page.GetByLabel("How many people live in your home?").FillAsync("4");
        await page.GetByRole(AriaRole.Button, new() { Name = "Estimate it" }).ClickAsync();

        await page.GetByRole(AriaRole.Heading, new() { Name = "Solar and a battery could save you about" }).WaitForAsync();
        (await page.Locator(".figure__amount").InnerTextAsync()).ShouldStartWith("£");
        await ShouldNotScrollSidewaysAsync(page);
    }

    [Fact]
    public async Task The_board_redraws_for_a_source_a_season_and_a_scenario()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);

        ILocator house = page.Locator(".board__sheet[data-source=\"house\"][data-season=\"Winter\"]");
        ILocator battery = page.Locator(".board__sheet[data-source=\"rooftop-solar-and-battery\"][data-season=\"Winter\"]");
        await Expect(house).ToBeVisibleAsync();
        await Expect(battery).ToHaveCountAsync(0);

        // A source posts for its sheet; the pill stays chosen on the redrawn board and the figure stays the house's.
        string before = (await page.Locator("days-ready").GetAttributeAsync("value"))!;
        await page.Locator("label[for=\"source-rooftop-solar-and-battery\"]").ClickAsync();
        await Expect(battery).ToBeVisibleAsync();
        await Expect(house).ToHaveCountAsync(0);
        await Expect(battery.Locator(".board__need", new() { HasText = "Warmth" })).ToBeVisibleAsync();
        await Expect(page.Locator("#source-rooftop-solar-and-battery")).ToBeCheckedAsync();
        await Expect(page.Locator("days-ready")).ToHaveAttributeAsync("value", before);

        await page.Locator("label[for=\"season-summer\"]").ClickAsync();
        await Expect(page.Locator(".board__sheet[data-source=\"rooftop-solar-and-battery\"][data-season=\"Summer\"]")).ToBeVisibleAsync();

        await page.Locator("[data-scenario-card][value=\"snow\"]").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Snow closes the road: what holds, what goes, and when" })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-hero-scenario]")).ToContainTextAsync("In snow closes the road.");

        // A row opens to its reason.
        await page.Locator(".board__sheet:not([hidden]) .board__row").First.Locator("summary").ClickAsync();
        await Expect(page.Locator(".board__sheet:not([hidden]) .board__why").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Something_the_household_relies_on_gets_a_row_and_survives_a_reload()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);

        await page.Locator("#rely-name").FillAsync("Oxygen concentrator");
        await page.Locator("#rely-watts").FillAsync("300");
        await page.Locator("#rely-hours").FillAsync("24");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add it to the board" }).ClickAsync();

        await Expect(page.Locator("[data-rely-list]")).ToContainTextAsync("Oxygen concentrator: 300 W for 24 hours a day");
        await Expect(page.Locator("[data-board]")).ToContainTextAsync("Oxygen concentrator");
        await Expect(page.Locator("days-ready")).ToHaveAttributeAsync("value", "0.5");

        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(page.Locator("[data-rely-list]")).ToContainTextAsync("Oxygen concentrator");
        await Expect(page.Locator("days-ready")).ToHaveAttributeAsync("value", "0.5");
    }

    [Fact]
    public async Task Use_my_location_fills_the_postcode_box()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        await context.GrantPermissionsAsync(["geolocation"]);
        await context.SetGeolocationAsync(new Geolocation { Latitude = 51.6655f, Longitude = -3.2031f });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");

        ILocator button = page.GetByRole(AriaRole.Button, new() { Name = "Use my location" });
        await Expect(button).ToBeVisibleAsync();
        await button.ClickAsync();

        // On the landing page finding the postcode is starting the plan: the wizard opens with it in the box.
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/plan/start");
        await Expect(page.Locator("#Form_Postcode")).ToHaveValueAsync("NP12 1GW");

        // On the wizard the button fills the box and waits for the person.
        await page.Locator("#Form_Postcode").FillAsync("");
        await page.GetByRole(AriaRole.Button, new() { Name = "Use my location" }).ClickAsync();
        await Expect(page.Locator("#Form_Postcode")).ToHaveValueAsync("NP12 1GW");
        await Expect(page.Locator("#wizard-locate-status")).ToHaveTextAsync("Your postcode is NP12 1GW. Check it and carry on.");
    }

    [Fact]
    public async Task Clearing_the_plan_empties_this_browser()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        await page.Locator("[data-action-id=\"headtorches\"]").CheckAsync();

        await page.Locator(".clear summary").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear it" }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/");

        await page.GotoAsync("/plan");
        await Expect(page.Locator("[data-resubmit]")).ToBeAttachedAsync();
        await Expect(page.Locator("[data-plan]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_install_button_is_hidden_where_the_browser_cannot_install()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/");

        await Expect(page.Locator("[data-install]")).ToBeHiddenAsync();
        await ShouldNotScrollSidewaysAsync(page);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/plan/start")]
    [InlineData("/plan/solar")]
    [InlineData("/plan/photos")]
    [InlineData("/privacy")]
    [InlineData("/terms")]
    [InlineData("/offline")]
    [InlineData("/styleguide")]
    public async Task Every_page_has_no_accessibility_violations_and_no_sideways_scroll(string path)
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync(path);
        AxeResult axe = await RunAxeWhenSettledAsync(page);

        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));
        await ShouldNotScrollSidewaysAsync(page);
    }

    [Fact]
    public async Task The_plan_itself_has_no_accessibility_violations()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);

        AxeResult axe = await RunAxeWhenSettledAsync(page);

        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));
    }

    /// <summary>
    /// The Days Ready figure lifts in over 900 ms; sampling its colour mid-reveal reports the
    /// faded frame as a contrast failure. Audit once every animation on the page has finished.
    /// </summary>
    private static async Task<AxeResult> RunAxeWhenSettledAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.getAnimations().every((a) => a.playState === 'finished' || a.playState === 'idle')");
        return await page.RunAxe();
    }

    [Fact]
    public async Task Sharing_the_figure_copies_a_link_that_lands_on_the_card_and_says_nothing_about_the_house()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Share this figure" }).ClickAsync();
        await Expect(page.Locator("[data-share-status]")).ToHaveTextAsync("Link copied. Paste it into the group chat.");
        string copied = await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        copied.ShouldEndWith("/share/storm/1");
        copied.ShouldNotContain("NP12");
        await page.GotoAsync(copied);
        await page.GetByRole(AriaRole.Heading, new() { Name = "Our house could stand on its own for" }).WaitForAsync();
        (await page.Locator("days-ready").GetAttributeAsync("value")).ShouldBe("1");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Work out yours" })).ToBeVisibleAsync();
        await ShouldNotScrollSidewaysAsync(page);
        IAPIResponse card = await page.APIRequest.GetAsync(copied + ".png");
        card.Status.ShouldBe(200);
        card.Headers["content-type"].ShouldBe("image/png");
        (await card.BodyAsync()).Length.ShouldBeGreaterThan(10_000);
    }

    [Fact]
    public async Task The_deals_page_lists_the_cheapest_kit_and_a_tip_is_taken_with_a_sentence()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/deals");
        await page.GetByRole(AriaRole.Heading, new() { Name = "The cheapest kit for a three-day plan" }).WaitForAsync();
        (await page.Locator(".deal").CountAsync()).ShouldBeGreaterThan(15);
        await ShouldNotScrollSidewaysAsync(page);

        await BuildPlanAsync(page);
        ILocator picks = page.Locator("[data-action-id=\"headtorches\"]").Locator("xpath=ancestor::li[contains(@class,'action')]").Locator("details.picks");
        await picks.Locator("summary").First.ClickAsync();
        await picks.Locator("details.tip summary").ClickAsync();
        await picks.Locator("input[name=\"Link\"]").FillAsync("https://www.argos.co.uk/product/123");
        await picks.GetByRole(AriaRole.Button, new() { Name = "Send the link" }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Thank you" }).WaitForAsync();
        await Expect(page.Locator("[data-tip-outcome=\"taken\"]")).ToContainTextAsync("We will open it tonight");
    }

    private static async Task BuildPlanAsync(IPage page)
    {
        await page.GotoAsync("/plan/start");
        await FillRobsHouseAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Your house could stand on its own for" }).WaitForAsync();
        // The page is complete without its script, but a tick made before the script listens
        // would not be remembered.
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
    }

    internal static async Task FillRobsHouseAsync(IPage page)
    {
        await page.GetByLabel("Postcode", new() { Exact = true }).FillAsync("NP12 1GW");
        await page.GetByLabel("How many people live in your home?").FillAsync("4");
        await page.GetByLabel("How many of them are children?").FillAsync("2");
        await page.GetByLabel("Does anyone's income depend on being online at home?").CheckAsync();
        await page.GetByLabel("What kind of home is it?").SelectOptionAsync("Detached");
        await page.GetByLabel("How many storeys, counting rooms in the roof?").FillAsync("3");
        await page.GetByLabel("Is there a car?").CheckAsync();
        await page.GetByLabel("Is there a garden?").CheckAsync();
        await page.GetByLabel("Is there timber decking?").CheckAsync();
        await page.GetByLabel("Is there a tree tall enough to reach the house if it fell?").CheckAsync();
    }

    private static async Task ShouldNotScrollSidewaysAsync(IPage page)
    {
        bool scrolls = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        scrolls.ShouldBeFalse("the page must not scroll sideways at phone width");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
