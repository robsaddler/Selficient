using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Selficient.E2E.Tests;

/// <summary>
/// Saving picks for later, in a real browser: the button beside a pick, the count on the plan, the
/// page that lists them by shop, and the list travelling with the plan to an account's other
/// device. The cases are the ones in <c>docs/threat-models/shortlist.md</c>.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class Shortlist
{
    private readonly Browser _browser;

    public Shortlist(Browser browser) => _browser = browser;

    [Fact]
    public async Task A_household_saves_picks_for_later_sees_them_by_shop_and_takes_one_off()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        ILocator link = page.Locator("[data-shortlist-link]");
        await Expect(link).ToBeHiddenAsync();

        // Two picks from different shops, by the button beside each.
        (string first, string second) = await TwoFromDifferentShopsAsync(page);
        ILocator firstButton = page.Locator($"[data-shortlist=\"{first}\"]");
        await Expect(firstButton).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(firstButton.Locator("span:visible")).ToHaveTextAsync("Save for later");
        await firstButton.ClickAsync();
        await Expect(firstButton).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(firstButton.Locator("span:visible")).ToHaveTextAsync("Saved for later");
        await page.Locator($"[data-shortlist=\"{second}\"]").ClickAsync();
        await Expect(link).ToHaveTextAsync("Saved for later: 2");
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0);
        AxeResult planAxe = await page.RunAxe();
        planAxe.Violations.ShouldBeEmpty(string.Join("\n", planAxe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));

        // It is still saved after a reload, because it is kept with the plan.
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(link).ToHaveTextAsync("Saved for later: 2");
        await Expect(page.Locator($"[data-shortlist=\"{first}\"]")).ToHaveAttributeAsync("aria-pressed", "true");

        // The page: one panel a shop, every link out through the counter.
        await link.Locator("a").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Saved for later", Level = 1 })).ToBeVisibleAsync();
        await Expect(page.Locator(".shortlist__shop")).ToHaveCountAsync(2);
        await Expect(page.Locator(".shortlist__list .pick")).ToHaveCountAsync(2);
        await Expect(page.Locator(".shortlist__lede")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^2 things saved from 2 shops, £[0-9.,]+ in all at the prices we saw\\. The list is kept with your plan\\.$"));
        (await page.Locator(".shortlist__list a").EvaluateAllAsync<string[]>("links => links.map(a => a.getAttribute('href'))")).ShouldAllBe(href => href.StartsWith("/out/", StringComparison.Ordinal));
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0);
        AxeResult axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));

        await page.ScreenshotAsync(new() { Path = Screenshots.Target("shortlist-phone-light.png"), FullPage = true });
        await page.SetViewportSizeAsync(2000, 1100);
        await page.ScreenshotAsync(new() { Path = Screenshots.Target("shortlist-wide-light.png"), FullPage = true });
        await page.SetViewportSizeAsync(390, 844);

        // One taken off: the page is drawn again without it.
        await page.Locator($"[data-shortlist-remove=\"{first}\"]").ClickAsync();
        await Expect(page.Locator(".shortlist__shop")).ToHaveCountAsync(1);
        await Expect(page.Locator(".shortlist__lede")).ToContainTextAsync("1 thing saved from 1 shop");
        await Until.TrueAsync(page, "want => import('/js/plan-store.js').then(store => store.getShortlist()).then(list => list.join(',') === want)", second);

        // The last one taken off: the page says how to save something.
        await page.Locator($"[data-shortlist-remove=\"{second}\"]").ClickAsync();
        await Expect(page.Locator("[data-shortlist-nothing]")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Back to your plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(page.Locator("[data-shortlist-link]")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task What_was_saved_travels_with_the_plan_and_a_saved_offer_that_has_gone_comes_off_the_list()
    {
        const string Email = "shortlist@example.com";
        await using IBrowserContext first = await _browser.SizedAsync(1280, 900, mobile: false, theme: "dark");
        IPage a = await first.NewPageAsync();
        await BuildPlanAsync(a);
        (string saved, _) = await TwoFromDifferentShopsAsync(a);
        await a.Locator($"[data-shortlist=\"{saved}\"]").ClickAsync();
        await Until.TrueAsync(a, "want => import('/js/plan-store.js').then(store => store.getShortlist()).then(list => list.join(',') === want)", saved);
        // An offer that is no longer in the catalogue, saved on some earlier day.
        await a.EvaluateAsync("id => import('/js/plan-store.js').then(store => store.setShortlisted(id, true))", "987654321");

        // An account's other device gets the list with the plan.
        await Accounts.SignInAsync(_browser, a, Email);
        await Until.TrueAsync(a, "async want => { const answer = await (await fetch('/api/account/plan')).json(); return answer.body !== null && JSON.parse(answer.body).plan.shortlist.join(',') === want; }", $"{saved},987654321");
        await using IBrowserContext second = await _browser.PhoneAsync();
        IPage b = await second.NewPageAsync();
        await Accounts.SignInAsync(_browser, b, Email);
        await b.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(b.Locator("[data-shortlist-link]")).ToHaveTextAsync("Saved for later: 2");

        // On the page the one that has gone is said so, once, and taken off; the other is listed.
        await b.Locator("[data-shortlist-link] a").ClickAsync();
        await Expect(b.Locator("[data-shortlist-gone]")).ToHaveTextAsync("1 thing you saved is no longer in the catalogue, and has been taken off your list.");
        await Expect(b.Locator(".shortlist__list .pick")).ToHaveCountAsync(1);
        await Until.TrueAsync(b, "want => import('/js/plan-store.js').then(store => store.getShortlist()).then(list => list.join(',') === want)", saved);
        await b.ReloadAsync();
        await Expect(b.Locator(".shortlist__list .pick")).ToHaveCountAsync(1);
        await Expect(b.Locator("[data-shortlist-gone]")).ToHaveCountAsync(0);

        // A file whose shortlist is not a list of numbers is not a plan, and changes nothing.
        string refused = await b.EvaluateAsync<string>(
            """
            () => import('/js/plan-store.js').then(async store => {
              const plan = await store.snapshot();
              try { await store.restore({ ...plan, shortlist: ['<img src=x onerror=alert(1)>'] }); return 'restored'; } catch (error) { return error.message; }
            })
            """);
        refused.ShouldBe("That file is not a Selficient plan.");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static async Task BuildPlanAsync(IPage page)
    {
        await page.GotoAsync("/plan/start");
        await Journeys.FillRobsHouseAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
    }

    /// <summary>
    /// Opens every "Where to look" on the plan and answers the numbers of two picks sold by
    /// different shops, read from the page as a household would read them.
    /// </summary>
    private static async Task<(string First, string Second)> TwoFromDifferentShopsAsync(IPage page)
    {
        await Expect(page.Locator("[data-shortlist]:not([data-shortlist=\"\"])").First).ToBeAttachedAsync();
        string[] found = await page.EvaluateAsync<string[]>(
            """
            () => {
              for (const fold of document.querySelectorAll('details.sheet, details.picks')) { fold.open = true; }
              const picks = [...document.querySelectorAll('.pick')].map(pick => ({ id: pick.querySelector('[data-shortlist]')?.dataset.shortlist ?? '', shop: (pick.querySelector('.pick__meta')?.textContent ?? '').split(',')[0].trim() })).filter(pick => pick.id !== '');
              const other = picks.find(pick => pick.shop !== picks[0].shop);
              return [picks[0].id, other.id];
            }
            """);
        await Expect(page.Locator($"[data-shortlist=\"{found[0]}\"]")).ToBeVisibleAsync();
        return (found[0], found[1]);
    }
}
