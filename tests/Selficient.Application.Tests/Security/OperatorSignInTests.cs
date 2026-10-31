using System.Text;
using Selficient.TestKit;
using Selficient.Application.Security;

namespace Selficient.Application.Tests.Security;

/// <summary>
/// Signing an operator in, against a store held in memory. These are the cases from
/// <c>docs/threat-models/operator-auth.md</c>: the refusals that say the same thing, the lockout
/// that grows, the code that is spent when it is used, and the recovery code that works once.
/// </summary>
public sealed class OperatorSignInTests
{
    private const string Password = "the lantern on the kitchen table";
    private static readonly DateTimeOffset Evening = new(2026, 10, 31, 20, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly ManualClock _clock = new(Evening);
    private readonly OperatorSignIn _signIn;

    public OperatorSignInTests() => _signIn = new OperatorSignIn(_store, new PlainProtector(), _clock);

    [Fact]
    public async Task A_password_alone_is_not_a_sign_in()
    {
        await EnrolledAsync();

        OperatorSignInResult result = await _signIn.PasswordAsync("rob", Password, CancellationToken.None);

        result.Outcome.ShouldBe(SignInOutcome.NeedsSecondFactor, "the password is a step, not the door");
        result.Account.ShouldNotBeNull().Username.ShouldBe("rob");
        _store.Attempts.Last().Outcome.ShouldBe("password");
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_name_answer_alike()
    {
        await EnrolledAsync();

        OperatorSignInResult wrong = await _signIn.PasswordAsync("rob", "not the password at all", CancellationToken.None);
        OperatorSignInResult unknown = await _signIn.PasswordAsync("nobody", Password, CancellationToken.None);
        OperatorSignInResult blank = await _signIn.PasswordAsync("  ", Password, CancellationToken.None);

        wrong.Outcome.ShouldBe(SignInOutcome.Refused);
        unknown.Outcome.ShouldBe(SignInOutcome.Refused);
        blank.Outcome.ShouldBe(SignInOutcome.Refused);
        wrong.Account.ShouldBeNull("a refusal never hands back the account");
        unknown.Account.ShouldBeNull();
        _store.Attempts.Count(a => a.Outcome == "refused").ShouldBe(3);
        _store.Attempts.Last().Username.ShouldBe("(no name)");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_the_right_one_does_not_open_it()
    {
        await EnrolledAsync();

        for (int attempt = 0; attempt < LockoutPolicy.Threshold; attempt++)
        {
            await _signIn.PasswordAsync("rob", "wrong", CancellationToken.None);
        }

        OperatorSignInResult locked = await _signIn.PasswordAsync("rob", Password, CancellationToken.None);

        locked.Outcome.ShouldBe(SignInOutcome.LockedOut);
        _store.Account.FailedAttempts.ShouldBe(LockoutPolicy.Threshold);
        _store.Account.LockedUntil.ShouldBe(Evening.AddMinutes(1));
        _store.Attempts.Last().Outcome.ShouldBe("locked");
    }

    [Fact]
    public async Task A_lock_that_has_run_out_lets_the_right_password_through_and_forgets_the_failures()
    {
        await EnrolledAsync();
        for (int attempt = 0; attempt < LockoutPolicy.Threshold; attempt++)
        {
            await _signIn.PasswordAsync("rob", "wrong", CancellationToken.None);
        }

        _clock.Advance(TimeSpan.FromMinutes(2));
        OperatorSignInResult opened = await _signIn.PasswordAsync("rob", Password, CancellationToken.None);
        await FinishAsync();

        opened.Outcome.ShouldBe(SignInOutcome.NeedsSecondFactor);
        _store.Account.FailedAttempts.ShouldBe(0, "a completed sign-in forgets the failures");
        _store.Account.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public async Task A_disabled_account_cannot_sign_in()
    {
        await EnrolledAsync();
        await _store.SetEnabledAsync(_store.Account.Id, false, Guid.NewGuid(), CancellationToken.None);

        OperatorSignInResult result = await _signIn.PasswordAsync("rob", Password, CancellationToken.None);
        OperatorSignInResult second = await _signIn.SecondFactorAsync(_store.Account.Id, CodeNow(), CancellationToken.None);

        result.Outcome.ShouldBe(SignInOutcome.Disabled);
        second.Outcome.ShouldBe(SignInOutcome.Refused, "nor can a half-signed-in session be finished");
        _store.Attempts.ShouldContain(a => a.Outcome == "disabled");
    }

    [Fact]
    public async Task The_second_factor_completes_the_sign_in_and_the_code_is_spent()
    {
        await EnrolledAsync();
        await _signIn.PasswordAsync("rob", Password, CancellationToken.None);
        string code = CodeNow();

        OperatorSignInResult first = await _signIn.SecondFactorAsync(_store.Account.Id, code, CancellationToken.None);
        OperatorSignInResult again = await _signIn.SecondFactorAsync(_store.Account.Id, code, CancellationToken.None);

        first.Outcome.ShouldBe(SignInOutcome.SignedIn);
        again.Outcome.ShouldBe(SignInOutcome.Refused, "the same code is not taken twice");
        _store.Account.LastSignInAt.ShouldBe(Evening);
        _store.Attempts.ShouldContain(a => a.Outcome == "code");
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_and_is_then_gone()
    {
        IReadOnlyList<string> codes = await EnrolledAsync();
        await _signIn.PasswordAsync("rob", Password, CancellationToken.None);

        OperatorSignInResult first = await _signIn.SecondFactorAsync(_store.Account.Id, codes[0], CancellationToken.None);
        OperatorSignInResult again = await _signIn.SecondFactorAsync(_store.Account.Id, codes[0], CancellationToken.None);
        OperatorSignInResult another = await _signIn.SecondFactorAsync(_store.Account.Id, codes[1], CancellationToken.None);

        first.Outcome.ShouldBe(SignInOutcome.SignedIn);
        again.Outcome.ShouldBe(SignInOutcome.Refused);
        another.Outcome.ShouldBe(SignInOutcome.SignedIn);
        _signIn.RemainingCodes(_store.Account).Count.ShouldBe(RecoveryCodes.Count - 2);
        _store.Attempts.ShouldContain(a => a.Outcome == "recovery");
    }

    [Fact]
    public async Task An_account_with_no_second_factor_is_sent_to_enrolment_and_cannot_finish_without_one()
    {
        await CreatedAsync();

        OperatorSignInResult result = await _signIn.PasswordAsync("rob", Password, CancellationToken.None);
        OperatorSignInResult second = await _signIn.SecondFactorAsync(_store.Account.Id, "000000", CancellationToken.None);
        OperatorSignInResult missing = await _signIn.SecondFactorAsync(404, "000000", CancellationToken.None);

        result.Outcome.ShouldBe(SignInOutcome.NeedsEnrolment);
        second.Outcome.ShouldBe(SignInOutcome.Refused);
        missing.Outcome.ShouldBe(SignInOutcome.Refused);
        _signIn.RemainingCodes(_store.Account).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_account_that_owes_a_password_is_signed_in_but_sent_to_change_it()
    {
        await EnrolledAsync(mustChangePassword: true);
        await _signIn.PasswordAsync("rob", Password, CancellationToken.None);

        OperatorSignInResult result = await _signIn.SecondFactorAsync(_store.Account.Id, CodeNow(), CancellationToken.None);

        result.Outcome.ShouldBe(SignInOutcome.NeedsNewPassword);
    }

    [Fact]
    public async Task A_wrong_second_factor_is_refused_and_recorded()
    {
        await EnrolledAsync();
        await _signIn.PasswordAsync("rob", Password, CancellationToken.None);

        OperatorSignInResult result = await _signIn.SecondFactorAsync(_store.Account.Id, "123456", CancellationToken.None);

        result.Outcome.ShouldBe(SignInOutcome.Refused);
        result.Account.ShouldNotBeNull("the caller still knows who was refused, so the page can stay on the code step");
        _store.Attempts.Last().Detail.ShouldBe("The second factor was wrong.");
    }

    [Fact]
    public async Task A_hash_stored_to_a_weaker_policy_is_written_again_on_the_next_right_password()
    {
        await CreatedAsync();
        byte[] salt = new byte[PasswordHasher.SaltBytes];
        byte[] key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(Password), salt, 1000, System.Security.Cryptography.HashAlgorithmName.SHA512, PasswordHasher.KeyBytes);
        _store.Account = _store.Account with { PasswordHash = $"v1$1000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}" };

        await _signIn.PasswordAsync("rob", Password, CancellationToken.None);

        _store.Account.PasswordHash.Split('$')[1].ShouldBe(PasswordHasher.Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_missing_username_is_refused_without_a_lookup()
    {
        await EnrolledAsync();

        OperatorSignInResult result = await _signIn.PasswordAsync(null, Password, CancellationToken.None);

        result.Outcome.ShouldBe(SignInOutcome.Refused);
        _store.Attempts.Last().Username.ShouldBe("(no name)");
    }

    [Fact]
    public async Task An_account_with_a_second_factor_but_no_recovery_codes_refuses_one()
    {
        await EnrolledAsync();
        _store.Account = _store.Account with { RecoveryCodesProtected = null };
        await _signIn.PasswordAsync("rob", Password, CancellationToken.None);

        OperatorSignInResult result = await _signIn.SecondFactorAsync(_store.Account.Id, "ABCDE-FGHIJ-KLMNO-PQRST", CancellationToken.None);

        result.Outcome.ShouldBe(SignInOutcome.Refused, "there is nothing to spend");
        _signIn.RemainingCodes(_store.Account).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_codes_blob_holding_nothing_reads_as_no_codes_rather_than_throwing()
    {
        await EnrolledAsync();
        // The literal JSON null: a row written by hand, or by a version that stored it that way.
        _store.Account = _store.Account with { RecoveryCodesProtected = Encoding.UTF8.GetBytes("null") };

        _signIn.RemainingCodes(_store.Account).ShouldBeEmpty();
        (await _signIn.SecondFactorAsync(_store.Account.Id, "ABCDE-FGHIJ-KLMNO-PQRST", CancellationToken.None)).Outcome.ShouldBe(SignInOutcome.Refused);
    }

    [Fact]
    public void The_sign_in_refuses_to_be_built_without_its_parts()
    {
        Should.Throw<ArgumentNullException>(() => new OperatorSignIn(null!, new PlainProtector(), _clock));
        Should.Throw<ArgumentNullException>(() => new OperatorSignIn(_store, null!, _clock));
        Should.Throw<ArgumentNullException>(() => new OperatorSignIn(_store, new PlainProtector(), null!));
        Should.Throw<ArgumentNullException>(() => _signIn.RemainingCodes(null!));
        Should.Throw<ArgumentNullException>(() => _signIn.ProtectCodes(null!));
        Should.Throw<ArgumentNullException>(() => _signIn.ProtectSecret(null!));
        _signIn.ReadSecret(null).ShouldBeNull();
    }

    private string CodeNow() => Totp.CodeAt(_signIn.ReadSecret(_store.Account.TotpSecretProtected)!, _clock.GetUtcNow());

    private Task FinishAsync() => _signIn.SecondFactorAsync(_store.Account.Id, CodeNow(), CancellationToken.None);

    private async Task CreatedAsync(bool mustChangePassword = false)
    {
        await _store.CreateAsync("rob", "Rob", PasswordHasher.Hash(Password), mustChangePassword, Evening, CancellationToken.None);
    }

    private async Task<IReadOnlyList<string>> EnrolledAsync(bool mustChangePassword = false)
    {
        await CreatedAsync(mustChangePassword);
        IReadOnlyList<string> codes = RecoveryCodes.Issue();
        await _store.SetSecondFactorAsync(_store.Account.Id, _signIn.ProtectSecret(Totp.NewSecret()), Evening, _signIn.ProtectCodes(codes), Guid.NewGuid(), CancellationToken.None);
        return codes;
    }

    /// <summary>The key ring stands aside here: the question is the sign-in, not the encryption.</summary>
    private sealed class PlainProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext;

        public byte[]? Unprotect(byte[]? ciphertext) => ciphertext is { Length: > 0 } ? ciphertext : null;
    }

    /// <summary>One operator, held in memory, behaving as the SQL store does.</summary>
    private sealed class FakeStore : IOperatorStore
    {
        public OperatorAccount Account { get; set; } = null!;

        public List<SignInAttempt> Attempts { get; } = [];

        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Account is null ? 0 : 1);

        public Task<IReadOnlyList<OperatorAccount>> AllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OperatorAccount>>(Account is null ? [] : [Account]);

        public Task<OperatorAccount?> FindAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(Account is not null && string.Equals(Account.Username, username, StringComparison.OrdinalIgnoreCase) ? Account : null);

        public Task<OperatorAccount?> FindAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(Account is not null && Account.Id == id ? Account : null);

        public Task<long> CreateAsync(string username, string displayName, string passwordHash, bool mustChangePassword, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Account = new OperatorAccount(1, username, displayName, passwordHash, now, null, null, 0, null, Guid.NewGuid(), true, mustChangePassword, 0, null, null, now);
            return Task.FromResult(1L);
        }

        public Task SetPasswordAsync(long id, string passwordHash, Guid stamp, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Account = Account with { PasswordHash = passwordHash, PasswordSetAt = now, SecurityStamp = stamp, MustChangePassword = false, FailedAttempts = 0, LockedUntil = null };
            return Task.CompletedTask;
        }

        public Task SetSecondFactorAsync(long id, byte[] totpSecretProtected, DateTimeOffset confirmedAt, byte[] recoveryCodesProtected, Guid stamp, CancellationToken cancellationToken)
        {
            Account = Account with { TotpSecretProtected = totpSecretProtected, TotpConfirmedAt = confirmedAt, RecoveryCodesProtected = recoveryCodesProtected, SecurityStamp = stamp, LastTotpStep = 0 };
            return Task.CompletedTask;
        }

        public Task SetRecoveryCodesAsync(long id, byte[] recoveryCodesProtected, CancellationToken cancellationToken)
        {
            Account = Account with { RecoveryCodesProtected = recoveryCodesProtected };
            return Task.CompletedTask;
        }

        public Task SetLastTotpStepAsync(long id, long lastStep, CancellationToken cancellationToken)
        {
            Account = Account with { LastTotpStep = lastStep };
            return Task.CompletedTask;
        }

        public Task SetEnabledAsync(long id, bool enabled, Guid stamp, CancellationToken cancellationToken)
        {
            Account = Account with { IsEnabled = enabled, SecurityStamp = stamp };
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(long id, int failedAttempts, DateTimeOffset? lockedUntil, CancellationToken cancellationToken)
        {
            Account = Account with { FailedAttempts = failedAttempts, LockedUntil = lockedUntil };
            return Task.CompletedTask;
        }

        public Task RecordSuccessAsync(long id, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Account = Account with { FailedAttempts = 0, LockedUntil = null, LastSignInAt = now };
            return Task.CompletedTask;
        }

        public Task RecordAttemptAsync(SignInAttempt attempt, CancellationToken cancellationToken)
        {
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SignInAttempt>> AttemptsAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SignInAttempt>>([.. Attempts.AsEnumerable().Reverse().Take(take)]);
    }
}
