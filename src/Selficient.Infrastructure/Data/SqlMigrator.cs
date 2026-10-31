using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Selficient.Infrastructure.Data;

/// <summary>
/// Brings the database to the current schema from the scripts embedded in this assembly, in order,
/// each in its own transaction, each recorded in <c>schema_versions</c> so it never runs twice. An
/// application lock serialises the run, so two app-pool processes starting together cannot race.
/// Scripts are named <c>NNNN_what-it-does.sql</c>; a script once shipped is never edited, only
/// followed by the next number.
/// </summary>
public sealed partial class SqlMigrator
{
    /// <summary>The prefix every embedded script's resource name carries.</summary>
    public const string ResourcePrefix = "Selficient.Infrastructure.Data.Migrations.";

    /// <summary>The application lock the run takes.</summary>
    public const string LockName = "selficient-schema-migration";

    private readonly string _connectionString;
    private readonly TimeSpan _lockTimeout;

    /// <summary>Creates the migrator over the connection string, waiting up to a minute for another process's run.</summary>
    public SqlMigrator(string connectionString)
        : this(connectionString, TimeSpan.FromMinutes(1))
    {
    }

    /// <summary>Creates the migrator with its own patience for the lock.</summary>
    public SqlMigrator(string connectionString, TimeSpan lockTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(lockTimeout, TimeSpan.Zero);
        _connectionString = connectionString;
        _lockTimeout = lockTimeout;
    }

    /// <summary>The scripts shipped in this build, in the order they apply.</summary>
    public static IReadOnlyList<Migration> Shipped { get; } = Load(
        typeof(SqlMigrator).Assembly.GetManifestResourceNames(),
        static name =>
        {
            using Stream stream = typeof(SqlMigrator).Assembly.GetManifestResourceStream(name)!;
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        });

    /// <summary>Applies every script not yet recorded; returns how many ran.</summary>
    public async Task<int> MigrateAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync("""
            IF OBJECT_ID('dbo.schema_versions', 'U') IS NULL
                CREATE TABLE dbo.schema_versions (
                    version INT NOT NULL CONSTRAINT pk_schema_versions PRIMARY KEY,
                    name NVARCHAR(200) NOT NULL,
                    applied_at DATETIME2(0) NOT NULL
                );
            """);

        int taken = await connection.ExecuteScalarAsync<int>(
            "DECLARE @r INT; EXEC @r = sp_getapplock @Resource = @name, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @timeout; SELECT @r;",
            new { name = LockName, timeout = (int)_lockTimeout.TotalMilliseconds });
        if (taken < 0)
        {
            throw new InvalidOperationException($"The schema migration lock could not be taken within {_lockTimeout.TotalSeconds:0} seconds; another process is still migrating.");
        }

        int applied = 0;
        try
        {
            HashSet<int> done = [.. await connection.QueryAsync<int>("SELECT version FROM dbo.schema_versions")];
            foreach (Migration migration in Shipped)
            {
                if (done.Contains(migration.Version))
                {
                    continue;
                }

                await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
                foreach (string batch in Batches(migration.Sql))
                {
                    await connection.ExecuteAsync(batch, transaction: transaction);
                }

                await connection.ExecuteAsync(
                    "INSERT INTO dbo.schema_versions (version, name, applied_at) VALUES (@version, @name, SYSUTCDATETIME())",
                    new { version = migration.Version, name = migration.Name },
                    transaction);
                await transaction.CommitAsync(cancellationToken);
                applied++;
            }
        }
        finally
        {
            await connection.ExecuteAsync("EXEC sp_releaseapplock @Resource = @name, @LockOwner = 'Session'", new { name = LockName });
        }

        return applied;
    }

    /// <summary>The script split on its <c>GO</c> lines, as the tools do, with empty batches dropped.</summary>
    internal static IReadOnlyList<string> Batches(string sql) =>
        GoLine().Split(sql).Select(static batch => batch.Trim()).Where(static batch => batch.Length > 0).ToList();

    /// <summary>The migrations among the resource names, read and ordered; a name not shaped <c>NNNN_what-it-does.sql</c> is a build error, not a skipped script.</summary>
    internal static List<Migration> Load(IEnumerable<string> resourceNames, Func<string, string> read)
    {
        List<Migration> migrations = [];
        foreach (string resource in resourceNames.Where(static name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            string file = resource[ResourcePrefix.Length..];
            Match match = ScriptName().Match(file);
            if (!match.Success)
            {
                throw new InvalidOperationException($"The migration script {file} is not named NNNN_what-it-does.sql.");
            }

            migrations.Add(new Migration(int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture), match.Groups["name"].Value, read(resource)));
        }

        return migrations;
    }

    [GeneratedRegex(@"^(?<version>\d{4})_(?<name>[a-z0-9-]+)\.sql$")]
    private static partial Regex ScriptName();

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();
}

/// <summary>One embedded script.</summary>
public sealed record Migration(int Version, string Name, string Sql);
