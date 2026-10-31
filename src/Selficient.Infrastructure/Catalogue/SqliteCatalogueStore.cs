using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Selficient.Application.Catalogue;
using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;

namespace Selficient.Infrastructure.Catalogue;

/// <summary>
/// The catalogue in SQLite through Dapper: offers, their checks, the clicks they earn, and when the
/// last run finished. Every statement is parameterised; the schema is created on first use; the
/// file is the site's first database and holds no person in it.
/// </summary>
public sealed class SqliteCatalogueStore : ICatalogueStore, IDisposable
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS offers (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            action TEXT NOT NULL,
            name TEXT NOT NULL,
            retailer TEXT NOT NULL,
            price_pence INTEGER NOT NULL,
            seen TEXT NOT NULL,
            link TEXT NOT NULL,
            note TEXT NOT NULL DEFAULT '',
            status INTEGER NOT NULL DEFAULT 0,
            failures INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS offers_action ON offers(action, status, price_pence);
        CREATE TABLE IF NOT EXISTS checks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            offer_id INTEGER NOT NULL REFERENCES offers(id),
            at TEXT NOT NULL,
            verdict INTEGER NOT NULL,
            status INTEGER NOT NULL,
            evidence TEXT NOT NULL,
            price_pence INTEGER NULL,
            judged_by TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS checks_offer ON checks(offer_id, at);
        CREATE TABLE IF NOT EXISTS clicks (
            offer_id INTEGER NOT NULL REFERENCES offers(id),
            day TEXT NOT NULL,
            count INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (offer_id, day)
        );
        CREATE TABLE IF NOT EXISTS runs (
            name TEXT PRIMARY KEY,
            finished_at TEXT NOT NULL
        );
        """;

    private readonly string _connectionString;
    private readonly Lazy<Task> _schema;

    /// <summary>Creates the store over the file at the path, which is created if absent.</summary>
    public SqliteCatalogueStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
        _schema = new Lazy<Task>(CreateSchemaAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The file.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM offers");
    }

    /// <inheritdoc />
    public async Task SeedAsync(IReadOnlyList<KitPick> picks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(picks);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string now = Stamp(DateTimeOffset.UtcNow);
        foreach (KitPick pick in picks)
        {
            await connection.ExecuteAsync(
                "INSERT INTO offers (action, name, retailer, price_pence, seen, link, note, status, failures, created_at) VALUES (@action, @name, @retailer, @price, @seen, @link, @note, 0, 0, @now)",
                new { action = pick.Action.Value, name = pick.Name, retailer = pick.Retailer, price = pick.Price.Pence, seen = pick.Seen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), link = pick.Link.ToString(), note = pick.Note, now },
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Offer>> ShownAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<Row> rows = await connection.QueryAsync<Row>("SELECT * FROM offers WHERE status IN (0, 1) ORDER BY action, price_pence, id");
        return ToOffers(rows);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Offer>> AllAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<Row> rows = await connection.QueryAsync<Row>("SELECT * FROM offers ORDER BY status, action, price_pence, id");
        return ToOffers(rows);
    }

    /// <inheritdoc />
    public async Task<Offer?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>("SELECT * FROM offers WHERE id = @id", new { id });
        return row is null ? null : ToOffer(row);
    }

    /// <inheritdoc />
    public async Task RecordClickAsync(long id, DateOnly day, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "INSERT INTO clicks (offer_id, day, count) VALUES (@id, @day, 1) ON CONFLICT(offer_id, day) DO UPDATE SET count = count + 1",
            new { id, day = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, long>> ClicksAsync(int days, DateOnly today, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        string since = today.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        IEnumerable<(long OfferId, long Count)> rows = await connection.QueryAsync<(long, long)>("SELECT offer_id, SUM(count) FROM clicks WHERE day > @since GROUP BY offer_id", new { since });
        return rows.ToDictionary(static r => r.OfferId, static r => r.Count);
    }

    /// <inheritdoc />
    public async Task RecordCheckAsync(long id, OfferCheck check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(
            "INSERT INTO checks (offer_id, at, verdict, status, evidence, price_pence, judged_by) VALUES (@id, @at, @verdict, @status, @evidence, @price, @judgedBy)",
            new { id, at = Stamp(check.At), verdict = (int)check.Verdict, status = check.Status, evidence = check.Evidence, price = check.PriceSeen?.Pence, judgedBy = check.JudgedBy },
            transaction);

        // A landing makes the offer live and refreshes what was seen; a robot wall changes nothing;
        // anything else is a failure, and two running hide the offer. A hidden offer stays hidden
        // until a hand or a landing after a hand shows it again.
        switch (check.Verdict)
        {
            case Verdict.Landed:
                await connection.ExecuteAsync(
                    "UPDATE offers SET failures = 0, seen = @seen, price_pence = COALESCE(@price, price_pence), status = CASE WHEN status = 1 THEN 0 ELSE status END WHERE id = @id",
                    new { id, seen = check.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), price = check.PriceSeen?.Pence },
                    transaction);
                break;
            case Verdict.RobotWall:
                break;
            default:
                await connection.ExecuteAsync(
                    "UPDATE offers SET failures = failures + 1, status = CASE WHEN status IN (0, 1) THEN (CASE WHEN failures + 1 >= 2 THEN 2 ELSE 1 END) ELSE status END WHERE id = @id",
                    new { id },
                    transaction);
                break;
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfferCheck>> ChecksAsync(long id, int take, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<CheckRow> rows = await connection.QueryAsync<CheckRow>("SELECT * FROM checks WHERE offer_id = @id ORDER BY at DESC, id DESC LIMIT @take", new { id, take });
        return rows.Select(static r => new OfferCheck(DateTimeOffset.Parse(r.at, CultureInfo.InvariantCulture), (Verdict)(int)r.verdict, (int)r.status, r.evidence, r.price_pence is { } p ? Money.FromPence(p) : null, r.judged_by)).ToList();
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(long id, OfferStatus status, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("UPDATE offers SET status = @status, failures = CASE WHEN @status = 0 THEN 0 ELSE failures END WHERE id = @id", new { id, status = (int)status });
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> LastVerifiedAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        string? at = await connection.ExecuteScalarAsync<string?>("SELECT finished_at FROM runs WHERE name = 'verify'");
        return at is null ? null : DateTimeOffset.Parse(at, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task MarkVerifiedAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("INSERT INTO runs (name, finished_at) VALUES ('verify', @at) ON CONFLICT(name) DO UPDATE SET finished_at = @at", new { at = Stamp(at) });
    }

    /// <inheritdoc />
    public async Task<long?> ProposeAsync(ActionId action, Uri link, string retailer, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(retailer);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        int known = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM offers WHERE link = @link", new { link = link.ToString() }, transaction);
        if (known > 0)
        {
            return null;
        }

        long id = await connection.ExecuteScalarAsync<long>(
            "INSERT INTO offers (action, name, retailer, price_pence, seen, link, note, status, failures, created_at) VALUES (@action, 'Suggested link', @retailer, 0, @seen, @link, 'Suggested by a reader.', 3, 0, @now) RETURNING id",
            new { action = action.Value, retailer, seen = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), link = link.ToString(), now = Stamp(DateTimeOffset.UtcNow) },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Offer>> ProposedAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<Row> rows = await connection.QueryAsync<Row>("SELECT * FROM offers WHERE status = 3 ORDER BY id");
        return ToOffers(rows);
    }

    /// <inheritdoc />
    public async Task DescribeAsync(long id, string name, Money? price, DateOnly seen, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "UPDATE offers SET name = @name, price_pence = COALESCE(@price, price_pence), seen = CASE WHEN @price IS NULL THEN seen ELSE @seen END WHERE id = @id",
            new { id, name, price = price?.Pence, seen = seen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, PriceSpan>> PriceSpansAsync(int days, DateOnly today, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        string since = today.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        IEnumerable<(long OfferId, long Low, long High)> rows = await connection.QueryAsync<(long, long, long)>(
            "SELECT offer_id, MIN(price_pence), MAX(price_pence) FROM checks WHERE price_pence IS NOT NULL AND at > @since GROUP BY offer_id",
            new { since });
        return rows.ToDictionary(static r => r.OfferId, static r => new PriceSpan(Money.FromPence(r.Low), Money.FromPence(r.High)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Connections are pooled per connection string; without this the file stays open after the host is gone.
        using SqliteConnection connection = new(_connectionString);
        SqliteConnection.ClearPool(connection);
    }

    private static string Stamp(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static List<Offer> ToOffers(IEnumerable<Row> rows) => rows.Select(ToOffer).ToList();

    private static Offer ToOffer(Row row) => new(
        row.id,
        ActionId.From(row.action),
        row.name,
        row.retailer,
        Money.FromPence(row.price_pence),
        DateOnly.ParseExact(row.seen, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        new Uri(row.link),
        row.note,
        (OfferStatus)(int)row.status,
        (int)row.failures);

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        // The schema is created once per store, by whichever call gets there first; the rest wait on the same task.
        await _schema.Value;
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task CreateSchemaAsync()
    {
        await using SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(Schema);
    }

#pragma warning disable CA1812, IDE1006, CA1707 // Dapper materialises these by column name.
    private sealed record Row(long id, string action, string name, string retailer, long price_pence, string seen, string link, string note, long status, long failures, string created_at);

    private sealed record CheckRow(long id, long offer_id, string at, long verdict, long status, string evidence, long? price_pence, string judged_by);
#pragma warning restore CA1812, IDE1006, CA1707
}
