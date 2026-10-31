using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Security;

namespace Selficient.Infrastructure.Security;

/// <summary>
/// The operators in the platform database, through Dapper. Every secret on a row arrives here
/// already encrypted by the key ring, so nothing in this class can read a seed or a recovery code,
/// and no method on it takes a password.
/// </summary>
public sealed class SqlOperatorStore : IOperatorStore
{
    private const string Columns =
        "id, username, display_name, password_hash, password_set_at, totp_secret, totp_confirmed_at, last_totp_step, recovery_codes, security_stamp, is_enabled, must_change_password, failed_attempts, locked_until, last_signin_at, created_at";

    private readonly string _connectionString;

    /// <summary>Creates the store over the connection string.</summary>
    public SqlOperatorStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.operators");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperatorAccount>> AllAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<Row> rows = await connection.QueryAsync<Row>($"SELECT {Columns} FROM dbo.operators ORDER BY id");
        return [.. rows.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<OperatorAccount?> FindAsync(string username, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>($"SELECT {Columns} FROM dbo.operators WHERE username = @username", new { username });
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<OperatorAccount?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>($"SELECT {Columns} FROM dbo.operators WHERE id = @id", new { id });
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<long> CreateAsync(string username, string displayName, string passwordHash, bool mustChangePassword, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO dbo.operators (username, display_name, password_hash, password_set_at, security_stamp, is_enabled, must_change_password, failed_attempts, created_at)
            OUTPUT INSERTED.id
            VALUES (@username, @displayName, @passwordHash, @now, @stamp, 1, @mustChangePassword, 0, @now)
            """,
            new { username = Clip(username, 64), displayName = Clip(displayName, 128), passwordHash, now = now.UtcDateTime, stamp = Guid.NewGuid(), mustChangePassword });
    }

    /// <inheritdoc />
    public async Task SetPasswordAsync(long id, string passwordHash, Guid stamp, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            UPDATE dbo.operators
            SET password_hash = @passwordHash, password_set_at = @now, security_stamp = @stamp,
                must_change_password = 0, failed_attempts = 0, locked_until = NULL
            WHERE id = @id
            """,
            new { id, passwordHash, now = now.UtcDateTime, stamp });
    }

    /// <inheritdoc />
    public async Task SetSecondFactorAsync(long id, byte[] totpSecretProtected, DateTimeOffset confirmedAt, byte[] recoveryCodesProtected, Guid stamp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(totpSecretProtected);
        ArgumentNullException.ThrowIfNull(recoveryCodesProtected);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            UPDATE dbo.operators
            SET totp_secret = @secret, totp_confirmed_at = @confirmedAt, recovery_codes = @codes, security_stamp = @stamp, last_totp_step = 0
            WHERE id = @id
            """,
            new { id, secret = totpSecretProtected, confirmedAt = confirmedAt.UtcDateTime, codes = recoveryCodesProtected, stamp });
    }

    /// <inheritdoc />
    public async Task SetRecoveryCodesAsync(long id, byte[] recoveryCodesProtected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodesProtected);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("UPDATE dbo.operators SET recovery_codes = @codes WHERE id = @id", new { id, codes = recoveryCodesProtected });
    }

    /// <inheritdoc />
    public async Task SetLastTotpStepAsync(long id, long lastStep, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("UPDATE dbo.operators SET last_totp_step = @lastStep WHERE id = @id", new { id, lastStep });
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(long id, bool enabled, Guid stamp, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("UPDATE dbo.operators SET is_enabled = @enabled, security_stamp = @stamp WHERE id = @id", new { id, enabled, stamp });
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(long id, int failedAttempts, DateTimeOffset? lockedUntil, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "UPDATE dbo.operators SET failed_attempts = @failedAttempts, locked_until = @lockedUntil WHERE id = @id",
            new { id, failedAttempts, lockedUntil = lockedUntil?.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task RecordSuccessAsync(long id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "UPDATE dbo.operators SET failed_attempts = 0, locked_until = NULL, last_signin_at = @now WHERE id = @id",
            new { id, now = now.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task RecordAttemptAsync(SignInAttempt attempt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "INSERT INTO dbo.operator_signin_attempts (at, username, outcome, detail) VALUES (@at, @username, @outcome, @detail)",
            new { at = attempt.At.UtcDateTime, username = Clip(attempt.Username, 64), outcome = Clip(attempt.Outcome, 32), detail = Clip(attempt.Detail, 400) });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SignInAttempt>> AttemptsAsync(int take, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<AttemptRow> rows = await connection.QueryAsync<AttemptRow>(
            "SELECT TOP (@take) at, username, outcome, detail FROM dbo.operator_signin_attempts ORDER BY id DESC", new { take });
        return [.. rows.Select(static r => new SignInAttempt(Utc(r.at), r.username, r.outcome, r.detail))];
    }

    private static OperatorAccount Map(Row row) => new(
        row.id,
        row.username,
        row.display_name,
        row.password_hash,
        Utc(row.password_set_at),
        row.totp_secret,
        row.totp_confirmed_at is { } confirmed ? Utc(confirmed) : null,
        row.last_totp_step,
        row.recovery_codes,
        row.security_stamp,
        row.is_enabled,
        row.must_change_password,
        row.failed_attempts,
        row.locked_until is { } locked ? Utc(locked) : null,
        row.last_signin_at is { } signedIn ? Utc(signedIn) : null,
        Utc(row.created_at));

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string Clip(string text, int most) => text.Length > most ? text[..most] : text;

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

#pragma warning disable CA1812, IDE1006, CA1707 // Dapper materialises these by column name.
    private sealed record Row(
        long id,
        string username,
        string display_name,
        string password_hash,
        DateTime password_set_at,
        byte[]? totp_secret,
        DateTime? totp_confirmed_at,
        long last_totp_step,
        byte[]? recovery_codes,
        Guid security_stamp,
        bool is_enabled,
        bool must_change_password,
        int failed_attempts,
        DateTime? locked_until,
        DateTime? last_signin_at,
        DateTime created_at);

    private sealed record AttemptRow(DateTime at, string username, string outcome, string detail);
#pragma warning restore CA1812, IDE1006, CA1707
}
