using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Selficient.TestKit;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Security;

public sealed class OperatorSessionTests
{
    private const string Key = "a-long-enough-operator-key-2026";

    private static OperatorSession Session(ManualClock clock, string? key = Key, int hours = 12, IDataProtectionProvider? protection = null) =>
        new(protection ?? new EphemeralDataProtectionProvider(), Options.Create(new OperatorOptions { Key = key, SessionHours = hours }), clock);

    [Fact]
    public void The_session_needs_its_three_parts()
    {
        ManualClock clock = new();
        IOptions<OperatorOptions> options = Options.Create(new OperatorOptions());

        Should.Throw<ArgumentNullException>(() => new OperatorSession(null!, options, clock));
        Should.Throw<ArgumentNullException>(() => new OperatorSession(new EphemeralDataProtectionProvider(), null!, clock));
        Should.Throw<ArgumentNullException>(() => new OperatorSession(new EphemeralDataProtectionProvider(), options, null!));
    }

    [Fact]
    public void Without_a_key_nothing_matches_and_nothing_is_valid()
    {
        OperatorSession session = Session(new ManualClock(), key: null);

        session.IsConfigured.ShouldBeFalse();
        session.Matches(Key).ShouldBeFalse();
        session.IsValid(session.Issue()).ShouldBeFalse("a cookie issued while the key was set dies with it");
    }

    [Fact]
    public void The_key_matches_exactly_and_only_the_key()
    {
        OperatorSession session = Session(new ManualClock());

        session.IsConfigured.ShouldBeTrue();
        session.Matches(Key).ShouldBeTrue();
        session.Matches(Key + " ").ShouldBeFalse();
        session.Matches(Key.ToUpperInvariant()).ShouldBeFalse();
        session.Matches("").ShouldBeFalse();
        session.Matches(null).ShouldBeFalse();
    }

    [Fact]
    public void A_cookie_is_valid_until_the_session_runs_out_and_nothing_else_passes()
    {
        ManualClock clock = new();
        EphemeralDataProtectionProvider protection = new();
        OperatorSession session = Session(clock, protection: protection);
        string cookie = session.Issue();

        session.IsValid(cookie).ShouldBeTrue();
        clock.Advance(TimeSpan.FromHours(11));
        session.IsValid(cookie).ShouldBeTrue();
        clock.Advance(TimeSpan.FromHours(2));
        session.IsValid(cookie).ShouldBeFalse("twelve hours have passed");
        session.IsValid(null).ShouldBeFalse();
        session.IsValid("").ShouldBeFalse();
        session.IsValid("not-a-protected-value").ShouldBeFalse("a forged cookie fails the signature");
        session.IsValid(Session(clock).Issue()).ShouldBeFalse("a cookie signed with another key ring is not ours");
        session.IsValid(protection.CreateProtector("Selficient.Operator.v1").Protect("not a date")).ShouldBeFalse("a signed value that is not a moment is refused");
    }

    [Fact]
    public void The_cookie_is_locked_down_and_scoped_to_the_reviewer()
    {
        CookieOptions options = Session(new ManualClock(), hours: 3).CookieOptions();

        options.HttpOnly.ShouldBeTrue();
        options.Secure.ShouldBeTrue();
        options.SameSite.ShouldBe(SameSiteMode.Strict);
        options.Path.ShouldBe("/admin");
        options.MaxAge.ShouldBe(TimeSpan.FromHours(3));
        options.IsEssential.ShouldBeTrue();
        OperatorSession.CookieName.ShouldBe("selficient.operator");
    }
}
