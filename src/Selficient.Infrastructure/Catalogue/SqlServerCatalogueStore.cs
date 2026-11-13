using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Catalogue;
using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;

namespace Selficient.Infrastructure.Catalogue;

/// <summary>
/// The catalogue in SQL Server through Dapper: offers, their checks, the clicks they earn, and
/// when the last run finished. Every statement is parameterised; the schema comes from the
/// embedded migrations; nothing in it is a person's.
/// </summary>
public sealed class SqlServerCatalogueStore : ICatalogueStore
{
    private readonly string _connectionString;

    /// <summary>Creates the store over the connection string.</summary>
    public SqlServerCatalogueStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task SeedAsync(IReadOnlyList<KitPick> picks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(picks);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (KitPick pick in picks)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.offers (action, name, retailer, price_pence, seen, link, note, status, failures, created_at) VALUES (@action, @name, @retailer, @price, @seen, @link, @note, 0, 0, SYSUTCDATETIME())",
                new { action = pick.Action.Value, name = pick.Name, retailer = pick.Retailer, price = pick.Price.Pence, seen = Day(pick.Seen), link = pick.Link.ToString(), note = pick.Note },
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReviseAsync(long id, KitPick pick, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pick);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "UPDATE dbo.offers SET name = @name, retailer = @retailer, price_pence = @price, seen = @seen, note = @note WHERE id = @id",
            new { id, name = pick.Name, retailer = pick.Retailer, price = pick.Price.Pence, seen = Day(pick.Seen), note = pick.Note });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Offer>> ShownAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return ToOffers(await connection.QueryAsync<Row>("SELECT * FROM dbo.offers WHERE status IN (0, 1) ORDER BY action, price_pence, id"));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Offer>> AllAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return ToOffers(await connection.QueryAsync<Row>("SELECT * FROM dbo.offers ORDER BY status, action, price_pence, id"));
    }

    /// <inheritdoc />
    public async Task<Offer?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>("SELECT * FROM dbo.offers WHERE id = @id", new { id });
        return row is null ? null : ToOffer(row);
    }

    /// <inheritdoc />
    public async Task RecordClicksAsync(IReadOnlyList<ClickBatch> batches, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batches);
        if (batches.Count == 0)
        {
            return;
        }

        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (ClickBatch batch in batches)
        {
            await connection.ExecuteAsync("""
                UPDATE dbo.clicks WITH (UPDLOCK, HOLDLOCK) SET count = count + @count WHERE offer_id = @id AND day = @day;
                IF @@ROWCOUNT = 0 INSERT INTO dbo.clicks (offer_id, day, count) VALUES (@id, @day, @count);
                """,
                new { id = batch.OfferId, day = Day(batch.Day), count = batch.Count },
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, long>> ClicksAsync(int days, DateOnly today, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<(long OfferId, long Count)> rows = await connection.QueryAsync<(long, long)>(
            "SELECT offer_id, SUM(count) FROM dbo.clicks WHERE day > @since GROUP BY offer_id",
            new { since = Day(today.AddDays(-days)) });
        return rows.ToDictionary(static r => r.OfferId, static r => r.Count);
    }

    /// <inheritdoc />
    public async Task RecordCheckAsync(long id, OfferCheck check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(
            "INSERT INTO dbo.checks (offer_id, at, verdict, status, evidence, price_pence, judged_by) VALUES (@id, @at, @verdict, @status, @evidence, @price, @judgedBy)",
            new { id, at = check.At.UtcDateTime, verdict = (int)check.Verdict, status = check.Status, evidence = Clip(check.Evidence, 400), price = check.PriceSeen?.Pence, judgedBy = check.JudgedBy },
            transaction);

        // A landing makes the offer live and refreshes what was seen; a robot wall changes nothing;
        // anything else is a failure, and two running hide the offer. A hidden offer stays hidden
        // until a hand or a landing after a hand shows it again.
        switch (check.Verdict)
        {
            case Verdict.Landed:
                await connection.ExecuteAsync(
                    "UPDATE dbo.offers SET failures = 0, seen = @seen, price_pence = COALESCE(@price, price_pence), status = CASE WHEN status = 1 THEN 0 ELSE status END WHERE id = @id",
                    new { id, seen = check.At.UtcDateTime.Date, price = check.PriceSeen?.Pence },
                    transaction);
                break;
            case Verdict.RobotWall:
                break;
            default:
                await connection.ExecuteAsync(
                    "UPDATE dbo.offers SET failures = failures + 1, status = CASE WHEN status IN (0, 1) THEN (CASE WHEN failures + 1 >= 2 THEN 2 ELSE 1 END) ELSE status END WHERE id = @id",
                    new { id },
                    transaction);
                break;
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfferCheck>> ChecksAsync(long id, int take, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<CheckRow> rows = await connection.QueryAsync<CheckRow>("SELECT TOP (@take) * FROM dbo.checks WHERE offer_id = @id ORDER BY at DESC, id DESC", new { id, take });
        return rows.Select(static r => new OfferCheck(new DateTimeOffset(DateTime.SpecifyKind(r.at, DateTimeKind.Utc)), (Verdict)r.verdict, r.status, r.evidence, r.price_pence is { } p ? Money.FromPence(p) : null, r.judged_by)).ToList();
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(long id, OfferStatus status, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("UPDATE dbo.offers SET status = @status, failures = CASE WHEN @status = 0 THEN 0 ELSE failures END WHERE id = @id", new { id, status = (int)status });
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> LastVerifiedAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        DateTime? at = await connection.ExecuteScalarAsync<DateTime?>("SELECT finished_at FROM dbo.runs WHERE name = 'verify'");
        return at is { } when ? new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Utc)) : null;
    }

    /// <inheritdoc />
    public async Task MarkVerifiedAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("""
            BEGIN TRANSACTION;
            UPDATE dbo.runs WITH (UPDLOCK, HOLDLOCK) SET finished_at = @at WHERE name = 'verify';
            IF @@ROWCOUNT = 0 INSERT INTO dbo.runs (name, finished_at) VALUES ('verify', @at);
            COMMIT TRANSACTION;
            """,
            new { at = at.UtcDateTime });
    }

    /// <inheritdoc />
    public async Task<long?> ProposeAsync(ActionId action, Uri link, string retailer, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(retailer);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        int known = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.offers WITH (UPDLOCK, HOLDLOCK) WHERE link = @link", new { link = link.ToString() }, transaction);
        if (known > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        long id = await connection.ExecuteScalarAsync<long>(
            "INSERT INTO dbo.offers (action, name, retailer, price_pence, seen, link, note, status, failures, created_at) OUTPUT INSERTED.id VALUES (@action, N'Suggested link', @retailer, 0, @seen, @link, N'Suggested by a reader.', 3, 0, SYSUTCDATETIME())",
            new { action = action.Value, retailer = Clip(retailer, 160), seen = Day(today), link = link.ToString() },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Offer>> ProposedAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return ToOffers(await connection.QueryAsync<Row>("SELECT * FROM dbo.offers WHERE status = 3 ORDER BY id"));
    }

    /// <inheritdoc />
    public async Task DescribeAsync(long id, string name, Money? price, DateOnly seen, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "UPDATE dbo.offers SET name = @name, price_pence = COALESCE(@price, price_pence), seen = CASE WHEN @price IS NULL THEN seen ELSE @seen END WHERE id = @id",
            new { id, name = Clip(name, 160), price = price?.Pence, seen = Day(seen) });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, PriceSpan>> PriceSpansAsync(int days, DateOnly today, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<(long OfferId, long Low, long High)> rows = await connection.QueryAsync<(long, long, long)>(
            "SELECT offer_id, MIN(price_pence), MAX(price_pence) FROM dbo.checks WHERE price_pence IS NOT NULL AND at > @since GROUP BY offer_id",
            new { since = Day(today.AddDays(-days)) });
        return rows.ToDictionary(static r => r.OfferId, static r => new PriceSpan(Money.FromPence(r.Low), Money.FromPence(r.High)));
    }

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static string Clip(string text, int most) => text.Length > most ? text[..most] : text;

    private static List<Offer> ToOffers(IEnumerable<Row> rows) => rows.Select(ToOffer).ToList();

    private static Offer ToOffer(Row row) => new(
        row.id,
        ActionId.From(row.action),
        row.name,
        row.retailer,
        Money.FromPence(row.price_pence),
        DateOnly.FromDateTime(row.seen),
        new Uri(row.link),
        row.note,
        (OfferStatus)row.status,
        row.failures);

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

#pragma warning disable CA1812, IDE1006, CA1707 // Dapper materialises these by column name.
    private sealed record Row(long id, string action, string name, string retailer, long price_pence, DateTime seen, string link, string note, int status, int failures, DateTime created_at);

    private sealed record CheckRow(long id, long offer_id, DateTime at, int verdict, int status, string evidence, long? price_pence, string judged_by);
#pragma warning restore CA1812, IDE1006, CA1707
}
