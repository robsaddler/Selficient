using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Accounts;
using Selficient.TestKit;
using Selficient.Web.Endpoints;
using Selficient.Web.Pages;
using Selficient.Web.Pages.Account;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Security;

/// <summary>
/// A household's account, through the real pipeline, the real database and the real pages, with
/// the code read from the message that was sent. The cases are the ones in
/// <c>docs/threat-models/household-accounts.md</c>.
/// </summary>
public sealed partial class HouseholdAccountTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private HttpClient Following() => _factory.CreateClient();

    private HttpClient NotFollowing() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task A_household_signs_in_with_the_code_it_was_sent_and_lands_on_its_plan()
    {
        using HttpClient client = Following();

        string signIn = await client.GetStringAsync(new Uri("/account/sign-in", UriKind.Relative));
        using HttpResponseMessage asked = await client.AskForCodeAsync("Rob@Example.com");
        string codePage = await asked.Content.ReadAsStringAsync();
        using HttpResponseMessage typed = await client.TypeCodeAsync(_factory.Mail.CodeFor("rob@example.com"));
        AccountSummary who = (await client.GetFromJsonAsync<AccountSummary>(new Uri("/api/account", UriKind.Relative)))!;

        signIn.ShouldContain("<h1 id=\"signin-heading\">Sign in or create an account</h1>");
        signIn.ShouldContain("Just an email address, no password. We send a six-digit code to it; type the code in and you are signed in. It works the same the first time and every time after.");
        signIn.ShouldContain("<li>It keeps every device you sign in on in step.</li>");
        signIn.ShouldContain("<li>It keeps your plan if this device is lost.</li>");
        signIn.ShouldContain("<button type=\"submit\" class=\"button button--primary\">Send me a code</button>");
        signIn.ShouldContain("<p class=\"field__hint\">You do not need an account to use Selficient. Your plan is saved on this device either way.</p>");
        signIn.ShouldContain("<meta name=\"robots\" content=\"noindex, nofollow\">");
        signIn.ShouldContain("type=\"email\"");
        signIn.ShouldContain("autocomplete=\"email\"");
        asked.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/account/code");
        codePage.ShouldContain("<h1 id=\"code-heading\">Check your email</h1>");
        codePage.ShouldContain("We have sent a six-digit code to <strong data-code-address>rob@example.com</strong>. It works for ten minutes, once, and only in this browser.");
        codePage.ShouldContain("autocomplete=\"one-time-code\"");
        codePage.ShouldContain("inputmode=\"numeric\"");
        codePage.ShouldContain("<button type=\"submit\" class=\"button button--quiet\">Send a new code</button>");
        codePage.ShouldContain("<a href=\"/account/sign-in\">Use a different address</a>");
        typed.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/plan", "signing in lands on the plan; there is no return address to be sent through");
        who.Email.ShouldBe("rob@example.com");
        _factory.Mail.CountTo("rob@example.com").ShouldBe(1);
        (await client.GetAsync(new Uri("/account/sign-in", UriKind.Relative))).RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/plan", "a household that is signed in is not asked to sign in");
    }

    [Fact]
    public async Task The_session_cookie_is_locked_down_and_signing_in_sets_a_fresh_session_and_clears_the_pending_one()
    {
        using HttpClient client = NotFollowing();

        using HttpResponseMessage asked = await client.AskForCodeAsync("rob@example.com");
        string[] afterAsking = [.. asked.Headers.GetValues("Set-Cookie")];
        using HttpResponseMessage typed = await client.TypeCodeAsync(_factory.Mail.CodeFor("rob@example.com"));
        string[] afterTyping = [.. typed.Headers.GetValues("Set-Cookie")];
        using HttpResponseMessage who = await client.GetAsync(new Uri("/api/account", UriKind.Relative));

        asked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        asked.Headers.Location!.ToString().ShouldBe("/account/code");
        asked.Headers.CacheControl!.NoStore.ShouldBeTrue();
        string pending = afterAsking.Single(c => c.StartsWith("selficient.signin=", StringComparison.Ordinal));
        pending.ShouldEndWith("; max-age=600; path=/account; secure; samesite=strict; httponly", Case.Insensitive, "ten minutes, the account pages only, never to script, never cross-site");
        pending.ShouldNotContain("rob", Case.Insensitive, "the address in it is encrypted");
        afterAsking.ShouldNotContain(c => c.StartsWith("selficient.household=", StringComparison.Ordinal), "asking for a code signs nobody in");

        typed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        typed.Headers.Location!.ToString().ShouldBe("/plan");
        string session = afterTyping.Single(c => c.StartsWith("selficient.household=", StringComparison.Ordinal));
        session.ShouldContain("; path=/; secure; samesite=lax; httponly", Case.Insensitive);
        session.ShouldContain("; expires=", Case.Insensitive, "it outlives the browser being closed, for ninety days");
        Expires().Match(session).Groups["when"].Value.ShouldSatisfyAllConditions(
            when => DateTimeOffset.Parse(when, System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddDays(89)),
            when => DateTimeOffset.Parse(when, System.Globalization.CultureInfo.InvariantCulture).ShouldBeLessThan(DateTimeOffset.UtcNow.AddDays(91)));
        string hint = afterTyping.Single(c => c.StartsWith("selficient.signed-in=", StringComparison.Ordinal));
        hint.ToLowerInvariant().ShouldBe("selficient.signed-in=1; max-age=7776000; path=/; secure; samesite=lax", "script may read it, and it says nothing but that somebody is signed in");
        afterTyping.Single(c => c.StartsWith("selficient.signin=", StringComparison.Ordinal)).ShouldStartWith("selficient.signin=; ", Case.Insensitive, "the pending sign-in is gone once it is proved");
        who.StatusCode.ShouldBe(HttpStatusCode.OK);
        who.Headers.CacheControl!.NoStore.ShouldBeTrue();
        who.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("selficient.signed-in=1; max-age=7776000", StringComparison.OrdinalIgnoreCase), "the hint is renewed whenever it is used, as the session slides");
        HouseholdAuth.SessionLength.ShouldBe(TimeSpan.FromDays(90));
    }

    [Fact]
    public async Task Asking_for_a_code_is_the_first_thing_that_sets_an_account_cookie()
    {
        using HttpClient client = NotFollowing();

        foreach (string path in new[] { "/", "/plan", "/plan/start", "/privacy", "/receive", "/account/sign-in", "/account/deleted" })
        {
            using HttpResponseMessage page = await client.GetAsync(new Uri(path, UriKind.Relative));
            string[] cookies = page.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? set) ? [.. set] : [];

            page.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            cookies.ShouldAllBe(c => c.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal), $"{path}: a guest is given nothing but the cookie that protects a form");
        }

        using HttpResponseMessage who = await client.GetAsync(new Uri("/api/account", UriKind.Relative));
        using HttpResponseMessage code = await client.GetAsync(new Uri("/account/code", UriKind.Relative));
        using HttpResponseMessage signOut = await client.GetAsync(new Uri("/account/sign-out", UriKind.Relative));
        using HttpResponseMessage delete = await client.GetAsync(new Uri("/account/delete", UriKind.Relative));

        who.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "an API is answered, not sent to a page");
        who.Headers.Contains("Location").ShouldBeFalse();
        code.Headers.Location!.ToString().ShouldBe("/account/sign-in", "a browser that never asked for a code has none to type");
        signOut.Headers.Location!.ToString().ShouldBe("/account/sign-in", "no return address is carried");
        delete.Headers.Location!.ToString().ShouldBe("/account/sign-in");
    }

    [Fact]
    public async Task A_known_address_and_a_new_one_are_told_the_same_thing()
    {
        using HttpClient first = Following();
        await _factory.SignInHouseholdAsync(first, "known@example.com");
        using HttpClient known = Following();
        using HttpClient stranger = Following();

        using HttpResponseMessage toKnown = await known.AskForCodeAsync("known@example.com");
        using HttpResponseMessage toStranger = await stranger.AskForCodeAsync("stranger@example.com");
        string knownPage = Scrub(await toKnown.Content.ReadAsStringAsync(), "known@example.com");
        string strangerPage = Scrub(await toStranger.Content.ReadAsStringAsync(), "stranger@example.com");

        toKnown.StatusCode.ShouldBe(toStranger.StatusCode);
        knownPage.ShouldBe(strangerPage, "nothing on the page says whether the address has an account");
        _factory.Mail.CountTo("stranger@example.com").ShouldBe(1, "a code goes to any address");
        (await CountAsync("SELECT COUNT(*) FROM dbo.household_accounts WHERE email = 'stranger@example.com'")).ShouldBe(0, "an account is made when the code is proved, never when one is asked for");
    }

    [Fact]
    public async Task A_code_is_refused_in_a_browser_that_did_not_ask_for_it()
    {
        using HttpClient asker = Following();
        using HttpClient thief = Following();
        using HttpResponseMessage asked = await asker.AskForCodeAsync("rob@example.com");
        string code = _factory.Mail.CodeFor("rob@example.com");
        using HttpResponseMessage thiefAsked = await thief.AskForCodeAsync("eve@example.org");

        using HttpResponseMessage stolen = await thief.TypeCodeAsync(code);
        string refusal = await stolen.Content.ReadAsStringAsync();
        using HttpClient bystander = NotFollowing();
        using HttpResponseMessage noPending = await bystander.PostAsync(new Uri("/account/code", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(bystander, "/account/sign-in"), ("Code", code)));
        using HttpResponseMessage resendWithNothing = await bystander.PostAsync(new Uri("/account/code?handler=Resend", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(bystander, "/account/sign-in")));
        using HttpResponseMessage rightful = await asker.TypeCodeAsync(code);

        stolen.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/account/code");
        refusal.ShouldContain("<p class=\"callout callout--warning\" role=\"alert\">That code is not right. Check the email and try again.</p>");
        refusal.ShouldContain("<strong data-code-address>eve@example.org</strong>");
        noPending.Headers.Location!.ToString().ShouldBe("/account/sign-in");
        resendWithNothing.Headers.Location!.ToString().ShouldBe("/account/sign-in");
        rightful.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/plan", "and the browser that asked can still use it");
        (await thief.GetAsync(new Uri("/api/account", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Five_wrong_codes_burn_the_code_and_a_new_one_can_be_asked_for()
    {
        using HttpClient client = Following();
        using HttpResponseMessage asked = await client.AskForCodeAsync("rob@example.com");
        string right = _factory.Mail.CodeFor("rob@example.com");
        string wrong = right == "000000" ? "000001" : "000000";
        List<string> pages = [];

        for (int i = 0; i < 5; i++)
        {
            using HttpResponseMessage tried = await client.TypeCodeAsync(wrong);
            pages.Add(await tried.Content.ReadAsStringAsync());
        }

        using HttpResponseMessage tooLate = await client.TypeCodeAsync(right);
        string late = await tooLate.Content.ReadAsStringAsync();
        using HttpResponseMessage resent = await client.PostAsync(new Uri("/account/code?handler=Resend", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(client, "/account/code")));
        string resentPage = await resent.Content.ReadAsStringAsync();
        using HttpResponseMessage withNew = await client.TypeCodeAsync(_factory.Mail.CodeFor("rob@example.com"));

        pages.Take(4).ShouldAllBe(p => p.Contains("That code is not right. Check the email and try again.", StringComparison.Ordinal));
        pages[4].ShouldContain("That code has been tried too many times. Ask for a new one.");
        pages[0].ShouldNotContain($"value=\"{wrong}\"", customMessage: "what was typed is not written back into the page");
        late.ShouldContain("That code has been tried too many times. Ask for a new one.", customMessage: "the right code is too late");
        resentPage.ShouldContain("<p class=\"callout\" role=\"status\">A new code is on its way. The one before it no longer works in this browser.</p>");
        resentPage.ShouldContain("<strong data-code-address>rob@example.com</strong>");
        _factory.Mail.CountTo("rob@example.com").ShouldBe(2);
        _factory.Mail.CodeFor("rob@example.com").ShouldNotBeNull();
        withNew.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/plan");
    }

    [Fact]
    public async Task What_is_refused_is_said_on_the_page_it_was_typed_into()
    {
        using HttpClient client = Following();

        using HttpResponseMessage notAnAddress = await client.AskForCodeAsync("rob at example dot com");
        string refusedAddress = await notAnAddress.Content.ReadAsStringAsync();
        for (int i = 0; i < 3; i++)
        {
            using HttpResponseMessage sent = await client.AskForCodeAsync("busy@example.com");
        }

        using HttpResponseMessage fourth = await client.AskForCodeAsync("busy@example.com");
        string tooMany = await fourth.Content.ReadAsStringAsync();
        using HttpResponseMessage resend = await client.PostAsync(new Uri("/account/code?handler=Resend", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(client, "/account/code")));
        string resendRefused = await resend.Content.ReadAsStringAsync();
        _factory.Mail.Failing = true;
        using HttpClient other = NotFollowing();
        using HttpResponseMessage down = await other.AskForCodeAsync("rob@example.com");
        string notSent = await down.Content.ReadAsStringAsync();

        notAnAddress.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/account/sign-in");
        refusedAddress.ShouldContain("<p class=\"callout callout--warning\" role=\"alert\">That does not look like an email address. Check it and try again.</p>");
        refusedAddress.ShouldContain("value=\"rob at example dot com\"", customMessage: "what was typed is still in the box to be corrected");
        tooMany.ShouldContain("Too many codes have been sent to that address. Wait fifteen minutes and try again.");
        _factory.Mail.CountTo("busy@example.com").ShouldBe(3);
        resendRefused.ShouldContain("Too many codes have been sent to that address. Wait fifteen minutes and try again.");
        resendRefused.ShouldContain("<strong data-code-address>busy@example.com</strong>");
        down.StatusCode.ShouldBe(HttpStatusCode.OK);
        notSent.ShouldContain("The code could not be sent just now. Try again in a moment.");
        down.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies).ShouldBeFalse(cookies is null ? string.Empty : string.Join(" | ", cookies));
        (await CountAsync("SELECT COUNT(*) FROM dbo.household_signin_codes")).ShouldBe(3, "a message the provider refused is not kept or counted");
    }

    [Fact]
    public async Task The_forms_refuse_a_post_without_a_token()
    {
        using HttpClient client = Following();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");

        foreach (string path in new[] { "/account/sign-in", "/account/code", "/account/code?handler=Resend", "/account/sign-out", "/account/delete" })
        {
            using HttpResponseMessage response = await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "eve@example.org", ["Code"] = "123456" }));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, path);
        }

        (await client.GetAsync(new Uri("/api/account", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK, "nothing was signed out or deleted");
        _factory.Mail.CountTo("eve@example.org").ShouldBe(0);
    }

    [Fact]
    public async Task The_sign_in_forms_are_rate_limited_on_a_policy_of_their_own()
    {
        using SelficientWebApplicationFactory limited = new SelficientWebApplicationFactory().WithSetting("RateLimits:SignInPerMinute", "2");
        using HttpClient client = limited.CreateClient();

        using HttpResponseMessage first = await client.AskForCodeAsync("a@example.com");
        using HttpResponseMessage second = await client.TypeCodeAsync("000000");
        string token = await OperatorSignInHelper.TokenAsync(client, "/account/sign-in");
        using HttpResponseMessage third = await client.PostAsync(new Uri("/account/sign-in", UriKind.Relative), OperatorSignInHelper.Form(token, ("Email", "b@example.com")));
        using HttpResponseMessage reading = await client.GetAsync(new Uri("/account/sign-in", UriKind.Relative));
        using HttpResponseMessage elsewhere = await client.GetAsync(new Uri("/", UriKind.Relative));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Mail.CountTo("b@example.com").ShouldBe(0);
        reading.StatusCode.ShouldBe(HttpStatusCode.OK, "reading the page is never limited, only sending and trying");
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.OK);
        new RateLimitOptions().SignInPerMinute.ShouldBe(10);
    }

    [Fact]
    public async Task Sign_out_and_delete_are_posts_with_a_token_and_a_visit_by_address_changes_nothing()
    {
        using HttpClient client = Following();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");

        string question = await client.GetStringAsync(new Uri("/account/sign-out", UriKind.Relative));
        string confirmation = await client.GetStringAsync(new Uri("/account/delete", UriKind.Relative));
        HttpStatusCode stillIn = (await client.GetAsync(new Uri("/api/account", UriKind.Relative))).StatusCode;
        using HttpResponseMessage signedOut = await client.PostAsync(new Uri("/account/sign-out", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(client, "/account/sign-out")));
        HttpStatusCode afterwards = (await client.GetAsync(new Uri("/api/account", UriKind.Relative))).StatusCode;

        question.ShouldContain("<p class=\"eyebrow\">Signed in as rob@example.com</p>");
        question.ShouldContain("<h1 id=\"signout-heading\">Sign out of this device</h1>");
        question.ShouldContain("Signing out stops this device following your account. Your account, and the plan kept in it, stay as they are.");
        question.ShouldContain("<div class=\"field field--check\" data-keep-copy hidden>", customMessage: "the one question is asked only where the script can act on the answer");
        question.ShouldContain("<label for=\"keep-copy\">Keep a copy of my plan on this device</label>");
        question.ShouldContain("<input type=\"checkbox\" id=\"keep-copy\" checked>", customMessage: "yes is the default");
        question.ShouldContain("Untick this on a shared or borrowed device and the plan is removed from this browser as you sign out.");
        confirmation.ShouldContain("<h1 id=\"delete-heading\">Delete your account</h1>");
        confirmation.ShouldContain("This removes your email address, the copy of your plan we keep, your passkeys and every sign-in, straight away. It cannot be undone.");
        confirmation.ShouldContain("The plan on this device stays, and Selficient goes on working here without an account.");
        confirmation.ShouldContain("<button type=\"submit\" class=\"button button--danger\">Delete my account</button>");
        confirmation.ShouldContain("<a class=\"button button--quiet\" href=\"/plan\">Keep my account</a>");
        stillIn.ShouldBe(HttpStatusCode.OK, "looking at either page changes nothing");
        signedOut.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/");
        afterwards.ShouldBe(HttpStatusCode.Unauthorized);
        (await CountAsync("SELECT COUNT(*) FROM dbo.household_accounts WHERE email = 'rob@example.com'")).ShouldBe(1, "signing out leaves the account as it was");
    }

    [Fact]
    public async Task Signing_out_takes_the_session_and_the_hint_and_nothing_else()
    {
        using HttpClient client = NotFollowing();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");

        using HttpResponseMessage signedOut = await client.PostAsync(new Uri("/account/sign-out", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(client, "/account/sign-out")));
        string[] cookies = [.. signedOut.Headers.GetValues("Set-Cookie")];

        signedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signedOut.Headers.Location!.ToString().ShouldBe("/");
        cookies.ShouldContain(c => c.StartsWith("selficient.household=; expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        cookies.ShouldContain(c => c.StartsWith("selficient.signed-in=; expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Deleting_the_account_ends_every_session_and_removes_every_row()
    {
        using HttpClient phone = Following();
        using HttpClient laptop = Following();
        await _factory.SignInHouseholdAsync(phone, "rob@example.com");
        await _factory.SignInHouseholdAsync(laptop, "rob@example.com");
        using HttpClient neighbour = Following();
        await _factory.SignInHouseholdAsync(neighbour, "neighbour@example.com");

        using HttpResponseMessage deleted = await phone.PostAsync(new Uri("/account/delete", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(phone, "/account/delete")));
        string gone = await deleted.Content.ReadAsStringAsync();
        using HttpResponseMessage onLaptop = await laptop.GetAsync(new Uri("/api/account", UriKind.Relative));
        using HttpResponseMessage onPhone = await phone.GetAsync(new Uri("/api/account", UriKind.Relative));
        using HttpResponseMessage theNeighbour = await neighbour.GetAsync(new Uri("/api/account", UriKind.Relative));

        deleted.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/account/deleted");
        gone.ShouldContain("<h1 id=\"deleted-heading\">Your account has been deleted</h1>");
        gone.ShouldContain("We no longer hold your email address or a copy of your plan. The plan on this device is still here, and you do not need an account to go on using it.");
        gone.ShouldContain("data-account-gone");
        onPhone.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        onLaptop.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the other device's session names an account that is gone");
        onLaptop.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("selficient.signed-in=; expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase), "and its hint goes with it");
        theNeighbour.StatusCode.ShouldBe(HttpStatusCode.OK, "nobody else is touched");
        (await CountAsync("SELECT COUNT(*) FROM dbo.household_accounts WHERE email = 'rob@example.com'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) FROM dbo.household_accounts")).ShouldBe(1);
    }

    [Fact]
    public async Task Signing_in_again_after_deleting_makes_a_new_account_and_the_old_session_does_not_open_it()
    {
        using HttpClient old = Following();
        await _factory.SignInHouseholdAsync(old, "rob@example.com");
        using HttpClient deleter = Following();
        await _factory.SignInHouseholdAsync(deleter, "rob@example.com");
        using HttpResponseMessage deleted = await deleter.PostAsync(new Uri("/account/delete", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(deleter, "/account/delete")));
        using HttpClient fresh = Following();
        await _factory.SignInHouseholdAsync(fresh, "rob@example.com");

        using HttpResponseMessage oldSession = await old.GetAsync(new Uri("/api/account", UriKind.Relative));
        using HttpResponseMessage newSession = await fresh.GetAsync(new Uri("/api/account", UriKind.Relative));

        oldSession.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "a session from before the delete names an account number that no longer exists");
        newSession.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_household_is_not_an_operator_and_an_operator_is_not_a_household()
    {
        using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory().WithSetting("Operator:Enabled", "true");
        await factory.CreateOperatorAsync();
        using HttpClient household = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await factory.SignInHouseholdAsync(household, "rob@example.com");
        using HttpClient staff = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await factory.SignInAsync(staff);

        using HttpResponseMessage householdAtReviewer = await household.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));
        using HttpResponseMessage staffAtReviewer = await staff.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));
        using HttpResponseMessage staffAsHousehold = await staff.GetAsync(new Uri("/api/account", UriKind.Relative));
        using HttpResponseMessage staffAtSignOut = await staff.GetAsync(new Uri("/account/sign-out", UriKind.Relative));
        using HttpResponseMessage householdAsHousehold = await household.GetAsync(new Uri("/api/account", UriKind.Relative));

        householdAtReviewer.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        householdAtReviewer.Headers.Location!.AbsolutePath.ShouldBe("/admin/sign-in", "a household's session opens nothing in the back office");
        staffAtReviewer.StatusCode.ShouldBe(HttpStatusCode.OK);
        staffAsHousehold.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "an operator's session is not a household's");
        staffAtSignOut.Headers.Location!.ToString().ShouldBe("/account/sign-in");
        householdAsHousehold.StatusCode.ShouldBe(HttpStatusCode.OK);
        HouseholdAuth.Scheme.ShouldNotBe(OperatorAuth.Scheme);
        HouseholdAuth.CookieName.ShouldNotBe(OperatorAuth.CookieName);
    }

    [Fact]
    public async Task A_pending_cookie_that_does_not_open_is_no_pending_sign_in()
    {
        using HttpClient client = NotFollowing();
        foreach (string cookie in new[] { "not base64url!", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", string.Empty })
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/account/code", UriKind.Relative));
            request.Headers.Add("Cookie", $"selficient.signin={Uri.EscapeDataString(cookie)}");

            using HttpResponseMessage response = await client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.Redirect, cookie);
            response.Headers.Location!.ToString().ShouldBe("/account/sign-in");
        }
    }

    [Fact]
    public async Task A_server_with_no_mailer_offers_no_account()
    {
        using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory().WithoutMail();
        using HttpClient client = factory.CreateClient();

        string signIn = await client.GetStringAsync(new Uri("/account/sign-in", UriKind.Relative));
        using HttpResponseMessage posted = await client.PostAsync(new Uri("/account/sign-in", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(client, "/plan/start"), ("Email", "rob@example.com")));
        string landing = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        using HttpResponseMessage plan = await Pages.PlanPagesTests.PostRobsHouse(client);
        string planPage = await plan.Content.ReadAsStringAsync();

        signIn.ShouldContain("<p class=\"callout callout--warning\" role=\"status\">Accounts are not available on this server yet. Your plan is saved on this device, as it always is.</p>");
        signIn.ShouldNotContain("<form");
        posted.StatusCode.ShouldBe(HttpStatusCode.OK);
        posted.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/account/unavailable", "a post by hand is sent to the same sentence and sends nothing");
        (await posted.Content.ReadAsStringAsync()).ShouldContain("Accounts are not available on this server yet.");
        landing.ShouldNotContain("data-keep-account", customMessage: "the panel offers an account only where one can be made");
        landing.ShouldNotContain("/account/sign-in");
        landing.ShouldNotContain("An account would keep both in step.");
        landing.ShouldNotContain("data-sync-", customMessage: "with no accounts there is nothing to keep in step and nothing said about it");
        landing.ShouldContain("<p class=\"keep__warning\" data-keep-safari hidden>Safari clears a site&#x27;s data after a week without a visit. Add Selficient to your home screen to keep your plan.</p>");
        planPage.ShouldContain("data-kept-notice");
        planPage.ShouldNotContain("Create an account");
        factory.Services.GetRequiredService<IConfiguration>()["Email:Provider"].ShouldBe("None");
        factory.Services.GetRequiredService<AccountSignIn>().IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Where_an_account_can_be_made_the_panel_and_the_first_plan_offer_one_and_never_require_one()
    {
        using HttpClient client = Following();

        string landing = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        using HttpResponseMessage plan = await Pages.PlanPagesTests.PostRobsHouse(client);
        string planPage = await plan.Content.ReadAsStringAsync();

        landing.ShouldContain("<section class=\"keep__row\" aria-labelledby=\"keep-account\" data-keep-account data-keep-row=\"account\">");
        landing.ShouldContain("<section class=\"keep__row\" aria-labelledby=\"keep-device\" data-keep-row=\"device\">");
        System.Text.RegularExpressions.Regex.Count(landing, "<span class=\"keep__here\" data-keep-here hidden>Kept here</span>").ShouldBe(2, "the row the plan is kept in is marked by the script; neither is until it knows");
        landing.ShouldContain("<span data-keep-guest>An account</span><span data-keep-member hidden>Your account</span>");
        landing.ShouldContain("<p>Keep every device in step and keep your plan if this one is lost. Just an email address, no password.</p>");
        landing.ShouldContain("<a class=\"button\" href=\"/account/sign-in\">Create an account</a>");
        landing.ShouldContain("<p>Signed in as <strong class=\"keep__who\" data-keep-email></strong>.</p>");
        landing.ShouldContain("<a class=\"button button--quiet\" href=\"/account/sign-out\">Sign out</a> <a class=\"button button--quiet\" href=\"/account/delete\">Delete my account</a>");
        landing.ShouldContain("<p class=\"keep__already\" data-keep-guest>Already have an account? <a href=\"/account/sign-in\">Sign in</a>.</p>");
        landing.ShouldContain("<p class=\"keep__warning\" data-keep-safari hidden>Safari clears a site&#x27;s data after a week without a visit. Add Selficient to your home screen, or make an account, to keep your plan.</p>");
        landing.ShouldContain("Changes you make on one will not appear on the other.<span data-keep-guest> An account would keep both in step.</span></p>");
        landing.ShouldContain("<span data-keep-label=\"account\" hidden>Signed in as <span class=\"keep-chip__who\" data-keep-email></span></span>");
        landing.ShouldContain("<p data-keep-says=\"account\" hidden>A copy of your plan lives in this browser, so it opens without a connection.</p>");
        landing.ShouldContain("<p data-keep-says=\"account-none\" hidden>Nothing is saved on this device yet. When you make a plan it is kept here and in your account.</p>");
        landing.ShouldContain("If you want your plan on every device, you can make an account later. You never have to.</p>");
        planPage.ShouldContain("<a class=\"button button--quiet\" href=\"/account/sign-in\" data-kept-dismiss data-keep-guest>Create an account</a>");

        // Sync is quiet: one line in the panel, a strip that is hidden until it has something to say, and the choice between two plans.
        landing.ShouldContain("<p data-sync-status=\"synced\">Your plan is kept in your account, and every device you sign in on stays in step.</p>");
        landing.ShouldContain("<p data-sync-status=\"waiting\" hidden>Changes made on this device are waiting for a connection. They will go to your account when there is one.</p>");
        landing.ShouldContain("<p data-sync-status=\"choosing\" hidden>This device and your account each have a plan. <button type=\"button\" class=\"button button--quiet\" data-sync-choose>Choose which to keep</button></p>");
        landing.ShouldContain("<div class=\"sync-notice\" data-sync-notice role=\"status\" hidden>");
        foreach ((string key, string sentence) in SyncCopy.Says)
        {
            landing.ShouldContain($"<p data-sync-says=\"{key}\" hidden>{sentence}</p>");
        }

        SyncCopy.Says.Select(s => s.Key).ShouldBe(["kept", "arrived", "replaced", "kept-here", "details"]);
        SyncCopy.Says.Single(s => s.Key == "details").Sentence.ShouldBe("Your other device changed your household details. This device now shows those.");
        SyncCopy.Says.Single(s => s.Key == "kept").Sentence.ShouldBe("Your plan is now kept in your account.");
        landing.ShouldContain("<dialog class=\"dialog keep\" data-sync-choice aria-labelledby=\"sync-choice-heading\">");
        landing.ShouldContain("<h2 id=\"sync-choice-heading\">This device and your account each have a plan</h2>");
        landing.ShouldContain("<p>Choose one. The two are never mixed, and the one you choose replaces the other on every device you sign in on.</p>");
        landing.ShouldContain("<button type=\"button\" class=\"button button--primary\" data-sync-take=\"account\">Use the plan in my account</button>");
        landing.ShouldContain("<button type=\"button\" class=\"button\" data-sync-take=\"device\">Keep the plan on this device</button>");
        KeepCopy.Safari(accounts: true).ShouldBe(KeepCopy.SafariWarningWithAccount);
        KeepCopy.Safari(accounts: false).ShouldBe(KeepCopy.SafariWarning);
        AccountCopy.WhatItDoes.Count.ShouldBe(2, "an account does two things, and the page says only those two");
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>A page with everything that differs from request to request, and the address itself, taken out.</summary>
    private static string Scrub(string html, string email) =>
        Varying().Replace(html, string.Empty).Replace(email, "ADDRESS", StringComparison.Ordinal);

    private async Task<int> CountAsync(string sql)
    {
        string connectionString = _factory.Services.GetRequiredService<IConfiguration>()["Database:ConnectionString"]!;
        await using SqlConnection connection = new(connectionString);
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    [GeneratedRegex("nonce=\"[^\"]+\"|name=\"__RequestVerificationToken\" type=\"hidden\" value=\"[^\"]+\"|data-sw-build=\"[^\"]+\"|\\?v=[A-Za-z0-9_-]+")]
    private static partial Regex Varying();

    [GeneratedRegex("expires=(?<when>[^;]+);", RegexOptions.IgnoreCase)]
    private static partial Regex Expires();
}
