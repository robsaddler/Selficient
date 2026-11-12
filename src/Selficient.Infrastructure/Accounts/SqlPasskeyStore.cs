using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Accounts;

namespace Selficient.Infrastructure.Accounts;

/// <summary>
/// Passkeys in the platform database through Dapper. A credential id is unique across every
/// account, held by an index on its hash; adding one that is already kept is answered as nothing
/// by a single statement rather than discovered by an exception.
/// </summary>
public sealed class SqlPasskeyStore : IPasskeyStore
{
    private const string Columns = "id, account_id, credential_id, public_key, sign_count, label, created_at, last_used_at";

    private readonly string _connectionString;

    /// <summary>Creates the store over the connection string.</summary>
    public SqlPasskeyStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Passkey>> ForAccountAsync(long accountId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<Row> rows = await connection.QueryAsync<Row>($"SELECT {Columns} FROM dbo.household_passkeys WHERE account_id = @accountId ORDER BY id", new { accountId });
        return [.. rows.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<Passkey?> FindAsync(byte[] credentialId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentialId);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(
            $"SELECT {Columns} FROM dbo.household_passkeys WHERE credential_hash = HASHBYTES('SHA2_256', @credentialId) AND credential_id = @credentialId",
            new { credentialId });
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<long?> AddAsync(long accountId, VerifiedCredential credential, string label, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<long?>(
            """
            INSERT INTO dbo.household_passkeys (account_id, credential_id, public_key, sign_count, label, created_at)
            OUTPUT INSERTED.id
            SELECT @accountId, @credentialId, @publicKey, @signCount, @label, @now
            WHERE NOT EXISTS (SELECT 1 FROM dbo.household_passkeys WITH (UPDLOCK, HOLDLOCK) WHERE credential_hash = HASHBYTES('SHA2_256', @credentialId))
            """,
            new { accountId, credentialId = credential.CredentialId, publicKey = credential.PublicKey, signCount = credential.SignCount, label = label.Length > 40 ? label[..40] : label, now = now.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task UsedAsync(long id, long signCount, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("UPDATE dbo.household_passkeys SET sign_count = @signCount, last_used_at = @now WHERE id = @id", new { id, signCount, now = now.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(long accountId, long id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync("DELETE FROM dbo.household_passkeys WHERE id = @id AND account_id = @accountId", new { id, accountId }) > 0;
    }

    private static Passkey Map(Row row) => new(
        row.id, row.account_id, row.credential_id, row.public_key, row.sign_count, row.label,
        Utc(row.created_at), row.last_used_at is { } used ? Utc(used) : null);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

#pragma warning disable CA1812, IDE1006, CA1707 // Dapper materialises this by column name.
    private sealed record Row(long id, long account_id, byte[] credential_id, byte[] public_key, long sign_count, string label, DateTime created_at, DateTime? last_used_at);
#pragma warning restore CA1812, IDE1006, CA1707
}
