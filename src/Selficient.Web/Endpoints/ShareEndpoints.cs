using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Share;
using Selficient.Domain.Resilience;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// The share card as an image: <c>/share/{scenario}/{days}.png</c>. The two parts of the address
/// are a scenario the engine knows and a count of half days up to two months; anything else is 404,
/// so there is nothing a stranger can make the card say. Drawn once per pair and kept.
/// </summary>
public static class ShareEndpoints
{
    /// <summary>The most days a card will show.</summary>
    public static decimal MaxDays => 60m;

    /// <summary>Maps the card.</summary>
    public static IEndpointRouteBuilder MapShareEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/share/{scenario}/{days}.png", CardAsync)
            .RequireRateLimiting(RateLimits.ApiPolicy)
            .WithName("ShareCard")
            .ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>The card for a scenario id and a figure as written in an address, or null when either is not one we draw.</summary>
    public static ShareCard? Resolve(string? scenarioId, string? days)
    {
        Scenario? scenario = scenarioId is null ? null : Scenarios.Find(scenarioId);
        if (scenario is null || !decimal.TryParse(days, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal figure))
        {
            return null;
        }

        if (figure < 0m || figure > MaxDays || figure * 2m != decimal.Floor(figure * 2m))
        {
            return null;
        }

        return new ShareCard(figure, scenario.Id, scenario.Name);
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> CardAsync(string scenario, string days, HttpContext context, IShareCardRenderer renderer, HybridCache cache, CancellationToken cancellationToken)
    {
        ShareCard? card = Resolve(scenario, days);
        if (card is null)
        {
            return TypedResults.NotFound();
        }

        byte[] png = await cache.GetOrCreateAsync(
            $"share:{card.ScenarioId}:{card.Figure}",
            (card, renderer),
            static (state, _) => ValueTask.FromResult(state.renderer.Render(state.card)),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromDays(30), LocalCacheExpiration = TimeSpan.FromDays(30) },
            cancellationToken: cancellationToken);
        context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=86400, immutable";
        return TypedResults.File(png, "image/png");
    }
}
