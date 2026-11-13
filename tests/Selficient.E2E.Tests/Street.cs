using System.Text;
using System.Text.RegularExpressions;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Selficient.E2E.Tests;

/// <summary>
/// A street group in real browsers, one to a household: started, joined by an invitation, shared
/// in two circles, read on a second device, a home removed, a home leaving. What each neighbour
/// can and cannot see is the point of the feature, so each is looked at through its own browser,
/// and what the server holds is read back to see that it holds nothing it could read.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class Street
{
    private readonly Browser _browser;

    public Street(Browser browser) => _browser = browser;

    [Fact]
    public async Task Neighbours_share_what_each_chose_with_whom_each_chose_and_the_server_can_read_none_of_it()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        (string robMail, string dorisMail, string samMail) = ($"rob-{tag}@example.com", $"doris-{tag}@example.com", $"sam-{tag}@example.com");

        // Rob starts the group for his street.
        await using IBrowserContext robsBrowser = await _browser.SizedAsync(1440, 900, mobile: false, theme: "light");
        IPage rob = await robsBrowser.NewPageAsync();
        await rob.GotoAsync("/street");
        await Expect(rob.Locator("[data-street-guest-says]")).ToHaveTextAsync("A street group needs an account, because the group has to be somewhere other than one phone. An account is an email address and nothing else.");
        await Accounts.SignInAsync(_browser, rob, robMail);
        await rob.GotoAsync("/street");
        await Expect(rob.Locator("[data-street-groups] li")).ToHaveTextAsync("You are not in a street group yet. Start one below and invite your neighbours, or open an invitation a neighbour sent you.");
        await ShouldBeSoundAsync(rob);
        await rob.GetByRole(AriaRole.Button, new() { Name = "Start the group" }).ClickAsync();
        await Expect(rob.Locator("[data-street-says]")).ToHaveTextAsync("Give the group a name your neighbours will know, such as the name of your street.");
        await rob.GetByLabel("What your neighbours call it").FillAsync("Maple Road");
        await rob.GetByRole(AriaRole.Button, new() { Name = "Start the group" }).ClickAsync();
        await Expect(rob.Locator("[data-street-says]")).ToHaveTextAsync("Give your house number, and the first name your closer circle will see.");
        await rob.Locator("[data-street-start]").GetByLabel("Your house number or name").FillAsync("27");
        await rob.Locator("[data-street-start]").GetByLabel("Your first name").FillAsync("Rob");
        await rob.GetByRole(AriaRole.Button, new() { Name = "Start the group" }).ClickAsync();

        await Expect(rob.Locator("[data-street-title]")).ToHaveTextAsync("Maple Road");
        await Expect(rob.Locator("[data-street-count]")).ToHaveTextAsync("One home so far, which is yours. Send the invitation below to a neighbour.");
        await Expect(rob.Locator("[data-home-mine]")).ToContainTextAsync("27 Rob Your home");
        await Expect(rob.Locator("[data-street-circle-none]")).ToBeVisibleAsync();
        string invitation = await rob.Locator("[data-street-link]").InputValueAsync();
        invitation.ShouldMatch(@"/street/join#[0-9a-f-]{36}\.[A-Za-z0-9_-]{43}\.[A-Za-z0-9_-]{43}$");
        await Expect(rob.Locator("[data-street-qr] svg")).ToBeVisibleAsync();
        string groupAddress = rob.Url;

        // Doris opens the invitation before she has an account. She signs in and is taken on to it.
        await using IBrowserContext dorisBrowser = await _browser.PhoneAsync();
        IPage doris = await dorisBrowser.NewPageAsync();
        await doris.GotoAsync(invitation);
        await Expect(doris.Locator("[data-street-guest-says]")).ToHaveTextAsync("You have been invited to a street group. Sign in or make an account, and this page will take you on to join it.");
        await Accounts.SignInAsync(_browser, doris, dorisMail);
        await Expect(doris.GetByRole(AriaRole.Heading, new() { Name = "Join your street's group" })).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ShouldBeSoundAsync(doris);
        await doris.Locator("[data-street-join]").GetByLabel("Your house number or name").FillAsync("12");
        await doris.Locator("[data-street-join]").GetByLabel("Your first name").FillAsync("Doris");
        await doris.GetByRole(AriaRole.Button, new() { Name = "Join the group" }).ClickAsync();
        await Expect(doris.Locator("[data-street-title]")).ToHaveTextAsync("Maple Road");
        await Expect(doris.Locator("[data-street-count]")).ToHaveTextAsync("2 homes in this group.");
        await Expect(doris.Locator("[data-street-homes] > li").First).ToContainTextAsync("12 Doris Your home", new() { UseInnerText = true });
        ILocator robsHomeForDoris = doris.Locator("[data-street-homes] > li").Nth(1);
        await Expect(robsHomeForDoris).ToContainTextAsync("27");
        await Expect(robsHomeForDoris.Locator("[data-home-name]")).ToHaveCountAsync(0);
        await Expect(robsHomeForDoris).ToContainTextAsync("Nothing shared with you yet.");
        await Expect(doris.Locator("[data-street-between] li")).ToHaveTextAsync("Nobody has shared an offer with you yet. When they do, what the street has between it is counted here.");

        // Sam, at the far end of the street, joins too.
        await using IBrowserContext samsBrowser = await _browser.PhoneAsync();
        IPage sam = await samsBrowser.NewPageAsync();
        await Accounts.SignInAsync(_browser, sam, samMail);
        await sam.GotoAsync(invitation);
        await sam.Locator("[data-street-join]").GetByLabel("Your house number or name").FillAsync("78");
        await sam.Locator("[data-street-join]").GetByLabel("Your first name").FillAsync("Sam");
        await sam.GetByRole(AriaRole.Button, new() { Name = "Join the group" }).ClickAsync();
        await Expect(sam.Locator("[data-street-count]")).ToHaveTextAsync("3 homes in this group.");

        // Rob puts Doris in his closer circle, offers a warm room to everyone and phone charging to his circle.
        await rob.ReloadAsync();
        await Expect(rob.Locator("[data-street-count]")).ToHaveTextAsync("3 homes in this group.");
        await rob.GetByRole(AriaRole.Checkbox, new() { Name = "Number 12" }).CheckAsync();
        await rob.GetByLabel("A warm room").SelectOptionAsync("street");
        await rob.GetByLabel("Somewhere to charge a phone").SelectOptionAsync("close");
        await rob.GetByRole(AriaRole.Button, new() { Name = "Save what I share" }).ClickAsync();
        await Expect(rob.Locator("[data-street-says]")).ToHaveTextAsync("Your sharing is saved. Your neighbours see it the next time they open the group.");
        await Expect(rob.Locator("[data-home-mine] [data-home-offer]")).ToHaveTextAsync(["A warm room", "Somewhere to charge a phone"]);
        await ShouldBeSoundAsync(rob);
        await rob.ScreenshotAsync(new() { Path = Screenshots.Target("street-group-desktop-light.png"), FullPage = true });

        // Doris is in his circle: she sees his name and both offers.
        await doris.ReloadAsync();
        robsHomeForDoris = doris.Locator("[data-street-homes] > li").Nth(1);
        await Expect(robsHomeForDoris.Locator("[data-home-name]")).ToHaveTextAsync("Rob");
        await Expect(robsHomeForDoris.Locator("[data-home-offer]")).ToHaveTextAsync(["A warm room", "Somewhere to charge a phone"]);
        await Expect(doris.Locator("[data-street-between] li")).ToHaveTextAsync(["One home with a warm room.", "One home where a phone can be charged."]);
        await ShouldBeSoundAsync(doris);
        await doris.ScreenshotAsync(new() { Path = Screenshots.Target("street-group-phone-light.png"), FullPage = true });

        // Sam is not: he sees the house number and the offer made to everyone, and nothing else.
        await sam.ReloadAsync();
        ILocator robsHomeForSam = sam.Locator("[data-street-homes] > li").Nth(1);
        await Expect(robsHomeForSam).ToContainTextAsync("27");
        await Expect(robsHomeForSam.Locator("[data-home-name]")).ToHaveCountAsync(0);
        await Expect(robsHomeForSam.Locator("[data-home-offer]")).ToHaveTextAsync(["A warm room"]);
        await Expect(sam.Locator("[data-street-between] li")).ToHaveTextAsync(["One home with a warm room."]);
        (await sam.Locator("[data-street]").InnerTextAsync()).ShouldNotContain("Rob", customMessage: "a first name is for the closer circle");

        // And the server, asked for the group, answers nothing a person could read.
        string held = await sam.EvaluateAsync<string>("() => fetch('/api/street/' + location.hash.slice(1)).then(response => response.text())");
        // Every layer of it is unwrapped as far as it will go without a key, because a record is an envelope of envelopes.
        string unwrapped = Unwrapped(held);
        unwrapped.ShouldContain("\"s\":", Case.Sensitive, "the envelope itself is read, so the search below is looking in the right place");
        foreach (string plain in new[] { "Maple", "\"Rob\"", "Doris", "\"Sam\"", "warm-room", "phone-charging", "\"27\"", "\"house\"", "\"name\"", "\"offers\"" })
        {
            unwrapped.ShouldNotContain(plain, Case.Sensitive, "what the server holds is ciphertext");
        }

        // On paper it is the street and nothing that is a control.
        await doris.EmulateMediaAsync(new() { Media = Media.Print });
        await Expect(doris.Locator("[data-street-homes]")).ToBeVisibleAsync();
        await Expect(doris.GetByRole(AriaRole.Button, new() { Name = "Save what I share" })).ToBeHiddenAsync();
        await Expect(doris.Locator("[data-street-link]")).ToBeHiddenAsync();
        await doris.EmulateMediaAsync(new() { Media = Media.Screen });

        // Rob's other device needs only the invitation: it is in at once, with what he shares already there.
        await using IBrowserContext robsPhone = await _browser.PhoneAsync();
        IPage phone = await robsPhone.NewPageAsync();
        await Accounts.SignInAsync(_browser, phone, robMail);
        await phone.GotoAsync("/street");
        await Expect(phone.Locator("[data-street-groups] li")).ToHaveTextAsync("This device does not have this group's key. Open the invitation on this device, from a neighbour or from your other device, and it will.");
        await phone.GotoAsync(invitation);
        await Expect(phone.Locator("[data-street-title]")).ToHaveTextAsync("Maple Road");
        await Expect(phone.Locator("#share-first")).ToHaveValueAsync("Rob");
        await Expect(phone.GetByRole(AriaRole.Checkbox, new() { Name = "Number 12" })).ToBeCheckedAsync();
        await Expect(phone.GetByLabel("Somewhere to charge a phone")).ToHaveValueAsync("close");
        await phone.GotoAsync("/street");
        await Expect(phone.Locator("[data-street-groups] a")).ToHaveTextAsync("Maple Road");

        // Rob removes Sam. Sam is told the group is not there, and the invitation he had opens nothing.
        await rob.ReloadAsync();
        await rob.GetByRole(AriaRole.Button, new() { Name = "Remove number 78 from the group" }).ClickAsync();
        await Expect(rob.Locator("[data-street-says]")).ToHaveTextAsync("That home has been removed, and the invitation it had no longer works. There is a new invitation below.");
        await Expect(rob.Locator("[data-street-count]")).ToHaveTextAsync("2 homes in this group.");
        string newInvitation = await rob.Locator("[data-street-link]").InputValueAsync();
        newInvitation.ShouldNotBe(invitation);
        await Expect(doris.GetByRole(AriaRole.Button, new() { Name = "Remove number 27 from the group" })).ToHaveCountAsync(0);
        await sam.ReloadAsync();
        await Expect(sam.Locator("[data-street-gone-says]")).ToHaveTextAsync("That group is not here. It may have been closed, or your home may have been removed from it.");
        await sam.GotoAsync("/street");
        await sam.GotoAsync(invitation);
        await sam.Locator("[data-street-join]").GetByLabel("Your house number or name").FillAsync("78");
        await sam.Locator("[data-street-join]").GetByLabel("Your first name").FillAsync("Sam");
        await sam.GetByRole(AriaRole.Button, new() { Name = "Join the group" }).ClickAsync();
        await Expect(sam.Locator("[data-street-says]")).ToHaveTextAsync("That invitation does not open a group. Ask your neighbour for a new one.");

        // Doris reads the new invitation from her own page, because it travels to every member sealed; then she leaves.
        await doris.ReloadAsync();
        (await doris.Locator("[data-street-link]").InputValueAsync()).ShouldBe(newInvitation);
        await doris.GetByRole(AriaRole.Button, new() { Name = "Leave this group" }).ClickAsync();
        await Expect(doris.Locator("[data-street-groups] li")).ToHaveTextAsync("You are not in a street group yet. Start one below and invite your neighbours, or open an invitation a neighbour sent you.");
        await rob.GotoAsync("/street");
        await rob.GotoAsync(groupAddress);
        await Expect(rob.Locator("[data-street-count]")).ToHaveTextAsync("One home so far, which is yours. Send the invitation below to a neighbour.");

        // An invitation that is not one says so, and a group this device has never heard of is not there.
        await rob.GotoAsync("/street");
        await rob.GotoAsync("/street/join#not-an-invitation");
        await Expect(rob.Locator("[data-street-gone-says]")).ToHaveTextAsync("That invitation does not open a group. Ask your neighbour for a new one.");
        await rob.GotoAsync("/street");
        await rob.GotoAsync($"/street/group#{Guid.NewGuid()}");
        await Expect(rob.Locator("[data-street-gone-says]")).ToHaveTextAsync("This device does not have this group's key. Open the invitation on this device, from a neighbour or from your other device, and it will.");
    }

    [Fact]
    public async Task A_neighbour_whose_browser_lies_cannot_break_another_home_s_page()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        await using IBrowserContext adasBrowser = await _browser.SizedAsync(1440, 900, mobile: false, theme: "light");
        IPage ada = await adasBrowser.NewPageAsync();
        await Accounts.SignInAsync(_browser, ada, $"ada-{tag}@example.com");
        await ada.GotoAsync("/street");
        await ada.GetByLabel("What your neighbours call it").FillAsync("Orchard Close");
        await ada.Locator("[data-street-start]").GetByLabel("Your house number or name").FillAsync("1");
        await ada.Locator("[data-street-start]").GetByLabel("Your first name").FillAsync("Ada");
        await ada.GetByRole(AriaRole.Button, new() { Name = "Start the group" }).ClickAsync();
        await Expect(ada.Locator("[data-street-title]")).ToHaveTextAsync("Orchard Close");
        string invitation = await ada.Locator("[data-street-link]").InputValueAsync();

        await using IBrowserContext malsBrowser = await _browser.PhoneAsync();
        IPage mal = await malsBrowser.NewPageAsync();
        await Accounts.SignInAsync(_browser, mal, $"mal-{tag}@example.com");
        await mal.GotoAsync(invitation);
        await mal.Locator("[data-street-join]").GetByLabel("Your house number or name").FillAsync("13");
        await mal.Locator("[data-street-join]").GetByLabel("Your first name").FillAsync("Mal");
        await mal.GetByRole(AriaRole.Button, new() { Name = "Join the group" }).ClickAsync();
        await Expect(mal.Locator("[data-street-count]")).ToHaveTextAsync("2 homes in this group.");

        // Mal has the group's key, as every member does, and writes a record by hand instead of through the page.
        const string forge =
            """
            async (kind) => {
              const crypt = await import("/js/street-crypto.js");
              const gid = location.hash.slice(1);
              const held = JSON.parse(localStorage.getItem("selficient.streets"));
              const key = crypt.fromBase64(held[gid].replaceAll("-", "+").replaceAll("_", "/") + "=");
              const read = await (await fetch("/api/street/" + gid)).json();
              const identity = await crypt.newIdentity();
              const street = kind === "markup"
                ? { house: "<img src=x onerror=window.pwned=1>", pub: identity.pub, offers: ["warm-room", "made-up", "<b>", ...Array(20).fill("warm-room")] }
                : { house: "13", pub: "AAAA", offers: ["warm-room"] };
              const s = await crypt.seal(key, street);
              const noise = () => crypt.toBase64(crypt.randomBytes(60));
              const record = kind === "noise"
                ? crypt.toBase64(crypt.randomBytes(300))
                : crypt.toBase64(new TextEncoder().encode(JSON.stringify({ v: 1, s, c: s, w: Array.from({ length: 150 }, noise) })));
              const response = await fetch(`/api/street/${gid}/record`, {
                method: "PUT",
                headers: { "content-type": "application/json", "X-Selficient-Sync": "1" },
                body: JSON.stringify({ record, keep: read.keep }),
              });
              return response.status;
            }
            """;
        ILocator malsHome = ada.Locator("[data-street-homes] > li:not([data-home-mine])");

        // Markup in a house number is words, cut to the length a house number may be; an offer that is not one is not shown.
        (await mal.EvaluateAsync<int>(forge, "markup")).ShouldBe(200, "the server cannot tell, because it cannot read it");
        await ada.ReloadAsync();
        await Expect(malsHome.Locator(".data")).ToHaveTextAsync("<img src=x o");
        await Expect(malsHome.Locator("[data-home-offer]")).ToHaveTextAsync(["A warm room"]);
        await Expect(ada.Locator("[data-street] img")).ToHaveCountAsync(0);
        (await ada.EvaluateAsync<bool>("() => 'pwned' in window")).ShouldBeFalse("nothing a neighbour wrote is ever markup");
        await Expect(ada.Locator("[data-street-between] li")).ToHaveTextAsync(["One home with a warm room."]);

        // A public key that is not one, and a record that is only noise: the home is there, and says it could not be read.
        foreach (string kind in new[] { "bad-key", "noise" })
        {
            (await mal.EvaluateAsync<int>(forge, kind)).ShouldBe(200);
            await ada.ReloadAsync();
            await Expect(malsHome.Locator(".data")).ToHaveTextAsync("?");
            await Expect(malsHome).ToContainTextAsync("This home's sharing could not be read.");
            await Expect(ada.Locator("[data-home-mine]")).ToContainTextAsync("1 Ada Your home");
            await Expect(ada.GetByRole(AriaRole.Checkbox, new() { Name = "Number" })).ToHaveCountAsync(0, new() { Timeout = 2000 });
        }

        // And Ada can still remove it.
        await ada.Locator("[data-street-remove]").ClickAsync();
        await Expect(ada.Locator("[data-street-count]")).ToHaveTextAsync("One home so far, which is yours. Send the invitation below to a neighbour.");
    }

    [Fact]
    public async Task The_plan_leads_to_the_street_and_the_street_says_what_the_plan_holds_without_choosing_for_anyone()
    {
        await using IBrowserContext context = await _browser.SizedAsync(1440, 900, mobile: false, theme: "light");
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/plan/start");
        await Journeys.FillRobsHouseAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Sync.TickAsync(page, "safety-kit");
        await Accounts.SignInAsync(_browser, page, $"plan-{Guid.NewGuid():N}@example.com");
        await page.GotoAsync("/plan");
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Start a group for your street" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Your street groups" })).ToBeVisibleAsync();
        await page.GetByLabel("What your neighbours call it").FillAsync("Maple Road");
        await page.Locator("[data-street-start]").GetByLabel("Your house number or name").FillAsync("27");
        await page.Locator("[data-street-start]").GetByLabel("Your first name").FillAsync("Rob");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start the group" }).ClickAsync();
        await Expect(page.Locator("[data-street-title]")).ToHaveTextAsync("Maple Road");

        // The kit that was ticked is said beside its offer; a radio that was not is not; the car is as the plan was answered.
        await Expect(page.Locator("[data-offer-plan=\"first-aid\"]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-offer-plan=\"first-aid\"]")).ToHaveTextAsync("Your plan says you have this.");
        await Expect(page.Locator("[data-offer-plan=\"news\"]")).ToBeHiddenAsync();
        bool hasCar = await page.EvaluateAsync<bool>("() => import('/js/plan-store.js').then(store => store.getProfile()).then(profile => profile.Car !== 'None')");
        (await page.Locator("[data-offer-plan=\"lift\"]").IsVisibleAsync()).ShouldBe(hasCar);

        // It is a prompt and no more: every offer still goes to nobody, and nothing has been shared.
        foreach (ILocator choice in await page.Locator("[data-street-offer]").AllAsync())
        {
            await Expect(choice).ToHaveValueAsync(string.Empty);
        }

        await Expect(page.Locator("[data-street-between] li")).ToHaveTextAsync("Nobody has shared an offer with you yet. When they do, what the street has between it is counted here.");
        await ShouldBeSoundAsync(page);
        await page.Locator("[data-street-sharing]").ScreenshotAsync(new() { Path = Screenshots.Target("street-plan-says-desktop-light.png") });

        // And the way back, from where the plan is kept.
        await page.Locator("[data-keep-chip]").ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Your street groups" }).ClickAsync();
        await Expect(page.Locator("[data-street-groups] a")).ToHaveTextAsync("Maple Road");
    }

    [Theory]
    [InlineData("wide", 2000, 1100, "dark")]
    [InlineData("phone", 390, 844, "dark")]
    public async Task The_street_holds_its_layout_wide_and_narrow_in_the_dark(string size, int width, int height, string theme)
    {
        await using IBrowserContext context = await _browser.SizedAsync(width, height, mobile: width < 600, theme);
        IPage page = await context.NewPageAsync();
        await Accounts.SignInAsync(_browser, page, $"layout-{Guid.NewGuid():N}@example.com");
        await page.GotoAsync("/street");
        await page.GetByLabel("What your neighbours call it").FillAsync("Orchard Close");
        await page.Locator("[data-street-start]").GetByLabel("Your house number or name").FillAsync("Rose Cottage");
        await page.Locator("[data-street-start]").GetByLabel("Your first name").FillAsync("Ada");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start the group" }).ClickAsync();
        await Expect(page.Locator("[data-street-title]")).ToHaveTextAsync("Orchard Close");
        await page.GetByLabel("A first-aid kit").SelectOptionAsync("street");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save what I share" }).ClickAsync();
        await Expect(page.Locator("[data-street-between] li")).ToHaveTextAsync(["One home with a first-aid kit."]);

        await ShouldBeSoundAsync(page);
        double[] boxes = await page.EvaluateAsync<double[]>(
            """
            () => {
              const box = (selector) => document.querySelector(selector).getBoundingClientRect();
              return [box("#sharing-heading").left, box("#invite-heading").left, box("#sharing-heading").top, box("#invite-heading").top, box("[data-home-mine]").width];
            }
            """);
        if (width >= 1200)
        {
            boxes[1].ShouldBeGreaterThan(boxes[0] + 400, "on a wide screen the invitation sits beside what you share");
            boxes[3].ShouldBe(boxes[2], 2, "and level with it");
        }
        else
        {
            boxes[1].ShouldBe(boxes[0], 2, "on a phone the invitation is under what you share");
        }

        boxes[4].ShouldBeLessThanOrEqualTo(385, "one home is a card, not a banner across the page");
        bool codeIsOnWhite = await page.Locator("[data-street-qr]").EvaluateAsync<bool>("box => getComputedStyle(box).backgroundColor === 'rgb(255, 255, 255)'");
        codeIsOnWhite.ShouldBeTrue("a code inverted in the dark does not scan");
        await page.ScreenshotAsync(new() { Path = Screenshots.Target($"street-group-{size}-{theme}.png"), FullPage = true });
    }

    /// <summary>No accessibility violations and nothing scrolling sideways, on the page as it stands.</summary>
    private static async Task ShouldBeSoundAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.fonts.status === 'loaded' && document.getAnimations().every((a) => a.playState === 'finished' || a.playState === 'idle')");
        AxeResult axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes) {v.Nodes[0].Html}")));
        string widest = await page.EvaluateAsync<string>(
            """
            () => {
              // The elements that widen the page are the ones whose absence narrows it; name the innermost.
              const root = document.documentElement;
              const wide = () => root.scrollWidth > root.clientWidth;
              if (!wide()) {
                return "";
              }
              const cause = [...document.querySelectorAll("body *")].filter((element) => {
                if (element.hidden) {
                  return false;
                }
                element.hidden = true;
                const cured = !wide();
                element.hidden = false;
                return cured;
              });
              return cause.filter((element) => !cause.some((other) => other !== element && element.contains(other))).slice(0, 4).map((element) => `${element.tagName.toLowerCase()}.${element.className}#${element.id}`).join("; ");
            }
            """);
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0, $"nothing scrolls sideways: {widest}");
    }

    /// <summary>
    /// The text with every base64 value in it decoded beside it, and the same again inside what that gave,
    /// so a plain word one or two envelopes down is found as readily as one on the surface.
    /// </summary>
    private static string Unwrapped(string text, int depth = 0)
    {
        StringBuilder all = new(text);
        if (depth == 3)
        {
            return all.ToString();
        }

        foreach (Match match in Regex.Matches(text, "\"([A-Za-z0-9+/]{16,}={0,2})\""))
        {
            byte[] bytes = new byte[match.Groups[1].Length];
            if (Convert.TryFromBase64String(match.Groups[1].Value, bytes, out int written))
            {
                all.Append('\n').Append(Unwrapped(Encoding.Latin1.GetString(bytes, 0, written), depth + 1));
            }
        }

        return all.ToString();
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
