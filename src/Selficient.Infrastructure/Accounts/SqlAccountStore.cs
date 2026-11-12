using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Accounts;

namespace Selficient.Infrastructure.Accounts;

/// <summary>
/// Household accounts and their pending sign-ins, in the platform database through Dapper. The
/// statements that matter are single statements: making an account takes a range lock so two
/// first sign-ins make one row, and trying a code increments its counter and reads its hash in one
/// go, so tries that race cannot share a number.
/// </summary>
public sealed class SqlAccountStore : IAccountStore
{
    private const string Columns = "id, email, security_stamp, created_at, last_seen_at";

    private readonly string _connectionString;

    /// <summary>Creates the store over the connection string.</summary>
    public SqlAccountStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<HouseholdAccount?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>($"SELECT {Columns} FROM dbo.household_accounts WHERE id = @id", new { id });
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<HouseholdAccount> FindOrCreateAsync(string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        Row row = await connection.QuerySingleAsync<Row>(
            $"""
            INSERT INTO dbo.household_accounts (email, security_stamp, created_at, last_seen_at)
            SELECT @email, @stamp, @now, @now
            WHERE NOT EXISTS (SELECT 1 FROM dbo.household_accounts WITH (UPDLOCK, HOLDLOCK) WHERE email = @email);
            SELECT {Columns} FROM dbo.household_accounts WHERE email = @email;
            """,
            new { email, stamp = Guid.NewGuid(), now = now.UtcDateTime });
        return Map(row);
    }

    /// <inheritdoc />
    public async Task TouchAsync(long id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("UPDATE dbo.household_accounts SET last_seen_at = @now WHERE id = @id", new { id, now = now.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync("DELETE FROM dbo.household_accounts WHERE id = @id", new { id }) > 0;
    }

    /// <inheritdoc />
    public async Task<int> CountCodesAsync(byte[] emailHash, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emailHash);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.household_signin_codes WHERE email_hash = @emailHash AND created_at > @since",
            new { emailHash, since = since.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task AddCodeAsync(SignInCode code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.household_signin_codes (email_hash, binding_hash, code_hash, created_at, expires_at)
            VALUES (@emailHash, @bindingHash, @codeHash, @createdAt, @expiresAt)
            """,
            new { emailHash = code.EmailHash, bindingHash = code.BindingHash, codeHash = code.CodeHash, createdAt = code.CreatedAt.UtcDateTime, expiresAt = code.ExpiresAt.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task<CodeAttempt?> AttemptCodeAsync(byte[] bindingHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindingHash);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        AttemptRow? row = await connection.QuerySingleOrDefaultAsync<AttemptRow>(
            """
            UPDATE dbo.household_signin_codes
            SET attempts = attempts + 1
            OUTPUT INSERTED.id, INSERTED.attempts, INSERTED.code_hash
            WHERE binding_hash = @bindingHash AND used_at IS NULL AND expires_at > @now
            """,
            new { bindingHash, now = now.UtcDateTime });
        return row is null ? null : new CodeAttempt(row.id, row.attempts, row.code_hash);
    }

    /// <inheritdoc />
    public async Task<bool> SpendCodeAsync(long id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(
            "UPDATE dbo.household_signin_codes SET used_at = @now WHERE id = @id AND used_at IS NULL",
            new { id, now = now.UtcDateTime }) > 0;
    }

    /// <inheritdoc />
    public async Task<int> SweepCodesAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync("DELETE FROM dbo.household_signin_codes WHERE created_at <= @before", new { before = before.UtcDateTime });
    }

    private static HouseholdAccount Map(Row row) => new(row.id, row.email, row.security_stamp, Utc(row.created_at), Utc(row.last_seen_at));

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

#pragma warning disable CA1812, IDE1006, CA1707 // Dapper materialises these by column name.
    private sealed record Row(long id, string email, Guid security_stamp, DateTime created_at, DateTime last_seen_at);

    private sealed record AttemptRow(long id, int attempts, byte[] code_hash);
#pragma warning restore CA1812, IDE1006, CA1707
}
