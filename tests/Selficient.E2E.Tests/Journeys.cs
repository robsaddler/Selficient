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
        (await page.Locator("days-ready").GetAttributeAsync("value")).ShouldBe("0.5");
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

        // Days Ready is the weakest of water, food and light, so all three must move.
        await page.Locator("[data-action-id=\"water-bottled-three-days\"]").CheckAsync();
        await page.Locator("[data-action-id=\"food-no-cook-three-days\"]").CheckAsync();
        await page.Locator("[data-action-id=\"headtorches\"]").CheckAsync();
        await Expect(page.Locator("days-ready")).ToHaveAttributeAsync("value", new System.Text.RegularExpressions.Regex("^(?!0\\.5$).+"));
        decimal after = decimal.Parse((await page.Locator("days-ready").GetAttributeAsync("value"))!, CultureInfo.InvariantCulture);
        after.ShouldBeGreaterThan(0.5m);

        await page.ReloadAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Your house could stand on its own for" }).WaitForAsync();
        await Expect(page.Locator("[data-action-id=\"water-bottled-three-days\"]")).ToBeCheckedAsync();
        await Expect(page.Locator("days-ready")).ToHaveAttributeAsync("value", after.ToString(CultureInfo.InvariantCulture));
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
        json.ShouldContain("\"selficient\": 1");
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
