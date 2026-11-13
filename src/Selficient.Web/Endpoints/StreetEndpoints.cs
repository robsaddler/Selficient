using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Streets;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// Street groups, as the browser talks to the server about them. Everything a member shares is
/// encrypted in its browser, so these endpoints carry membership and blobs: they are never sent a
/// key and hold nothing they could read. Every one is for the household the session names, and
/// every write must come from this site's own script, as a write to the account's plan must.
/// </summary>
public static class StreetEndpoints
{
    /// <summary>The largest body a write may carry, in bytes: a record and a little room.</summary>
    public const int MaxBodyBytes = 48_000;

    private static readonly Dictionary<string, int> Statuses = new(StringComparer.Ordinal)
    {
        ["street.none"] = StatusCodes.Status404NotFound,
        ["street.invitation"] = StatusCodes.Status404NotFound,
        ["street.record"] = StatusCodes.Status400BadRequest,
        ["street.full"] = StatusCodes.Status409Conflict,
        ["street.too-many"] = StatusCodes.Status409Conflict,
        ["street.forbidden"] = StatusCodes.Status403Forbidden,
    };

    /// <summary>Maps the endpoints.</summary>
    public static IEndpointRouteBuilder MapStreetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder street = endpoints.MapGroup("/api/street")
            .RequireAuthorization(HouseholdAuth.Policy)
            .RequireRateLimiting(RateLimits.SyncPolicy)
            .DisableAntiforgery()
            .ExcludeFromDescription();
        street.MapGet("/", MineAsync).WithName("StreetGroups");
        street.MapPost("/", (HttpContext context, StreetGroups groups, CancellationToken cancellationToken) =>
            WriteAsync(context, async (account, body) => Answer(await groups.StartAsync(account, body.PassHash, body.About, body.Record, body.Keep, cancellationToken), static made => new StreetJoined(made.GroupId, made.MemberId)), cancellationToken)).WithName("StartStreetGroup");
        street.MapGet("/{id:guid}", ReadAsync).WithName("StreetGroup");
        street.MapPost("/{id:guid}/join", (Guid id, HttpContext context, StreetGroups groups, CancellationToken cancellationToken) =>
            WriteAsync(context, async (account, body) => Answer(await groups.JoinAsync(account, id, body.Pass, body.Record, body.Keep, cancellationToken), static made => new StreetJoined(made.GroupId, made.MemberId)), cancellationToken)).WithName("JoinStreetGroup");
        street.MapPut("/{id:guid}/record", (Guid id, HttpContext context, StreetGroups groups, CancellationToken cancellationToken) =>
            WriteAsync(context, async (account, body) => Answer(await groups.ShareAsync(account, id, body.Record, body.Keep, cancellationToken), static _ => new StreetDone(true)), cancellationToken)).WithName("ShareWithStreetGroup");
        street.MapPost("/{id:guid}/remove", (Guid id, HttpContext context, StreetGroups groups, CancellationToken cancellationToken) =>
            WriteAsync(context, async (account, body) => Answer(await groups.RemoveAsync(account, id, body.MemberId ?? Guid.Empty, body.PassHash, body.About, cancellationToken), static _ => new StreetDone(true)), cancellationToken)).WithName("RemoveFromStreetGroup");
        street.MapPost("/{id:guid}/leave", (Guid id, HttpContext context, StreetGroups groups, CancellationToken cancellationToken) =>
            WriteAsync(context, async (account, _) => Answer(await groups.LeaveAsync(account, id, cancellationToken), static _ => new StreetDone(true)), cancellationToken)).WithName("LeaveStreetGroup");

        return endpoints;
    }

    private static async Task<IResult> MineAsync(HttpContext context, StreetGroups groups, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        IReadOnlyList<StreetMembership> mine = await groups.MineAsync(HouseholdAuth.IdOf(context.User)!.Value, cancellationToken);
        return TypedResults.Ok(mine.Select(static membership => new StreetMine(membership.GroupId, membership.MemberId, membership.MayRemove)).ToList());
    }

    private static async Task<IResult> ReadAsync(Guid id, HttpContext context, StreetGroups groups, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        Result<StreetGroup> group = await groups.ReadAsync(HouseholdAuth.IdOf(context.User)!.Value, id, cancellationToken);
        return Answer(group, static read => new StreetRead(
            read.GroupId,
            read.About,
            read.You,
            read.Keep,
            [.. read.Members.Select(static member => new StreetHome(member.MemberId, member.Record, member.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture), member.MayRemove))]));
    }

    // A write: from our own pages, small enough, and a body that reads as one. Then the act, for the account the session names.
    private static async Task<IResult> WriteAsync(HttpContext context, Func<long, StreetBody, Task<IResult>> act, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        if (!AccountEndpoints.IsFromOurPages(context.Request))
        {
            return TypedResults.Json(new Sentence(AccountEndpoints.NotFromHere), statusCode: StatusCodes.Status403Forbidden);
        }

        if (context.Request.ContentLength is not (>= 0 and <= MaxBodyBytes))
        {
            return TypedResults.Json(new Sentence(StreetGroups.NotARecord.Message), statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        StreetBody? body;
        try
        {
            body = await context.Request.ReadFromJsonAsync<StreetBody>(cancellationToken);
        }
        catch (JsonException)
        {
            body = null;
        }

        return body is null
            ? TypedResults.Json(new Sentence(StreetGroups.NotARecord.Message), statusCode: StatusCodes.Status400BadRequest)
            : await act(HouseholdAuth.IdOf(context.User)!.Value, body);
    }

    private static IResult Answer<T, TBody>(Result<T> result, Func<T, TBody> body) =>
        result.IsSuccess
            ? TypedResults.Ok(body(result.Value))
            : TypedResults.Json(new Sentence(result.Error.Message), statusCode: Statuses[result.Error.Code]);
}

/// <summary>What a write carries. Each endpoint reads the members it needs; every one is ciphertext or a random value made in the browser.</summary>
/// <param name="PassHash">The SHA-256 of the invitation's pass, in base64, when starting a group or replacing its pass.</param>
/// <param name="Pass">The invitation's pass, in base64, when joining.</param>
/// <param name="About">The group's name, encrypted, when starting.</param>
/// <param name="Record">What the home shares, encrypted.</param>
/// <param name="MemberId">The home to remove.</param>
/// <param name="Keep">What the home's own devices keep, sealed with the group's key, which the server does not have.</param>
public sealed record StreetBody(string? PassHash, string? Pass, string? About, string? Record, Guid? MemberId, string? Keep = null);

/// <summary>The answer to starting or joining a group.</summary>
/// <param name="GroupId">The group.</param>
/// <param name="MemberId">The household's member id in it.</param>
public sealed record StreetJoined(Guid GroupId, Guid MemberId);

/// <summary>The answer to a write that has nothing to say but that it was done.</summary>
/// <param name="Done">Always true.</param>
public sealed record StreetDone(bool Done);

/// <summary>One of the groups a household is in.</summary>
/// <param name="GroupId">The group.</param>
/// <param name="MemberId">The household's member id in it.</param>
/// <param name="MayRemove">Whether the household is the one who may remove a home.</param>
public sealed record StreetMine(Guid GroupId, Guid MemberId, bool MayRemove);

/// <summary>A group, for one of its members.</summary>
/// <param name="GroupId">The group.</param>
/// <param name="About">The group's name, encrypted.</param>
/// <param name="You">The asking household's member id.</param>
/// <param name="Keep">What the asking household's own devices keep here, or null. No other home's is ever answered.</param>
/// <param name="Homes">Every home in it, longest-standing first.</param>
public sealed record StreetRead(Guid GroupId, string About, Guid You, string? Keep, IReadOnlyList<StreetHome> Homes);

/// <summary>One home in a group.</summary>
/// <param name="MemberId">Its id in this group.</param>
/// <param name="Record">What it shares, encrypted.</param>
/// <param name="UpdatedAt">When it last wrote, in UTC.</param>
/// <param name="MayRemove">Whether it is the longest-standing member.</param>
public sealed record StreetHome(Guid MemberId, string Record, string UpdatedAt, bool MayRemove);
