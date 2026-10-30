using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Selficient.Application.Maps;

namespace Selficient.Infrastructure.Maps;

/// <summary>
/// Serves vector tiles out of the self-hosted OS Open Zoomstack MBTiles archive.
/// <para>
/// The file is around three gigabytes and that number is almost irrelevant to the browser: MBTiles
/// is a SQLite database of individually addressed tiles, a view needs six to twelve of them, and
/// each lookup is one indexed read on the archive's own (zoom, column, row) index. Every query is
/// parameterised and every connection is opened read-only.
/// </para>
/// <para>
/// Absence is a normal state. The deploy fetches the archive after the site is up, so an absent
/// archive is checked again after <see cref="MapOptions.RetryAfterSeconds"/>; a present one is
/// remembered for the life of the process.
/// </para>
/// </summary>
public sealed partial class MbTilesBasemap : IBasemapTiles
{
    private const string TileSql = "SELECT tile_data FROM tiles WHERE zoom_level = @z AND tile_column = @x AND tile_row = @y";
    private const string MetadataSql = "SELECT name, value FROM metadata";
    private const int DefaultMaxZoom = 14;

    private readonly string _connectionString;
    private readonly TimeSpan _retry;
    private readonly TimeProvider _clock;
    private readonly ILogger<MbTilesBasemap> _logger;
    private readonly Lock _gate = new();
    private BasemapStatus? _status;
    private long _checkedAt;

    /// <summary>Creates the store. Nothing is opened until the first request.</summary>
    public MbTilesBasemap(IOptions<MapOptions> options, IHostEnvironment environment, TimeProvider clock, ILogger<MbTilesBasemap> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        string root = string.IsNullOrEmpty(environment.ContentRootPath) ? AppContext.BaseDirectory : environment.ContentRootPath;
        ArchivePath = Path.GetFullPath(Path.Combine(root, options.Value.MbTilesPath));
        _connectionString = ConnectionString(ArchivePath);
        _retry = TimeSpan.FromSeconds(options.Value.RetryAfterSeconds);
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Where the archive is expected. Never sent to a browser.</summary>
    public string ArchivePath { get; }

    /// <summary>Read-only, shared cache, pooled: many concurrent readers and no writer, ever.</summary>
    internal static string ConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Shared,
        Pooling = true,
    }.ToString();

    /// <inheritdoc />
    public BasemapStatus Status()
    {
        long now = _clock.GetTimestamp();
        lock (_gate)
        {
            if (_status is { Available: true })
            {
                return _status;
            }

            if (_status is not null && _clock.GetElapsedTime(_checkedAt, now) < _retry)
            {
                return _status;
            }

            _status = Probe();
            _checkedAt = now;
            return _status;
        }
    }

    /// <inheritdoc />
    public async ValueTask<MapTile?> GetTileAsync(TileAddress address, CancellationToken cancellationToken)
    {
        if (!Status().Available)
        {
            return null;
        }

        byte[]? data;
        try
        {
            await using SqliteConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            CommandDefinition command = new(TileSql, new { z = address.Z, x = address.X, y = address.TmsRow }, cancellationToken: cancellationToken);
            data = await connection.QuerySingleOrDefaultAsync<byte[]>(command);
        }
        catch (SqliteException exception)
        {
            Log.TileUnreadable(_logger, exception, address.Z, address.X, address.Y);
            return null;
        }

        return data is { Length: > 0 } ? new MapTile(data) : null;
    }

    private BasemapStatus Probe()
    {
        if (!File.Exists(ArchivePath))
        {
            Log.Absent(_logger);
            return BasemapStatus.Unavailable;
        }

        try
        {
            using SqliteConnection connection = new(_connectionString);
            connection.Open();
            Dictionary<string, string> metadata = connection.Query<(string Name, string Value)>(MetadataSql)
                .ToDictionary(static row => row.Name, static row => row.Value, StringComparer.OrdinalIgnoreCase);

            int min = int.TryParse(metadata.GetValueOrDefault("minzoom"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMin) ? parsedMin : 0;
            int max = int.TryParse(metadata.GetValueOrDefault("maxzoom"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax) ? parsedMax : DefaultMaxZoom;
            Log.Ready(_logger, min, max);
            return new BasemapStatus(true, min, max);
        }
        catch (SqliteException exception)
        {
            // A corrupt or half-written archive degrades exactly like a missing one.
            Log.Unreadable(_logger, exception);
            return BasemapStatus.Unavailable;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "No basemap archive is present; the map shows the plain panel until one arrives.")]
        public static partial void Absent(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Basemap ready, zoom {MinZoom} to {MaxZoom}.")]
        public static partial void Ready(ILogger logger, int minZoom, int maxZoom);

        [LoggerMessage(Level = LogLevel.Error, Message = "The basemap archive could not be opened; the map shows the plain panel.")]
        public static partial void Unreadable(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Tile {Z}/{X}/{Y} could not be read from the basemap.")]
        public static partial void TileUnreadable(ILogger logger, Exception exception, int z, int x, int y);
    }
}
