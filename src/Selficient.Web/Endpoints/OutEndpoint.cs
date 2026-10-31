using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Catalogue;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// Every shop link on the site goes out through here: one count per offer per day, then a redirect
/// to the shop. No cookie, no address, no person; the count is how the catalogue learns what earns.
/// </summary>
public static class OutEndpoint
{
    /// <summary>Maps <c>GET /out/{offer}</c>.</summary>
    public static IEndpointRouteBuilder MapOutEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/out/{offer:long}", OutAsync)
            .RequireRateLimiting(RateLimits.ApiPolicy)
            .WithName("Out")
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<Results<RedirectHttpResult, NotFound<string>>> OutAsync(long offer, HttpContext context, ICatalogueStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        Offer? found = await store.FindAsync(offer, cancellationToken);
        if (found is null || found.Status is OfferStatus.Hidden or OfferStatus.Proposed)
        {
            return TypedResults.NotFound("That item is no longer in the catalogue. The plan will show another.");
        }

        await store.RecordClickAsync(found.Id, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), cancellationToken);
        return TypedResults.Redirect(found.Link.ToString(), permanent: false, preserveMethod: false);
    }
}
