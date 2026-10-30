namespace Selficient.Application.Maps;

/// <summary>
/// One tile in the web-map pyramid, in XYZ addressing (rows counted from the top). Only addresses
/// that exist can be constructed, so nothing downstream has to re-check a range.
/// </summary>
public readonly record struct TileAddress
{
    /// <summary>The deepest zoom any tile source is allowed to be asked for.</summary>
    public const int MaxZoom = 22;

    private TileAddress(int z, int x, int y)
    {
        Z = z;
        X = x;
        Y = y;
    }

    /// <summary>Zoom level.</summary>
    public int Z { get; }

    /// <summary>Column, counted from the west.</summary>
    public int X { get; }

    /// <summary>Row, counted from the north.</summary>
    public int Y { get; }

    /// <summary>
    /// The same row counted from the south, which is how MBTiles stores it. Getting this wrong
    /// does not error; it quietly serves the mirror image of the country.
    /// </summary>
    public int TmsRow => (1 << Z) - 1 - Y;

    /// <summary>The address, or null when it lies outside the pyramid.</summary>
    public static TileAddress? TryCreate(int z, int x, int y)
    {
        if (z is < 0 or > MaxZoom)
        {
            return null;
        }

        int span = 1 << z;
        return x < 0 || x >= span || y < 0 || y >= span ? null : new TileAddress(z, x, y);
    }
}

/// <summary>The bytes of one vector tile. Zoomstack ships them gzipped; the header says so.</summary>
public readonly record struct MapTile(ReadOnlyMemory<byte> Data)
{
    /// <summary>True when the bytes start with the gzip magic number.</summary>
    public bool IsGzipped => Data.Length > 2 && Data.Span[0] == 0x1f && Data.Span[1] == 0x8b;
}

/// <summary>What the browser needs to know before it asks for a single tile.</summary>
public sealed record BasemapStatus(bool Available, int MinZoom, int MaxZoom)
{
    /// <summary>
    /// Required on the map by the Open Government Licence, which is also what lets a household
    /// print the page.
    /// </summary>
    public const string Attribution = "Contains OS data © Crown copyright and database right 2026";

    /// <summary>No archive, or one that could not be read. The page shows the plain panel.</summary>
    public static BasemapStatus Unavailable { get; } = new(false, 0, 0);
}

/// <summary>A source of basemap tiles. Absence is a state, never an exception.</summary>
public interface IBasemapTiles
{
    /// <summary>Whether tiles can be served, and over which zoom range.</summary>
    BasemapStatus Status();

    /// <summary>The tile, or null where there is nothing to draw (sea, or beyond the archive).</summary>
    ValueTask<MapTile?> GetTileAsync(TileAddress address, CancellationToken cancellationToken);
}
