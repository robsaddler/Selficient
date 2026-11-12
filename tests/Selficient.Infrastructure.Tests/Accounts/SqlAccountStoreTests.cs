using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Accounts;
using Selficient.Infrastructure.Accounts;
using Selficient.Infrastructure.Data;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Accounts;

/// <summary>
/// Household accounts and sign-in codes in a real SQL Server, through the real migrations. The
/// questions here are the ones only the database can answer: that two first sign-ins make one
/// account, that tries which race each take a number of their own, that a code is spent once, and
/// that a code's row holds nothing that can be read.
/// </summary>
public sealed class SqlAccountStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = TestDatabase.Create();
    private SqlAccountStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);
        _store = new SqlAccountStore(_database.ConnectionString);
    }

    [Fact]
    public async Task An_account_is_made_once_for_an_address_however_it_is_typed()
    {
        HouseholdAccount made = await _store.FindOrCreateAsync("rob@example.com", Evening, CancellationToken.None);
        HouseholdAccount again = await _store.FindOrCreateAsync("ROB@Example.com", Evening.AddDays(3), CancellationToken.None);
        HouseholdAccount other = await _store.FindOrCreateAsync("someone@example.org", Evening, CancellationToken.None);
        HouseholdAccount? byId = await _store.FindAsync(made.Id, CancellationToken.None);

        made.Email.ShouldBe("rob@example.com");
        made.CreatedAt.ShouldBe(Evening);
        made.LastSeenAt.ShouldBe(Evening);
        made.SecurityStamp.ShouldNotBe(Guid.Empty);
        again.ShouldBe(made, "the second sign-in finds the account; it is not made again and its stamp does not move");
        other.Id.ShouldNotBe(made.Id);
        other.SecurityStamp.ShouldNotBe(made.SecurityStamp);
        byId.ShouldBe(made);
        (await _store.FindAsync(404, CancellationToken.None)).ShouldBeNull();
        await Should.ThrowAsync<ArgumentException>(() => _store.FindOrCreateAsync(" ", Evening, CancellationToken.None));
    }

    [Fact]
    public async Task Two_first_sign_ins_racing_make_one_account()
    {
        HouseholdAccount[] made = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => _store.FindOrCreateAsync("rob@example.com", Evening, CancellationToken.None))));

        made.Select(a => a.Id).Distinct().Count().ShouldBe(1);
        made.Select(a => a.SecurityStamp).Distinct().Count().ShouldBe(1);
        await using SqlConnection connection = new(_database.ConnectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.household_accounts")).ShouldBe(1);
    }

    [Fact]
    public async Task An_account_is_touched_and_deleted()
    {
        HouseholdAccount made = await _store.FindOrCreateAsync("rob@example.com", Evening, CancellationToken.None);

        await _store.TouchAsync(made.Id, Evening.AddDays(2), CancellationToken.None);
        HouseholdAccount touched = (await _store.FindAsync(made.Id, CancellationToken.None))!;
        bool deleted = await _store.DeleteAsync(made.Id, CancellationToken.None);
        bool deletedAgain = await _store.DeleteAsync(made.Id, CancellationToken.None);

        touched.LastSeenAt.ShouldBe(Evening.AddDays(2));
        touched.CreatedAt.ShouldBe(Evening);
        deleted.ShouldBeTrue();
        deletedAgain.ShouldBeFalse();
        (await _store.FindAsync(made.Id, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_code_row_holds_nothing_that_can_be_read()
    {
        await _store.AddCodeAsync(Code(1), CancellationToken.None);

        await using SqlConnection connection = new(_database.ConnectionString);
        IEnumerable<(string Name, string Type)> columns = await connection.QueryAsync<(string, string)>(
            "SELECT c.name, t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('dbo.household_signin_codes') ORDER BY c.column_id");

        columns.ShouldBe(
        [
            ("id", "bigint"), ("email_hash", "binary"), ("binding_hash", "binary"), ("code_hash", "binary"),
            ("created_at", "datetime2"), ("expires_at", "datetime2"), ("attempts", "int"), ("used_at", "datetime2"),
        ], "hashes and times: no address, no code, and nothing that says who asked");
    }

    [Fact]
    public async Task Attempts_are_counted_before_the_code_is_read()
    {
        SignInCode code = Code(1);
        await _store.AddCodeAsync(code, CancellationToken.None);

        CodeAttempt first = (await _store.AttemptCodeAsync(code.BindingHash, Evening.AddMinutes(1), CancellationToken.None))!;
        CodeAttempt second = (await _store.AttemptCodeAsync(code.BindingHash, Evening.AddMinutes(1), CancellationToken.None))!;
        CodeAttempt? unknown = await _store.AttemptCodeAsync(Hash(99), Evening.AddMinutes(1), CancellationToken.None);
        CodeAttempt? late = await _store.AttemptCodeAsync(code.BindingHash, Evening.AddMinutes(10), CancellationToken.None);

        first.Attempts.ShouldBe(1);
        first.CodeHash.ShouldBe(code.CodeHash);
        second.Attempts.ShouldBe(2);
        second.Id.ShouldBe(first.Id);
        unknown.ShouldBeNull("a browser that never asked has no code");
        late.ShouldBeNull("at the tenth minute it is over");
    }

    [Fact]
    public async Task Racing_guesses_share_five_tries_between_them()
    {
        SignInCode code = Code(1);
        await _store.AddCodeAsync(code, CancellationToken.None);

        CodeAttempt?[] attempts = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => _store.AttemptCodeAsync(code.BindingHash, Evening, CancellationToken.None))));

        attempts.Select(a => a!.Attempts).Order().ShouldBe(Enumerable.Range(1, 40), "every try took a number of its own, so only five were ever within the limit");
        attempts.Count(a => a!.Attempts <= AccountSignIn.MaxAttempts).ShouldBe(5);
    }

    [Fact]
    public async Task A_code_is_spent_once_and_a_spent_code_is_not_found()
    {
        SignInCode code = Code(1);
        await _store.AddCodeAsync(code, CancellationToken.None);
        CodeAttempt attempt = (await _store.AttemptCodeAsync(code.BindingHash, Evening, CancellationToken.None))!;

        bool[] spent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _store.SpendCodeAsync(attempt.Id, Evening, CancellationToken.None))));
        CodeAttempt? afterwards = await _store.AttemptCodeAsync(code.BindingHash, Evening, CancellationToken.None);

        spent.Count(s => s).ShouldBe(1, "two tabs with the right code make one sign-in");
        afterwards.ShouldBeNull();
    }

    [Fact]
    public async Task Codes_are_counted_by_address_and_swept_by_age()
    {
        await _store.AddCodeAsync(Code(1, address: 7, sentAt: Evening), CancellationToken.None);
        await _store.AddCodeAsync(Code(2, address: 7, sentAt: Evening.AddMinutes(10)), CancellationToken.None);
        await _store.AddCodeAsync(Code(3, address: 8, sentAt: Evening.AddMinutes(10)), CancellationToken.None);

        int all = await _store.CountCodesAsync(Hash(7), Evening.AddMinutes(-1), CancellationToken.None);
        int recent = await _store.CountCodesAsync(Hash(7), Evening, CancellationToken.None);
        int other = await _store.CountCodesAsync(Hash(8), Evening.AddMinutes(-1), CancellationToken.None);
        int swept = await _store.SweepCodesAsync(Evening, CancellationToken.None);
        int sweptAgain = await _store.SweepCodesAsync(Evening, CancellationToken.None);

        all.ShouldBe(2);
        recent.ShouldBe(1, "sent after, not at, the moment asked about");
        other.ShouldBe(1);
        swept.ShouldBe(1);
        sweptAgain.ShouldBe(0);
        (await _store.CountCodesAsync(Hash(7), Evening.AddMinutes(-1), CancellationToken.None)).ShouldBe(1);
        await Should.ThrowAsync<SqlException>(() => _store.AddCodeAsync(Code(2, address: 9), CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.AddCodeAsync(null!, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.CountCodesAsync(null!, Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.AttemptCodeAsync(null!, Evening, CancellationToken.None));
        Should.Throw<ArgumentException>(() => new SqlAccountStore(""));
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }

    private static SignInCode Code(int browser, int address = 1, DateTimeOffset? sentAt = null) =>
        new(Hash(address), Hash(1000 + browser), Hash(2000 + browser), sentAt ?? Evening, (sentAt ?? Evening).AddMinutes(10));

    private static byte[] Hash(int n) => System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(n));
}
