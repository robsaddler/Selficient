using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Operations;

namespace Selficient.Infrastructure.Data;

/// <summary>The operations log in the platform database.</summary>
public sealed class SqlOperationsLog : IOperationsLog
{
    private readonly string _connectionString;
    private readonly TimeProvider _clock;

    /// <summary>Creates the log over the connection string.</summary>
    public SqlOperationsLog(string connectionString, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(clock);
        _connectionString = connectionString;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task RecordHandAsync(string hand, long? offerId, string outcome, string? operatorName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hand);
        ArgumentNullException.ThrowIfNull(outcome);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "INSERT INTO dbo.operator_actions (at, hand, offer_id, outcome, operator_name) VALUES (@at, @hand, @offerId, @outcome, @operatorName)",
            new { at = _clock.GetUtcNow().UtcDateTime, hand = Clip(hand, 40), offerId, outcome = Clip(outcome, 400), operatorName = operatorName is null ? null : Clip(operatorName, 64) });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperatorAction>> RecentHandsAsync(int take, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<HandRow> rows = await connection.QueryAsync<HandRow>("SELECT TOP (@take) at, hand, offer_id, outcome, operator_name FROM dbo.operator_actions ORDER BY id DESC", new { take });
        return [.. rows.Select(static r => new OperatorAction(Utc(r.at), r.hand, r.offer_id, r.outcome, r.operator_name))];
    }

    /// <inheritdoc />
    public async Task<long> StartJobAsync(string job, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(
            "INSERT INTO dbo.job_runs (job, started_at, finished_at, outcome, detail) OUTPUT INSERTED.id VALUES (@job, @at, NULL, @outcome, N'')",
            new { job = Clip(job, 40), at = _clock.GetUtcNow().UtcDateTime, outcome = JobOutcome.Running.ToString() });
    }

    /// <inheritdoc />
    public async Task FinishJobAsync(long id, JobOutcome outcome, string detail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detail);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "UPDATE dbo.job_runs SET finished_at = @at, outcome = @outcome, detail = @detail WHERE id = @id",
            new { id, at = _clock.GetUtcNow().UtcDateTime, outcome = outcome.ToString(), detail = Clip(detail, 400) });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRun>> RecentRunsAsync(int take, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<RunRow> rows = await connection.QueryAsync<RunRow>("SELECT TOP (@take) id, job, started_at, finished_at, outcome, detail FROM dbo.job_runs ORDER BY id DESC", new { take });
        return rows.Select(static r => new JobRun(r.id, r.job, Utc(r.started_at), r.finished_at is { } f ? Utc(f) : null, Enum.Parse<JobOutcome>(r.outcome), r.detail)).ToList();
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string Clip(string text, int most) => text.Length > most ? text[..most] : text;

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

#pragma warning disable CA1812, IDE1006, CA1707 // Dapper materialises these by column name.
    private sealed record HandRow(DateTime at, string hand, long? offer_id, string outcome, string? operator_name);

    private sealed record RunRow(long id, string job, DateTime started_at, DateTime? finished_at, string outcome, string detail);
#pragma warning restore CA1812, IDE1006, CA1707
}
