using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Selficient.Application.Catalogue;
using Selficient.Application.Maps;

namespace Selficient.Infrastructure.Health;

/// <summary>The database answers a query within the probe's patience.</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    /// <summary>Creates the check.</summary>
    public DatabaseHealthCheck(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        int versions;
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            versions = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.schema_versions");
        }
        catch (SqlException exception)
        {
            return HealthCheckResult.Unhealthy("The database did not answer.", exception);
        }
        catch (ArgumentException exception)
        {
            return HealthCheckResult.Unhealthy("The database connection string is not valid.", exception);
        }

        return HealthCheckResult.Healthy($"The database answers and holds {versions} migrations.");
    }
}

/// <summary>The catalogue the pages read has been loaded from the store at least once.</summary>
public sealed class CatalogueHealthCheck : IHealthCheck
{
    private readonly StoredCatalogue _catalogue;

    /// <summary>Creates the check.</summary>
    public CatalogueHealthCheck(StoredCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        _catalogue = catalogue;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalogue.IsLoaded
            ? HealthCheckResult.Healthy($"The catalogue is loaded with {_catalogue.All.Count} picks.")
            : HealthCheckResult.Degraded("The catalogue has not been read from the store yet; the shipped picks stand in."));
}

/// <summary>The basemap archive is present, which the map needs and the site survives without.</summary>
public sealed class BasemapHealthCheck : IHealthCheck
{
    private readonly IBasemapTiles _tiles;

    /// <summary>Creates the check.</summary>
    public BasemapHealthCheck(IBasemapTiles tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        _tiles = tiles;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tiles.Status().Available
            ? HealthCheckResult.Healthy("The basemap is present.")
            : HealthCheckResult.Degraded("The basemap archive is absent; the map shows the plain panel."));
}
