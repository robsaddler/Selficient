using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Catalogue;
using Selficient.Domain.Kit;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// Every shop link on the site goes out through here: one count per offer per day, then a redirect
/// to the shop. No cookie, no address, no person; the count is how the catalogue learns what earns.
/// The hop touches no database: the offer comes from the catalogue the pages already hold and the
/// click goes on the counter, which the flusher writes once a second.
/// </summary>
public static class OutEndpoint
{
    /// <summary>Maps <c>GET /out/{offer}</c>.</summary>
    public static IEndpointRouteBuilder MapOutEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/out/{offer:long}", Out)
            .RequireRateLimiting(RateLimits.ApiPolicy)
            .WithName("Out")
            .ExcludeFromDescription();

        return endpoints;
    }

    private static Results<RedirectHttpResult, NotFound<string>> Out(long offer, HttpContext context, StoredCatalogue catalogue, ClickCounter counter, TimeProvider clock)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        KitPick? pick = catalogue.Find(offer);
        if (pick is null)
        {
            return TypedResults.NotFound("That item is no longer in the catalogue. The plan will show another.");
        }

        counter.Record(offer, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        return TypedResults.Redirect(pick.Link.ToString(), permanent: false, preserveMethod: false);
    }
}
