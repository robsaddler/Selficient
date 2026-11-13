using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Figures;
using Selficient.Domain.Parameters;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// <c>GET /api/figures</c>: which set of figures is in force, and the figures a household may give
/// its own value for, each with its question, its range and the value in force. The browser keeps
/// the last answer it had, and carries the shipped ones in its own script, so a page that opens
/// with no network still knows them; when the number here has moved on from the one a plan was
/// built with, the browser builds the plan again. Nothing about any household is in the answer.
/// </summary>
public static class FiguresEndpoint
{
    /// <summary>Maps the endpoint.</summary>
    public static IEndpointRouteBuilder MapFiguresEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/figures", Handle)
            .RequireRateLimiting(RateLimits.ApiPolicy)
            .WithName("Figures")
            .ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>The published answer for a set: its number, when it was published, and the household's figures under it.</summary>
    public static FiguresResponse For(ParameterSet figures, DateTimeOffset? publishedAt)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return new FiguresResponse(
            figures.Version,
            publishedAt?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            [.. ParameterRegistry.All
                .Where(parameter => figures.MayGive(parameter.Id))
                .Select(parameter => new FigureResponse(parameter.Id, figures.QuestionFor(parameter.Id)!, parameter.Unit, parameter.Min, parameter.Max, parameter.WholeNumber, figures[parameter.Id]))]);
    }

    private static Ok<FiguresResponse> Handle(HttpContext context, ICurrentFigures figures)
    {
        // Always asked afresh: the point of asking is to learn that the number has moved.
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        return TypedResults.Ok(For(figures.Current, figures.PublishedAt));
    }
}

/// <summary>One figure a household may give its own value for.</summary>
/// <param name="Id">The figure's id.</param>
/// <param name="Question">What the household is asked.</param>
/// <param name="Unit">What it is counted in.</param>
/// <param name="Min">The least a household may say.</param>
/// <param name="Max">The most a household may say.</param>
/// <param name="Whole">Whether only a whole number will do.</param>
/// <param name="Value">The value in force.</param>
public sealed record FigureResponse(string Id, string Question, string Unit, decimal Min, decimal Max, bool Whole, decimal Value);

/// <summary>Which set is in force and the household's figures under it.</summary>
/// <param name="Version">The set's number; nought for the shipped figures.</param>
/// <param name="PublishedAt">When it was published, in UTC, or null for the shipped figures.</param>
/// <param name="Figures">The figures a household may give its own value for.</param>
public sealed record FiguresResponse(int Version, string? PublishedAt, IReadOnlyList<FigureResponse> Figures);
