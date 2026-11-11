using Microsoft.Playwright;
using Selficient.TestKit;

namespace Selficient.E2E.Tests;

/// <summary>
/// Full-page captures of every public page at phone and desktop widths, in both themes, saved
/// under artifacts/screens. They are looked at, not asserted on: the point is that a design that
/// drifts or a page that never grew up for a desktop is seen in the run's artifacts rather than
/// discovered by a person months later.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class Screenshots
{
    private static readonly (string Name, int Width, int Height, bool Mobile)[] Sizes =
    [
        ("phone", 390, 844, true),
        ("desktop", 1440, 900, false),
        // Rob reviews on a 2000px window, where a page that stops at the mockup's 78rem leaves a
        // third of the screen empty. A capture at that width is the one that shows it.
        ("wide", 2000, 1100, false),
    ];

    private readonly Browser _browser;

    public Screenshots(Browser browser) => _browser = browser;

    [Theory]
    [InlineData("/", "home")]
    [InlineData("/plan/start", "start")]
    [InlineData("/plan/solar", "solar")]
    [InlineData("/plan/photos", "photos")]
    [InlineData("/privacy", "privacy")]
    [InlineData("/deals", "deals")]
    [InlineData("/share/storm/1", "share")]
    public async Task Every_page_is_captured_at_phone_and_desktop_widths_in_both_themes(string path, string name)
    {
        foreach ((string size, int width, int height, bool mobile) in Sizes)
        {
            foreach (string theme in new[] { "light", "dark" })
            {
                await using IBrowserContext context = await _browser.SizedAsync(width, height, mobile, theme);
                IPage page = await context.NewPageAsync();
                await page.GotoAsync(path);
                await SettleAsync(page);
                await page.ScreenshotAsync(new() { Path = Target($"{name}-{size}-{theme}.png"), FullPage = true });
            }
        }
    }

    [Fact]
    public async Task The_plan_is_captured_at_phone_and_desktop_widths_in_both_themes()
    {
        foreach ((string size, int width, int height, bool mobile) in Sizes)
        {
            foreach (string theme in new[] { "light", "dark" })
            {
                await using IBrowserContext context = await _browser.SizedAsync(width, height, mobile, theme);
                IPage page = await context.NewPageAsync();
                await page.GotoAsync("/plan/start");
                await Journeys.FillRobsHouseAsync(page);
                await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
                await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
                await page.Locator("[data-action-id=\"water-bottled-three-days\"]").CheckAsync();
                await page.Locator("[data-action-id=\"headtorches\"]").CheckAsync();
                // One of each choice, so the capture shows the three states and a tally that counts them.
                await page.Locator("[data-action-row=\"food-no-cook-three-days\"] [data-choice=\"plan\"]").CheckAsync();
                await page.Locator("[data-action-row=\"power-banks\"] [data-choice=\"plan\"]").CheckAsync();
                await page.Locator("[data-action-row=\"radio-dab-fm-with-crank\"] [data-choice=\"skip\"]").CheckAsync();
                await Assertions.Expect(page.Locator(".plan__tally [data-tally-kind=\"skip\"] [data-tally-figure]")).ToHaveTextAsync("1");
                await SettleAsync(page);
                await page.ScreenshotAsync(new() { Path = Target($"plan-{size}-{theme}.png"), FullPage = true });
            }
        }
    }

    [Fact]
    public async Task The_reviewer_is_captured_signed_out_and_signed_in_at_every_width_in_both_themes()
    {
        foreach ((string size, int width, int height, bool mobile) in Sizes)
        {
            foreach (string theme in new[] { "light", "dark" })
            {
                await using IBrowserContext context = await _browser.SizedAsync(width, height, mobile, theme);
                IPage page = await context.NewPageAsync();
                await page.GotoAsync("/admin/catalogue");
                await SettleAsync(page);
                await page.ScreenshotAsync(new() { Path = Target($"reviewer-signin-{size}-{theme}.png"), FullPage = true });

                // The real two steps: the password, then a code from the account's own seed.
                // A newcomer first, so the enrolment screen with its QR code is captured too.
                await _browser.Application.CreateOperatorAsync(username: "newcomer", enrolled: false);
                await page.Locator("#Username").FillAsync("newcomer");
                await page.Locator("#Password").FillAsync(OperatorSignInHelper.Password);
                await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
                await page.Locator(".enrol__qr svg").WaitForAsync();
                await SettleAsync(page);
                await page.ScreenshotAsync(new() { Path = Target($"reviewer-enrol-{size}-{theme}.png"), FullPage = true });

                await page.GotoAsync("/admin/sign-out");
                await page.GotoAsync("/admin/catalogue");
                await SettleAsync(page);
                await _browser.Application.CreateOperatorAsync();
                await page.Locator("#Username").FillAsync(OperatorSignInHelper.Username);
                await page.Locator("#Password").FillAsync(OperatorSignInHelper.Password);
                await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
                await page.Locator("#Code").WaitForAsync();
                await SettleAsync(page);
                await page.ScreenshotAsync(new() { Path = Target($"reviewer-code-{size}-{theme}.png"), FullPage = true });

                await page.Locator("#Code").FillAsync(await _browser.Application.CodeForAsync());
                await page.GetByRole(AriaRole.Button, new() { Name = "Finish signing in" }).ClickAsync();
                await page.Locator(".review__offers").WaitForAsync();
                await SettleAsync(page);
                await page.ScreenshotAsync(new() { Path = Target($"reviewer-{size}-{theme}.png"), FullPage = true });
            }
        }
    }

    private static async Task SettleAsync(IPage page)
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForFunctionAsync("() => document.fonts.status === 'loaded' && document.getAnimations().every((a) => a.playState === 'finished' || a.playState === 'idle')");
    }

    private static string Target(string file)
    {
        string directory = Path.Combine(FindRepositoryRoot(), "artifacts", "screens");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, file);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Selficient.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
