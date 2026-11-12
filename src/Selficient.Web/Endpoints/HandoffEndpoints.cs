using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Handoff;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// The relay behind "Send a copy": one browser leaves bytes it encrypted with a key that never
/// comes here, and another collects them once within ten minutes. There is no session and no
/// cookie on any of these, nothing is cached, and nothing about the caller is kept. The threat
/// model is <c>docs/threat-models/handoff.md</c>.
/// </summary>
public static class HandoffEndpoints
{
    /// <summary>What a collector is told when there is nothing under the id: used, run out, or never there.</summary>
    public const string Gone = "That link has already been used or has run out. Send a copy again from the other device.";

    /// <summary>Maps the three endpoints.</summary>
    public static IEndpointRouteBuilder MapHandoffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/api/handoff", LeaveAsync)
            .RequireRateLimiting(RateLimits.HandoffPolicy)
            .DisableAntiforgery()
            .WithName("LeaveCopy")
            .ExcludeFromDescription();
        endpoints.MapGet("/api/handoff/{id}", CollectAsync)
            .RequireRateLimiting(RateLimits.HandoffPolicy)
            .WithName("CollectCopy")
            .ExcludeFromDescription();
        endpoints.MapGet("/api/handoff/{id}/waiting", WaitingAsync)
            .RequireRateLimiting(RateLimits.ApiPolicy)
            .WithName("CopyWaiting")
            .ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>
    /// Reads a body to a cap and no further. The cap is on what arrives, not on the length a client
    /// declares, so a body that lies about its size is still refused. Nothing when it is over.
    /// </summary>
    internal static async Task<byte[]?> ReadCappedAsync(Stream body, int most, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[most + 1];
        int total = 0;
        int read = -1;
        while (total < buffer.Length && read != 0)
        {
            read = await body.ReadAsync(buffer.AsMemory(total), cancellationToken);
            total += read;
        }

        return total > most ? null : buffer[..total];
    }

    private static async Task<Results<Created<HandoffTicket>, JsonHttpResult<Sentence>>> LeaveAsync(HttpContext context, HandoffDesk desk, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        byte[]? body = await ReadCappedAsync(context.Request.Body, HandoffDesk.MaxBytes, cancellationToken);
        if (body is null)
        {
            return TypedResults.Json(new Sentence(HandoffDesk.TooLarge.Message), statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        Result<string> left = await desk.LeaveAsync(body, cancellationToken);
        if (left.IsSuccess)
        {
            return TypedResults.Created($"/api/handoff/{left.Value}", new HandoffTicket(left.Value));
        }

        return TypedResults.Json(new Sentence(left.Error.Message), statusCode: StatusFor[left.Error.Code]);
    }

    private static async Task<Results<FileContentHttpResult, NotFound<Sentence>>> CollectAsync(string id, HttpContext context, HandoffDesk desk, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        byte[]? copy = await desk.CollectAsync(id, cancellationToken);
        return copy is null ? TypedResults.NotFound(new Sentence(Gone)) : TypedResults.File(copy, "application/octet-stream");
    }

    private static async Task<Ok<HandoffWaiting>> WaitingAsync(string id, HttpContext context, HandoffDesk desk, CancellationToken cancellationToken)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        return TypedResults.Ok(new HandoffWaiting(await desk.IsWaitingAsync(id, cancellationToken)));
    }

    private static Dictionary<string, int> StatusFor { get; } = new(StringComparer.Ordinal)
    {
        [HandoffDesk.Malformed.Code] = StatusCodes.Status400BadRequest,
        [HandoffDesk.TooLarge.Code] = StatusCodes.Status413PayloadTooLarge,
        [HandoffDesk.Full.Code] = StatusCodes.Status503ServiceUnavailable,
    };
}

/// <summary>The id a copy was left under.</summary>
public sealed record HandoffTicket([property: JsonPropertyName("id")] string Id);

/// <summary>Whether a copy is still waiting to be collected.</summary>
public sealed record HandoffWaiting([property: JsonPropertyName("waiting")] bool Waiting);
