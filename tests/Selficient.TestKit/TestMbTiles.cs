using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace Selficient.TestKit;

/// <summary>
/// Builds tiny MBTiles archives so the tile store is exercised end to end without a three
/// gigabyte download. Tiles are given in XYZ and stored in TMS, exactly as a real archive is.
/// </summary>
public static class TestMbTiles
{
    /// <summary>The metadata rows a real Zoomstack archive carries.</summary>
    public static IReadOnlyDictionary<string, string> ZoomstackMetadata { get; } = new Dictionary<string, string>
    {
        ["name"] = "OS Open Zoomstack",
        ["format"] = "pbf",
        ["minzoom"] = "0",
        ["maxzoom"] = "14",
    };

    /// <summary>Creates an archive in the temp folder and returns its path.</summary>
    public static string Create(IReadOnlyDictionary<string, string>? metadata, params (int Z, int X, int Y, byte[] Data)[] tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        string path = Path.Combine(Path.GetTempPath(), $"selficient-{Guid.NewGuid():N}.mbtiles");
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();

        using (SqliteCommand schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE metadata (name TEXT, value TEXT);
                CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);
                CREATE UNIQUE INDEX tile_index ON tiles (zoom_level, tile_column, tile_row);
                """;
            schema.ExecuteNonQuery();
        }

        foreach ((string name, string value) in metadata ?? new Dictionary<string, string>())
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO metadata (name, value) VALUES ($name, $value)";
            insert.Parameters.AddWithValue("$name", name);
            insert.Parameters.AddWithValue("$value", value);
            insert.ExecuteNonQuery();
        }

        foreach ((int z, int x, int y, byte[] data) in tiles)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO tiles (zoom_level, tile_column, tile_row, tile_data) VALUES ($z, $x, $row, $data)";
            insert.Parameters.AddWithValue("$z", z);
            insert.Parameters.AddWithValue("$x", x);
            insert.Parameters.AddWithValue("$row", (1 << z) - 1 - y);
            insert.Parameters.AddWithValue("$data", data);
            insert.ExecuteNonQuery();
        }

        return path;
    }

    /// <summary>A file that is not a SQLite database at all.</summary>
    public static string CreateCorrupt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"selficient-{Guid.NewGuid():N}.mbtiles");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11]);
        return path;
    }

    /// <summary>Gzips a payload, as Zoomstack tiles are stored.</summary>
    public static byte[] Gzipped(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(payload);
        }

        return output.ToArray();
    }

    /// <summary>Removes an archive, releasing any pooled read-only connections first.</summary>
    public static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>A clock the tests move by hand.</summary>
public sealed class ManualClock : TimeProvider
{
    private const long Start = 1_000_000_000L;
    private long _ticks = Start;

    /// <summary>
    /// One tick is one <see cref="TimeSpan"/> tick for both the timestamp and the wall clock, on
    /// every operating system. The default is the Stopwatch frequency, which is ten million a
    /// second on Windows and a thousand million on Linux; mixing it with TimeSpan ticks made a
    /// day's advance a hundred days on the build runner.
    /// </summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => _ticks;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero) + TimeSpan.FromTicks(_ticks - Start);

    /// <summary>Moves time forward.</summary>
    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
