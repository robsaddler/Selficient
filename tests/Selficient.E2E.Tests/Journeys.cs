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
        // Part of the month is not nothing: the pill says "Have some" and is half marked, while the shelf it reaches says "Have it".
        await Expect(month.Locator("[data-choice-pill=\"have\"]")).ToHaveAttributeAsync("data-part", "");
        await Expect(month.Locator("[data-choice-pill=\"have\"] span:visible")).ToHaveTextAsync("Have some");
        await Expect(month.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync(new() { Indeterminate = true });
        await Expect(shelf.Locator("[data-choice-pill=\"have\"] span:visible")).ToHaveTextAsync("Have it");
        await Expect(shelf.Locator("[data-choice-pill=\"have\"]")).Not.ToHaveAttributeAsync("data-part", "");
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
    public async Task Less_water_than_the_plan_asks_is_still_counted_and_the_pill_says_have_some()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        ILocator water = page.Locator("[data-action-row=\"water-bottled-three-days\"]");
        ILocator box = page.Locator("#amount-water-bottled-three-days");
        ILocator pill = water.Locator("[data-choice-pill=\"have\"]");
        ILocator drinking = page.Locator("[data-board-slot] [data-board-progress] [data-progress-action=\"water-bottled-three-days\"]");

        // "Have it" offers the 36 litres the plan asks four people for.
        await water.Locator("[data-choice=\"have\"]").CheckAsync();
        await Expect(box).ToHaveValueAsync("36");
        await Expect(pill.Locator("span:visible")).ToHaveTextAsync("Have it");

        // Twenty is what is in the house: the pill does not go blank, the figure stays, and the board is drawn from it.
        await box.FillAsync("20");
        await box.PressAsync("Tab");
        await Expect(pill.Locator("span:visible")).ToHaveTextAsync("Have some");
        await Expect(pill).ToHaveAttributeAsync("data-part", "");
        await Expect(water.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync(new() { Indeterminate = true });
        await Expect(box).ToHaveValueAsync("20");
        await Expect(water.Locator("[data-progress-for]")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^You have 20 of 36 litres of bottled water\\. The rest would cost £[0-9.,]+ to £[0-9.,]+\\.$"));
        await Expect(drinking).ToHaveCountAsync(1);
        await water.ScreenshotAsync(new() { Path = Screenshots.Target("have-some-phone-light.png") });
        await page.SetViewportSizeAsync(2000, 1100);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await water.ScreenshotAsync(new() { Path = Screenshots.Target("have-some-wide-dark.png") });
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await page.SetViewportSizeAsync(390, 844);

        // It can be planned as well: some had, the rest to get.
        await water.Locator("[data-choice=\"plan\"]").CheckAsync();
        await Expect(pill.Locator("span:visible")).ToHaveTextAsync("Have some");
        await Expect(box).ToHaveValueAsync("20");
        await water.Locator("[data-choice=\"plan\"]").UncheckAsync();

        // Kept across a reload, and pressing the pill is "I have all of it".
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(pill.Locator("span:visible")).ToHaveTextAsync("Have some");
        await pill.ClickAsync();
        await Expect(box).ToHaveValueAsync("36");
        await Expect(pill.Locator("span:visible")).ToHaveTextAsync("Have it");
        await Expect(pill).Not.ToHaveAttributeAsync("data-part", "");
        await Expect(water.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync();

        // None at all is not "some".
        await box.FillAsync("0");
        await box.PressAsync("Tab");
        await Expect(pill.Locator("span:visible")).ToHaveTextAsync("Have it");
        await Expect(water.Locator("[data-choice=\"have\"]")).Not.ToBeCheckedAsync();

        AxeResult axe = await RunAxeWhenSettledAsync(page);
        axe.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_cash_rate_is_the_households_own_and_everything_planned_is_a_board_of_its_own()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        ILocator cash = page.Locator("[data-action-row=\"cash-in-small-notes\"]");
        ILocator cashBox = page.Locator("#amount-cash-in-small-notes");
        ILocator rate = page.Locator("#assume-cash");

        // The plan's rate is on the page with what it comes to, and the cash action says what it rests on.
        await Expect(rate).ToHaveValueAsync("10");
        await Expect(page.Locator("[data-assume-cash-says]")).ToHaveTextAsync("At £10 a person a day, ten days of cash for the 4 of you is £400.");
        await cash.Locator("[data-choice=\"have\"]").CheckAsync();
        await Expect(cashBox).ToHaveValueAsync("400");
        await cashBox.FillAsync("200");
        await cashBox.PressAsync("Tab");
        await Expect(cash.Locator("[data-progress-for]")).ToHaveTextAsync("You have 200 of 400 pounds in cash.");
        await Expect(cash.Locator("[data-choice=\"have\"]")).Not.ToBeCheckedAsync();

        // At five pounds a day the same £200 is the ten days; at twenty it is a quarter of them.
        await rate.FillAsync("5");
        await rate.PressAsync("Tab");
        await Expect(page.Locator("[data-assume-cash-says]")).ToHaveTextAsync("At £5 a person a day, ten days of cash for the 4 of you is £200.");
        await Expect(cash.Locator(".amount__hint")).ToContainTextAsync("The plan suggests 200, at £5 a person a day.");
        await Expect(cash.Locator("[data-progress-for]")).ToHaveTextAsync("You have 200 pounds in cash.");
        await Expect(cash.Locator("[data-choice=\"have\"]")).ToBeCheckedAsync();
        await rate.FillAsync("20");
        await rate.PressAsync("Tab");
        await Expect(cash.Locator("[data-progress-for]")).ToHaveTextAsync("You have 200 of 800 pounds in cash.");
        await Expect(cash.Locator("[data-choice=\"have\"]")).Not.ToBeCheckedAsync();
        await Expect(cash.Locator("[data-choice-pill=\"have\"] span:visible")).ToHaveTextAsync("Have some", new() { Timeout = 10_000 });

        // It is kept, a figure out of range is held to the range, and one press puts the plan's back.
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(rate).ToHaveValueAsync("20");
        await Expect(cash.Locator("[data-progress-for]")).ToHaveTextAsync("You have 200 of 800 pounds in cash.");
        await rate.FillAsync("5000");
        await rate.PressAsync("Tab");
        await Expect(rate).ToHaveValueAsync("100");
        await page.GetByRole(AriaRole.Button, new() { Name = "Use the plan's £10" }).ClickAsync();
        await Expect(rate).ToHaveValueAsync("10");
        await Expect(cash.Locator("[data-progress-for]")).ToHaveTextAsync("You have 200 of 400 pounds in cash.");

        // Nothing planned, no pill. Plan the shelf and the board can be switched to where the plan leads, while the headline stays tonight's.
        await Expect(page.Locator("#source-planned")).ToHaveCountAsync(0);
        await page.Locator("[data-action-row=\"food-no-cook-three-days\"] [data-choice=\"plan\"]").CheckAsync();
        await page.Locator("[data-action-row=\"cash-in-small-notes\"] [data-choice=\"plan\"]").CheckAsync();
        await Expect(page.Locator("label[for=\"source-planned\"]")).ToHaveTextAsync("Everything we plan to get");
        string tonight = (await page.Locator("days-ready").GetAttributeAsync("value"))!;
        await page.Locator("label[for=\"source-planned\"]").ClickAsync();
        await Expect(page.Locator("[data-board]")).ToHaveAttributeAsync("data-source", "planned");
        await Expect(page.Locator(".board__headline")).ToContainTextAsync("with everything we plan to get, this house stands on its own for");
        await Expect(page.Locator("#source-planned")).ToBeCheckedAsync();
        (await page.Locator("days-ready").GetAttributeAsync("value")).ShouldBe(tonight, "the figure in the hero is the house as it is");
        // The one table is read against today: a sentence, and every need that changes drawn twice, "Today" above "With the plan".
        await Expect(page.Locator("[data-board-comparison]")).ToContainTextAsync("Everything we plan to get turns");
        (await page.Locator(".board__row[data-paired=\"true\"][data-turned=\"false\"]").CountAsync()).ShouldBe(0, "every need drawn twice says by how much it moves");
        ILocator pair = page.Locator(".board__row[data-paired=\"true\"]").First;
        await Expect(pair.Locator(".board__delta")).ToBeVisibleAsync();
        await Expect(pair.Locator(".board__when")).ToHaveTextAsync(["Today", "With the plan"]);
        (await pair.Locator(".cell").CountAsync()).ShouldBe(10, "five cells for today and five with the plan");
        // The two lines sit one above the other, cell under cell, each the size of any other cell on the board and under the same
        // column heads. On a phone each line's words sit above it; on a wide screen they sit in front of it.
        const string Measure =
            """
            (row, wide) => {
              const cells = [...row.querySelectorAll(".cell")].map(cell => cell.getBoundingClientRect());
              const words = [...row.querySelectorAll(".board__when")].map(word => word.getBoundingClientRect());
              const single = document.querySelector(".board__row[data-paired='false'] .cell").getBoundingClientRect();
              const head = row.closest(".board__rung").querySelector(".board__col").getBoundingClientRect();
              return JSON.stringify({
                under: Math.abs(cells[0].left - cells[5].left) < 1 && cells[5].top >= cells[0].bottom,
                sameSize: Math.abs(cells[0].height - single.height) < 1 && Math.abs(cells[0].width - single.width) < 1,
                underHead: Math.abs(cells[0].left - head.left) < 1 && Math.abs(single.left - head.left) < 1,
                worded: wide
                  ? words[0].right <= cells[0].left && words[1].right <= cells[5].left && words[0].top < cells[0].bottom && words[1].top >= cells[0].bottom - 1
                  : words[0].bottom <= cells[0].top && words[1].top >= cells[0].bottom && words[1].bottom <= cells[5].top,
              });
            }
            """;
        const string Sound = "{\"under\":true,\"sameSize\":true,\"underHead\":true,\"worded\":true}";
        (await pair.EvaluateAsync<string>(Measure, false)).ShouldBe(Sound, "on a phone");
        PageViewportSizeResult phone = page.ViewportSize!;
        await page.SetViewportSizeAsync(2000, 1200);
        (await pair.EvaluateAsync<string>(Measure, true)).ShouldBe(Sound, "on a wide screen");
        // A scenario with fewer columns than the storm's five: the second line still starts under the first, with its words in
        // front. It once ran on into the empty columns beside the first line.
        await page.Locator("[data-scenario-card][value=\"stay-home\"]").ClickAsync();
        await Expect(page.Locator("[data-board]")).ToHaveAttributeAsync("data-scenario", "stay-home");
        ILocator short3 = page.Locator(".board__row[data-paired=\"true\"]").First;
        (await short3.Locator(".cell").CountAsync()).ShouldBe(6, "three cells for today and three with the plan");
        (await short3.EvaluateAsync<string>(Measure.Replace("cells[5]", "cells[3]", StringComparison.Ordinal), true)).ShouldBe(Sound, "on a wide screen with three columns");
        await page.Locator("[data-scenario-card][value=\"storm\"]").ClickAsync();
        await Expect(page.Locator("[data-board]")).ToHaveAttributeAsync("data-scenario", "storm");
        await page.SetViewportSizeAsync(phone.Width, phone.Height);
        // Narrowed to what changes, and it stays narrowed when the board is drawn again for another season.
        ILocator only = page.Locator("[data-board-only-changes]");
        await Expect(only).Not.ToBeCheckedAsync();
        ILocator once = page.Locator(".board__row[data-paired=\"false\"]").First;
        await Expect(once).ToBeVisibleAsync();
        await only.CheckAsync();
        await Expect(once).ToBeHiddenAsync();
        await Expect(pair).ToBeVisibleAsync();
        await page.Locator("label[for=\"season-summer\"]").ClickAsync();
        await Expect(page.Locator("[data-board]")).ToHaveAttributeAsync("data-season", "Summer");
        await Expect(page.Locator("[data-board-only-changes]")).ToBeCheckedAsync();
        await page.Locator("[data-board-only-changes]").UncheckAsync();
        await Expect(page.Locator(".board__row[data-paired=\"false\"]").First).ToBeVisibleAsync();
        // Clearing what is planned takes the pill away and the board goes back to the house.
        await page.Locator(".plan__tally").GetByRole(AriaRole.Button, new() { Name = "Clear what you plan to get: The whole plan" }).ClickAsync();
        await Expect(page.Locator("[data-board]")).ToHaveAttributeAsync("data-source", "house");
        await Expect(page.Locator("#source-planned")).ToHaveCountAsync(0);
        await ShouldNotScrollSidewaysAsync(page);

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
        await Until.TrueAsync(page, "async () => !!(await caches.match('/plan'))", seconds: 20);

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
    public async Task A_visitor_is_told_no_account_is_needed_and_then_where_the_plan_is_kept()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        ILocator chip = page.Locator("[data-keep-chip]");
        ILocator panel = page.Locator("#keep-dialog");

        // Before a plan: the header says it where "Sign in" would be, and so does each start button.
        await page.GotoAsync("/");
        await Expect(chip.Locator("span:visible")).ToHaveTextAsync("No account needed");
        await Expect(page.Locator(".hero .start-note").First).ToHaveTextAsync("No account needed. Your plan is saved on this device.");
        await Expect(page.Locator(".hero .start-note--account")).ToHaveTextAsync("Want it on your phone too, or safe if this device is lost? Make a free account whenever you like: just an email address, no password.");
        await Expect(page.Locator(".closing .start-note").First).ToHaveTextAsync("No account needed. Your plan is saved on this device.");
        await Expect(page.Locator(".closing .start-note--account a")).ToHaveAttributeAsync("href", "/account/sign-in");
        (await page.Locator(".ways__card h3").AllInnerTextsAsync()).ShouldBe(["On this device", "Send a copy", "An account, if you want one"], "the landing page and the panel teach the same three ways, in the same order");
        await ShouldNotScrollSidewaysAsync(page);
        await chip.ClickAsync();
        await Expect(panel).ToBeVisibleAsync();
        new Uri(page.Url).AbsolutePath.ShouldBe("/", "with the script running the chip opens the panel and goes nowhere");
        (await panel.EvaluateAsync<bool>("panel => { const box = panel.getBoundingClientRect(); return Math.abs((box.left + box.right) / 2 - innerWidth / 2) < 10 && Math.abs((box.top + box.bottom) / 2 - innerHeight / 2) < 10; }")).ShouldBeTrue("the panel opens in the middle of the screen, not in its corner");
        await Expect(panel.GetByRole(AriaRole.Heading, new() { Name = "Where your plan is kept" })).ToBeVisibleAsync();
        await Expect(panel.Locator("[data-keep-says=\"device-none\"]")).ToHaveTextAsync("Nothing is saved yet. When you make a plan it lives in this browser and nowhere else. You do not need an account.");
        await Expect(panel.Locator("[data-keep-says=\"device\"]")).ToBeHiddenAsync();
        await Expect(panel.Locator(".keep__row[aria-current]")).ToHaveCountAsync(0);
        await Expect(panel.Locator("[data-keep-safari]")).ToBeHiddenAsync();
        AxeResult open = await RunAxeWhenSettledAsync(page);
        open.Violations.ShouldBeEmpty(string.Join("\n", open.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));
        await panel.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await Expect(panel).ToBeHiddenAsync();

        // The first plan on this device: the chip changes, and the page says so once under the figure.
        await BuildPlanAsync(page);
        await Expect(chip.Locator("span:visible")).ToHaveTextAsync("Saved on this device");
        ILocator notice = page.Locator("[data-kept-notice]");
        await Expect(notice).ToBeVisibleAsync();
        await Expect(notice.Locator("span")).ToHaveTextAsync("Your plan is saved on this device. You do not need an account.");
        (await page.EvaluateAsync<bool>("() => navigator.storage.persisted().then(() => localStorage.getItem('selficient.persist-asked') === '1')")).ShouldBeTrue("the browser is asked once to keep this site's storage");
        await ShouldNotScrollSidewaysAsync(page);
        await notice.GetByRole(AriaRole.Button, new() { Name = "Got it" }).ClickAsync();
        await Expect(notice).ToBeHiddenAsync();
        await chip.ClickAsync();
        await Expect(panel.Locator("[data-keep-says=\"device\"]")).ToHaveTextAsync("Your plan lives in this browser and nowhere else. You do not need an account.");
        await Expect(panel.Locator("[data-keep-says=\"device-none\"]")).ToBeHiddenAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();

        // The next visit: the chip still says where the plan is, and the sentence is not said again.
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(chip.Locator("span:visible")).ToHaveTextAsync("Saved on this device");
        await Expect(notice).ToBeHiddenAsync();
        await page.GotoAsync("/");
        await Expect(chip.Locator("span:visible")).ToHaveTextAsync("Saved on this device");
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
    [InlineData("/receive")]
    [InlineData("/plan/shortlist")]
    [InlineData("/account/sign-in")]
    [InlineData("/account/unavailable")]
    [InlineData("/account/deleted")]
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

    [Fact]
    public async Task One_answer_can_be_changed_without_typing_the_rest_again_and_the_plan_follows_it()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();

        // A first visit: nothing kept, so nothing filled in and nothing said about it.
        await page.GotoAsync("/plan/start");
        await Expect(page.Locator("[data-wizard-kept]")).ToBeHiddenAsync();
        await Expect(page.GetByLabel("How likely is flooding where you live?")).ToHaveValueAsync("Unknown");
        await FillRobsHouseAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();

        // The plan says what it found about flooding, whatever it found, and whose map that was.
        ILocator flood = page.Locator(".risks__flood");
        await Expect(flood).ToContainTextAsync("We looked the middle of your postcode up on Natural Resources Wales's flood map and it shows a very low risk from rivers, the sea and surface water, so there is nothing about flooding in your plan.");
        await Expect(flood.GetByRole(AriaRole.Link, new() { Name = "Check your address with Natural Resources Wales" })).ToHaveAttributeAsync("href", "https://naturalresources.wales/flooding/check-your-flood-risk-by-postcode/");
        await flood.ScreenshotAsync(new() { Path = Screenshots.Target("flood-note-phone-light.png") });
        await page.SetViewportSizeAsync(2000, 1100);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await flood.ScreenshotAsync(new() { Path = Screenshots.Target("flood-note-wide-dark.png") });
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await page.SetViewportSizeAsync(390, 844);
        await Expect(page.Locator("[data-action-row=\"pet-food-two-weeks\"]")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-action-row=\"car-above-half-a-tank\"]")).ToHaveCountAsync(1);
        await page.Locator("[data-action-row=\"water-bottled-three-days\"] [data-choice=\"have\"]").CheckAsync();
        await Until.TrueAsync(page, "() => import('/js/plan-store.js').then(store => store.getChoices()).then(choices => choices['water-bottled-three-days'] === 'have')", null);

        // Back to the form: every answer is where it was left, and the page says so.
        await page.GetByRole(AriaRole.Link, new() { Name = "Change your answers" }).ClickAsync();
        await Expect(page.Locator("[data-wizard-kept]")).ToHaveTextAsync("These are the answers your plan was built from. Change what is different and build it again; what you have ticked stays as it is.");
        await Expect(page.GetByLabel("Postcode", new() { Exact = true })).ToHaveValueAsync("NP12 1GW");
        await Expect(page.GetByLabel("How many people live in your home?")).ToHaveValueAsync("4");
        await Expect(page.GetByLabel("What kind of home is it?")).ToHaveValueAsync("Detached");
        await Expect(page.GetByLabel("Is there timber decking?")).ToBeCheckedAsync();
        await Expect(page.GetByLabel("Do you have pets?")).Not.ToBeCheckedAsync();
        await ShouldNotScrollSidewaysAsync(page);
        await page.Locator("[data-wizard-start]").ScreenshotAsync(new() { Path = Screenshots.Target("wizard-kept-phone-light.png") });
        await page.SetViewportSizeAsync(2000, 1100);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await page.Locator("[data-wizard-start]").ScreenshotAsync(new() { Path = Screenshots.Target("wizard-kept-wide-dark.png") });
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await page.SetViewportSizeAsync(390, 844);
        AxeResult wizard = await page.RunAxe();
        wizard.Violations.ShouldBeEmpty();

        // Two answers changed, and the plan is the plan for them: pets get their row, and an electric car is never told about a tank.
        await page.GetByLabel("Do you have pets?").CheckAsync();
        await page.GetByLabel("Is there a car?").SelectOptionAsync("Electric");
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(page.Locator("[data-action-row=\"pet-food-two-weeks\"]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-action-row=\"car-above-half-charge\"] .action__title")).ToHaveTextAsync("Keep the car above half charge from November to March");
        await Expect(page.Locator("[data-action-row=\"car-above-half-a-tank\"]")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-action-row=\"water-bottled-three-days\"] [data-choice=\"have\"]")).ToBeCheckedAsync(new() { Timeout = 10_000 });
        await ShouldNotScrollSidewaysAsync(page);
        AxeResult plan = await RunAxeWhenSettledAsync(page);
        plan.Violations.ShouldBeEmpty();

        // A different postcode typed on the landing page is a different house: the form is not filled in from this one.
        await page.GotoAsync("/plan/start?postcode=SW1A+1AA");
        await Expect(page.GetByLabel("Postcode", new() { Exact = true })).ToHaveValueAsync("SW1A 1AA");
        await page.WaitForFunctionAsync("() => document.readyState === 'complete'");
        await Expect(page.Locator("[data-wizard-kept]")).ToBeHiddenAsync();
        await Expect(page.GetByLabel("How many people live in your home?")).ToHaveValueAsync("2");

        // The same postcode, however it was typed, is this house.
        await page.GotoAsync("/plan/start?postcode=np121gw");
        await Expect(page.Locator("[data-wizard-kept]")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("How many people live in your home?")).ToHaveValueAsync("4");
        await Expect(page.GetByLabel("Is there a car?")).ToHaveValueAsync("Electric");
    }

    [Fact]
    public async Task A_postcode_the_flood_map_calls_high_gets_its_nations_warnings_as_a_link_on_the_action()
    {
        // A York postcode the stand-in map answers as high from a river, found the way the site finds it.
        Selficient.Infrastructure.Geography.FakeGeocoder geocoder = new();
        Selficient.Infrastructure.Geography.FakeFloodRiskProvider map = new();
        string postcode = string.Empty;
        for (int district = 1; district < 60 && postcode.Length == 0; district++)
        {
            string candidate = $"YO{district} 1AA";
            Selficient.Application.Geography.Locality place = (await geocoder.LocateAsync(Selficient.Domain.Measures.Postcode.Create(candidate).Value, CancellationToken.None)).Value;
            Selficient.Domain.Primitives.Result<Selficient.Application.Geography.FloodFinding> found = await map.AtAsync(place.Coordinates, place.Nation, CancellationToken.None);
            if (found.IsSuccess && found.Value.Band == Selficient.Domain.Households.FloodRisk.High)
            {
                postcode = candidate;
            }
        }

        postcode.ShouldNotBeEmpty();
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/plan/start");
        await FillRobsHouseAsync(page);
        await page.GetByLabel("Postcode", new() { Exact = true }).FillAsync(postcode);
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();

        ILocator action = page.Locator("[data-action-row=\"flood-warnings-and-plan\"]");
        await Expect(action.Locator(".action__title")).ToHaveTextAsync("Sign up for free flood warnings and write a one-page flood plan");
        ILocator official = action.Locator("[data-official-link]");
        await Expect(official).ToHaveAttributeAsync("href", "https://www.gov.uk/get-flood-warnings");
        await Expect(official).ToHaveAttributeAsync("target", "_blank");
        await Expect(official).ToContainTextAsync("Sign up for flood warnings on GOV.UK");
        await Expect(page.Locator(".risks__flood")).ToContainTextAsync("it shows a high risk from rivers or the sea, so flooding is in your plan.");
        await Expect(page.Locator("[data-action-row=\"flood-protection-kit\"]")).ToHaveCountAsync(1);
        await ShouldNotScrollSidewaysAsync(page);
        await action.ScreenshotAsync(new() { Path = Screenshots.Target("flood-action-phone-light.png") });
        await page.SetViewportSizeAsync(2000, 1100);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await action.ScreenshotAsync(new() { Path = Screenshots.Target("flood-action-wide-dark.png") });
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await page.SetViewportSizeAsync(390, 844);

        AxeResult axe = await RunAxeWhenSettledAsync(page);
        axe.Violations.ShouldBeEmpty();
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
        await page.GetByLabel("Is there a car?").SelectOptionAsync("Petrol");
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
