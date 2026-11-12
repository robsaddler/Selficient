using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Accounts;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// What the page asks about a signed-in household. The pages themselves are the same for everyone,
/// so they can be cached and opened offline; the header chip asks here who is signed in, and only
/// when the hint cookie says somebody is, so a guest's browser never calls it. The same household
/// reads and writes the copy of its plan the account keeps. The account is always the one the
/// session names: nothing in an address, a header or a body chooses it.
/// </summary>
public static class AccountEndpoints
{
    /// <summary>The header a write must carry. A form cannot send it, and another site's script cannot without our leave.</summary>
    public const string SyncHeader = "X-Selficient-Sync";

    /// <summary>What a write that did not come from this site's own pages is told.</summary>
    public const string NotFromHere = "That request did not come from a Selficient page, so nothing was changed.";

    /// <summary>What a write with no version, or one that is not a number, is told.</summary>
    public const string NoBase = "The write did not say which version of the plan it was based on, so nothing was changed.";

    /// <summary>Maps the account's endpoints.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/account", Who)
            .RequireAuthorization(HouseholdAuth.Policy)
            .RequireRateLimiting(RateLimits.ApiPolicy)
            .WithName("Account")
            .ExcludeFromDescription();
        endpoints.MapGet("/api/account/plan", ReadPlanAsync)
            .RequireAuthorization(HouseholdAuth.Policy)
            .RequireRateLimiting(RateLimits.SyncPolicy)
            .WithName("ReadPlan")
            .ExcludeFromDescription();
        endpoints.MapPut("/api/account/plan", WritePlanAsync)
            .RequireAuthorization(HouseholdAuth.Policy)
            .RequireRateLimiting(RateLimits.SyncPolicy)
            .DisableAntiforgery()
            .WithName("WritePlan")
            .ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>
    /// Whether a request was made by this site's own script: it carries the header only script can
    /// add, and the browser, where it says anything, says the request came from this origin.
    /// </summary>
    internal static bool IsFromOurPages(HttpRequest request) =>
        request.Headers.ContainsKey(SyncHeader)
        && (!request.Headers.TryGetValue("Sec-Fetch-Site", out Microsoft.Extensions.Primitives.StringValues site) || site == "same-origin");

    private static Ok<AccountSummary> Who(HttpContext context)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        // The hint lasts as long as the session can, and the session slides; so the hint is renewed whenever it is used.
        HouseholdAuth.SetHint(context.Response);
        return TypedResults.Ok(new AccountSummary(HouseholdAuth.EmailOf(context.User)));
    }

    private static async Task<Ok<PlanAnswer>> ReadPlanAsync(HttpContext context, PlanSync sync, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        // The policy let this request in, so the principal carries the account's number.
        SyncedPlan plan = await sync.ReadAsync(HouseholdAuth.IdOf(context.User)!.Value, cancellationToken);
        return TypedResults.Ok(new PlanAnswer(plan.Version, plan.Body));
    }

    private static async Task<Results<Ok<PlanAnswer>, Conflict<PlanAnswer>, JsonHttpResult<Sentence>>> WritePlanAsync(HttpContext context, PlanSync sync, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        if (!IsFromOurPages(context.Request))
        {
            return TypedResults.Json(new Sentence(NotFromHere), statusCode: StatusCodes.Status403Forbidden);
        }

        if (!long.TryParse(context.Request.Query["base"], NumberStyles.None, CultureInfo.InvariantCulture, out long baseVersion))
        {
            return TypedResults.Json(new Sentence(NoBase), statusCode: StatusCodes.Status400BadRequest);
        }

        byte[]? body = await HandoffEndpoints.ReadCappedAsync(context.Request.Body, PlanGuard.MaxBytes, cancellationToken);
        if (body is null)
        {
            return TypedResults.Json(new Sentence(PlanSync.NotAPlan.Message), statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        long account = HouseholdAuth.IdOf(context.User)!.Value;
        Result<long> written = await sync.WriteAsync(account, Text(body), baseVersion, cancellationToken);
        if (written.IsSuccess)
        {
            return TypedResults.Ok(new PlanAnswer(written.Value, null));
        }

        if (written.Error == PlanSync.Stale)
        {
            // The writer is handed what is there now, so it can merge and write again without a second request.
            SyncedPlan current = await sync.ReadAsync(account, cancellationToken);
            return TypedResults.Conflict(new PlanAnswer(current.Version, current.Body));
        }

        return TypedResults.Json(new Sentence(written.Error.Message), statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>The body as text, or nothing when it is not UTF-8, which no plan a browser wrote is not.</summary>
    internal static string? Text(byte[] body)
    {
        string? text = null;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(body);
        }
        catch (DecoderFallbackException)
        {
            // Left as nothing, which the guard refuses.
        }

        return text;
    }
}

/// <summary>Who is signed in: the address, and nothing else, because an account holds nothing else about a person.</summary>
public sealed record AccountSummary([property: JsonPropertyName("email")] string Email);

/// <summary>The copy of the plan an account keeps: its version, and the plan as the browser wrote it, when there is one to give.</summary>
public sealed record PlanAnswer([property: JsonPropertyName("version")] long Version, [property: JsonPropertyName("body")] string? Body);
