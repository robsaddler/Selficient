using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Infrastructure.Data;
using Selficient.Infrastructure.Handoff;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Handoff;

/// <summary>
/// Copies waiting between two devices, in a real SQL Server through the real migrations. The
/// promises here are the ones only the database can keep: a copy is read once however many readers
/// race for it, a copy past its time is never handed over, and the table holds nothing about who
/// left it.
/// </summary>
public sealed class SqlHandoffStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = TestDatabase.Create();
    private SqlHandoffStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);
        _store = new SqlHandoffStore(_database.ConnectionString);
    }

    [Fact]
    public async Task A_copy_goes_in_and_comes_out_once()
    {
        byte[] id = Id(1);
        byte[] copy = [.. Enumerable.Range(0, 5000).Select(i => (byte)i)];

        await _store.PutAsync(id, copy, Evening, Evening.AddMinutes(10), CancellationToken.None);
        bool waiting = await _store.IsWaitingAsync(id, Evening.AddMinutes(1), CancellationToken.None);
        bool stillWaiting = await _store.IsWaitingAsync(id, Evening.AddMinutes(1), CancellationToken.None);
        byte[]? first = await _store.TakeAsync(id, Evening.AddMinutes(2), CancellationToken.None);
        byte[]? second = await _store.TakeAsync(id, Evening.AddMinutes(2), CancellationToken.None);

        waiting.ShouldBeTrue();
        stillWaiting.ShouldBeTrue("asking whether it waits does not collect it");
        first.ShouldBe(copy);
        second.ShouldBeNull("the row went in the statement that read it");
        (await _store.IsWaitingAsync(id, Evening.AddMinutes(2), CancellationToken.None)).ShouldBeFalse();
        (await _store.TakeAsync(Id(2), Evening, CancellationToken.None)).ShouldBeNull("an id nobody left anything under");
        (await _store.CountAsync(CancellationToken.None)).ShouldBe(0);

        await using SqlConnection connection = new(_database.ConnectionString);
        IEnumerable<string> columns = await connection.QueryAsync<string>("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.handoffs') ORDER BY column_id");
        columns.ShouldBe(["id", "ciphertext", "created_at", "expires_at"], "nothing about who left it: no address, no agent, no account");
    }

    [Fact]
    public async Task A_copy_that_has_run_out_is_not_handed_over_and_is_swept()
    {
        await _store.PutAsync(Id(1), [1], Evening, Evening.AddMinutes(10), CancellationToken.None);
        await _store.PutAsync(Id(2), [2], Evening.AddMinutes(5), Evening.AddMinutes(15), CancellationToken.None);

        bool lateWaiting = await _store.IsWaitingAsync(Id(1), Evening.AddMinutes(10), CancellationToken.None);
        byte[]? late = await _store.TakeAsync(Id(1), Evening.AddMinutes(10), CancellationToken.None);
        int before = await _store.CountAsync(CancellationToken.None);
        int swept = await _store.SweepAsync(Evening.AddMinutes(10), CancellationToken.None);
        int sweptAgain = await _store.SweepAsync(Evening.AddMinutes(10), CancellationToken.None);

        lateWaiting.ShouldBeFalse();
        late.ShouldBeNull("at the tenth minute it is over, before any sweeper has been");
        before.ShouldBe(2, "refused, but still on the disk until it is swept");
        swept.ShouldBe(1);
        sweptAgain.ShouldBe(0);
        (await _store.TakeAsync(Id(2), Evening.AddMinutes(10), CancellationToken.None)).ShouldBe([2], "the one with time left is untouched");
    }

    [Fact]
    public async Task Two_collectors_racing_get_one_copy_between_them()
    {
        for (int round = 0; round < 20; round++)
        {
            byte[] id = Id(100 + round);
            await _store.PutAsync(id, [7, 7, 7], Evening, Evening.AddMinutes(10), CancellationToken.None);

            byte[]?[] taken = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _store.TakeAsync(id, Evening, CancellationToken.None))));

            taken.Count(copy => copy is not null).ShouldBe(1, "one reader has it and seven are told it is gone");
        }
    }

    [Fact]
    public async Task The_same_id_twice_is_refused_and_nothing_is_taken_without_arguments()
    {
        await _store.PutAsync(Id(1), [1], Evening, Evening.AddMinutes(10), CancellationToken.None);

        await Should.ThrowAsync<SqlException>(() => _store.PutAsync(Id(1), [2], Evening, Evening.AddMinutes(10), CancellationToken.None));

        await Should.ThrowAsync<ArgumentNullException>(() => _store.PutAsync(null!, [1], Evening, Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.PutAsync(Id(3), null!, Evening, Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.TakeAsync(null!, Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.IsWaitingAsync(null!, Evening, CancellationToken.None));
        Should.Throw<ArgumentException>(() => new SqlHandoffStore(" "));
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }

    private static byte[] Id(int n)
    {
        byte[] id = new byte[16];
        BitConverter.TryWriteBytes(id, n);
        return id;
    }
}
