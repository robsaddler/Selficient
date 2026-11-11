using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Security;
using Selficient.TestKit;

namespace Selficient.Web.Tests.Security;

/// <summary>
/// The reviewer's door, driven as a person drives it. The cases are the web half of
/// <c>docs/threat-models/operator-auth.md</c>: a password alone opens nothing, a stale cookie is
/// refused on the next request rather than at the next expiry, and the account that owes a
/// password can reach the one page that lets it pay.
/// </summary>
public sealed class OperatorAuthTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    // The sign-in cookie is Secure, as it must be, so a client on http would never keep it and
    // every one of these tests would fail for the wrong reason.
    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task An_unsigned_caller_is_sent_to_the_sign_in_and_sees_no_catalogue()
    {
        using HttpClient client = Client();

        using HttpResponseMessage review = await client.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));
        using HttpResponseMessage code = await client.GetAsync(new Uri("/admin/sign-in/code", UriKind.Relative));
        using HttpResponseMessage password = await client.GetAsync(new Uri("/admin/password", UriKind.Relative));

        review.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        review.Headers.Location!.ToString().ShouldContain("/admin/sign-in");
        code.StatusCode.ShouldBe(HttpStatusCode.Redirect, "the second step is not a page you can start at");
        password.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task A_password_alone_is_not_a_sign_in()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();
        string token = await OperatorSignInHelper.TokenAsync(client, "/admin/sign-in");

        using HttpResponseMessage step = await client.PostAsync(
            new Uri("/admin/sign-in", UriKind.Relative),
            OperatorSignInHelper.Form(token, ("Username", OperatorSignInHelper.Username), ("Password", OperatorSignInHelper.Password)));
        using HttpResponseMessage review = await client.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));

        step.Headers.Location!.ToString().ShouldContain("/admin/sign-in/code");
        review.StatusCode.ShouldBe(HttpStatusCode.Redirect, "half signed in is not signed in");
        review.Headers.Location!.ToString().ShouldContain("/admin/sign-in");
    }

    [Fact]
    public async Task Both_steps_open_the_reviewer_and_it_greets_the_operator_by_name()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();

        string review = await _factory.SignInAsync(client);

        review.ShouldContain("review__offers");
        review.ShouldContain("Signed in as <strong>rob</strong>");
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_name_say_the_same_sentence()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();

        string wrong = await _factory.SignInAsync(client, password: "not the password at all");
        string unknown = await _factory.SignInAsync(client, username: "nobody");

        wrong.ShouldContain(OperatorSignIn.RefusedSentence);
        unknown.ShouldContain(OperatorSignIn.RefusedSentence);
        wrong.ShouldNotContain("review__offers");
    }

    [Fact]
    public async Task A_wrong_code_keeps_the_operator_on_the_code_step()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();

        string page = await _factory.SignInAsync(client, code: "123456");

        page.ShouldContain(OperatorSignIn.CodeRefusedSentence);
        page.ShouldNotContain("review__offers");
    }

    [Fact]
    public async Task A_recovery_code_opens_the_reviewer_once()
    {
        IReadOnlyList<string> codes = await _factory.CreateOperatorAsync();
        using HttpClient client = Client();

        string first = await _factory.SignInAsync(client, code: codes[0]);
        using HttpClient second = Client();
        string again = await _factory.SignInAsync(second, code: codes[0]);

        first.ShouldContain("review__offers");
        again.ShouldContain(OperatorSignIn.CodeRefusedSentence, customMessage: "a recovery code is spent when it is used");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_say_so()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();

        for (int attempt = 0; attempt < LockoutPolicy.Threshold; attempt++)
        {
            await _factory.SignInAsync(client, password: "wrong");
        }

        string locked = await _factory.SignInAsync(client);

        locked.ShouldContain(OperatorSignIn.LockedSentence);
    }

    [Fact]
    public async Task Changing_the_password_signs_every_other_session_out()
    {
        IReadOnlyList<string> codes = await _factory.CreateOperatorAsync();
        using HttpClient here = Client();
        using HttpClient elsewhere = Client();
        await _factory.SignInAsync(here);
        // The second device cannot use the same authenticator code inside the same thirty seconds,
        // which is the point of spending a code, so it signs in on a recovery code instead.
        (await _factory.SignInAsync(elsewhere, code: codes[0])).ShouldContain("review__offers");

        string token = await OperatorSignInHelper.TokenAsync(here, "/admin/password");
        using HttpResponseMessage changed = await here.PostAsync(
            new Uri("/admin/password", UriKind.Relative),
            OperatorSignInHelper.Form(token, ("Current", OperatorSignInHelper.Password), ("Fresh", "a different sentence entirely"), ("Again", "a different sentence entirely")));
        using HttpResponseMessage stillHere = await here.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));
        using HttpResponseMessage theOther = await elsewhere.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));

        changed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        stillHere.StatusCode.ShouldBe(HttpStatusCode.OK, "the device doing the changing stays signed in");
        theOther.StatusCode.ShouldBe(HttpStatusCode.Redirect, "every other device is out at its next request, not at its next expiry");
    }

    [Theory]
    [InlineData("wrong now", "a different sentence entirely", "a different sentence entirely", "That is not your password now.")]
    [InlineData(OperatorSignInHelper.Password, "a different sentence entirely", "something else again", "The two new passwords are not the same.")]
    [InlineData(OperatorSignInHelper.Password, OperatorSignInHelper.Password, OperatorSignInHelper.Password, "The new password has to be different from the old one.")]
    // Not the word "short": the page's own help sentence contains it, and the assertion below is
    // that the typed password is never written back into the page.
    [InlineData(OperatorSignInHelper.Password, "qrtvbnm", "qrtvbnm", "A password needs at least")]
    public async Task A_password_change_that_does_not_hold_up_is_refused_with_a_sentence(string current, string fresh, string again, string expected)
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();
        await _factory.SignInAsync(client);
        string token = await OperatorSignInHelper.TokenAsync(client, "/admin/password");

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/admin/password", UriKind.Relative),
            OperatorSignInHelper.Form(token, ("Current", current), ("Fresh", fresh), ("Again", again)));
        string page = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.ShouldContain(expected);
        page.ShouldNotContain(fresh, customMessage: "a password is never written back into the page");
    }

    [Fact]
    public async Task An_account_that_owes_a_password_reaches_the_password_page_and_nothing_else()
    {
        await _factory.CreateOperatorAsync(username: "owes", mustChangePassword: true);
        using HttpClient client = Client();

        string landed = await _factory.SignInAsync(client, username: "owes");
        using HttpResponseMessage review = await client.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));

        landed.ShouldContain("has to be replaced before you can review anything");
        review.StatusCode.ShouldBe(HttpStatusCode.Redirect, "the reviewer waits until the starting password is gone");
    }

    [Fact]
    public async Task A_brand_new_account_can_walk_the_whole_way_in_without_being_sent_round_in_a_circle()
    {
        // The bootstrap account: no authenticator and a password it owes. It has to be able to
        // enrol, then pay the password, and reach the reviewer, without any step turning it away.
        await _factory.CreateOperatorAsync(username: "fresh", mustChangePassword: true, enrolled: false);
        using HttpClient client = Client();
        string token = await OperatorSignInHelper.TokenAsync(client, "/admin/sign-in");
        using HttpResponseMessage step = await client.PostAsync(
            new Uri("/admin/sign-in", UriKind.Relative),
            OperatorSignInHelper.Form(token, ("Username", "fresh"), ("Password", OperatorSignInHelper.Password)));
        step.Headers.Location!.ToString().ShouldContain("/admin/enrol");

        string enrol = await client.GetStringAsync(new Uri("/admin/enrol", UriKind.Relative));
        string secret = System.Text.RegularExpressions.Regex.Match(enrol, "name=\"Secret\" value=\"(?<secret>[^\"]+)\"").Groups["secret"].Value;
        using HttpResponseMessage armed = await client.PostAsync(
            new Uri("/admin/enrol", UriKind.Relative),
            OperatorSignInHelper.Form(
                await OperatorSignInHelper.TokenAsync(client, "/admin/enrol"),
                ("Secret", secret),
                ("Code", Totp.CodeAt(Selficient.Web.Pages.Admin.EnrolModel.Base32Bytes(secret), _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow()))));

        (await armed.Content.ReadAsStringAsync()).ShouldContain("href=\"/admin/password\"", customMessage: "the codes page points at the password it still owes, not at the reviewer");
        using HttpResponseMessage bounced = await client.GetAsync(new Uri("/admin/sign-in", UriKind.Relative));
        bounced.Headers.Location!.ToString().ShouldContain("/admin/password", customMessage: "the sign-in sends it to the password rather than back to a reviewer that will refuse it");

        string change = await OperatorSignInHelper.TokenAsync(client, "/admin/password");
        using HttpResponseMessage changed = await client.PostAsync(
            new Uri("/admin/password", UriKind.Relative),
            OperatorSignInHelper.Form(change, ("Current", OperatorSignInHelper.Password), ("Fresh", "a password of its own choosing"), ("Again", "a password of its own choosing")));
        using HttpResponseMessage review = await client.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));

        changed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        review.StatusCode.ShouldBe(HttpStatusCode.OK, "and then the reviewer is open");
    }

    [Fact]
    public async Task Enrolment_arms_the_second_factor_and_shows_the_recovery_codes_once()
    {
        await _factory.CreateOperatorAsync(username: "newcomer", enrolled: false);
        using HttpClient client = Client();
        string token = await OperatorSignInHelper.TokenAsync(client, "/admin/sign-in");
        using HttpResponseMessage step = await client.PostAsync(
            new Uri("/admin/sign-in", UriKind.Relative),
            OperatorSignInHelper.Form(token, ("Username", "newcomer"), ("Password", OperatorSignInHelper.Password)));

        step.Headers.Location!.ToString().ShouldContain("/admin/enrol");
        string enrol = await client.GetStringAsync(new Uri("/admin/enrol", UriKind.Relative));
        string secret = System.Text.RegularExpressions.Regex.Match(enrol, "name=\"Secret\" value=\"(?<secret>[^\"]+)\"").Groups["secret"].Value;
        secret.ShouldNotBeNullOrWhiteSpace();
        // The code is drawn into the page, never fetched: the address under it carries the seed.
        enrol.ShouldContain("enrol__qr");
        enrol.ShouldContain("A QR code holding this account", customMessage: "the code carries a label a screen reader can say");
        enrol.ShouldContain("<path d=\"M", customMessage: "the modules are drawn as a path in the page, not fetched as an image");
        string enrolToken = await OperatorSignInHelper.TokenAsync(client, "/admin/enrol");

        using HttpResponseMessage wrong = await client.PostAsync(
            new Uri("/admin/enrol", UriKind.Relative),
            OperatorSignInHelper.Form(enrolToken, ("Secret", secret), ("Code", "123456")));
        (await wrong.Content.ReadAsStringAsync()).ShouldContain("That code is not right.");

        string right = Totp.CodeAt(Selficient.Web.Pages.Admin.EnrolModel.Base32Bytes(secret), _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow());
        using HttpResponseMessage armed = await client.PostAsync(
            new Uri("/admin/enrol", UriKind.Relative),
            OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(client, "/admin/enrol"), ("Secret", secret), ("Code", right)));
        string shown = await armed.Content.ReadAsStringAsync();

        shown.ShouldContain("Write these recovery codes down");
        System.Text.RegularExpressions.Regex.Count(shown, "codes__code").ShouldBe(RecoveryCodes.Count);
        using HttpResponseMessage review = await client.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));
        review.StatusCode.ShouldBe(HttpStatusCode.OK, "enrolment finishes the sign-in");
    }

    [Fact]
    public async Task A_sign_out_is_a_post_and_a_visit_by_address_signs_nobody_out()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();
        await _factory.SignInAsync(client);

        using HttpResponseMessage visited = await client.GetAsync(new Uri("/admin/sign-out", UriKind.Relative));
        using HttpResponseMessage stillIn = await client.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));
        using HttpResponseMessage signedOut = await client.PostAsync(
            new Uri("/admin/sign-out", UriKind.Relative),
            OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(client, "/admin/catalogue")));
        using HttpResponseMessage after = await client.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));

        visited.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        stillIn.StatusCode.ShouldBe(HttpStatusCode.OK, "a link cannot end a session");
        signedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        after.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task A_sign_in_posted_without_its_antiforgery_token_is_refused()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/admin/sign-in", UriKind.Relative),
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("Username", OperatorSignInHelper.Username),
                new KeyValuePair<string, string>("Password", OperatorSignInHelper.Password),
            ]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_disabled_account_cannot_sign_in_and_its_sessions_end()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();
        await _factory.SignInAsync(client);
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IOperatorStore store = scope.ServiceProvider.GetRequiredService<IOperatorStore>();
            OperatorAccount account = (await store.FindAsync(OperatorSignInHelper.Username, CancellationToken.None))!;
            await store.SetEnabledAsync(account.Id, false, Guid.NewGuid(), CancellationToken.None);
        }

        using HttpResponseMessage after = await client.GetAsync(new Uri("/admin/catalogue", UriKind.Relative));
        using HttpClient fresh = Client();
        string refused = await _factory.SignInAsync(fresh);

        after.StatusCode.ShouldBe(HttpStatusCode.Redirect, "turning an account off ends its sessions at the next request");
        refused.ShouldContain(OperatorSignIn.RefusedSentence);
    }

    [Fact]
    public async Task Every_attempt_is_recorded_and_no_password_is_ever_written()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();
        await _factory.SignInAsync(client, password: "wrong");
        await _factory.SignInAsync(client);

        using IServiceScope scope = _factory.Services.CreateScope();
        IReadOnlyList<SignInAttempt> attempts = await scope.ServiceProvider.GetRequiredService<IOperatorStore>().AttemptsAsync(10, CancellationToken.None);

        attempts.ShouldContain(a => a.Outcome == "refused");
        attempts.ShouldContain(a => a.Outcome == "code");
        attempts.ShouldAllBe(a => !a.Detail.Contains(OperatorSignInHelper.Password, StringComparison.Ordinal));
        attempts.ShouldAllBe(a => !a.Username.Contains(OperatorSignInHelper.Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hand_names_the_operator_who_took_it()
    {
        await _factory.CreateOperatorAsync();
        using HttpClient client = Client();
        string review = await _factory.SignInAsync(client);
        string token = System.Text.RegularExpressions.Regex.Match(review, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"").Groups["token"].Value;

        // A hand that touches no network: hiding an offer that is not there is still a hand, and is
        // still recorded. Pressing "Open every link now" here once reached out to real shops.
        using HttpResponseMessage hidden = await client.PostAsync(
            new Uri("/admin/catalogue?handler=Hide&id=987654321", UriKind.Relative),
            OperatorSignInHelper.Form(token));

        hidden.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using IServiceScope scope = _factory.Services.CreateScope();
        IReadOnlyList<Selficient.Application.Operations.OperatorAction> hands =
            await scope.ServiceProvider.GetRequiredService<Selficient.Application.Operations.IOperationsLog>().RecentHandsAsync(5, CancellationToken.None);
        hands.ShouldContain(hand => hand.Hand == "hidden" && hand.OperatorName == "rob");
    }

    [Fact]
    public async Task With_the_reviewer_switched_off_its_pages_are_not_there()
    {
        using SelficientWebApplicationFactory off = new SelficientWebApplicationFactory().WithSetting("Operator:Enabled", "false");
        using HttpClient client = off.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using HttpResponseMessage signIn = await client.GetAsync(new Uri("/admin/sign-in", UriKind.Relative));
        using HttpResponseMessage posted = await client.PostAsync(new Uri("/admin/sign-in", UriKind.Relative), new StringContent(string.Empty));

        signIn.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        posted.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_bootstrap_makes_one_account_that_owes_a_password_and_never_a_second()
    {
        using SelficientWebApplicationFactory first = new SelficientWebApplicationFactory()
            .WithSetting("Operator:BootstrapUsername", "first")
            .WithSetting("Operator:BootstrapPassword", "the first password of all");
        using HttpClient client = first.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        using IServiceScope scope = first.Services.CreateScope();
        IOperatorStore store = scope.ServiceProvider.GetRequiredService<IOperatorStore>();

        IReadOnlyList<OperatorAccount> made = await store.AllAsync(CancellationToken.None);

        made.Count.ShouldBe(1);
        made[0].Username.ShouldBe("first");
        made[0].MustChangePassword.ShouldBeTrue("a bootstrap credential that stays valid is a back door");
        made[0].HasSecondFactor.ShouldBeFalse();

        // Starting again against the same database must not add a second account: the bootstrap
        // settings can stay on the server without being a standing second way in.
        Selficient.Web.Security.OperatorBootstrap again = new(
            store,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Selficient.Web.Security.OperatorOptions>>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Selficient.Web.Security.OperatorBootstrap>.Instance);
        await again.StartAsync(CancellationToken.None);
        await again.StopAsync(CancellationToken.None);
        (await store.AllAsync(CancellationToken.None)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_bootstrap_settings_no_account_is_made()
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<IOperatorStore>().CountAsync(CancellationToken.None)).ShouldBe(0);
        await Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();
}
