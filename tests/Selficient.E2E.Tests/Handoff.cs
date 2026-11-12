using System.Text;
using System.Text.RegularExpressions;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Selficient.E2E.Tests;

/// <summary>
/// Sending a copy of a plan from one browser to another, through the real relay. Two contexts
/// stand for two devices: nothing is shared between them but the link. The cases are the ones in
/// <c>docs/threat-models/handoff.md</c>.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed partial class Handoff
{
    private const string WindowsChrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    private const string AndroidChrome = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36";

    private readonly Browser _browser;

    public Handoff(Browser browser) => _browser = browser;

    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(42)]
    [InlineData(43)]
    [InlineData(62)]
    [InlineData(84)]
    [InlineData(96)]
    [InlineData(97)]
    [InlineData(106)]
    [InlineData(107)]
    [InlineData(122)]
    [InlineData(123)]
    [InlineData(152)]
    [InlineData(153)]
    [InlineData(180)]
    public async Task A_code_drawn_in_the_browser_reads_back_as_the_text_it_was_given(int length)
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/receive");
        StringBuilder text = new("https://selficient.app/receive#");
        while (text.Length < length)
        {
            text.Append("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_."[((text.Length * 7) + length) % 65]);
        }

        string wanted = text.ToString()[..length];

        bool[][] modules = await page.EvaluateAsync<bool[][]>("text => import('/js/qr.js').then(qr => qr.qrMatrix(text))", wanted);

        Read(modules).ShouldBe(wanted, "an independent decoder reads the code the browser drew");
        (modules.Length % 4).ShouldBe(1, "a code is 17 modules plus four for each version");
    }

    [Fact]
    public async Task A_text_too_long_for_a_code_is_refused_rather_than_drawn_wrongly()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/receive");

        string refused = await page.EvaluateAsync<string>("() => import('/js/qr.js').then(qr => { try { qr.qrMatrix('x'.repeat(qr.MAX_BYTES + 1)); return 'drawn'; } catch (error) { return error.message; } })");

        refused.ShouldBe("That text is too long for a code.");
    }

    [Fact]
    public async Task A_plan_is_sent_to_another_device_which_takes_it_and_the_server_never_sees_the_key()
    {
        await using IBrowserContext sending = await _browser.SizedAsync(1280, 900, mobile: false, theme: "light", userAgent: WindowsChrome);
        await sending.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        IPage sender = await sending.NewPageAsync();
        List<IRequest> requests = [];
        sender.Request += (_, request) => requests.Add(request);
        await BuildPlanAsync(sender);
        await sender.Locator("[data-action-id=\"water-bottled-three-days\"]").CheckAsync();
        await sender.Locator("[data-action-id=\"headtorches\"]").CheckAsync();
        await Expect(sender.Locator("[data-action-id=\"headtorches\"]")).ToBeCheckedAsync();

        // The panel offers a second row once there is a plan to send.
        ILocator panel = sender.Locator("#keep-dialog");
        ILocator row = panel.Locator("[data-keep-another]");
        await sender.Locator("[data-keep-chip]").ClickAsync();
        await Expect(row.GetByRole(AriaRole.Heading, new() { Name = "Another device" })).ToBeVisibleAsync();
        await Expect(row.Locator("[data-send-state=\"idle\"] p").First).ToHaveTextAsync("Send a copy of your plan to your phone or another computer. Changes you make afterwards will not follow it.");
        await row.GetByRole(AriaRole.Button, new() { Name = "Send a copy", Exact = true }).ClickAsync();

        // The code, the sentence under it, and the link for a device with no camera.
        ILocator code = row.Locator("[data-send-code] svg");
        await Expect(code).ToBeVisibleAsync();
        await Expect(row.Locator("[data-send-state=\"code\"] p").First).ToHaveTextAsync("Point your other device's camera at this. It works for ten minutes, once.");
        (await row.Locator("[data-send-code]").EvaluateAsync<string>("box => getComputedStyle(box).backgroundColor")).ShouldBe("rgb(255, 255, 255)", "dark on white whatever the theme");
        AxeResult axe = await sender.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));
        await row.GetByRole(AriaRole.Button, new() { Name = "Copy the link instead" }).ClickAsync();
        await Expect(row.Locator("[data-send-says]:visible")).ToHaveTextAsync("Link copied. Open it on the other device within ten minutes.");
        string link = await sender.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Match parts = LinkShape().Match(link);
        parts.Success.ShouldBeTrue(link);
        Read(await ModulesAsync(code)).ShouldBe(link, "the code on the screen is the link, read by an independent decoder");

        // What went to the server: bytes that are not the plan, and never the key.
        string key = parts.Groups["key"].Value;
        IRequest posted = requests.Single(r => r.Method == "POST" && r.Url.EndsWith("/api/handoff", StringComparison.Ordinal));
        byte[] body = posted.PostDataBuffer!;
        Encoding.Latin1.GetString(body).ShouldNotContain("NP12", customMessage: "what the server keeps cannot be read without the key");
        Encoding.Latin1.GetString(body).ShouldNotContain("headtorches");
        body.Length.ShouldBeLessThan(4096, "a whole plan, compressed and sealed, is small");
        requests.ShouldAllBe(r => !r.Url.Contains(key, StringComparison.Ordinal) && !(r.PostData ?? string.Empty).Contains(key, StringComparison.Ordinal), "the key is in the fragment and no request carries it");

        // The other device: told what arrived, and nothing is written until it says so.
        await using IBrowserContext receiving = await _browser.SizedAsync(390, 844, mobile: true, theme: "dark", userAgent: AndroidChrome);
        IPage receiver = await receiving.NewPageAsync();
        await receiver.GotoAsync(link);
        ILocator arrived = receiver.Locator("[data-receive-state=\"arrived\"]");
        await Expect(arrived.GetByRole(AriaRole.Heading, new() { Name = "A plan has arrived" })).ToBeVisibleAsync();
        await Expect(arrived.Locator("[data-receive-summary]")).ToHaveTextAsync("A plan for 4 people in NP12, 2 things ticked, made on a Windows computer just now.");
        new Uri(receiver.Url).Fragment.ShouldBeEmpty("the key leaves the address bar as soon as it has been read");
        (await receiver.EvaluateAsync<bool>("() => import('/js/plan-store.js').then(store => store.getProfile()).then(profile => profile === null)")).ShouldBeTrue("nothing is on the device before the press");
        (await receiver.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0);
        AxeResult arrivedAxe = await receiver.RunAxe();
        arrivedAxe.Violations.ShouldBeEmpty(string.Join("\n", arrivedAxe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));

        // A reload before the press does not lose the only copy.
        await receiver.ReloadAsync();
        await Expect(arrived.Locator("[data-receive-summary]")).ToContainTextAsync("A plan for 4 people in NP12, 2 things ticked, made on a Windows computer");
        await arrived.GetByRole(AriaRole.Button, new() { Name = "Put this plan on this device" }).ClickAsync();
        ILocator done = receiver.Locator("[data-receive-state=\"done\"]");
        await Expect(done.GetByRole(AriaRole.Heading, new() { Name = "Your plan is on this device" })).ToBeVisibleAsync();
        await Expect(done.Locator("p").First).ToHaveTextAsync("This device has its own copy now. Changes you make here will not appear on the other device, and changes there will not appear here.");
        await done.GetByRole(AriaRole.Link, new() { Name = "Open your plan" }).ClickAsync();
        await receiver.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(receiver.Locator("[data-action-id=\"water-bottled-three-days\"]")).ToBeCheckedAsync();
        await Expect(receiver.Locator("[data-action-id=\"headtorches\"]")).ToBeCheckedAsync();
        await Expect(receiver.Locator("[data-keep-chip] span:visible")).ToHaveTextAsync("Saved on this device");

        // Back on the sender: told it went, and told the two are not linked. The code has left the screen.
        ILocator sent = row.Locator("[data-send-state=\"sent\"]");
        await Expect(sent).ToBeVisibleAsync();
        await Expect(sent.Locator("p").First).ToHaveTextAsync("Sent. Your other device has its own copy now. Changes you make on one will not appear on the other.");
        (await row.Locator("[data-send-code]").EvaluateAsync<int>("box => box.childElementCount")).ShouldBe(0);

        // The link works once: a second visit, and a visit after reloading with nothing waiting, are told so.
        IPage again = await receiving.NewPageAsync();
        await again.GotoAsync(link);
        await Expect(again.GetByRole(AriaRole.Heading, new() { Name = "That link has been used" })).ToBeVisibleAsync();
        await Expect(again.Locator("[data-receive-state=\"used\"] p").First).ToHaveTextAsync("A link works once, for ten minutes, and this one has been used or has run out. Send a copy again from the other device.");
        new Uri(again.Url).Fragment.ShouldBeEmpty();
        await again.ReloadAsync();
        await Expect(again.GetByRole(AriaRole.Heading, new() { Name = "Receive a plan from another device" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_device_that_already_has_a_plan_chooses_to_keep_it_or_replace_it_and_the_two_are_never_mixed()
    {
        await using IBrowserContext sending = await _browser.SizedAsync(1280, 900, mobile: false, theme: "dark", userAgent: WindowsChrome);
        await sending.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        IPage sender = await sending.NewPageAsync();
        await BuildPlanAsync(sender);
        await sender.Locator("[data-action-id=\"headtorches\"]").CheckAsync();
        await Expect(sender.Locator("[data-action-id=\"headtorches\"]")).ToBeCheckedAsync();

        await using IBrowserContext receiving = await _browser.SizedAsync(390, 844, mobile: true, theme: "light", userAgent: AndroidChrome);
        IPage receiver = await receiving.NewPageAsync();
        await receiver.GotoAsync("/plan/start");
        await receiver.GetByLabel("Postcode", new() { Exact = true }).FillAsync("NP12 1GW");
        await receiver.GetByLabel("How many people live in your home?").FillAsync("1");
        await receiver.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await receiver.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await receiver.Locator("[data-action-id=\"water-bottled-three-days\"]").CheckAsync();
        await Expect(receiver.Locator("[data-action-id=\"water-bottled-three-days\"]")).ToBeCheckedAsync();

        // The link under the first plan opens the panel and starts at once.
        await sender.Locator("[data-kept-notice]").GetByRole(AriaRole.Button, new() { Name = "Send a copy to my phone" }).ClickAsync();
        await Expect(sender.Locator("[data-kept-notice]")).ToBeHiddenAsync();
        string first = await LinkAsync(sender);

        await receiver.GotoAsync(first);
        ILocator choose = receiver.Locator("[data-receive-state=\"choose\"]");
        await Expect(choose.GetByRole(AriaRole.Heading, new() { Name = "This device already has a plan" })).ToBeVisibleAsync();
        await Expect(choose.Locator("[data-receive-summary]")).ToHaveTextAsync("A plan for 4 people in NP12, 1 thing ticked, made on a Windows computer just now.");
        await Expect(choose.Locator("[data-receive-here]")).ToHaveTextAsync("The plan on this device is for 1 person in NP12, 1 thing ticked.");
        await Expect(choose.Locator("p").Nth(2)).ToHaveTextAsync("Replace it with the one you sent, or keep the one that is here. The two are never mixed.");
        AxeResult axe = await receiver.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));

        // Keep: nothing changes, and the copy is gone.
        await choose.GetByRole(AriaRole.Button, new() { Name = "Keep the one that is here" }).ClickAsync();
        await Expect(receiver.GetByRole(AriaRole.Heading, new() { Name = "Nothing was changed" })).ToBeVisibleAsync();
        (await PlanAsync(receiver)).ShouldBe("1|water-bottled-three-days");
        await receiver.ReloadAsync();
        await Expect(receiver.GetByRole(AriaRole.Heading, new() { Name = "Receive a plan from another device" })).ToBeVisibleAsync();

        // Replace: the plan here becomes the one sent, whole. Nothing of the old one is left in it.
        await Expect(sender.Locator("[data-send-state=\"sent\"]")).ToBeVisibleAsync();
        await sender.Locator("[data-send-state=\"sent\"]").GetByRole(AriaRole.Button, new() { Name = "Send a copy again" }).ClickAsync();
        string second = await LinkAsync(sender);
        second.ShouldNotBe(first, "each copy has a ticket and a key of its own");
        await receiver.GotoAsync(second);
        await choose.GetByRole(AriaRole.Button, new() { Name = "Replace it with the one I sent" }).ClickAsync();
        await Expect(receiver.GetByRole(AriaRole.Heading, new() { Name = "Your plan is on this device" })).ToBeVisibleAsync();
        (await PlanAsync(receiver)).ShouldBe("4|headtorches");
    }

    [Fact]
    public async Task A_link_that_is_wrong_broken_or_hostile_changes_nothing_and_says_why()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        string before = await PlanAsync(page);
        string key = new('A', 43);

        // Half a link.
        await page.GotoAsync("/receive#AAAAAAAAAAAAAAAAAAAAAA");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "That link is not complete" })).ToBeVisibleAsync();
        new Uri(page.Url).Fragment.ShouldBeEmpty();
        await page.GotoAsync("/receive#not.a.link");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "That link is not complete" })).ToBeVisibleAsync();

        // A ticket nothing was left under.
        await page.GotoAsync($"/receive#AAAAAAAAAAAAAAAAAAAAAA.{key}");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "That link has been used" })).ToBeVisibleAsync();

        // A real copy opened with the wrong key: the cipher's own check refuses it.
        string wrongKey = await LeaveAsync(page, "handoff => handoff.seal({ v: 1, made: Date.now(), from: 'windows', plan: { profile: { People: '9' }, ticks: [] } }).then(sealed => sealed.body)", key);
        await OpenAsync(page, wrongKey);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "That copy could not be read" })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-receive-state=\"unreadable\"] p").First).ToHaveTextAsync("What arrived could not be opened as a Selficient plan, so nothing was changed on this device. Send a copy again from the other device.");

        // Sealed properly, but what is inside is not a plan: a device name that is markup, a list that is not ids.
        foreach (string inside in new[]
        {
            "{ v: 1, made: Date.now(), from: '<img src=x onerror=alert(1)>', plan: { profile: { People: '9' }, ticks: [] } }",
            "{ v: 1, made: Date.now(), from: 'windows', plan: { profile: { People: '9' }, ticks: ['<script>'] } }",
            "{ v: 2, made: Date.now(), from: 'windows', plan: { profile: { People: '9' }, ticks: [] } }",
            "{ v: 1, made: 'yesterday', from: 'windows', plan: { profile: { People: '9' }, ticks: [] } }",
            "{ v: 1, made: Date.now(), from: 'windows', plan: { profile: { People: '9' }, ticks: [], house: { lat: 0, lng: 0 } } }",
        })
        {
            string hostile = await LeaveAsync(page, $"handoff => handoff.seal({inside})", null);
            await OpenAsync(page, hostile);
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "That copy could not be read" })).ToBeVisibleAsync();
        }

        // A copy that inflates past the cap: two megabytes of nothing in two kilobytes.
        string bomb = await LeaveAsync(page, """
            async () => {
              const zeros = new Blob([new Uint8Array(2 * 1024 * 1024)]).stream().pipeThrough(new CompressionStream('gzip'));
              const squeezed = new Uint8Array(await new Response(zeros).arrayBuffer());
              const plain = new Uint8Array(squeezed.length + 1);
              plain[0] = 1;
              plain.set(squeezed, 1);
              const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt']);
              const nonce = crypto.getRandomValues(new Uint8Array(12));
              const sealed = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv: nonce }, key, plain));
              const body = new Uint8Array(12 + sealed.length);
              body.set(nonce, 0);
              body.set(sealed, 12);
              return { body, key: new Uint8Array(await crypto.subtle.exportKey('raw', key)) };
            }
            """, null);
        await OpenAsync(page, bomb);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "That copy could not be read" })).ToBeVisibleAsync();

        (await PlanAsync(page)).ShouldBe(before, "none of it touched the plan that was here");
        await page.GotoAsync("/receive");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Receive a plan from another device" })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-receive-state]:visible")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task A_copy_that_cannot_be_collected_yet_is_not_spent_and_a_code_that_is_closed_leaves_the_screen()
    {
        await using IBrowserContext sending = await _browser.SizedAsync(1280, 900, mobile: false, theme: "light", userAgent: WindowsChrome);
        await sending.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        IPage sender = await sending.NewPageAsync();
        await BuildPlanAsync(sender);
        await sender.Locator("[data-keep-chip]").ClickAsync();
        ILocator row = sender.Locator("[data-keep-another]");

        // Without a connection the sender is told so, and nothing is left on the screen.
        await sending.SetOfflineAsync(true);
        await row.GetByRole(AriaRole.Button, new() { Name = "Send a copy", Exact = true }).ClickAsync();
        await Expect(row.Locator("[data-send-says]:visible")).ToHaveTextAsync("A copy cannot be sent without a connection. Try again when you are back online.");
        await sending.SetOfflineAsync(false);
        await row.GetByRole(AriaRole.Button, new() { Name = "Send a copy", Exact = true }).ClickAsync();
        string link = await LinkAsync(sender);

        // The receiver has no connection at first: the copy is not spent, and trying again collects it.
        await using IBrowserContext receiving = await _browser.SizedAsync(390, 844, mobile: true, theme: "light", userAgent: AndroidChrome);
        IPage receiver = await receiving.NewPageAsync();
        await receiver.GotoAsync("/receive");
        await receiver.RouteAsync("**/api/handoff/*", route => route.AbortAsync());
        await receiver.GotoAsync(link);
        await Expect(receiver.GetByRole(AriaRole.Heading, new() { Name = "The copy could not be collected" })).ToBeVisibleAsync();
        await Expect(receiver.Locator("[data-receive-state=\"later\"] p").First).ToHaveTextAsync("The copy could not be collected just now, and it has not been used up. Check the connection and try again within ten minutes of making it.");
        new Uri(receiver.Url).Fragment.ShouldNotBeEmpty("the link is kept so that trying again can work");
        await receiver.UnrouteAsync("**/api/handoff/*");
        await receiver.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await Expect(receiver.GetByRole(AriaRole.Heading, new() { Name = "A plan has arrived" })).ToBeVisibleAsync();
        await receiver.GetByRole(AriaRole.Button, new() { Name = "Do not put it here" }).ClickAsync();
        await Expect(receiver.GetByRole(AriaRole.Heading, new() { Name = "Nothing was changed" })).ToBeVisibleAsync();
        (await receiver.EvaluateAsync<bool>("() => import('/js/plan-store.js').then(store => store.getProfile()).then(profile => profile === null)")).ShouldBeTrue();

        // Closing the panel takes a code off the screen.
        await Expect(row.Locator("[data-send-state=\"sent\"]")).ToBeVisibleAsync();
        await row.Locator("[data-send-state=\"sent\"]").GetByRole(AriaRole.Button, new() { Name = "Send a copy again" }).ClickAsync();
        await Expect(row.Locator("[data-send-code] svg")).ToBeVisibleAsync();
        await sender.Keyboard.PressAsync("Escape");
        await sender.Locator("[data-keep-chip]").ClickAsync();
        await Expect(row.Locator("[data-send-state=\"idle\"]")).ToBeVisibleAsync();
        (await row.Locator("[data-send-code]").EvaluateAsync<int>("box => box.childElementCount")).ShouldBe(0);
    }

    [GeneratedRegex(@"^https?://[^/]+/receive#(?<id>[A-Za-z0-9_-]{22})\.(?<key>[A-Za-z0-9_-]{43})$")]
    private static partial Regex LinkShape();

    [GeneratedRegex(@"M(?<x>\d+) (?<y>\d+)h(?<run>\d+)v1h-\d+z")]
    private static partial Regex Bar();

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static async Task BuildPlanAsync(IPage page)
    {
        await page.GotoAsync("/plan/start");
        await Journeys.FillRobsHouseAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
    }

    /// <summary>The link the sender's panel is showing, by the button for a device with no camera.</summary>
    private static async Task<string> LinkAsync(IPage sender)
    {
        ILocator row = sender.Locator("[data-keep-another]");
        await Expect(row.Locator("[data-send-code] svg")).ToBeVisibleAsync();
        await row.GetByRole(AriaRole.Button, new() { Name = "Copy the link instead" }).ClickAsync();
        await Expect(row.Locator("[data-send-says=\"copied\"]")).ToBeVisibleAsync();
        return await sender.EvaluateAsync<string>("() => navigator.clipboard.readText()");
    }

    /// <summary>The people in the plan on a device and what is ticked, as "4|a,b".</summary>
    private static Task<string> PlanAsync(IPage page) =>
        page.EvaluateAsync<string>("() => import('/js/plan-store.js').then(store => store.snapshot()).then(plan => `${plan.profile?.People ?? ''}|${plan.ticks.join(',')}`)");

    /// <summary>
    /// Leaves whatever a script seals on the relay and answers the fragment that would open it. The
    /// script is given the handoff module and answers the body and the key; a key passed here is
    /// used in the link instead, to make a link whose key is wrong.
    /// </summary>
    private static Task<string> LeaveAsync(IPage page, string seal, string? keyInstead) =>
        page.EvaluateAsync<string>(
            $$"""
            async keyInstead => {
              const handoff = await import('/js/handoff.js');
              const made = await ({{seal}})(handoff);
              const body = made.body ?? made;
              const response = await fetch('/api/handoff', { method: 'POST', headers: { 'Content-Type': 'application/octet-stream' }, body });
              const ticket = await response.json();
              return `${ticket.id}.${keyInstead ?? handoff.toBase64Url(made.key)}`;
            }
            """,
            keyInstead);

    /// <summary>Opens a link from a page that is not the receive page, as a camera or a paste does.</summary>
    private static async Task OpenAsync(IPage page, string fragment)
    {
        await page.GotoAsync("/offline");
        await page.GotoAsync($"/receive#{fragment}");
    }

    /// <summary>The modules of the code on the screen, from the bars of its path.</summary>
    private static async Task<bool[][]> ModulesAsync(ILocator svg)
    {
        string[] box = (await svg.GetAttributeAsync("viewBox"))!.Split(' ');
        int size = int.Parse(box[2], System.Globalization.CultureInfo.InvariantCulture) - 8;
        string d = (await svg.Locator("path").GetAttributeAsync("d"))!;
        bool[][] modules = [.. Enumerable.Range(0, size).Select(_ => new bool[size])];
        foreach (Match bar in Bar().Matches(d))
        {
            int x = int.Parse(bar.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture) - 4;
            int y = int.Parse(bar.Groups["y"].Value, System.Globalization.CultureInfo.InvariantCulture) - 4;
            int run = int.Parse(bar.Groups["run"].Value, System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < run; i++)
            {
                modules[y][x + i] = true;
            }
        }

        return modules;
    }

    /// <summary>Reads a code with ZXing, which shares no code with the encoder under test.</summary>
    private static string Read(bool[][] modules)
    {
        const int Scale = 4;
        const int Quiet = 4;
        int size = (modules.Length + (Quiet * 2)) * Scale;
        byte[] luminance = [.. Enumerable.Repeat((byte)255, size * size)];
        for (int y = 0; y < modules.Length; y++)
        {
            for (int x = 0; x < modules.Length; x++)
            {
                if (!modules[y][x])
                {
                    continue;
                }

                for (int dy = 0; dy < Scale; dy++)
                {
                    for (int dx = 0; dx < Scale; dx++)
                    {
                        luminance[((((y + Quiet) * Scale) + dy) * size) + ((x + Quiet) * Scale) + dx] = 0;
                    }
                }
            }
        }

        ZXing.RGBLuminanceSource source = new(luminance, size, size, ZXing.RGBLuminanceSource.BitmapFormat.Gray8);
        // The image is exactly the code and its quiet zone, so the reader is told not to go looking for one in a photograph.
        Dictionary<ZXing.DecodeHintType, object> hints = new() { [ZXing.DecodeHintType.PURE_BARCODE] = true };
        ZXing.Result? result = new ZXing.QrCode.QRCodeReader().decode(new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(source)), hints);
        return result?.Text ?? string.Empty;
    }
}
