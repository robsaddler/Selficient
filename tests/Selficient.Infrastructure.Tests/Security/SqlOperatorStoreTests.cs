using Selficient.Application.Security;
using Selficient.Infrastructure.Data;
using Selficient.Infrastructure.Security;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Security;

/// <summary>
/// The operators in a real SQL Server, through the real migrations. The questions here are the
/// ones a store answers and a fake cannot: that the username is matched without case but kept as
/// typed, that the unique index holds, that the secrets go in and come back as the bytes they
/// were, and that a lockout and a stamp survive a round trip.
/// </summary>
public sealed class SqlOperatorStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 31, 20, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = TestDatabase.Create();
    private SqlOperatorStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);
        _store = new SqlOperatorStore(_database.ConnectionString);
    }

    [Fact]
    public async Task An_account_goes_in_whole_and_comes_back_whole()
    {
        long id = await _store.CreateAsync("Rob", "Rob Saddler", "v1$210000$salt$hash", mustChangePassword: true, Evening, CancellationToken.None);

        OperatorAccount byName = (await _store.FindAsync("rob", CancellationToken.None))!;
        OperatorAccount byId = (await _store.FindAsync(id, CancellationToken.None))!;

        byName.Id.ShouldBe(id);
        byName.Username.ShouldBe("Rob", "the name is matched without case and kept as it was typed");
        byName.DisplayName.ShouldBe("Rob Saddler");
        byName.PasswordHash.ShouldBe("v1$210000$salt$hash");
        byName.PasswordSetAt.ShouldBe(Evening);
        byName.CreatedAt.ShouldBe(Evening);
        byName.IsEnabled.ShouldBeTrue();
        byName.MustChangePassword.ShouldBeTrue();
        byName.FailedAttempts.ShouldBe(0);
        byName.LockedUntil.ShouldBeNull();
        byName.LastSignInAt.ShouldBeNull();
        byName.LastTotpStep.ShouldBe(0);
        byName.HasSecondFactor.ShouldBeFalse();
        byName.SecurityStamp.ShouldNotBe(Guid.Empty);
        byId.ShouldBe(byName);
        (await _store.CountAsync(CancellationToken.None)).ShouldBe(1);
        (await _store.AllAsync(CancellationToken.None)).Count.ShouldBe(1);
        (await _store.FindAsync("someone else", CancellationToken.None)).ShouldBeNull();
        (await _store.FindAsync(404L, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Two_accounts_cannot_share_a_name_however_it_is_typed()
    {
        await _store.CreateAsync("rob", "Rob", "hash", false, Evening, CancellationToken.None);

        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() => _store.CreateAsync("ROB", "Rob again", "hash", false, Evening, CancellationToken.None));
        (await _store.CountAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task The_second_factor_secret_is_the_bytes_it_was_and_the_stamp_moves_with_it()
    {
        long id = await _store.CreateAsync("rob", "Rob", "hash", false, Evening, CancellationToken.None);
        Guid before = (await _store.FindAsync(id, CancellationToken.None))!.SecurityStamp;
        byte[] secret = [1, 2, 3, 250, 251, 252];
        byte[] codes = [9, 8, 7];
        Guid stamp = Guid.NewGuid();

        await _store.SetSecondFactorAsync(id, secret, Evening.AddMinutes(1), codes, stamp, CancellationToken.None);
        OperatorAccount armed = (await _store.FindAsync(id, CancellationToken.None))!;

        armed.TotpSecretProtected.ShouldBe(secret);
        armed.RecoveryCodesProtected.ShouldBe(codes);
        armed.TotpConfirmedAt.ShouldBe(Evening.AddMinutes(1));
        armed.HasSecondFactor.ShouldBeTrue();
        armed.SecurityStamp.ShouldBe(stamp);
        armed.SecurityStamp.ShouldNotBe(before, "arming the second factor ends every live session");

        await _store.SetRecoveryCodesAsync(id, [5], CancellationToken.None);
        await _store.SetLastTotpStepAsync(id, 1234, CancellationToken.None);
        OperatorAccount spent = (await _store.FindAsync(id, CancellationToken.None))!;
        spent.RecoveryCodesProtected.ShouldBe([5]);
        spent.LastTotpStep.ShouldBe(1234);
    }

    [Fact]
    public async Task A_new_password_clears_the_failures_the_lock_and_the_debt_and_moves_the_stamp()
    {
        long id = await _store.CreateAsync("rob", "Rob", "old", mustChangePassword: true, Evening, CancellationToken.None);
        await _store.RecordFailureAsync(id, 5, Evening.AddMinutes(1), CancellationToken.None);
        OperatorAccount locked = (await _store.FindAsync(id, CancellationToken.None))!;
        Guid stamp = Guid.NewGuid();

        await _store.SetPasswordAsync(id, "fresh", stamp, Evening.AddHours(1), CancellationToken.None);
        OperatorAccount changed = (await _store.FindAsync(id, CancellationToken.None))!;

        locked.FailedAttempts.ShouldBe(5);
        locked.LockedUntil.ShouldBe(Evening.AddMinutes(1));
        changed.PasswordHash.ShouldBe("fresh");
        changed.PasswordSetAt.ShouldBe(Evening.AddHours(1));
        changed.SecurityStamp.ShouldBe(stamp);
        changed.MustChangePassword.ShouldBeFalse();
        changed.FailedAttempts.ShouldBe(0);
        changed.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public async Task A_completed_sign_in_forgets_the_failures_and_a_disabled_account_keeps_its_row()
    {
        long id = await _store.CreateAsync("rob", "Rob", "hash", false, Evening, CancellationToken.None);
        await _store.RecordFailureAsync(id, 3, null, CancellationToken.None);

        await _store.RecordSuccessAsync(id, Evening.AddMinutes(5), CancellationToken.None);
        OperatorAccount after = (await _store.FindAsync(id, CancellationToken.None))!;
        Guid stamp = Guid.NewGuid();
        await _store.SetEnabledAsync(id, false, stamp, CancellationToken.None);
        OperatorAccount off = (await _store.FindAsync(id, CancellationToken.None))!;

        after.FailedAttempts.ShouldBe(0);
        after.LockedUntil.ShouldBeNull();
        after.LastSignInAt.ShouldBe(Evening.AddMinutes(5));
        off.IsEnabled.ShouldBeFalse();
        off.SecurityStamp.ShouldBe(stamp);
    }

    [Fact]
    public async Task Every_attempt_is_kept_newest_first_and_clipped_to_its_column()
    {
        await _store.RecordAttemptAsync(new SignInAttempt(Evening, "rob", "refused", "The password was wrong.", "203.0.113.7", "a browser"), CancellationToken.None);
        await _store.RecordAttemptAsync(new SignInAttempt(Evening.AddMinutes(1), new string('n', 100), "code", new string('d', 500), new string('a', 100), new string('g', 500)), CancellationToken.None);
        await _store.RecordAttemptAsync(new SignInAttempt(Evening.AddMinutes(2), "console", "code", "No request behind it."), CancellationToken.None);

        IReadOnlyList<SignInAttempt> attempts = await _store.AttemptsAsync(10, CancellationToken.None);

        attempts.Count.ShouldBe(3);
        attempts[0].Address.ShouldBeNull("a call with no request behind it records no address rather than inventing one");
        attempts[0].Agent.ShouldBeNull();
        attempts[1].Address!.Length.ShouldBe(64, "an address is clipped to the column");
        attempts[1].Agent!.Length.ShouldBe(400);
        attempts[2].Address.ShouldBe("203.0.113.7", "an operator is staff, so the trail says where the attempt came from");
        attempts[2].Agent.ShouldBe("a browser");
        attempts[1].Outcome.ShouldBe("code", "newest first");
        attempts[1].Username.Length.ShouldBe(64, "a long name is clipped to the column rather than failing the insert");
        attempts[1].Detail.Length.ShouldBe(400);
        attempts[2].At.ShouldBe(Evening);
        attempts[2].Detail.ShouldBe("The password was wrong.");
        (await _store.AttemptsAsync(1, CancellationToken.None)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_long_name_is_clipped_when_an_account_is_made()
    {
        long id = await _store.CreateAsync(new string('u', 100), new string('d', 200), "hash", false, Evening, CancellationToken.None);

        OperatorAccount made = (await _store.FindAsync(id, CancellationToken.None))!;

        made.Username.Length.ShouldBe(64);
        made.DisplayName.Length.ShouldBe(128);
    }

    [Fact]
    public async Task The_store_refuses_to_be_built_or_called_without_what_it_needs()
    {
        Should.Throw<ArgumentException>(() => new SqlOperatorStore(" "));
        await Should.ThrowAsync<ArgumentException>(() => _store.FindAsync(" ", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => _store.CreateAsync(" ", "d", "h", false, Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => _store.CreateAsync("u", " ", "h", false, Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => _store.CreateAsync("u", "d", " ", false, Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => _store.SetPasswordAsync(1, " ", Guid.NewGuid(), Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.SetSecondFactorAsync(1, null!, Evening, [1], Guid.NewGuid(), CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.SetSecondFactorAsync(1, [1], Evening, null!, Guid.NewGuid(), CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.SetRecoveryCodesAsync(1, null!, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.RecordAttemptAsync(null!, CancellationToken.None));
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }
}
