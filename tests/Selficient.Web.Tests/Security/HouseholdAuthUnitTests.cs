using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Selficient.Application.Accounts;
using Selficient.TestKit;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Security;

/// <summary>
/// The pieces of a household's sign-in that a browser cannot reach one at a time: the claims a
/// principal carries, the cookie check that ends a session whose account has gone, the note of
/// when an account was last seen, and the pending cookie when it is handed something it did not
/// write.
/// </summary>
public sealed class HouseholdAuthUnitTests
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);

    private static HouseholdAccount Account(Guid stamp = default, DateTimeOffset? lastSeen = null) =>
        new(41, "rob@example.com", stamp == default ? Guid.NewGuid() : stamp, Evening, lastSeen ?? Evening);

    [Fact]
    public void A_principal_carries_the_number_the_address_and_the_stamp_and_nothing_else()
    {
        HouseholdAccount account = Account();

        ClaimsPrincipal principal = HouseholdAuth.Principal(account);

        HouseholdAuth.IdOf(principal).ShouldBe(41);
        HouseholdAuth.EmailOf(principal).ShouldBe("rob@example.com");
        principal.FindFirstValue(HouseholdAuth.StampClaim).ShouldBe(account.SecurityStamp.ToString());
        principal.Claims.Count().ShouldBe(3);
        principal.Identity!.AuthenticationType.ShouldBe(HouseholdAuth.Scheme);
        Should.Throw<ArgumentNullException>(() => HouseholdAuth.Principal(null!));
    }

    [Fact]
    public void Nobody_and_an_operator_are_not_a_household()
    {
        ClaimsPrincipal operatorOnly = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Email, "staff@selficient.app")], OperatorAuth.Scheme));
        ClaimsPrincipal bare = new(new ClaimsIdentity([], HouseholdAuth.Scheme));
        ClaimsPrincipal notANumber = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "rob")], HouseholdAuth.Scheme));

        HouseholdAuth.IdOf(null).ShouldBeNull();
        HouseholdAuth.EmailOf(null).ShouldBeEmpty();
        HouseholdAuth.IdOf(operatorOnly).ShouldBeNull("an operator's number is not a household's, even on the same request");
        HouseholdAuth.EmailOf(operatorOnly).ShouldBeEmpty();
        HouseholdAuth.IdOf(bare).ShouldBeNull();
        HouseholdAuth.EmailOf(bare).ShouldBeEmpty();
        HouseholdAuth.IdOf(notANumber).ShouldBeNull();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task A_session_is_kept_only_while_the_account_is_there_and_its_stamp_has_not_moved(bool gone, bool stampMoved, bool kept)
    {
        Guid stamp = Guid.NewGuid();
        IAccountStore store = Substitute.For<IAccountStore>();
        store.FindAsync(41L, Arg.Any<CancellationToken>()).Returns(gone ? null : Account(stampMoved ? Guid.NewGuid() : stamp));
        HouseholdCookieEvents events = new(store, new ManualClock(Evening));
        CookieValidatePrincipalContext context = Context(HouseholdAuth.Principal(Account(stamp)));

        await events.ValidatePrincipal(context);

        (context.Principal is not null).ShouldBe(kept);
        context.HttpContext.Response.Headers.SetCookie.ToString().Contains("selficient.signed-in=;", StringComparison.Ordinal).ShouldBe(!kept, "a session that ends takes its hint with it");
        await store.DidNotReceive().TouchAsync(Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        Should.Throw<ArgumentNullException>(() => new HouseholdCookieEvents(null!, TimeProvider.System));
        Should.Throw<ArgumentNullException>(() => new HouseholdCookieEvents(store, null!));
        await Should.ThrowAsync<ArgumentNullException>(() => events.ValidatePrincipal(null!));
    }

    [Fact]
    public async Task A_session_with_no_number_on_it_is_ended_without_a_lookup()
    {
        IAccountStore store = Substitute.For<IAccountStore>();
        HouseholdCookieEvents events = new(store, TimeProvider.System);
        CookieValidatePrincipalContext context = Context(new ClaimsPrincipal(new ClaimsIdentity([], HouseholdAuth.Scheme)));

        await events.ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
        await store.DidNotReceive().FindAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_account_is_noted_as_seen_once_a_day_and_no_more()
    {
        Guid stamp = Guid.NewGuid();
        IAccountStore store = Substitute.For<IAccountStore>();
        ManualClock clock = new(Evening);
        HouseholdCookieEvents events = new(store, clock);
        store.FindAsync(41L, Arg.Any<CancellationToken>()).Returns(Account(stamp, lastSeen: Evening));

        clock.Advance(TimeSpan.FromHours(23));
        await events.ValidatePrincipal(Context(HouseholdAuth.Principal(Account(stamp))));
        await store.DidNotReceive().TouchAsync(Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        clock.Advance(TimeSpan.FromHours(1));
        await events.ValidatePrincipal(Context(HouseholdAuth.Principal(Account(stamp))));

        await store.Received(1).TouchAsync(41L, Evening.AddDays(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_api_call_that_is_not_signed_in_is_answered_and_a_page_is_sent_to_sign_in_with_no_return_address()
    {
        HouseholdCookieEvents events = new(Substitute.For<IAccountStore>(), TimeProvider.System);
        DefaultHttpContext api = new();
        api.Request.Path = "/api/account/plan";
        DefaultHttpContext page = new();
        page.Request.Path = "/account/delete";

        await events.RedirectToLogin(Redirect(api));
        await events.RedirectToLogin(Redirect(page));

        api.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        api.Response.Headers.Location.Count.ShouldBe(0);
        page.Response.StatusCode.ShouldBe(StatusCodes.Status302Found);
        page.Response.Headers.Location.ToString().ShouldBe("/account/sign-in", "the framework's own redirect would carry ?ReturnUrl=, and there is nothing to return through");
        await Should.ThrowAsync<ArgumentNullException>(() => events.RedirectToLogin(null!));
    }

    [Fact]
    public void The_pending_cookie_round_trips_and_reads_nothing_it_did_not_write()
    {
        IDataProtectionProvider ring = DataProtectionProvider.Create("Selficient.Tests");
        PendingSignInCookie cookie = new(ring);
        PendingSignIn pending = new("rob@example.com", [.. Enumerable.Range(0, 32).Select(i => (byte)i)]);
        DefaultHttpContext writing = new();

        cookie.Write(writing.Response, pending);
        string set = writing.Response.Headers.SetCookie.ToString();
        string value = set["selficient.signin=".Length..set.IndexOf(';', StringComparison.Ordinal)];
        PendingSignIn? read = cookie.Read(Request(value));

        read!.Email.ShouldBe("rob@example.com");
        read.Nonce.ShouldBe(pending.Nonce);
        set.ShouldNotContain("rob@example.com");
        set.ShouldEndWith("; max-age=600; path=/account; secure; samesite=strict; httponly");
        cookie.Read(new DefaultHttpContext().Request).ShouldBeNull("no cookie, no pending sign-in");
        cookie.Read(Request("not base64url!")).ShouldBeNull();
        cookie.Read(Request(value[..^4] + "AAAA")).ShouldBeNull("a value that has been altered does not open");
        new PendingSignInCookie(DataProtectionProvider.Create("Another.Ring")).Read(Request(value)).ShouldBeNull("nor does one written under another key ring");
        DefaultHttpContext clearing = new();
        PendingSignInCookie.Clear(clearing.Response);
        clearing.Response.Headers.SetCookie.ToString().ShouldStartWith("selficient.signin=; ");
        Should.Throw<ArgumentNullException>(() => new PendingSignInCookie(null!));
        Should.Throw<ArgumentNullException>(() => cookie.Write(null!, pending));
        Should.Throw<ArgumentNullException>(() => cookie.Write(writing.Response, null!));
        Should.Throw<ArgumentNullException>(() => cookie.Read(null!));
        Should.Throw<ArgumentNullException>(() => PendingSignInCookie.Clear(null!));
        Should.Throw<ArgumentNullException>(() => HouseholdAuth.SetHint(null!));
    }

    [Fact]
    public async Task Signing_in_and_out_need_a_request_to_do_it_on()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => HouseholdAuth.SignInAsync(null!, Account()));
        await Should.ThrowAsync<ArgumentNullException>(() => HouseholdAuth.SignOutAsync(null!));
    }

    private static HttpRequest Request(string cookie)
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Cookie = $"selficient.signin={cookie}";
        return context.Request;
    }

    private static CookieValidatePrincipalContext Context(ClaimsPrincipal principal) =>
        new(
            new DefaultHttpContext { RequestServices = Services() },
            new AuthenticationScheme(HouseholdAuth.Scheme, HouseholdAuth.Scheme, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(principal, HouseholdAuth.Scheme));

    private static RedirectContext<CookieAuthenticationOptions> Redirect(HttpContext context) =>
        new(
            context,
            new AuthenticationScheme(HouseholdAuth.Scheme, HouseholdAuth.Scheme, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationProperties(),
            "https://localhost/account/sign-in?ReturnUrl=%2Faccount%2Fdelete");

    private static IServiceProvider Services()
    {
        // Ending a session signs the scheme out, which needs an authentication service behind it.
        ServiceCollection services = new();
        services.AddSingleton(Substitute.For<IAuthenticationService>());
        return services.BuildServiceProvider();
    }
}
