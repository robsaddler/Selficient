using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Selficient.TestKit;

namespace Selficient.E2E.Tests;

/// <summary>
/// The assumptions desk in a real browser: an expert corrects a figure, the publisher reads what it
/// would change and publishes, the next plan is built from it, and the set is put back. It hosts an
/// application of its own, because a published set is in force for every plan the host builds and
/// the other journeys pin the shipped figures.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class Desk : IAsyncLifetime
{
    private const string Lift = "[data-figure=\"stairlift-batteries.stock\"]";

    private readonly Browser _browser;
    private readonly SelficientWebApplicationFactory _application = new SelficientWebApplicationFactory()
        .WithSetting("RateLimits:PlanPerMinute", "10000")
        .WithSetting("RateLimits:BoardPerMinute", "10000")
        .WithSetting("RateLimits:ApiPerMinute", "10000")
        .WithSetting("RateLimits:SignInPerMinute", "10000")
        .WithSetting("Operator:Enabled", "true");

    private string _origin = string.Empty;

    public Desk(Browser browser) => _browser = browser;

    public async ValueTask InitializeAsync()
    {
        _origin = _application.StartOnKestrel(0);
        // Rob first: on a new database the first account made is the one who may publish.
        await _application.CreateOperatorAsync();
        await _application.CreateOperatorAsync("expert");
    }

    public ValueTask DisposeAsync()
    {
        _application.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task An_expert_corrects_a_figure_the_publisher_reads_what_it_changes_and_publishes_and_the_next_plan_follows()
    {
        await using IBrowserContext expertsBrowser = await _browser.SizedAsync(1440, 900, mobile: false, theme: "light", origin: _origin);
        IPage expert = await expertsBrowser.NewPageAsync();
        await SignInAsync(expert, "expert");

        // The expert finds the stairlift where a household would, under getting upstairs, and by searching.
        await expert.GetByRole(AriaRole.Link, new() { Name = "The figures" }).ClickAsync();
        await Expect(expert.Locator("[data-group=\"Getting upstairs\"] h2")).ToHaveTextAsync("Getting upstairs");
        await expert.GetByLabel("Find a figure").FillAsync("stairlift");
        await expert.GetByRole(AriaRole.Button, new() { Name = "Find", Exact = true }).ClickAsync();
        await Expect(expert.Locator("[data-sheet-lede]")).ToContainTextAsync("figures match \"stairlift\", from both tabs.");
        ILocator lift = expert.Locator(Lift);
        await Expect(lift.Locator("[data-in-force-value]")).ToHaveTextAsync("2");
        await Expect(lift.Locator(".desk-figure__form")).ToBeHiddenAsync();

        // A value that is not a number is answered at the figure, with the box still open on what was typed.
        await lift.GetByText("Correct this figure").ClickAsync();
        await lift.GetByLabel("The value, in days").FillAsync("not long");
        await lift.GetByLabel("Why it should change").FillAsync("Old batteries hold less.");
        await lift.GetByLabel("Where the value comes from").FillAsync("A service engineer's visit");
        await lift.GetByRole(AriaRole.Button, new() { Name = "Put it in the draft" }).ClickAsync();
        await Expect(lift.Locator("[data-figure-refusal]")).ToHaveTextAsync("Give the new value as a number, such as 2 or 0.5.");
        await Expect(lift.GetByLabel("Why it should change")).ToHaveValueAsync("Old batteries hold less.");
        await Expect(lift).ToBeInViewportAsync();

        // Corrected, it is drafted: the card says what a household would be told, and the figure in force has not moved.
        await lift.GetByLabel("The value, in days").FillAsync("1");
        await lift.GetByRole(AriaRole.Button, new() { Name = "Put it in the draft" }).ClickAsync();
        await Expect(expert.Locator(".callout[role=\"status\"]")).ToContainTextAsync("is drafted as 1. Nothing changes until the draft is published.");
        await Expect(expert).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("#f-stairlift-batteries\\.stock$"));
        await Expect(lift).ToBeInViewportAsync();
        await Expect(lift).ToHaveAttributeAsync("data-state", "drafted");
        await Expect(lift.Locator("[data-in-force-value]")).ToHaveTextAsync("2");
        await Expect(lift.Locator("[data-chip=\"drafted\"]")).ToHaveTextAsync("Drafted as 1 by expert");
        await Expect(expert.Locator("[data-drafted-says]")).ToContainTextAsync("of trips to bed and the bathroom on healthy batteries");
        await Expect(expert.Locator(".figure-card", new() { Has = expert.Locator(Lift) }).Locator("[data-says]")).ToContainTextAsync("About two days of trips to bed and the bathroom");
        await ShouldBeSoundAsync(expert);
        await expert.ScreenshotAsync(new() { Path = Screenshots.Target("desk-drafted-desktop-light.png") });

        // The expert may not publish, and is told who can.
        await expert.GetByRole(AriaRole.Link, new() { Name = "The draft (1)" }).ClickAsync();
        await Expect(expert.Locator("[data-not-publisher]")).ToBeVisibleAsync();
        await Expect(expert.GetByRole(AriaRole.Button, new() { Name = "Publish these figures" })).ToHaveCountAsync(0);

        // The publisher reads what it would change for the reference households, and publishes with a note.
        await using IBrowserContext robsBrowser = await _browser.SizedAsync(1440, 900, mobile: false, theme: "light", origin: _origin);
        IPage rob = await robsBrowser.NewPageAsync();
        await SignInAsync(rob, OperatorSignInHelper.Username);
        await rob.GotoAsync("/admin/figures?tab=draft");
        await Expect(rob.Locator("[data-draft=\"stairlift-batteries.stock\"]")).ToContainTextAsync("Drafted by expert on ");
        await Expect(rob.Locator("[data-preview-says]")).ToContainTextAsync("of 96 Days Ready figures and 0 of 6 plan costs would move.");
        await Expect(rob.Locator("[data-preview-days] tbody tr").First).ToContainTextAsync("An older person in a terrace with a battery stairlift and a prepayment meter");
        await ShouldBeSoundAsync(rob);
        await rob.ScreenshotAsync(new() { Path = Screenshots.Target("desk-draft-desktop-light.png"), FullPage = true });
        await rob.GetByLabel("What this publication is for").FillAsync("The stairlift figures, from the engineer's visit.");
        await rob.GetByRole(AriaRole.Button, new() { Name = "Publish these figures" }).ClickAsync();
        await Expect(rob.Locator(".callout[role=\"status\"]")).ToHaveTextAsync("Set 1 is in force. Every plan built from now on is worked out from it.");
        await Expect(rob.Locator("[data-set=\"1\"]")).ToHaveAttributeAsync("data-state", "in-force");
        await Expect(rob.Locator("[data-set=\"1\"] .desk__note")).ToHaveTextAsync("The stairlift figures, from the engineer's visit.");
        await ShouldBeSoundAsync(rob);
        await rob.ScreenshotAsync(new() { Path = Screenshots.Target("desk-history-desktop-light.png"), FullPage = true });

        // The next plan anybody builds is worked out from the set, and says which.
        await using IBrowserContext household = await _browser.SizedAsync(390, 844, mobile: true, theme: "light", origin: _origin);
        IPage plan = await household.NewPageAsync();
        await plan.GotoAsync("/plan/start");
        await Journeys.FillRobsHouseAsync(plan);
        await plan.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await plan.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(plan.Locator("[data-plan]")).ToHaveAttributeAsync("data-figures-version", "1");
        await Expect(plan.Locator("[data-figures-updated]")).ToBeHiddenAsync();

        // What the set changed is on the record, and the shipped figures are put back as a new set.
        await rob.Locator("[data-set=\"1\"]").GetByRole(AriaRole.Link, new() { Name = "What it changed" }).ClickAsync();
        await Expect(rob.Locator("[data-changes] li")).ToHaveCountAsync(1);
        await Expect(rob.Locator("[data-changes] li")).ToContainTextAsync("2 to 1");
        await Expect(rob.Locator("[data-changes] li")).ToContainTextAsync("A service engineer's visit");
        await rob.GetByRole(AriaRole.Link, new() { Name = "Every published set" }).ClickAsync();
        await rob.Locator("#restore-note-0").FillAsync("The engineer measured a worn set.");
        await rob.Locator("[data-set=\"0\"]").GetByRole(AriaRole.Button, new() { Name = "Put this set back" }).ClickAsync();
        await Expect(rob.Locator(".callout[role=\"status\"]")).ToContainTextAsync("Set 2 is in force.");
        await Expect(rob.Locator("[data-set=\"2\"]")).ToContainTextAsync("It put the shipped figures back.");
        await rob.GetByRole(AriaRole.Link, new() { Name = "How things work" }).ClickAsync();
        await Expect(rob.Locator(Lift)).ToHaveAttributeAsync("data-state", "shipped");
    }

    [Theory]
    [InlineData("phone", 390, 844, true)]
    [InlineData("desktop", 1440, 900, false)]
    [InlineData("wide", 2000, 1100, false)]
    public async Task The_desk_holds_its_layout_at_every_width_in_both_themes(string size, int width, int height, bool mobile)
    {
        // One sign-in: a code from the authenticator is good once, so the theme is changed under the same session.
        await using IBrowserContext context = await _browser.SizedAsync(width, height, mobile, "light", origin: _origin);
        IPage page = await context.NewPageAsync();
        await SignInAsync(page, OperatorSignInHelper.Username);
        foreach (string theme in new[] { "light", "dark" })
        {
            await page.EmulateMediaAsync(new() { ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light });

            foreach ((string tab, string name) in new[] { ("work", "desk-work"), ("prices", "desk-prices"), ("history", "desk-sets") })
            {
                await page.GotoAsync($"/admin/figures?tab={tab}");
                await ShouldBeSoundAsync(page);
                await page.ScreenshotAsync(new() { Path = Screenshots.Target($"{name}-{size}-{theme}.png") });
            }

            // A figure's box, open, with every field inside its card.
            await page.GotoAsync("/admin/figures?tab=work");
            ILocator lift = page.Locator(Lift);
            await lift.GetByText("Correct this figure").ClickAsync();
            await lift.GetByLabel("Why it should change").ScrollIntoViewIfNeededAsync();
            await ShouldBeSoundAsync(page);
            double[] box = await lift.EvaluateAsync<double[]>(
                """
                row => {
                  const card = row.closest('.figure-card').getBoundingClientRect();
                  return [...row.querySelectorAll('input.input, button')].map(control => {
                    const at = control.getBoundingClientRect();
                    return Math.max(card.left - at.left, at.right - card.right, 0);
                  });
                }
                """);
            box.ShouldAllBe(over => over < 1, $"at {width}px a control in a figure's box runs outside its card");
            // The box opens under the figure at the row's full width. It once opened beside the name, a
            // few characters wide, because the row had borrowed another component's class.
            double share = await lift.EvaluateAsync<double>("row => row.querySelector('.desk-figure__form').getBoundingClientRect().width / row.getBoundingClientRect().width");
            share.ShouldBeGreaterThan(0.85, $"at {width}px a figure's box is {share:P0} of its row");
            await lift.ScreenshotAsync(new() { Path = Screenshots.Target($"desk-figure-open-{size}-{theme}.png") });
        }
    }

    private async Task SignInAsync(IPage page, string username)
    {
        await page.GotoAsync("/admin/catalogue");
        await page.Locator("#Username").FillAsync(username);
        await page.Locator("#Password").FillAsync(OperatorSignInHelper.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await page.Locator("#Code").FillAsync(await _application.CodeForAsync(username));
        await page.GetByRole(AriaRole.Button, new() { Name = "Finish signing in" }).ClickAsync();
        await page.Locator(".review__offers").WaitForAsync();
    }

    /// <summary>No accessibility violations and nothing scrolling sideways, on the page as it stands.</summary>
    private static async Task ShouldBeSoundAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.fonts.status === 'loaded' && document.getAnimations().every((a) => a.playState === 'finished' || a.playState === 'idle')");
        AxeResult axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes) {v.Nodes[0].Html}")));
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0, "nothing scrolls sideways");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);
}
