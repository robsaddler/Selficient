using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Accounts;
using Selficient.Infrastructure.Accounts;
using Selficient.Infrastructure.Data;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Accounts;

/// <summary>
/// An account's copy of its plan in a real SQL Server, through the real migrations. The promise
/// only the database can keep: a write lands on the version it names or not at all, however many
/// devices write at once; and the copy goes when the account does.
/// </summary>
public sealed class SqlPlanStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = TestDatabase.Create();
    private SqlPlanStore _plans = null!;
    private SqlAccountStore _accounts = null!;
    private long _account;

    public async ValueTask InitializeAsync()
    {
        await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);
        _plans = new SqlPlanStore(_database.ConnectionString);
        _accounts = new SqlAccountStore(_database.ConnectionString);
        _account = (await _accounts.FindOrCreateAsync("rob@example.com", Evening, CancellationToken.None)).Id;
    }

    [Fact]
    public async Task A_copy_is_written_first_then_replaced_version_by_version()
    {
        StoredPlan? none = await _plans.GetAsync(_account, CancellationToken.None);
        long? first = await _plans.TryPutAsync(_account, [1, 1, 1], 0, Evening, CancellationToken.None);
        long? second = await _plans.TryPutAsync(_account, [2, 2], 1, Evening.AddMinutes(5), CancellationToken.None);
        StoredPlan stored = (await _plans.GetAsync(_account, CancellationToken.None))!;

        none.ShouldBeNull();
        first.ShouldBe(1);
        second.ShouldBe(2);
        stored.Protected.ShouldBe([2, 2], "the bytes come back as they went in; the store reads nothing in them");
        stored.Version.ShouldBe(2);
        stored.UpdatedAt.ShouldBe(Evening.AddMinutes(5));
    }

    [Fact]
    public async Task A_write_based_on_a_stale_version_does_not_land()
    {
        await _plans.TryPutAsync(_account, [1], 0, Evening, CancellationToken.None);
        await _plans.TryPutAsync(_account, [2], 1, Evening, CancellationToken.None);

        long? firstAgain = await _plans.TryPutAsync(_account, [9], 0, Evening, CancellationToken.None);
        long? stale = await _plans.TryPutAsync(_account, [9], 1, Evening, CancellationToken.None);
        long? ahead = await _plans.TryPutAsync(_account, [9], 5, Evening, CancellationToken.None);
        long? nobody = await _plans.TryPutAsync(_account + 404, [9], 3, Evening, CancellationToken.None);

        firstAgain.ShouldBeNull("a device that thinks there is no copy cannot overwrite the one that is there");
        stale.ShouldBeNull();
        ahead.ShouldBeNull();
        nobody.ShouldBeNull("an update for an account with no copy changes nothing");
        (await _plans.GetAsync(_account, CancellationToken.None))!.Protected.ShouldBe([2]);
    }

    [Fact]
    public async Task Two_writers_racing_from_the_same_version_make_one_winner()
    {
        long?[] first = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => _plans.TryPutAsync(_account, [(byte)i], 0, Evening, CancellationToken.None))));
        long?[] next = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => _plans.TryPutAsync(_account, [(byte)(10 + i)], 1, Evening, CancellationToken.None))));

        first.Count(v => v is not null).ShouldBe(1, "eight devices writing the first copy at once: one writes it");
        next.Count(v => v is not null).ShouldBe(1, "and eight writing over version one: one does, and seven are told to look again");
        (await _plans.GetAsync(_account, CancellationToken.None))!.Version.ShouldBe(2);
    }

    [Fact]
    public async Task The_copy_goes_when_the_account_does_and_holds_nothing_but_ciphertext_a_version_and_a_time()
    {
        long other = (await _accounts.FindOrCreateAsync("neighbour@example.com", Evening, CancellationToken.None)).Id;
        await _plans.TryPutAsync(_account, [1], 0, Evening, CancellationToken.None);
        await _plans.TryPutAsync(other, [2], 0, Evening, CancellationToken.None);

        await _accounts.DeleteAsync(_account, CancellationToken.None);

        (await _plans.GetAsync(_account, CancellationToken.None)).ShouldBeNull();
        (await _plans.GetAsync(other, CancellationToken.None))!.Protected.ShouldBe([2], "nobody else's is touched");
        await using SqlConnection connection = new(_database.ConnectionString);
        IEnumerable<string> columns = await connection.QueryAsync<string>("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.household_plans') ORDER BY column_id");
        columns.ShouldBe(["account_id", "snapshot", "version", "updated_at"]);
        await Should.ThrowAsync<ArgumentNullException>(() => _plans.TryPutAsync(_account, null!, 0, Evening, CancellationToken.None));
        await Should.ThrowAsync<SqlException>(() => _plans.TryPutAsync(_account + 404, [1], 0, Evening, CancellationToken.None));
        Should.Throw<ArgumentException>(() => new SqlPlanStore(" "));
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }
}
