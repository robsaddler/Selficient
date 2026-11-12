using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Selficient.E2E.Tests;

/// <summary>
/// A household's account in a real browser: made with a code read from the message that was sent,
/// shown where "Sign in" would be, signed out of with and without the plan, and deleted. And a
/// guest, who must lose nothing by accounts existing. The cases are the ones in
/// <c>docs/threat-models/household-accounts.md</c>.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class Accounts
{
    private readonly Browser _browser;

    public Accounts(Browser browser) => _browser = browser;

    [Fact]
    public async Task A_guest_is_never_asked_who_they_are_and_is_offered_an_account_only_as_a_third_way_to_keep_a_plan()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        List<string> asked = [];
        page.Request += (_, request) => asked.Add(new Uri(request.Url).AbsolutePath);

        await page.GotoAsync("/");
        await BuildPlanAsync(page);
        ILocator notice = page.Locator("[data-kept-notice]");
        await Expect(notice.GetByRole(AriaRole.Link, new() { Name = "Create an account" })).ToBeVisibleAsync();
        await page.Locator("[data-keep-chip]").ClickAsync();
        ILocator panel = page.Locator("#keep-dialog");
        ILocator row = panel.Locator("[data-keep-account]");

        await Expect(row.GetByRole(AriaRole.Heading, new() { Name = "An account" })).ToBeVisibleAsync();
        await Expect(row.Locator("[data-keep-guest] p").First).ToHaveTextAsync("Keep every device in step and keep your plan if this one is lost. Just an email address, no password.");
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = "Create an account" })).ToHaveAttributeAsync("href", "/account/sign-in");
        await Expect(row.Locator("[data-keep-member]:visible")).ToHaveCountAsync(0);
        await Expect(panel.Locator(".keep__already")).ToHaveTextAsync("Already have an account? Sign in.");
        await Expect(panel.Locator("[data-keep-says]:visible")).ToHaveTextAsync("Your plan lives in this browser and nowhere else. You do not need an account.");
        // The row the plan is kept in now is the one marked, in words as well as colour.
        await Expect(panel.Locator(".keep__row[aria-current=\"true\"]")).ToHaveAttributeAsync("data-keep-row", "device");
        await Expect(panel.Locator("[data-keep-here]:visible")).ToHaveTextAsync("Kept here");
        await panel.Locator("[data-keep-here]:visible").EvaluateAsync("tag => tag.remove()");
        (await panel.Locator(".keep__row:visible h3").AllInnerTextsAsync()).ShouldBe(["On this device", "Another device", "An account"], "three rows, each with one job, in the order a person meets them");
        AxeResult axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));

        asked.ShouldNotContain("/api/account", "a guest's browser never asks who is signed in");
        asked.ShouldNotContain("/api/account/plan", "and never looks for a plan in an account it does not have");
        IReadOnlyList<BrowserContextCookiesResult> cookies = await context.CookiesAsync();
        cookies.ShouldAllBe(c => c.Name.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal), "a guest holds nothing but the cookie that protects a form");
    }

    [Fact]
    public async Task A_household_makes_an_account_is_shown_as_signed_in_and_signs_out_with_or_without_its_plan()
    {
        const string Email = "household-one@example.com";
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        ILocator chip = page.Locator("[data-keep-chip]");
        ILocator panel = page.Locator("#keep-dialog");

        // From the panel to the sign-in page, which is one box and says twice that it is optional.
        await chip.ClickAsync();
        await panel.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in or create an account" })).ToBeVisibleAsync();
        await page.EvaluateAsync("() => localStorage.setItem('selficient.passkey-declined', '1')");
        await Expect(page.Locator(".panel .field__hint")).ToHaveTextAsync("You do not need an account to use Selficient. Your plan is saved on this device either way.");
        await ShouldBeSoundAsync(page);
        await page.GetByLabel("Email address").FillAsync("not an address");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send me a code" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("That does not look like an email address. Check it and try again.");
        await page.GetByLabel("Email address").FillAsync(Email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send me a code" }).ClickAsync();

        // The code, from the message that was sent.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Check your email" })).ToBeVisibleAsync();
        await Expect(page.Locator(".panel__lede")).ToHaveTextAsync($"We have sent a six-digit code to {Email}. It works for ten minutes, once, and only in this browser.");
        await ShouldBeSoundAsync(page);
        string code = _browser.Application.Mail.CodeFor(Email);
        await page.GetByLabel("Six-digit code").FillAsync(code == "000000" ? "000001" : "000000");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("That code is not right. Check the email and try again.");
        await Expect(page.GetByLabel("Six-digit code")).ToHaveValueAsync(string.Empty);
        await page.GetByLabel("Six-digit code").FillAsync($"{code[..3]} {code[3..]}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();

        // Signed in: the plan page, and the chip says who, where "Sign in" would be.
        await page.WaitForURLAsync("**/plan");
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(chip.Locator("[data-keep-label]:visible")).ToHaveTextAsync($"Signed in as {Email}");
        await Expect(chip.Locator("[data-keep-mark=\"account\"]")).ToBeVisibleAsync();
        await Expect(chip.Locator("[data-keep-mark=\"device\"]")).ToBeHiddenAsync();
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0, "a long address does not widen the header");
        await chip.ClickAsync();
        ILocator account = panel.Locator("[data-keep-account]");
        await Expect(account.GetByRole(AriaRole.Heading, new() { Name = "Your account" })).ToBeVisibleAsync();
        await Expect(account.Locator("[data-keep-member] p").First).ToHaveTextAsync($"Signed in as {Email}.");
        await Expect(panel.Locator("[data-keep-guest]:visible")).ToHaveCountAsync(0);
        await Expect(panel.Locator("[data-keep-says]:visible")).ToHaveTextAsync("A copy of your plan lives in this browser, so it opens without a connection.");
        await Expect(panel.Locator(".keep__row[aria-current=\"true\"]")).ToHaveAttributeAsync("data-keep-row", "account");
        await Expect(account.Locator("[data-keep-here]")).ToBeVisibleAsync();
        await Expect(panel.Locator("[data-keep-row=\"device\"] [data-keep-here]")).ToBeHiddenAsync();
        AxeResult axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));

        // When the server cannot be asked, the chip still says who, from what it learned last time.
        await page.Keyboard.PressAsync("Escape");
        await page.RouteAsync("**/api/account", route => route.AbortAsync());
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(chip.Locator("[data-keep-label]:visible")).ToHaveTextAsync($"Signed in as {Email}");
        await page.UnrouteAsync("**/api/account");

        // Signing out asks one thing, and yes is the default: the plan stays.
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await chip.ClickAsync();
        await account.GetByRole(AriaRole.Link, new() { Name = "Sign out" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign out of this device" })).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Keep a copy of my plan on this device")).ToBeCheckedAsync();
        await ShouldBeSoundAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/");
        await Expect(chip.Locator("[data-keep-label]:visible")).ToHaveTextAsync("Saved on this device");
        (await page.EvaluateAsync<bool>("() => import('/js/plan-store.js').then(store => store.getProfile()).then(profile => profile !== null)")).ShouldBeTrue();
        (await page.EvaluateAsync<int>("() => fetch('/api/account').then(response => response.status)")).ShouldBe(401);

        // Signed in again, and out without keeping a copy: the plan leaves this browser with the session.
        await page.GotoAsync("/account/sign-in");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Use a passkey" })).ToBeVisibleAsync();
        await page.GetByLabel("Email address").FillAsync(Email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send me a code" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Check your email" })).ToBeVisibleAsync();
        await page.GetByLabel("Six-digit code").FillAsync(_browser.Application.Mail.CodeFor(Email));
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/plan");
        await Expect(chip.Locator("[data-keep-label]:visible")).ToHaveTextAsync($"Signed in as {Email}");
        await page.GotoAsync("/account/sign-out");
        await page.GetByLabel("Keep a copy of my plan on this device").UncheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/");
        await Expect(chip.Locator("[data-keep-label]:visible")).ToHaveTextAsync("No account needed");
        (await page.EvaluateAsync<bool>("() => import('/js/plan-store.js').then(store => store.getProfile()).then(profile => profile === null)")).ShouldBeTrue("the plan left the browser with the session");
        (await page.EvaluateAsync<string?>("() => localStorage.getItem('selficient.sync')")).ShouldBeNull("and so did the record of what it last agreed with the account");
    }

    [Fact]
    public async Task Deleting_the_account_takes_one_confirmation_and_leaves_the_plan_on_the_device()
    {
        const string Email = "household-two@example.com";
        await using IBrowserContext context = await _browser.SizedAsync(1280, 900, mobile: false, theme: "dark");
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        await SignInAsync(_browser, page, Email);
        ILocator chip = page.Locator("[data-keep-chip]");

        await chip.ClickAsync();
        await page.Locator("[data-keep-account]").GetByRole(AriaRole.Link, new() { Name = "Delete my account" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Delete your account" })).ToBeVisibleAsync();
        await Expect(page.Locator(".panel__lede")).ToHaveTextAsync("This removes your email address, the copy of your plan we keep, your passkeys and every sign-in, straight away. It cannot be undone.");
        await ShouldBeSoundAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete my account" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Your account has been deleted" })).ToBeVisibleAsync();
        await Expect(page.Locator(".notice p").Nth(1)).ToHaveTextAsync("We no longer hold your email address or a copy of your plan. The plan on this device is still here, and you do not need an account to go on using it.");
        await Expect(chip.Locator("[data-keep-label]:visible")).ToHaveTextAsync("Saved on this device");
        await ShouldBeSoundAsync(page);
        (await page.EvaluateAsync<int>("() => fetch('/api/account').then(response => response.status)")).ShouldBe(401);
        (await page.EvaluateAsync<string?>("() => localStorage.getItem('selficient.account')")).ShouldBeNull("the browser forgets the address too");
        await page.GetByRole(AriaRole.Link, new() { Name = "Open your plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        (await page.Locator("days-ready").GetAttributeAsync("value")).ShouldBe("1", "the plan on the device is as it was");
    }

    [Fact]
    public async Task A_session_that_ended_elsewhere_reads_as_a_guest_at_the_next_page()
    {
        const string Email = "household-three@example.com";
        await using IBrowserContext phone = await _browser.PhoneAsync();
        IPage onPhone = await phone.NewPageAsync();
        await BuildPlanAsync(onPhone);
        await SignInAsync(_browser, onPhone, Email);
        await using IBrowserContext laptop = await _browser.SizedAsync(1280, 900, mobile: false, theme: "light");
        IPage onLaptop = await laptop.NewPageAsync();
        await onLaptop.GotoAsync("/");
        await SignInAsync(_browser, onLaptop, Email);
        // The account's plan arrives and the page is rebuilt from it; leaving in the middle of that would be cut off.
        await onLaptop.Locator("[data-plan][data-plan-ready]").WaitForAsync();

        await onLaptop.GotoAsync("/account/delete");
        await onLaptop.GetByRole(AriaRole.Button, new() { Name = "Delete my account" }).ClickAsync();
        await Expect(onLaptop.GetByRole(AriaRole.Heading, new() { Name = "Your account has been deleted" })).ToBeVisibleAsync();
        await onPhone.ReloadAsync();

        await Expect(onPhone.Locator("[data-keep-chip] [data-keep-label]:visible")).ToHaveTextAsync("Saved on this device");
        (await phone.CookiesAsync()).ShouldNotContain(c => c.Name == "selficient.signed-in", "the hint goes when the session it pointed at is gone");
    }

    /// <summary>
    /// Signs a page in as a household, with the code from the message the running application was
    /// asked to send. The offer of a passkey that follows a first sign-in sits over the foot of the
    /// page, so a journey that is not about it says "not now" beforehand, as a person would once.
    /// </summary>
    internal static async Task SignInAsync(Browser browser, IPage page, string email, bool offerPasskey = false)
    {
        await page.GotoAsync("/account/sign-in");
        if (!offerPasskey)
        {
            await page.EvaluateAsync("() => localStorage.setItem('selficient.passkey-declined', '1')");
        }

        await page.GetByLabel("Email address").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send me a code" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Check your email" })).ToBeVisibleAsync();
        string code = browser.Application.Mail.CodeFor(email);
        await page.GetByLabel("Six-digit code").FillAsync(code);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/plan");
        await Expect(page.Locator("[data-keep-chip] [data-keep-label]:visible")).ToHaveTextAsync($"Signed in as {email}");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static async Task BuildPlanAsync(IPage page)
    {
        await page.GotoAsync("/plan/start");
        await Journeys.FillRobsHouseAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
    }

    /// <summary>No accessibility violations and nothing scrolling sideways, on the page as it stands.</summary>
    private static async Task ShouldBeSoundAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.getAnimations().every((a) => a.playState === 'finished' || a.playState === 'idle')");
        AxeResult axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0, "nothing scrolls sideways");
    }
}
