using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Maps;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// The self-hosted basemap: a status probe and one tile per request. A map view is six to twelve
/// tiles of a few tens of kilobytes each; within a release a tile never changes, so a hit is
/// immutable for a year and the browser stops asking almost at once.
/// </summary>
public static class TilesEndpoints
{
    /// <summary>Vector tiles are protobuf; MapLibre does not need the type but proxies do.</summary>
    public const string ProtobufContentType = "application/x-protobuf";

    internal const string ImmutableYear = "public, max-age=31536000, immutable";
    internal const string StatusMinute = "public, max-age=60";

    /// <summary>Maps <c>GET /api/tiles/status</c> and <c>GET /api/tiles/{z}/{x}/{y}.pbf</c>.</summary>
    public static IEndpointRouteBuilder MapTilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/tiles")
            .RequireRateLimiting(RateLimits.TilesPolicy)
            .ExcludeFromDescription();

        group.MapGet("/status", Status).WithName("TilesStatus");
        group.MapGet("/{z:int}/{x:int}/{y:int}.pbf", TileAsync).WithName("Tile");

        return endpoints;
    }

    private static Ok<BasemapStatusResponse> Status(IBasemapTiles tiles, HttpContext context)
    {
        context.Response.Headers[HeaderNames.CacheControl] = StatusMinute;
        BasemapStatus status = tiles.Status();
        return TypedResults.Ok(new BasemapStatusResponse(status.Available, status.MinZoom, status.MaxZoom, BasemapStatus.Attribution));
    }

    private static async Task<Results<NotFound, NoContent, FileContentHttpResult>> TileAsync(int z, int x, int y, IBasemapTiles tiles, HttpContext context, CancellationToken cancellationToken)
    {
        // Outside the pyramid is a malformed request, not a miss; nothing reaches the archive.
        TileAddress? address = TileAddress.TryCreate(z, x, y);
        if (address is null)
        {
            return TypedResults.NotFound();
        }

        // A miss is ordinary: sea, or past the archive's deepest zoom. 204 rather than 404 so it
        // does not read as an error in the network panel or the logs; there is nothing to draw.
        MapTile? tile = await tiles.GetTileAsync(address.Value, cancellationToken);
        if (tile is null)
        {
            return TypedResults.NoContent();
        }

        context.Response.Headers[HeaderNames.CacheControl] = ImmutableYear;
        if (tile.Value.IsGzipped)
        {
            context.Response.Headers[HeaderNames.ContentEncoding] = "gzip";
        }

        return TypedResults.Bytes(tile.Value.Data, ProtobufContentType);
    }
}

/// <summary>What the browser is told. No path, no size, nothing about the server.</summary>
public sealed record BasemapStatusResponse(bool Available, int MinZoom, int MaxZoom, string Attribution);
