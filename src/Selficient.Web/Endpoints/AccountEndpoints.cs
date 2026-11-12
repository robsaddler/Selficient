using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// What the page asks about a signed-in household. The pages themselves are the same for everyone,
/// so they can be cached and opened offline; the header chip asks here who is signed in, and only
/// when the hint cookie says somebody is, so a guest's browser never calls it.
/// </summary>
public static class AccountEndpoints
{
    /// <summary>Maps <c>GET /api/account</c>.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/account", Who)
            .RequireAuthorization(HouseholdAuth.Policy)
            .RequireRateLimiting(RateLimits.ApiPolicy)
            .WithName("Account")
            .ExcludeFromDescription();

        return endpoints;
    }

    private static Ok<AccountSummary> Who(HttpContext context)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        // The hint lasts as long as the session can, and the session slides; so the hint is renewed whenever it is used.
        HouseholdAuth.SetHint(context.Response);
        return TypedResults.Ok(new AccountSummary(HouseholdAuth.EmailOf(context.User)));
    }
}

/// <summary>Who is signed in: the address, and nothing else, because an account holds nothing else about a person.</summary>
public sealed record AccountSummary([property: JsonPropertyName("email")] string Email);
