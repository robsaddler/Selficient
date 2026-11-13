using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Figures;

namespace Selficient.Infrastructure.Figures;

/// <summary>
/// The published figures, the draft in hand and the record of every change, in SQL Server. A set's
/// corrections are kept as one JSON object beside the set, so a set is read in one row; the changes
/// table is the same information figure by figure, for the question "when did this change and why".
/// </summary>
public sealed class SqlFigureStore : IFigureStore
{
    private const int PrimaryKeyViolation = 2627;
    private readonly string _connectionString;

    /// <summary>Creates the store over the connection string.</summary>
    public SqlFigureStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<PublishedFigures?> CurrentAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        SetRow? row = await connection.QuerySingleOrDefaultAsync<SetRow>("SELECT TOP (1) version, published_at, published_by, note, restores, corrections FROM dbo.figure_sets ORDER BY version DESC");
        return row is null ? null : ToSet(row);
    }

    /// <inheritdoc />
    public async Task<PublishedFigures?> FindAsync(int version, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        SetRow? row = await connection.QuerySingleOrDefaultAsync<SetRow>("SELECT version, published_at, published_by, note, restores, corrections FROM dbo.figure_sets WHERE version = @version", new { version });
        return row is null ? null : ToSet(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PublishedFigures>> HistoryAsync(int take, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<SetRow> rows = await connection.QueryAsync<SetRow>("SELECT TOP (@take) version, published_at, published_by, note, restores, corrections FROM dbo.figure_sets ORDER BY version DESC", new { take });
        return [.. rows.Select(ToSet)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FigureChange>> ChangesAsync(string parameterId, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameterId);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<ChangeRow> rows = await connection.QueryAsync<ChangeRow>(
            "SELECT TOP (@take) version, parameter_id, value_before, value_after, reason, source, drafted_by, drafted_at FROM dbo.figure_changes WHERE parameter_id = @parameterId ORDER BY version DESC, id DESC",
            new { take, parameterId });
        return [.. rows.Select(static r => new FigureChange(r.version, r.parameter_id, r.value_before, r.value_after, r.reason, r.source, r.drafted_by, Utc(r.drafted_at)))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FigureDraft>> DraftsAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<DraftRow> rows = await connection.QueryAsync<DraftRow>("SELECT parameter_id, value, reason, source, drafted_by, drafted_at FROM dbo.figure_drafts ORDER BY parameter_id");
        return [.. rows.Select(static r => new FigureDraft(r.parameter_id, r.value, r.reason, r.source, r.drafted_by, Utc(r.drafted_at)))];
    }

    /// <inheritdoc />
    public async Task SaveDraftAsync(FigureDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            MERGE dbo.figure_drafts WITH (HOLDLOCK) AS held
            USING (SELECT @id AS parameter_id) AS given ON held.parameter_id = given.parameter_id
            WHEN MATCHED THEN UPDATE SET value = @value, reason = @reason, source = @source, drafted_by = @by, drafted_at = @at
            WHEN NOT MATCHED THEN INSERT (parameter_id, value, reason, source, drafted_by, drafted_at) VALUES (@id, @value, @reason, @source, @by, @at);
            """,
            new { id = draft.ParameterId, value = draft.Value, reason = draft.Reason, source = draft.Source, by = draft.DraftedBy, at = draft.DraftedAt.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task<bool> DiscardDraftAsync(string parameterId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameterId);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync("DELETE FROM dbo.figure_drafts WHERE parameter_id = @parameterId", new { parameterId }) > 0;
    }

    /// <inheritdoc />
    public async Task<bool> PublishAsync(PublishedFigures published, IReadOnlyList<FigureChange> changes, bool clearDrafts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(changes);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        bool taken = false;
        try
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.figure_sets (version, published_at, published_by, note, restores, corrections) VALUES (@version, @at, @by, @note, @restores, @corrections)",
                new
                {
                    version = published.Version,
                    at = published.At.UtcDateTime,
                    by = published.By,
                    note = published.Note,
                    restores = published.Restores,
                    corrections = JsonSerializer.Serialize(new Dictionary<string, decimal>(published.Corrections), FigureJson.Default.DictionaryStringDecimal),
                },
                transaction);
        }
        catch (SqlException exception) when (exception.Number == PrimaryKeyViolation)
        {
            // The number is taken: somebody else published first. Nothing of this publication is kept.
            taken = true;
        }

        if (taken)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        foreach (FigureChange change in changes)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.figure_changes (version, parameter_id, value_before, value_after, reason, source, drafted_by, drafted_at) VALUES (@version, @id, @before, @after, @reason, @source, @by, @at)",
                new { version = change.Version, id = change.ParameterId, before = change.Before, after = change.After, reason = change.Reason, source = change.Source, by = change.DraftedBy, at = change.DraftedAt.UtcDateTime },
                transaction);
        }

        if (clearDrafts)
        {
            await connection.ExecuteAsync("DELETE FROM dbo.figure_drafts", transaction: transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> MayPublishAsync(string operatorName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operatorName);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.operators AS o
            WHERE o.username = @operatorName AND o.is_enabled = 1
              AND (EXISTS (SELECT 1 FROM dbo.figure_publishers AS p WHERE p.operator_id = o.id)
                   OR (NOT EXISTS (SELECT 1 FROM dbo.figure_publishers) AND o.id = (SELECT MIN(id) FROM dbo.operators)))
            """,
            new { operatorName }) > 0;
    }

    private static PublishedFigures ToSet(SetRow row) => new(
        row.version,
        Utc(row.published_at),
        row.published_by,
        row.note,
        row.restores,
        JsonSerializer.Deserialize(row.corrections, FigureJson.Default.DictionaryStringDecimal)!);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

#pragma warning disable CA1812, IDE1006, CA1707 // Dapper materialises these by column name.
    private sealed record SetRow(int version, DateTime published_at, string published_by, string note, int? restores, string corrections);

    private sealed record ChangeRow(int version, string parameter_id, decimal value_before, decimal value_after, string reason, string source, string drafted_by, DateTime drafted_at);

    private sealed record DraftRow(string parameter_id, decimal value, string reason, string source, string drafted_by, DateTime drafted_at);
#pragma warning restore CA1812, IDE1006, CA1707
}

/// <summary>Source-generated serializer context for a set's corrections.</summary>
[JsonSerializable(typeof(Dictionary<string, decimal>))]
public sealed partial class FigureJson : JsonSerializerContext;

/// <summary>Reads the figures in force when the host starts, after the schema has been brought up to date.</summary>
public sealed class FiguresStartup : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly CurrentFigures _figures;

    /// <summary>Creates the service.</summary>
    public FiguresStartup(CurrentFigures figures)
    {
        ArgumentNullException.ThrowIfNull(figures);
        _figures = figures;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => _figures.ReloadAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
