using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Selficient.Application.Security;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Security;

/// <summary>
/// The pieces of the operator's sign-in that a browser cannot reach: the claims a principal
/// carries, the protector when it is handed something it did not write, the cookie check that ends
/// a session whose stamp has moved, and the bootstrap's refusals.
/// </summary>
public sealed class OperatorAuthUnitTests
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 31, 20, 0, 0, TimeSpan.Zero);

    private static OperatorAccount Account(bool mustChange = false, bool enabled = true, Guid stamp = default) =>
        new(7, "rob", "Rob", "hash", Evening, [1], Evening, 0, [2], stamp == default ? Guid.NewGuid() : stamp, enabled, mustChange, 0, null, null, Evening);

    [Fact]
    public void A_principal_carries_the_number_the_name_and_the_stamp_and_says_what_is_still_owed()
    {
        OperatorAccount account = Account(mustChange: true);

        ClaimsPrincipal pending = OperatorAuth.Principal(account, pending: true);
        ClaimsPrincipal done = OperatorAuth.Principal(account with { MustChangePassword = false }, pending: false);

        OperatorAuth.IdOf(pending).ShouldBe(7);
        pending.Identity!.Name.ShouldBe("rob");
        pending.FindFirstValue(OperatorAuth.StampClaim).ShouldBe(account.SecurityStamp.ToString());
        OperatorAuth.IsPending(pending).ShouldBeTrue();
        OperatorAuth.MustChangePassword(pending).ShouldBeTrue();
        OperatorAuth.IsPending(done).ShouldBeFalse();
        OperatorAuth.MustChangePassword(done).ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => OperatorAuth.Principal(null!, false));
    }

    [Fact]
    public void Nobody_at_all_is_not_an_operator()
    {
        OperatorAuth.IdOf(null).ShouldBeNull();
        OperatorAuth.IdOf(new ClaimsPrincipal(new ClaimsIdentity())).ShouldBeNull("a principal with no number is nobody");
        OperatorAuth.IsPending(null).ShouldBeFalse();
        OperatorAuth.MustChangePassword(null).ShouldBeFalse();
        OperatorAuth.NameOf(null).ShouldBeEmpty();
        OperatorAuth.NameOf(new ClaimsPrincipal(new ClaimsIdentity())).ShouldBeEmpty("a hand taken by nobody records nobody rather than throwing");
        OperatorAuth.NameOf(new ClaimsPrincipal()).ShouldBeEmpty("a principal carrying no identity at all is nobody too");
        OperatorAuth.NameOf(OperatorAuth.Principal(Account(), pending: false)).ShouldBe("rob");
    }

    [Fact]
    public void An_operators_trail_says_where_the_attempt_came_from()
    {
        DefaultHttpContext request = new();
        request.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        request.Request.Headers.UserAgent = "a browser";
        DefaultHttpContext bare = new();

        SignInOrigin seen = OperatorAuth.OriginOf(request);
        SignInOrigin thin = OperatorAuth.OriginOf(bare);
        SignInOrigin none = OperatorAuth.OriginOf(null);

        seen.Address.ShouldBe("203.0.113.7", "an operator is staff: the promise that nothing identifying is kept is made to readers");
        seen.Agent.ShouldBe("a browser");
        thin.Address.ShouldBeNull("a request with no address recorded says so rather than inventing one");
        thin.Agent.ShouldBeNull("an empty agent header is no agent");
        none.ShouldBe(SignInOrigin.Unknown, "a console or a background task has no request behind it");
    }

    [Fact]
    public void The_protector_hands_back_nothing_for_anything_it_did_not_write()
    {
        DataProtectionSecretProtector protector = new(DataProtectionProvider.Create("Selficient.Tests"));
        byte[] secret = [1, 2, 3];

        byte[] sealed_ = protector.Protect(secret);

        protector.Unprotect(sealed_).ShouldBe(secret);
        protector.Unprotect(null).ShouldBeNull();
        protector.Unprotect([]).ShouldBeNull();
        protector.Unprotect([9, 9, 9]).ShouldBeNull("a value from another ring is not a secret, and not an exception either");
        Should.Throw<ArgumentNullException>(() => protector.Protect(null!));
        Should.Throw<ArgumentNullException>(() => new DataProtectionSecretProtector(null!));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public async Task A_cookie_is_kept_only_while_the_account_still_matches_it(bool missing, bool disabled, bool stampMoved, bool kept)
    {
        Guid stamp = Guid.NewGuid();
        OperatorAccount account = Account(enabled: !disabled, stamp: stampMoved ? Guid.NewGuid() : stamp);
        IOperatorStore store = Substitute.For<IOperatorStore>();
        store.FindAsync(7L, Arg.Any<CancellationToken>()).Returns(missing ? null : account);
        OperatorCookieEvents events = new(store);
        CookieValidatePrincipalContext context = Context(OperatorAuth.Principal(Account(stamp: stamp), pending: false));

        await events.ValidatePrincipal(context);

        (context.Principal is not null).ShouldBe(kept);
        Should.Throw<ArgumentNullException>(() => new OperatorCookieEvents(null!));
        await Should.ThrowAsync<ArgumentNullException>(() => events.ValidatePrincipal(null!));
    }

    [Fact]
    public async Task A_cookie_with_no_number_on_it_is_rejected_without_a_lookup()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        OperatorCookieEvents events = new(store);
        CookieValidatePrincipalContext context = Context(new ClaimsPrincipal(new ClaimsIdentity()));

        await events.ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
        await store.DidNotReceive().FindAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, "rob", "a long enough password", 0, 0)]
    [InlineData(true, null, null, 0, 0)]
    [InlineData(true, "rob", null, 0, 0)]
    [InlineData(true, "rob", "a long enough password", 1, 0)]
    [InlineData(true, "rob", "a long enough password", 0, 1)]
    public async Task The_bootstrap_makes_an_account_only_when_it_should(bool enabled, string? username, string? password, int existing, int expected)
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        store.CountAsync(Arg.Any<CancellationToken>()).Returns(existing);
        OperatorBootstrap bootstrap = new(
            store,
            Options.Create(new OperatorOptions { Enabled = enabled, BootstrapUsername = username, BootstrapPassword = password }),
            TimeProvider.System,
            NullLogger<OperatorBootstrap>.Instance);

        await bootstrap.StartAsync(CancellationToken.None);
        await bootstrap.StopAsync(CancellationToken.None);

        await store.Received(expected).CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_bootstrap_refuses_to_be_built_without_its_parts()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        IOptions<OperatorOptions> options = Options.Create(new OperatorOptions());

        Should.Throw<ArgumentNullException>(() => new OperatorBootstrap(null!, options, TimeProvider.System, NullLogger<OperatorBootstrap>.Instance));
        Should.Throw<ArgumentNullException>(() => new OperatorBootstrap(store, null!, TimeProvider.System, NullLogger<OperatorBootstrap>.Instance));
        Should.Throw<ArgumentNullException>(() => new OperatorBootstrap(store, options, null!, NullLogger<OperatorBootstrap>.Instance));
        Should.Throw<ArgumentNullException>(() => new OperatorBootstrap(store, options, TimeProvider.System, null!));
        new OperatorOptions { BootstrapUsername = "a", BootstrapPassword = "b" }.HasBootstrap.ShouldBeTrue();
        new OperatorOptions { BootstrapUsername = " ", BootstrapPassword = "b" }.HasBootstrap.ShouldBeFalse();
    }

    private static CookieValidatePrincipalContext Context(ClaimsPrincipal principal) =>
        new(
            new DefaultHttpContext { RequestServices = Services() },
            new Microsoft.AspNetCore.Authentication.AuthenticationScheme(OperatorAuth.Scheme, OperatorAuth.Scheme, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, OperatorAuth.Scheme));

    private static IServiceProvider Services()
    {
        // Rejecting a principal signs the scheme out, which needs an authentication service behind it.
        ServiceCollection services = new();
        services.AddSingleton(Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationService>());
        return services.BuildServiceProvider();
    }
}
