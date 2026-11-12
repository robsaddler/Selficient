using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Handoff;

namespace Selficient.Infrastructure.Handoff;

/// <summary>
/// Copies waiting between two devices, in the platform database through Dapper. A row is an id,
/// bytes this server cannot read and two times. Taking a copy deletes it in the statement that
/// reads it, so there is no moment at which two readers can both have it.
/// </summary>
public sealed class SqlHandoffStore : IHandoffStore
{
    private readonly string _connectionString;

    /// <summary>Creates the store over the connection string.</summary>
    public SqlHandoffStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task PutAsync(byte[] id, byte[] ciphertext, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(ciphertext);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "INSERT INTO dbo.handoffs (id, ciphertext, created_at, expires_at) VALUES (@id, @ciphertext, @createdAt, @expiresAt)",
            new { id, ciphertext, createdAt = createdAt.UtcDateTime, expiresAt = expiresAt.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task<byte[]?> TakeAsync(byte[] id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<byte[]>(
            "DELETE FROM dbo.handoffs OUTPUT DELETED.ciphertext WHERE id = @id AND expires_at > @now",
            new { id, now = now.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task<bool> IsWaitingAsync(byte[] id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.handoffs WHERE id = @id AND expires_at > @now",
            new { id, now = now.UtcDateTime }) > 0;
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.handoffs");
    }

    /// <inheritdoc />
    public async Task<int> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync("DELETE FROM dbo.handoffs WHERE expires_at <= @now", new { now = now.UtcDateTime });
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
