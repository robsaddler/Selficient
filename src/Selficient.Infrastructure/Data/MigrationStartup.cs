using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Selficient.Infrastructure.Data;

/// <summary>
/// Runs the migrations before any other hosted service starts and before the site answers, so a
/// deploy that ships a new script never serves a page against the old schema. Registered first;
/// the host starts hosted services in order.
/// </summary>
public sealed partial class MigrationStartup : IHostedService
{
    private readonly SqlMigrator _migrator;
    private readonly IOptions<DatabaseOptions> _options;
    private readonly ILogger<MigrationStartup> _logger;

    /// <summary>Creates the start-up step.</summary>
    public MigrationStartup(SqlMigrator migrator, IOptions<DatabaseOptions> options, ILogger<MigrationStartup> logger)
    {
        ArgumentNullException.ThrowIfNull(migrator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _migrator = migrator;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.MigrateOnStart)
        {
            return;
        }

        int applied = await _migrator.MigrateAsync(cancellationToken);
        Log.Migrated(_logger, applied, SqlMigrator.Shipped.Count);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is current: {Applied} of {Shipped} migrations applied at this start.")]
        public static partial void Migrated(ILogger logger, int applied, int shipped);
    }
}
