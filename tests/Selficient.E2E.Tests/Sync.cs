using System.Text.Json;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Selficient.E2E.Tests;

/// <summary>
/// Keeping a signed-in household's devices in step, in real browsers against the real account
/// store. Each context is a device; nothing joins them but the account. The cases are section 5 of
/// <c>docs/threat-models/household-accounts.md</c>.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class Sync
{
    private const string Water = "water-bottled-three-days";
    private const string Food = "food-no-cook-three-days";
    private const string Torches = "headtorches";
    private const string Cash = "cash-in-small-notes";

    private readonly Browser _browser;

    public Sync(Browser browser) => _browser = browser;

    [Fact]
    public async Task Two_devices_stay_in_step_through_an_account_and_a_tick_taken_off_stays_off()
    {
        const string Email = "in-step@example.com";
        await using IBrowserContext phoneContext = await _browser.PhoneAsync();
        IPage phone = await phoneContext.NewPageAsync();
        await BuildPlanAsync(phone);
        await TickAsync(phone, Water);
        await TickAsync(phone, Torches);

        // The first device: its plan goes into the account, and it is told so once.
        await Accounts.SignInAsync(_browser, phone, Email);
        await Expect(phone.Locator("[data-sync-notice] [data-sync-says]:visible")).ToHaveTextAsync("Your plan is now kept in your account.");
        await TicksInAccountAsync(phone, $"{Torches},{Water}");
        await phone.Locator("[data-sync-notice]").GetByRole(AriaRole.Button, new() { Name = "Got it" }).ClickAsync();
        await Expect(phone.Locator("[data-sync-notice]")).ToBeHiddenAsync();
        await phone.Locator("[data-keep-chip]").ClickAsync();
        await Expect(phone.Locator("[data-sync-status]:visible")).ToHaveTextAsync("Your plan is kept in your account, and every device you sign in on stays in step.");
        AxeResult axe = await phone.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));
        await phone.Keyboard.PressAsync("Escape");

        // A second device with no plan: signing in is all it takes, and the plan is built there.
        await using IBrowserContext laptopContext = await _browser.SizedAsync(1280, 900, mobile: false, theme: "dark");
        IPage laptop = await laptopContext.NewPageAsync();
        await Accounts.SignInAsync(_browser, laptop, Email);
        await laptop.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(laptop.Locator($"[data-action-id=\"{Water}\"]")).ToBeCheckedAsync();
        await Expect(laptop.Locator($"[data-action-id=\"{Torches}\"]")).ToBeCheckedAsync();
        await Expect(laptop.Locator("[data-sync-notice] [data-sync-says]:visible")).ToHaveTextAsync("Your plan has arrived from your account.");
        (await laptop.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0);
        AxeResult arrived = await laptop.RunAxe();
        arrived.Violations.ShouldBeEmpty(string.Join("\n", arrived.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));

        // A tick added and a tick taken off on the laptop both reach the phone. The one taken off stays off.
        await TickAsync(laptop, Food);
        await UntickAsync(laptop, Water);
        await TicksInAccountAsync(laptop, $"{Food},{Torches}");
        await phone.ReloadAsync();
        await phone.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(phone.Locator($"[data-action-id=\"{Food}\"]")).ToBeCheckedAsync();
        await Expect(phone.Locator($"[data-action-id=\"{Water}\"]")).Not.ToBeCheckedAsync();
        await Expect(phone.Locator($"[data-action-id=\"{Torches}\"]")).ToBeCheckedAsync();
        await Expect(phone.Locator("html")).ToHaveAttributeAsync("data-sync", "synced");
        (await PlanAsync(phone)).ShouldBe($"4|{Food},{Torches}");
        await Expect(phone.Locator("[data-sync-notice]")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task A_second_device_with_its_own_plan_chooses_which_to_keep_and_the_two_are_never_mixed()
    {
        const string Email = "two-plans@example.com";
        await using IBrowserContext first = await _browser.SizedAsync(1280, 900, mobile: false, theme: "light");
        IPage a = await first.NewPageAsync();
        await BuildPlanAsync(a);
        await Accounts.SignInAsync(_browser, a, Email);
        await TicksInAccountAsync(a, string.Empty);

        // A device that already has a different plan signs in: it is asked, with each plan said in a clause.
        await using IBrowserContext second = await _browser.PhoneAsync();
        IPage b = await second.NewPageAsync();
        await BuildPlanAsync(b, people: "1");
        await TickAsync(b, Cash);
        await Accounts.SignInAsync(_browser, b, Email);
        ILocator choice = b.Locator("[data-sync-choice]");
        await Expect(choice.GetByRole(AriaRole.Heading, new() { Name = "This device and your account each have a plan" })).ToBeVisibleAsync();
        await Expect(choice.Locator("[data-sync-theirs]")).ToHaveTextAsync("In your account: a plan for 4 people in NP12, nothing ticked yet.");
        await Expect(choice.Locator("[data-sync-mine]")).ToHaveTextAsync("On this device: a plan for 1 person in NP12, 1 thing ticked.");
        await Expect(choice.Locator("p").Nth(2)).ToHaveTextAsync("Choose one. The two are never mixed, and the one you choose replaces the other on every device you sign in on.");
        AxeResult axe = await b.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("\n", axe.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)")));
        (await b.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")).ShouldBeLessThanOrEqualTo(0);

        // Putting the question off changes nothing on either side, and the panel offers it again.
        await b.Keyboard.PressAsync("Escape");
        await Expect(choice).ToBeHiddenAsync();
        (await PlanAsync(b)).ShouldBe($"1|{Cash}");
        (await AccountPlanAsync(b)).ShouldBe("4|");
        await b.Locator("[data-keep-chip]").ClickAsync();
        await Expect(b.Locator("[data-sync-status]:visible")).ToContainTextAsync("This device and your account each have a plan.");
        await b.GetByRole(AriaRole.Button, new() { Name = "Choose which to keep" }).ClickAsync();
        await Expect(choice).ToBeVisibleAsync();
        await Expect(b.Locator("#keep-dialog")).ToBeHiddenAsync();

        // The account's plan: this device becomes it, whole.
        await RebuiltAsync(b, () => choice.GetByRole(AriaRole.Button, new() { Name = "Use the plan in my account" }).ClickAsync());
        await Expect(b.Locator("[data-sync-notice] [data-sync-says]:visible")).ToHaveTextAsync("This device now has the plan from your account.");
        (await PlanAsync(b)).ShouldBe("4|", "nothing of the plan that was here is left in it");
        await Expect(b.Locator("html")).ToHaveAttributeAsync("data-sync", "synced");

        // A third device keeps its own: the account becomes that, and so does every other device.
        await using IBrowserContext third = await _browser.PhoneAsync();
        IPage c = await third.NewPageAsync();
        await BuildPlanAsync(c, people: "2");
        await TickAsync(c, Torches);
        await Accounts.SignInAsync(_browser, c, Email);
        await c.Locator("[data-sync-choice]").GetByRole(AriaRole.Button, new() { Name = "Keep the plan on this device" }).ClickAsync();
        await Expect(c.Locator("[data-sync-notice] [data-sync-says]:visible")).ToHaveTextAsync("Your account now keeps the plan from this device.");
        await TicksInAccountAsync(c, Torches);
        (await AccountPlanAsync(c)).ShouldBe($"2|{Torches}");
        await a.ReloadAsync();
        await Expect(a.Locator($"[data-action-id=\"{Torches}\"]")).ToBeCheckedAsync();
        (await PlanAsync(a)).ShouldBe($"2|{Torches}");
    }

    [Fact]
    public async Task Changes_made_without_a_connection_catch_up_and_household_details_changed_on_both_go_to_the_newer()
    {
        const string Email = "catch-up@example.com";
        await using IBrowserContext first = await _browser.SizedAsync(1280, 900, mobile: false, theme: "light");
        IPage a = await first.NewPageAsync();
        await BuildPlanAsync(a);
        await Accounts.SignInAsync(_browser, a, Email);
        await TicksInAccountAsync(a, string.Empty);
        await using IBrowserContext second = await _browser.PhoneAsync();
        IPage b = await second.NewPageAsync();
        await Accounts.SignInAsync(_browser, b, Email);
        await b.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(b.Locator("html")).ToHaveAttributeAsync("data-sync", "synced");
        await b.Locator("[data-sync-notice]").GetByRole(AriaRole.Button, new() { Name = "Got it" }).ClickAsync();

        // The phone loses its connection to the account. It goes on working, and says its changes are waiting.
        await b.RouteAsync("**/api/account/plan**", route => route.AbortAsync());
        await TickAsync(b, Cash);
        await BuildPlanAsync(b, people: "3");
        await Expect(b.Locator("html")).ToHaveAttributeAsync("data-sync", "waiting");
        // The phone has noted when its household details changed, which is what "newer" will be judged by.
        await Until.TrueAsync(b, "() => (JSON.parse(localStorage.getItem('selficient.sync') ?? '{}').detailsKey ?? '').includes('\"People\":\"3\"')");
        await b.Locator("[data-keep-chip]").ClickAsync();
        await Expect(b.Locator("[data-sync-status]:visible")).ToHaveTextAsync("Changes made on this device are waiting for a connection. They will go to your account when there is one.");
        await b.Keyboard.PressAsync("Escape");

        // Meanwhile the laptop ticks something else and changes the household too, later.
        await TickAsync(a, Torches);
        await BuildPlanAsync(a, people: "5", robsHouse: true);
        await Until.TrueAsync(a, "async () => { const answer = await (await fetch('/api/account/plan')).json(); return answer.body !== null && JSON.parse(answer.body).plan.profile.People === '5'; }");

        // The connection comes back: the ticks from both are kept, and the newer household details win and say so.
        await b.UnrouteAsync("**/api/account/plan**");
        await RebuiltAsync(b, () => b.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"));
        await Expect(b.Locator("[data-sync-notice] [data-sync-says]:visible")).ToHaveTextAsync("Your other device changed your household details. This device now shows those.");
        await TicksInAccountAsync(b, $"{Cash},{Torches}");
        (await PlanAsync(b)).ShouldBe($"5|{Cash},{Torches}");
        await a.ReloadAsync();
        await Expect(a.Locator($"[data-action-id=\"{Cash}\"]")).ToBeCheckedAsync();
        (await PlanAsync(a)).ShouldBe($"5|{Cash},{Torches}");
    }

    [Fact]
    public async Task A_copy_from_the_account_that_the_browser_would_not_accept_as_a_plan_changes_nothing()
    {
        const string Email = "guarded@example.com";
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await BuildPlanAsync(page);
        await TickAsync(page, Water);
        await Accounts.SignInAsync(_browser, page, Email);
        await TicksInAccountAsync(page, Water);
        string hostile = JsonSerializer.Serialize(new
        {
            version = 9,
            body = """{"plan":{"profile":{"People":"<img src=x onerror=document.title='owned'>"},"ticks":["<script>"]},"detailsAt":9999999999999}""",
        });

        await page.RouteAsync("**/api/account/plan", route => route.Request.Method == "GET" ? route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = hostile }) : route.ContinueAsync());
        await page.ReloadAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-sync", "synced");

        (await PlanAsync(page)).ShouldBe($"4|{Water}", "what failed the browser's own guard was never written to the device");
        (await page.TitleAsync()).ShouldNotBe("owned");
        await page.UnrouteAsync("**/api/account/plan");
        (await AccountPlanAsync(page)).ShouldBe($"4|{Water}", "and the account still holds the plan this device knows");
    }

    [Theory]
    [InlineData("a removal on one device survives a device that did nothing", "a,b", "a", "a,b", "a")]
    [InlineData("an addition on each device is kept", "a", "a,b", "a,c", "a,b,c")]
    [InlineData("a removal here and an addition there are both kept", "a,b", "a", "a,b,c", "a,c")]
    [InlineData("the same removal on both is a removal", "a,b", "a", "a", "a")]
    [InlineData("nothing changed anywhere", "a,b", "a,b", "a,b", "a,b")]
    public async Task Ticks_are_merged_action_by_action_against_what_the_two_last_agreed(string because, string agreed, string mine, string theirs, string merged)
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/offline");

        string result = await page.EvaluateAsync<string>(
            """
            ([agreed, mine, theirs]) => import('/js/sync.js').then(sync => {
              const plan = ticks => ({ profile: { People: '4' }, ticks: ticks === '' ? [] : ticks.split(',') });
              return sync.merge(plan(agreed), plan(mine), plan(theirs), 1, 2).plan.ticks.join(',');
            })
            """,
            new[] { agreed, mine, theirs });

        result.ShouldBe(merged, because);
    }

    [Fact]
    public async Task What_was_said_about_an_action_the_figures_and_the_household_details_each_merge_by_their_own_rule()
    {
        await using IBrowserContext context = await _browser.PhoneAsync();
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/offline");

        JsonElement merged = await page.EvaluateAsync<JsonElement>(
            """
            () => import('/js/sync.js').then(sync => {
              const base = { profile: { People: '4', Postcode: 'NP12 1GW' }, ticks: ['a'], planned: ['b'], skipped: [], amounts: { food: 3, water: 6 }, assumptions: {}, house: null, dependencies: [] };
              const mine = { ...base, profile: { People: '3', Postcode: 'NP12 1GW' }, ticks: ['a', 'c'], planned: [], skipped: ['b'], amounts: { food: 10, water: 6 }, house: { lat: 51.6, lng: -3.2 } };
              const theirs = { ...base, profile: { People: '5', Postcode: 'NP12 1GW' }, ticks: ['a', 'b'], planned: ['c'], amounts: { food: 7, water: 9 }, assumptions: { cashRate: 20 } };
              const newerThere = sync.merge(base, mine, theirs, 100, 200);
              const newerHere = sync.merge(base, mine, theirs, 200, 100);
              return {
                there: newerThere, here: newerHere,
                sameWhateverTheOrder: sync.same({ profile: { B: '1', A: '2' }, ticks: ['z', 'a'] }, { profile: { A: '2', B: '1' }, ticks: ['a', 'z'], planned: [], amounts: {} }),
                differentWhenTheyDiffer: sync.same({ profile: null, ticks: ['a'] }, { profile: null, ticks: [] }),
              };
            })
            """);

        JsonElement there = merged.GetProperty("there");
        JsonElement here = merged.GetProperty("here");
        // Where both changed what they said about an action, the stronger word wins: having beats planning beats ruling out.
        Ids(there, "ticks").ShouldBe("a,b,c");
        Ids(there, "planned").ShouldBeEmpty();
        Ids(there, "skipped").ShouldBeEmpty();
        // A figure only one side changed is that side's; one both changed goes to the newer.
        there.GetProperty("plan").GetProperty("amounts").GetProperty("water").GetInt32().ShouldBe(9);
        there.GetProperty("plan").GetProperty("amounts").GetProperty("food").GetInt32().ShouldBe(7);
        here.GetProperty("plan").GetProperty("amounts").GetProperty("food").GetInt32().ShouldBe(10);
        // The household details: both changed the profile, so the newer keeps it and the other is told; the pin and the assumption were each changed on one side only and both are kept.
        there.GetProperty("plan").GetProperty("profile").GetProperty("People").GetString().ShouldBe("5");
        there.GetProperty("tookTheirs").GetBoolean().ShouldBeTrue();
        here.GetProperty("plan").GetProperty("profile").GetProperty("People").GetString().ShouldBe("3");
        here.GetProperty("tookTheirs").GetBoolean().ShouldBeFalse();
        there.GetProperty("plan").GetProperty("house").GetProperty("lat").GetDouble().ShouldBe(51.6);
        there.GetProperty("plan").GetProperty("assumptions").GetProperty("cashRate").GetInt32().ShouldBe(20);
        here.GetProperty("plan").GetProperty("assumptions").GetProperty("cashRate").GetInt32().ShouldBe(20);
        merged.GetProperty("sameWhateverTheOrder").GetBoolean().ShouldBeTrue();
        merged.GetProperty("differentWhenTheyDiffer").GetBoolean().ShouldBeFalse();
    }

    private static string Ids(JsonElement merged, string list) =>
        string.Join(',', merged.GetProperty("plan").GetProperty(list).EnumerateArray().Select(id => id.GetString()));

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    /// <summary>Builds a plan through the wizard, as a household does: Rob's house, or a house with only its postcode and its people given.</summary>
    private static async Task BuildPlanAsync(IPage page, string? people = null, bool robsHouse = false)
    {
        await page.GotoAsync("/plan/start");
        if (people is null || robsHouse)
        {
            await Journeys.FillRobsHouseAsync(page);
        }
        else
        {
            await page.GetByLabel("Postcode", new() { Exact = true }).FillAsync("NP12 1GW");
        }

        if (people is not null)
        {
            await page.GetByLabel("How many people live in your home?").FillAsync(people);
        }

        await page.GetByRole(AriaRole.Button, new() { Name = "Build my plan" }).ClickAsync();
        await page.Locator("[data-plan][data-plan-ready]").WaitForAsync();
    }

    /// <summary>
    /// Does something that makes sync rebuild the plan page from new household details, and waits
    /// for the page that results: a new document, with the plan built and listening.
    /// </summary>
    private static async Task RebuiltAsync(IPage page, Func<Task> action)
    {
        await page.EvaluateAsync("() => { window.beforeTheRebuild = true; }");
        await action();
        await page.WaitForFunctionAsync("() => window.beforeTheRebuild !== true && document.querySelector('[data-plan][data-plan-ready]') !== null");
    }

    /// <summary>
    /// Ticks an action and waits until the browser's store holds it. The box is ticked at once and
    /// the store a moment later; a journey that left the page in between would leave the tick behind.
    /// </summary>
    internal static async Task TickAsync(IPage page, string action)
    {
        await page.Locator($"[data-action-id=\"{action}\"]").CheckAsync();
        await Until.TrueAsync(page, "id => import('/js/plan-store.js').then(store => store.getTicks()).then(ticks => ticks.includes(id))", action);
    }

    private static async Task UntickAsync(IPage page, string action)
    {
        await page.Locator($"[data-action-id=\"{action}\"]").UncheckAsync();
        await Until.TrueAsync(page, "id => import('/js/plan-store.js').then(store => store.getTicks()).then(ticks => !ticks.includes(id))", action);
    }

    /// <summary>Waits until the account's copy has exactly these ticks, in order.</summary>
    private static Task TicksInAccountAsync(IPage page, string ticks) =>
        Until.TrueAsync(
            page,
            "async want => { const answer = await (await fetch('/api/account/plan')).json(); return answer.body !== null && JSON.parse(answer.body).plan.ticks.join(',') === want; }",
            ticks);

    /// <summary>The people in the plan on a device and what is ticked, as "4|a,b".</summary>
    private static Task<string> PlanAsync(IPage page) =>
        page.EvaluateAsync<string>("() => import('/js/plan-store.js').then(store => store.snapshot()).then(plan => `${plan.profile?.People ?? ''}|${plan.ticks.join(',')}`)");

    /// <summary>The same, for the copy the account keeps.</summary>
    private static Task<string> AccountPlanAsync(IPage page) =>
        page.EvaluateAsync<string>("async () => { const plan = JSON.parse((await (await fetch('/api/account/plan')).json()).body).plan; return `${plan.profile?.People ?? ''}|${plan.ticks.join(',')}`; }");
}
