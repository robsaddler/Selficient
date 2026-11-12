using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Accounts;

namespace Selficient.Infrastructure.Accounts;

/// <summary>
/// An account's copy of its plan, in the platform database through Dapper: one row an account,
/// ciphertext this class cannot read. A write is one statement that only lands on the version the
/// writer named, so two devices writing at once make one winner and one that is told to look again.
/// </summary>
public sealed class SqlPlanStore : IPlanStore
{
    private readonly string _connectionString;

    /// <summary>Creates the store over the connection string.</summary>
    public SqlPlanStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<StoredPlan?> GetAsync(long accountId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(
            "SELECT snapshot, version, updated_at FROM dbo.household_plans WHERE account_id = @accountId", new { accountId });
        return row is null ? null : new StoredPlan(row.snapshot, row.version, new DateTimeOffset(DateTime.SpecifyKind(row.updated_at, DateTimeKind.Utc)));
    }

    /// <inheritdoc />
    public async Task<long?> TryPutAsync(long accountId, byte[] protectedPlan, long baseVersion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protectedPlan);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        // The first copy is an insert that only lands when there is none; every later one is an
        // update that only lands on the version named. Either way one statement decides.
        string sql = baseVersion == 0
            ? """
              INSERT INTO dbo.household_plans (account_id, snapshot, version, updated_at)
              SELECT @accountId, @protectedPlan, 1, @now
              WHERE NOT EXISTS (SELECT 1 FROM dbo.household_plans WITH (UPDLOCK, HOLDLOCK) WHERE account_id = @accountId)
              """
            : """
              UPDATE dbo.household_plans
              SET snapshot = @protectedPlan, version = version + 1, updated_at = @now
              WHERE account_id = @accountId AND version = @baseVersion
              """;
        int written = await connection.ExecuteAsync(sql, new { accountId, protectedPlan, baseVersion, now = now.UtcDateTime });
        return written > 0 ? baseVersion + 1 : null;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

#pragma warning disable CA1812, IDE1006, CA1707 // Dapper materialises this by column name.
    private sealed record Row(byte[] snapshot, long version, DateTime updated_at);
#pragma warning restore CA1812, IDE1006, CA1707
}
