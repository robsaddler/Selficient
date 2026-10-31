using System.Text;
using Selficient.Application.Security;

namespace Selficient.Application.Tests.Security;

/// <summary>
/// The primitives the operator's sign-in is built from: the password hash, the time-based code,
/// the password policy, the lockout and the recovery codes. The RFC's own published vectors are
/// pinned here, so the second factor is the standard rather than an impression of it.
/// </summary>
public sealed class PasswordAndCodeTests
{
    // RFC 6238, appendix B: the SHA-1 seed is the ASCII "12345678901234567890". The published
    // codes are eight digits; a six-digit authenticator shows the last six of each.
    private static readonly byte[] RfcSeed = Encoding.UTF8.GetBytes("12345678901234567890");

    [Fact]
    public void A_password_hash_carries_its_own_parameters_and_never_repeats_a_salt()
    {
        string first = PasswordHasher.Hash("a correct horse battery staple");
        string second = PasswordHasher.Hash("a correct horse battery staple");

        first.Split('$')[0].ShouldBe(PasswordHasher.Version);
        first.Split('$')[1].ShouldBe(PasswordHasher.Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture));
        first.ShouldNotBe(second, "a fresh salt every time, so two operators with the same password do not share a hash");
        PasswordHasher.Verify("a correct horse battery staple", first).Matches.ShouldBeTrue();
        PasswordHasher.Verify("a correct horse battery staple", second).Matches.ShouldBeTrue();
        PasswordHasher.Verify("A correct horse battery staple", first).Matches.ShouldBeFalse("a password is case sensitive");
        Should.Throw<ArgumentException>(() => PasswordHasher.Hash(" "));
    }

    [Fact]
    public void An_old_hash_is_verified_and_quietly_brought_up_to_the_current_policy()
    {
        // A hash written when the policy was cheaper: the same format, fewer iterations.
        byte[] salt = new byte[PasswordHasher.SaltBytes];
        byte[] key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes("a correct horse battery staple"), salt, 1000, System.Security.Cryptography.HashAlgorithmName.SHA512, PasswordHasher.KeyBytes);
        string old = $"{PasswordHasher.Version}$1000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";

        (bool matches, bool needsRehash) = PasswordHasher.Verify("a correct horse battery staple", old);
        (bool wrongMatches, bool wrongRehash) = PasswordHasher.Verify("something else", old);

        matches.ShouldBeTrue();
        needsRehash.ShouldBeTrue("the stored cost is below today's, so it is written again");
        wrongMatches.ShouldBeFalse();
        wrongRehash.ShouldBeFalse("a wrong password never causes a write");
        PasswordHasher.Verify("x", PasswordHasher.Hash("x" + new string('y', 20))).NeedsRehash.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not our format")]
    [InlineData("v2$210000$AAAA$AAAA")]
    [InlineData("v1$notanumber$AAAA$AAAA")]
    [InlineData("v1$0$AAAA$AAAA")]
    [InlineData("v1$210000$not base64!$AAAA")]
    [InlineData("v1$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
    [InlineData("v1$210000$AAAA")]
    public void A_stored_value_we_cannot_read_is_a_mismatch_and_never_an_exception(string? stored)
    {
        (bool matches, bool needsRehash) = PasswordHasher.Verify("a correct horse battery staple", stored);

        matches.ShouldBeFalse("a row corrupted by hand must not be a way in");
        needsRehash.ShouldBeFalse();
    }

    [Fact]
    public void An_empty_password_never_matches_and_the_timing_decoy_runs_for_anything()
    {
        PasswordHasher.Verify(null, PasswordHasher.Hash("a correct horse battery staple")).Matches.ShouldBeFalse();
        PasswordHasher.Verify("", PasswordHasher.Hash("a correct horse battery staple")).Matches.ShouldBeFalse();
        Should.NotThrow(() => PasswordHasher.SpendTheSameTime(null));
        Should.NotThrow(() => PasswordHasher.SpendTheSameTime("anything at all"));
    }

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void The_published_test_vectors_produce_the_published_codes(long unixSeconds, string expected) =>
        Totp.CodeAt(RfcSeed, DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).ShouldBe(expected);

    [Fact]
    public void A_code_one_step_either_side_is_accepted_and_two_steps_away_is_refused()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        long step = Totp.StepAt(now);

        Totp.Matches(RfcSeed, Totp.CodeAtStep(RfcSeed, step), now, -1, out long atNow).ShouldBeTrue();
        Totp.Matches(RfcSeed, Totp.CodeAtStep(RfcSeed, step - 1), now, -1, out _).ShouldBeTrue("clocks drift");
        Totp.Matches(RfcSeed, Totp.CodeAtStep(RfcSeed, step + 1), now, -1, out _).ShouldBeTrue();
        Totp.Matches(RfcSeed, Totp.CodeAtStep(RfcSeed, step - 2), now, -1, out _).ShouldBeFalse();
        Totp.Matches(RfcSeed, Totp.CodeAtStep(RfcSeed, step + 2), now, -1, out _).ShouldBeFalse();
        atNow.ShouldBe(step);
    }

    [Fact]
    public void A_code_cannot_be_used_twice()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        long step = Totp.StepAt(now);
        string code = Totp.CodeAtStep(RfcSeed, step);

        Totp.Matches(RfcSeed, code, now, -1, out long spent).ShouldBeTrue();
        Totp.Matches(RfcSeed, code, now, spent, out _).ShouldBeFalse("a shoulder-surfed code is good for ninety seconds otherwise");
        Totp.Matches(RfcSeed, Totp.CodeAtStep(RfcSeed, step + 1), now, spent, out _).ShouldBeTrue("the next code still works");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("000000")]
    [InlineData("not a code")]
    public void A_wrong_or_missing_code_is_refused(string? typed)
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1111111109);

        // 000000 is a real code for some step; it is not this one, which is what is being asserted.
        Totp.Matches(RfcSeed, typed, now, -1, out _).ShouldBeFalse();
        Totp.Matches(null, "050471", now, -1, out _).ShouldBeFalse("no seed, no second factor");
        Totp.Matches([], "050471", now, -1, out _).ShouldBeFalse();
    }

    [Fact]
    public void Spaces_in_a_typed_code_are_forgiven_because_apps_show_them()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1111111111);

        Totp.Matches(RfcSeed, "050 471", now, -1, out _).ShouldBeTrue();
    }

    [Fact]
    public void A_new_secret_is_the_rfcs_width_and_its_enrolment_address_is_what_an_app_reads()
    {
        byte[] secret = Totp.NewSecret();

        secret.Length.ShouldBe(Totp.SecretBytes);
        Totp.NewSecret().ShouldNotBe(secret);
        string enrolment = Totp.Enrolment(secret, "Selficient", "rob");
        enrolment.ShouldStartWith("otpauth://totp/Selficient%3Arob?secret=");
        enrolment.ShouldContain("&issuer=Selficient&algorithm=SHA1&digits=6&period=30");
        // RFC 4648 base32: the alphabet and no padding, which is what authenticator apps accept.
        Totp.Base32([0x61, 0x62, 0x63]).ShouldBe("MFRGG");
        Totp.Base32([]).ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => Totp.Base32(null!));
        Should.Throw<ArgumentNullException>(() => Totp.Enrolment(null!, "a", "b"));
        Should.Throw<ArgumentException>(() => Totp.Enrolment(secret, " ", "b"));
        Should.Throw<ArgumentException>(() => Totp.Enrolment(secret, "a", " "));
        Should.Throw<ArgumentNullException>(() => Totp.CodeAtStep(null!, 1));
    }

    [Theory]
    [InlineData(null, "A password needs at least 14 characters")]
    [InlineData("", "A password needs at least 14 characters")]
    [InlineData("short", "A password needs at least 14 characters")]
    [InlineData("robrobrobrobrob", "A password cannot contain the username.")]
    [InlineData("ababababababababab", "A password needs more than four different characters in it.")]
    public void A_password_that_breaks_the_policy_is_refused_with_a_sentence(string? password, string expected) =>
        PasswordPolicy.Refusal(password, "rob").ShouldNotBeNull().ShouldStartWith(expected[..Math.Min(expected.Length, 30)]);

    [Fact]
    public void A_password_that_passes_the_policy_is_refused_for_nothing()
    {
        PasswordPolicy.Refusal("the lantern on the kitchen table", "rob").ShouldBeNull();
        PasswordPolicy.Refusal(new string('q', PasswordPolicy.MaximumLength + 1), "rob")
            .ShouldBe($"A password can be at most {PasswordPolicy.MaximumLength} characters.");
        Should.Throw<ArgumentException>(() => PasswordPolicy.Refusal("x", " "));
    }

    [Fact]
    public void Each_lockout_is_twice_the_last_up_to_an_hour()
    {
        DateTimeOffset now = new(2026, 10, 31, 20, 0, 0, TimeSpan.Zero);

        LockoutPolicy.LockedUntil(0, now).ShouldBeNull();
        LockoutPolicy.LockedUntil(LockoutPolicy.Threshold - 1, now).ShouldBeNull("the fifth failure is the first lock");
        LockoutPolicy.LockedUntil(LockoutPolicy.Threshold, now).ShouldBe(now.AddMinutes(1));
        LockoutPolicy.LockedUntil(LockoutPolicy.Threshold + 1, now).ShouldBe(now.AddMinutes(2));
        LockoutPolicy.LockedUntil(LockoutPolicy.Threshold + 2, now).ShouldBe(now.AddMinutes(4));
        LockoutPolicy.LockedUntil(LockoutPolicy.Threshold + 20, now).ShouldBe(now.AddHours(1), "the wait is capped so an operator is not locked out for a day");
    }

    [Fact]
    public void A_recovery_code_works_once()
    {
        IReadOnlyList<string> codes = RecoveryCodes.Issue();
        IReadOnlyList<string> stored = [.. codes.Select(RecoveryCodes.Fingerprint)];

        codes.Count.ShouldBe(RecoveryCodes.Count);
        codes.Distinct().Count().ShouldBe(RecoveryCodes.Count);
        codes[0].ShouldMatch("^[0-9A-F]{5}-[0-9A-F]{5}-[0-9A-F]{5}-[0-9A-F]{5}$");
        stored.ShouldNotContain(codes[0], "a code is stored as its fingerprint, never as itself");

        RecoveryCodes.Spend(codes[0], stored, out IReadOnlyList<string> left).ShouldBeTrue();
        left.Count.ShouldBe(RecoveryCodes.Count - 1);
        RecoveryCodes.Spend(codes[0], left, out IReadOnlyList<string> after).ShouldBeFalse("a code never works twice");
        after.Count.ShouldBe(RecoveryCodes.Count - 1);
        RecoveryCodes.Spend(codes[1].Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant(), left, out _).ShouldBeTrue("the dashes and the case are the page's, not the code's");
        RecoveryCodes.Spend(null, stored, out _).ShouldBeFalse();
        RecoveryCodes.Spend("   ", stored, out _).ShouldBeFalse();
        RecoveryCodes.Spend("NOTA-CODE", stored, out _).ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => RecoveryCodes.Spend("x", null!, out _));
        Should.Throw<ArgumentException>(() => RecoveryCodes.Fingerprint(" "));
    }
}
