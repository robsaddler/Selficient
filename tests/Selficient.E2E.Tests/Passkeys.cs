using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Selficient.E2E.Tests;

/// <summary>
/// Passkeys in a real browser, with Chromium's own virtual authenticator standing in for the
/// fingerprint reader: offered once after signing in by code, added, used to sign in with no code,
/// listed in the panel and removed. The cases are section 6 of
/// <c>docs/threat-models/household-accounts.md</c>.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class Passkeys
{
    private readonly Browser _browser;

    public Passkeys(Browser browser) => _browser = browser;

    [Fact]
    public async Task A_household_adds_a_passkey_when_offered_signs_in_with_it_and_removes_it()
    {
        const string Email = "passkey-one@example.com";
        await using IBrowserContext context = await _browser.SizedAsync(1280, 900, mobile: false, theme: "light");
        IPage page = await context.NewPageAsync();
        await AuthenticatorAsync(context, page);
        await BuildPlanAsync(page);
        ILocator chip = page.Locator("[data-keep-chip]");
        ILocator panel = page.Locator("#keep-dialog");
        ILocator offer = page.Locator("[data-passkey-offer]");
        ILocator passkeys = panel.Locator("[data-passkeys]");

        // Straight after signing in by code, once: the offer, with the reason it is safe to take.
        await Accounts.SignInAsync(_browser, page, Email, offerPasskey: true);
        await Expect(offer).ToBeVisibleAsync();
        await Expect(offer.Locator("p")).ToHaveTextAsync("Sign in faster next time. Add a passkey and this device signs you in with your fingerprint, face or screen lock. The emailed code still works, so a lost phone is never a lost account.");
        await ShouldBeSoundAsync(page);
        await offer.GetByRole(AriaRole.Button, new() { Name = "Add a passkey" }).ClickAsync();
        await Expect(offer).ToBeHiddenAsync();

        // In the panel: the passkey, said in a sentence, with a way to remove it.
        await chip.ClickAsync();
        await Expect(passkeys.GetByRole(AriaRole.Heading, new() { Name = "Passkeys" })).ToBeVisibleAsync();
        await Expect(passkeys.Locator("[data-passkey-list] li span")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^(Windows computer|Linux computer|Mac), added \\d{1,2} \\w+ \\d{4}, not used yet\\.$"));
        await Expect(passkeys.Locator("[data-passkey-says]")).ToHaveTextAsync("A passkey has been added for this device.");
        await Expect(passkeys.Locator("[data-passkey-none]")).ToBeHiddenAsync();
        await ShouldBeSoundAsync(page);
        await page.Keyboard.PressAsync("Escape");

        // Signed out, and back in with no address typed and no code waited for.
        await page.GotoAsync("/account/sign-out");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/");
        await page.GotoAsync("/account/sign-in");
        int mailBefore = _browser.Application.Mail.CountTo(Email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Use a passkey" }).ClickAsync();
        await page.WaitForURLAsync("**/plan");
        await Expect(chip.Locator("[data-keep-label]:visible")).ToHaveTextAsync($"Signed in as {Email}");
        _browser.Application.Mail.CountTo(Email).ShouldBe(mailBefore, "no code was sent");
        await Expect(offer).ToBeHiddenAsync();

        // Used, and then removed: the code still signs in, and the passkey no longer does.
        await chip.ClickAsync();
        await Expect(passkeys.Locator("[data-passkey-list] li span")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(", last used \\d{1,2} \\w+ \\d{4}\\.$"));
        ILocator remove = passkeys.GetByRole(AriaRole.Button, new() { Name = "Remove" });
        (await remove.GetAttributeAsync("aria-label"))!.ShouldStartWith("Remove: ", Case.Sensitive, "the button says which passkey it removes");
        await remove.ClickAsync();
        await Expect(passkeys.Locator("[data-passkey-says]")).ToHaveTextAsync("That passkey has been removed. The emailed code still signs you in.");
        await Expect(passkeys.Locator("[data-passkey-list]")).ToBeHiddenAsync();
        await Expect(passkeys.Locator("[data-passkey-none]")).ToHaveTextAsync("Sign in with this device's fingerprint, face or screen lock in place of a code. The emailed code still works if the device is lost.");
        await Expect(passkeys.GetByRole(AriaRole.Button, new() { Name = "Add a passkey for this device" })).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.GotoAsync("/account/sign-out");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/");
        await page.GotoAsync("/account/sign-in");
        await page.GetByRole(AriaRole.Button, new() { Name = "Use a passkey" }).ClickAsync();
        await Expect(page.Locator("[data-passkey-sign-in-says]")).ToHaveTextAsync("That passkey did not sign you in. Try again, or sign in with a code sent to your email address.");
        new Uri(page.Url).AbsolutePath.ShouldBe("/account/sign-in");
        await ShouldBeSoundAsync(page);
        await Accounts.SignInAsync(_browser, page, Email);
    }

    [Fact]
    public async Task The_offer_is_made_once_and_not_now_is_taken_as_an_answer()
    {
        const string Email = "passkey-two@example.com";
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await AuthenticatorAsync(context, page);
        await BuildPlanAsync(page);
        ILocator offer = page.Locator("[data-passkey-offer]");

        await Accounts.SignInAsync(_browser, page, Email, offerPasskey: true);
        await Expect(offer).ToBeVisibleAsync();
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0);
        await offer.GetByRole(AriaRole.Button, new() { Name = "Not now" }).ClickAsync();
        await Expect(offer).ToBeHiddenAsync();
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await page.Locator("[data-keep-chip]").ClickAsync();

        await Expect(page.Locator("[data-passkeys]").GetByRole(AriaRole.Button, new() { Name = "Add a passkey for this device" })).ToBeVisibleAsync();
        await Expect(offer).ToBeHiddenAsync();
    }

    [Fact]
    public async Task A_guest_and_a_browser_with_no_passkeys_are_shown_nothing_about_them()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await page.AddInitScriptAsync("delete window.PublicKeyCredential;");

        await page.GotoAsync("/account/sign-in");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Send me a code" })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-passkey-sign-in]")).ToBeHiddenAsync();
        await BuildPlanAsync(page);
        await Accounts.SignInAsync(_browser, page, "passkey-none@example.com", offerPasskey: true);
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-sync", "synced");

        await Expect(page.Locator("[data-passkey-offer]")).ToBeHiddenAsync();
        await page.Locator("[data-keep-chip]").ClickAsync();
        await Expect(page.Locator("[data-passkeys]")).ToBeHiddenAsync();
    }

    /// <summary>Gives the page Chromium's virtual authenticator: a platform one that verifies its user without being asked.</summary>
    private static async Task AuthenticatorAsync(IBrowserContext context, IPage page)
    {
        ICDPSession devtools = await context.NewCDPSessionAsync(page);
        await devtools.SendAsync("WebAuthn.enable");
        await devtools.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
        {
            ["options"] = new Dictionary<string, object>
            {
                ["protocol"] = "ctap2",
                ["transport"] = "internal",
                ["hasResidentKey"] = true,
                ["hasUserVerification"] = true,
                ["isUserVerified"] = true,
                ["automaticPresenceSimulation"] = true,
            },
        });
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static async Task BuildPlanAsync(IPage page)
    {
        await page.GotoAsync("/plan/start");
        await Journeys.FillRobsHouseAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
    }

    private static async Task ShouldBeSoundAsync(IPage page)
    {
        AxeResult axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0, "nothing scrolls sideways");
    }
}
