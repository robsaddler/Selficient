using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Selficient.Infrastructure.Data;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Data;

public sealed class SqlMigratorTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    [Fact]
    public async Task The_shipped_scripts_apply_once_in_order_and_are_recorded()
    {
        SqlMigrator migrator = new(_database.ConnectionString);

        int first = await migrator.MigrateAsync(CancellationToken.None);
        int again = await migrator.MigrateAsync(CancellationToken.None);
        await using SqlConnection connection = new(_database.ConnectionString);
        IEnumerable<(int Version, string Name)> recorded = await connection.QueryAsync<(int, string)>("SELECT version, name FROM dbo.schema_versions ORDER BY version");
        int tables = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name IN ('offers', 'checks', 'clicks', 'runs')");

        first.ShouldBe(SqlMigrator.Shipped.Count);
        again.ShouldBe(0, "a script once applied never runs again");
        recorded.Select(r => r.Version).ShouldBe(SqlMigrator.Shipped.Select(m => m.Version));
        recorded.First().Name.ShouldBe("catalogue");
        tables.ShouldBe(4);
        SqlMigrator.Shipped.Select(m => m.Version).ShouldBeInOrder();
        SqlMigrator.Shipped[0].Version.ShouldBe(1);
    }

    [Fact]
    public void Scripts_split_on_their_go_lines_and_the_migrator_needs_a_connection_string()
    {
        IReadOnlyList<string> batches = SqlMigrator.Batches("CREATE TABLE a (x INT);\nGO\n\n  go  \nCREATE TABLE b (y INT);\nGO\n");

        batches.ShouldBe(["CREATE TABLE a (x INT);", "CREATE TABLE b (y INT);"]);
        Should.Throw<ArgumentException>(() => new SqlMigrator(" "));
    }

    [Fact]
    public async Task A_lock_another_process_holds_is_waited_for_and_then_given_up_on_loudly()
    {
        await using SqlConnection holder = new(_database.ConnectionString);
        await holder.OpenAsync();
        await holder.ExecuteAsync("EXEC sp_getapplock @Resource = @name, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0", new { name = SqlMigrator.LockName });
        SqlMigrator impatient = new(_database.ConnectionString, TimeSpan.FromMilliseconds(300));

        InvalidOperationException refused = await Should.ThrowAsync<InvalidOperationException>(() => impatient.MigrateAsync(CancellationToken.None));
        await holder.ExecuteAsync("EXEC sp_releaseapplock @Resource = @name, @LockOwner = 'Session'", new { name = SqlMigrator.LockName });
        int applied = await impatient.MigrateAsync(CancellationToken.None);

        refused.Message.ShouldBe("The schema migration lock could not be taken within 0 seconds; another process is still migrating.");
        applied.ShouldBe(SqlMigrator.Shipped.Count, "once the other process lets go the run proceeds");
        Should.Throw<ArgumentOutOfRangeException>(() => new SqlMigrator(_database.ConnectionString, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void A_script_not_named_by_the_rule_is_a_build_error_and_other_resources_are_ignored()
    {
        string[] names = [SqlMigrator.ResourcePrefix + "0002_second.sql", SqlMigrator.ResourcePrefix + "0001_first.sql", "Selficient.Infrastructure.Kit.kit-catalogue.json"];

        List<Migration> loaded = SqlMigrator.Load(names, name => "-- " + name);

        loaded.Select(m => (m.Version, m.Name)).ShouldBe([(1, "first"), (2, "second")]);
        loaded[0].Sql.ShouldBe("-- " + SqlMigrator.ResourcePrefix + "0001_first.sql");
        Should.Throw<InvalidOperationException>(() => SqlMigrator.Load([SqlMigrator.ResourcePrefix + "catalogue.sql"], _ => ""))
            .Message.ShouldBe("The migration script catalogue.sql is not named NNNN_what-it-does.sql.");
        Should.Throw<InvalidOperationException>(() => SqlMigrator.Load([SqlMigrator.ResourcePrefix + "001_Catalogue.sql"], _ => ""));
    }

    [Fact]
    public async Task Two_hosts_starting_together_migrate_once_between_them()
    {
        SqlMigrator one = new(_database.ConnectionString);
        SqlMigrator two = new(_database.ConnectionString);

        int[] applied = await Task.WhenAll(one.MigrateAsync(CancellationToken.None), two.MigrateAsync(CancellationToken.None));

        applied.Sum().ShouldBe(SqlMigrator.Shipped.Count, "the application lock serialises them and the second finds nothing to do");
    }

    [Fact]
    public async Task The_start_up_step_migrates_unless_told_not_to_and_logs_what_it_did()
    {
        SqlMigrator migrator = new(_database.ConnectionString);
        ListLogger<MigrationStartup> logger = new();
        MigrationStartup on = new(migrator, Options.Create(new DatabaseOptions { ConnectionString = _database.ConnectionString }), logger);
        MigrationStartup off = new(migrator, Options.Create(new DatabaseOptions { ConnectionString = _database.ConnectionString, MigrateOnStart = false }), logger);

        await off.StartAsync(CancellationToken.None);
        logger.Lines.ShouldBeEmpty();
        await on.StartAsync(CancellationToken.None);
        await on.StopAsync(CancellationToken.None);

        logger.Lines.ShouldBe([$"Information: Database schema is current: {SqlMigrator.Shipped.Count} of {SqlMigrator.Shipped.Count} migrations applied at this start."]);
        Should.Throw<ArgumentNullException>(() => new MigrationStartup(null!, Options.Create(new DatabaseOptions()), logger));
        Should.Throw<ArgumentNullException>(() => new MigrationStartup(migrator, null!, logger));
        Should.Throw<ArgumentNullException>(() => new MigrationStartup(migrator, Options.Create(new DatabaseOptions()), null!));
    }

    public void Dispose() => _database.Dispose();

    /// <summary>A logger that keeps the level and the rendered message of every line.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines => [.. _lines];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _lines.Add($"{logLevel}: {formatter(state, exception)}");
    }
}
