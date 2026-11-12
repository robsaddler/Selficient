using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Accounts;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// Passkeys: adding one to the account signed in, signing in with one, and the panel's list. Each
/// ceremony is two calls: one for the options the browser hands its authenticator, one for the
/// answer. The options are remembered between the two in a cookie only this server can read, and
/// taken away when the answer comes. Every call that changes anything must come from this site's
/// own script. The threat model is section 6 of <c>docs/threat-models/household-accounts.md</c>.
/// </summary>
public static class PasskeyEndpoints
{
    /// <summary>The largest answer read. An attestation with its certificate chain is a few thousand bytes.</summary>
    public static int MaxAnswerBytes => 32 * 1024;

    /// <summary>Maps the passkey endpoints.</summary>
    public static IEndpointRouteBuilder MapPasskeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/account/passkeys", ListAsync)
            .RequireAuthorization(HouseholdAuth.Policy).RequireRateLimiting(RateLimits.ApiPolicy).WithName("Passkeys").ExcludeFromDescription();
        endpoints.MapPost("/api/account/passkeys/options", BeginAddingAsync)
            .RequireAuthorization(HouseholdAuth.Policy).RequireRateLimiting(RateLimits.SignInPolicy).DisableAntiforgery().WithName("BeginPasskey").ExcludeFromDescription();
        endpoints.MapPost("/api/account/passkeys", FinishAddingAsync)
            .RequireAuthorization(HouseholdAuth.Policy).RequireRateLimiting(RateLimits.SignInPolicy).DisableAntiforgery().WithName("AddPasskey").ExcludeFromDescription();
        endpoints.MapDelete("/api/account/passkeys/{id:long}", RemoveAsync)
            .RequireAuthorization(HouseholdAuth.Policy).RequireRateLimiting(RateLimits.ApiPolicy).DisableAntiforgery().WithName("RemovePasskey").ExcludeFromDescription();
        endpoints.MapPost("/api/passkey/options", BeginSigningIn)
            .RequireRateLimiting(RateLimits.SignInPolicy).DisableAntiforgery().WithName("BeginPasskeySignIn").ExcludeFromDescription();
        endpoints.MapPost("/api/passkey", FinishSigningInAsync)
            .RequireRateLimiting(RateLimits.SignInPolicy).DisableAntiforgery().WithName("PasskeySignIn").ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<Ok<PasskeyLine[]>> ListAsync(HttpContext context, Passkeys passkeys, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        IReadOnlyList<Passkey> kept = await passkeys.ListAsync(HouseholdAuth.IdOf(context.User)!.Value, cancellationToken);
        return TypedResults.Ok(kept.Select(static passkey => new PasskeyLine(passkey.Id, Passkeys.Describe(passkey))).ToArray());
    }

    private static async Task<Results<ContentHttpResult, JsonHttpResult<Sentence>>> BeginAddingAsync(HttpContext context, Passkeys passkeys, PasskeyCeremonyCookie ceremony, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        if (!AccountEndpoints.IsFromOurPages(context.Request))
        {
            return Refused(AccountEndpoints.NotFromHere, StatusCodes.Status403Forbidden);
        }

        Result<string> options = await passkeys.BeginAddingAsync(HouseholdAuth.IdOf(context.User)!.Value, HouseholdAuth.EmailOf(context.User), cancellationToken);
        if (options.IsFailure)
        {
            return Refused(options.Error.Message, StatusCodes.Status409Conflict);
        }

        ceremony.Write(context.Response, PasskeyCeremonyCookie.Adding, options.Value);
        return TypedResults.Content(options.Value, "application/json", Encoding.UTF8);
    }

    private static async Task<Results<Ok<PasskeyLine>, JsonHttpResult<Sentence>>> FinishAddingAsync(HttpContext context, Passkeys passkeys, PasskeyCeremonyCookie ceremony, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        if (!AccountEndpoints.IsFromOurPages(context.Request))
        {
            return Refused(AccountEndpoints.NotFromHere, StatusCodes.Status403Forbidden);
        }

        string? options = ceremony.Take(context, PasskeyCeremonyCookie.Adding);
        string? answer = await AnswerAsync(context, cancellationToken);
        Result<Passkey> added = await passkeys.FinishAddingAsync(
            HouseholdAuth.IdOf(context.User)!.Value, options, answer, DeviceLabel.From(context.Request.Headers.UserAgent), cancellationToken);
        return added.IsSuccess
            ? TypedResults.Ok(new PasskeyLine(added.Value.Id, Passkeys.Describe(added.Value)))
            : Refused(added.Error.Message, StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<NoContent, NotFound, JsonHttpResult<Sentence>>> RemoveAsync(long id, HttpContext context, Passkeys passkeys, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        if (!AccountEndpoints.IsFromOurPages(context.Request))
        {
            return Refused(AccountEndpoints.NotFromHere, StatusCodes.Status403Forbidden);
        }

        return await passkeys.RemoveAsync(HouseholdAuth.IdOf(context.User)!.Value, id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static Results<ContentHttpResult, JsonHttpResult<Sentence>> BeginSigningIn(HttpContext context, Passkeys passkeys, PasskeyCeremonyCookie ceremony)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        if (!AccountEndpoints.IsFromOurPages(context.Request))
        {
            return Refused(AccountEndpoints.NotFromHere, StatusCodes.Status403Forbidden);
        }

        string options = passkeys.BeginSigningIn();
        ceremony.Write(context.Response, PasskeyCeremonyCookie.SigningIn, options);
        return TypedResults.Content(options, "application/json", Encoding.UTF8);
    }

    private static async Task<Results<NoContent, JsonHttpResult<Sentence>>> FinishSigningInAsync(HttpContext context, Passkeys passkeys, PasskeyCeremonyCookie ceremony, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        if (!AccountEndpoints.IsFromOurPages(context.Request))
        {
            return Refused(AccountEndpoints.NotFromHere, StatusCodes.Status403Forbidden);
        }

        string? options = ceremony.Take(context, PasskeyCeremonyCookie.SigningIn);
        string? answer = await AnswerAsync(context, cancellationToken);
        Result<HouseholdAccount> proved = await passkeys.FinishSigningInAsync(options, answer, cancellationToken);
        if (proved.IsFailure)
        {
            return Refused(proved.Error.Message, StatusCodes.Status400BadRequest);
        }

        // A new session from the server, exactly as the emailed code gives.
        await HouseholdAuth.SignInAsync(context, proved.Value);
        return TypedResults.NoContent();
    }

    /// <summary>The authenticator's answer as text, or nothing when it is over the cap or not text.</summary>
    private static async Task<string?> AnswerAsync(HttpContext context, CancellationToken cancellationToken)
    {
        byte[]? body = await HandoffEndpoints.ReadCappedAsync(context.Request.Body, MaxAnswerBytes, cancellationToken);
        return body is null ? null : AccountEndpoints.Text(body);
    }

    private static JsonHttpResult<Sentence> Refused(string sentence, int status) => TypedResults.Json(new Sentence(sentence), statusCode: status);
}

/// <summary>One passkey as the panel lists it: the number to remove it by, and what it is in a sentence.</summary>
public sealed record PasskeyLine([property: JsonPropertyName("id")] long Id, [property: JsonPropertyName("line")] string Line);
