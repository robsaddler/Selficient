using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// The postcode nearest a point, for the "Use my location" button beside every postcode box. A POST
/// with a JSON body rather than a GET with a query, so the coordinates never land in a request log
/// here or on the proxy in front; the answer is never cached; the point is rounded to about ten
/// metres before it leaves and is kept nowhere.
/// </summary>
public static class GeoEndpoints
{
    /// <summary>Great Britain and Northern Ireland, with a little sea around them. Anything else is refused before any upstream call.</summary>
    private const double SouthernmostLatitude = 49.5;
    private const double NorthernmostLatitude = 61.5;
    private const double WesternmostLongitude = -9.0;
    private const double EasternmostLongitude = 2.5;

    /// <summary>Maps <c>POST /api/geo/nearest</c>.</summary>
    public static IEndpointRouteBuilder MapGeoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/api/geo/nearest", NearestAsync)
            .RequireRateLimiting(RateLimits.ApiPolicy)
            .DisableAntiforgery()
            .WithName("NearestPostcode")
            .ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>Whether a point could be in the United Kingdom at all.</summary>
    internal static bool IsPlausible(double lat, double lng) =>
        double.IsFinite(lat) && double.IsFinite(lng)
        && lat is >= SouthernmostLatitude and <= NorthernmostLatitude
        && lng is >= WesternmostLongitude and <= EasternmostLongitude;

    private static async Task<Results<Ok<NearestResponse>, NotFound<Sentence>, BadRequest<Sentence>, StatusCodeHttpResult>> NearestAsync(
        HttpContext context, IReverseGeocoder geocoder, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        // The body is read here rather than bound, so a malformed one gets our sentence and not the framework's problem details.
        NearestRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<NearestRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null || !IsPlausible(request.Lat, request.Lng))
        {
            return TypedResults.BadRequest(new Sentence("That is not a place in the United Kingdom. Type the postcode instead."));
        }

        Result<Postcode> nearest = await geocoder.NearestAsync(new Coordinates(request.Lat, request.Lng), cancellationToken);
        if (nearest.IsSuccess)
        {
            return TypedResults.Ok(new NearestResponse(nearest.Value.Value));
        }

        return nearest.Error.Code == "postcode.none-here"
            ? TypedResults.NotFound(new Sentence(nearest.Error.Message))
            : TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}

/// <summary>Where the person is, as the browser reports it.</summary>
public sealed record NearestRequest([property: JsonPropertyName("lat")] double Lat, [property: JsonPropertyName("lng")] double Lng);

/// <summary>The postcode found.</summary>
public sealed record NearestResponse([property: JsonPropertyName("postcode")] string Postcode);

/// <summary>A whole sentence, for the box under the button.</summary>
public sealed record Sentence([property: JsonPropertyName("message")] string Message);
