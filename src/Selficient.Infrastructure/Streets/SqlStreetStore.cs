using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Streets;

namespace Selficient.Infrastructure.Streets;

/// <summary>
/// Street groups in the platform database through Dapper. Every statement that depends on a count
/// (a group that may be full, an account in too many groups) takes its locks and counts inside one
/// transaction, so two homes joining at once cannot both take the last place. Who may remove is
/// whoever has the lowest sequence number among a group's members, which is whoever joined first.
/// </summary>
public sealed class SqlStreetStore : IStreetStore
{
    private readonly string _connectionString;

    /// <summary>Creates the store over the connection string.</summary>
    public SqlStreetStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<bool> CreateAsync(long accountId, Guid groupId, Guid memberId, byte[] passHash, string about, string record, DateTimeOffset now, int mostGroups, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passHash);
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(record);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        int groups = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.street_members WITH (UPDLOCK, HOLDLOCK) WHERE account_id = @accountId", new { accountId }, transaction);
        bool made = groups < mostGroups;
        if (made)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.street_groups (id, created_at, pass_hash, about) VALUES (@groupId, @now, @passHash, @about);
                INSERT INTO dbo.street_members (group_id, account_id, member_id, joined_at, updated_at, record) VALUES (@groupId, @accountId, @memberId, @now, @now, @record);
                """,
                new { groupId, accountId, memberId, passHash, about, record, now = now.UtcDateTime },
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return made;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StreetMembership>> MembershipsAsync(long accountId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<MembershipRow> rows = await connection.QueryAsync<MembershipRow>(
            """
            SELECT m.group_id, m.member_id,
                   CAST(CASE WHEN m.seq = (SELECT MIN(o.seq) FROM dbo.street_members AS o WHERE o.group_id = m.group_id) THEN 1 ELSE 0 END AS BIT) AS may_remove
            FROM dbo.street_members AS m
            WHERE m.account_id = @accountId
            ORDER BY m.seq
            """,
            new { accountId });
        return [.. rows.Select(static row => new StreetMembership(row.group_id, row.member_id, row.may_remove))];
    }

    /// <inheritdoc />
    public async Task<StreetGroup?> ReadAsync(long accountId, Guid groupId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        // One statement decides membership and reads the group, so a stranger is told nothing by the time it takes.
        IEnumerable<MemberRow> rows = await connection.QueryAsync<MemberRow>(
            """
            SELECT m.member_id, m.record, m.updated_at, m.account_id, g.about
            FROM dbo.street_members AS m
            JOIN dbo.street_groups AS g ON g.id = m.group_id
            WHERE m.group_id = @groupId
              AND EXISTS (SELECT 1 FROM dbo.street_members AS me WHERE me.group_id = @groupId AND me.account_id = @accountId)
            ORDER BY m.seq
            """,
            new { groupId, accountId });
        List<MemberRow> members = [.. rows];
        if (members.Count == 0)
        {
            return null;
        }

        return new StreetGroup(
            groupId,
            members[0].about,
            members.Single(row => row.account_id == accountId).member_id,
            [.. members.Select(static (row, at) => new StreetMember(row.member_id, row.record, Utc(row.updated_at), at == 0))]);
    }

    /// <inheritdoc />
    public async Task<byte[]?> PassHashAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<byte[]>("SELECT pass_hash FROM dbo.street_groups WHERE id = @groupId", new { groupId });
    }

    /// <inheritdoc />
    public async Task<JoinOutcome> JoinAsync(long accountId, Guid groupId, Guid memberId, string record, DateTimeOffset now, int mostMembers, int mostGroups, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        Counts counts = await connection.QuerySingleAsync<Counts>(
            """
            SELECT
                (SELECT COUNT(*) FROM dbo.street_members WITH (UPDLOCK, HOLDLOCK) WHERE group_id = @groupId) AS members,
                (SELECT COUNT(*) FROM dbo.street_members WITH (UPDLOCK, HOLDLOCK) WHERE account_id = @accountId) AS groups,
                (SELECT COUNT(*) FROM dbo.street_members WHERE group_id = @groupId AND account_id = @accountId) AS already
            """,
            new { groupId, accountId },
            transaction);
        JoinOutcome outcome = Outcome(counts, mostMembers, mostGroups);
        if (outcome == JoinOutcome.Joined)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.street_members (group_id, account_id, member_id, joined_at, updated_at, record) VALUES (@groupId, @accountId, @memberId, @now, @now, @record)",
                new { groupId, accountId, memberId, record, now = now.UtcDateTime },
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    /// <inheritdoc />
    public async Task<bool> PutRecordAsync(long accountId, Guid groupId, string record, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(
            "UPDATE dbo.street_members SET record = @record, updated_at = @now WHERE group_id = @groupId AND account_id = @accountId",
            new { groupId, accountId, record, now = now.UtcDateTime }) > 0;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(long accountId, Guid groupId, Guid memberId, byte[] newPassHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newPassHash);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // Only the longest-standing member removes, and never themselves; the invitation's pass changes with the removal or not at all.
        int removed = await connection.ExecuteAsync(
            """
            DELETE m FROM dbo.street_members AS m
            WHERE m.group_id = @groupId AND m.member_id = @memberId AND m.account_id <> @accountId
              AND @accountId = (SELECT TOP (1) first.account_id FROM dbo.street_members AS first WITH (UPDLOCK, HOLDLOCK) WHERE first.group_id = @groupId ORDER BY first.seq)
            """,
            new { groupId, memberId, accountId },
            transaction);
        if (removed > 0)
        {
            await connection.ExecuteAsync("UPDATE dbo.street_groups SET pass_hash = @newPassHash WHERE id = @groupId", new { groupId, newPassHash }, transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return removed > 0;
    }

    /// <inheritdoc />
    public async Task<bool> LeaveAsync(long accountId, Guid groupId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        int left = await connection.ExecuteAsync(
            """
            DELETE FROM dbo.street_members WHERE group_id = @groupId AND account_id = @accountId;
            DELETE g FROM dbo.street_groups AS g WHERE g.id = @groupId AND @@ROWCOUNT > 0 AND NOT EXISTS (SELECT 1 FROM dbo.street_members AS m WHERE m.group_id = g.id);
            """,
            new { groupId, accountId },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return left > 0;
    }

    /// <inheritdoc />
    public async Task<int> SweepEmptyAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync("DELETE g FROM dbo.street_groups AS g WHERE NOT EXISTS (SELECT 1 FROM dbo.street_members AS m WHERE m.group_id = g.id)");
    }

    // The order matters: being in already is not a refusal, and a full group is said before the account's own limit.
    private static JoinOutcome Outcome(Counts counts, int mostMembers, int mostGroups)
    {
        if (counts.already > 0)
        {
            return JoinOutcome.AlreadyIn;
        }

        if (counts.members >= mostMembers)
        {
            return JoinOutcome.Full;
        }

        return counts.groups >= mostGroups ? JoinOutcome.TooManyGroups : JoinOutcome.Joined;
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

#pragma warning disable CA1812, IDE1006, CA1707 // Dapper materialises these by column name.
    private sealed record MembershipRow(Guid group_id, Guid member_id, bool may_remove);

    private sealed record MemberRow(Guid member_id, string record, DateTime updated_at, long account_id, string about);

    private sealed record Counts(int members, int groups, int already);
#pragma warning restore CA1812, IDE1006, CA1707
}
