using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Application.Ai;
using Selficient.Web.Security;

namespace Selficient.Web.Endpoints;

/// <summary>
/// The prompts and schemas, for the household that uses their own key in the browser. There is
/// nothing secret in a prompt; publishing it is what lets the browser ask exactly what the server
/// would have asked, so the two paths give the same shape of answer.
/// </summary>
public static class AiPromptsEndpoint
{
    /// <summary>Maps <c>GET /api/ai/prompts</c>.</summary>
    public static IEndpointRouteBuilder MapAiPromptsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/ai/prompts", Handle)
            .RequireRateLimiting(RateLimits.ApiPolicy)
            .WithName("AiPrompts")
            .ExcludeFromDescription();

        return endpoints;
    }

    private static Ok<PromptsResponse> Handle(HttpContext context)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=3600";
        return TypedResults.Ok(new PromptsResponse(
            AnalysisPrompts.Model,
            new PromptResponse(AnalysisPrompts.KitSystem(KitCandidates.All), AnalysisPrompts.KitSchema, AnalysisPrompts.UserTurn(AnalysisKind.Kit)),
            new PromptResponse(AnalysisPrompts.BoilerSystem, AnalysisPrompts.BoilerSchema, AnalysisPrompts.UserTurn(AnalysisKind.Boiler)),
            new PromptResponse(AnalysisPrompts.ConsumerUnitSystem, AnalysisPrompts.ConsumerUnitSchema, AnalysisPrompts.UserTurn(AnalysisKind.ConsumerUnit)),
            KitCandidates.All.Select(static c => c.Id.Value).ToList()));
    }
}

/// <summary>One prompt as the browser needs it.</summary>
public sealed record PromptResponse(string System, string Schema, string User);

/// <summary>The three photo prompts, the model, and the action ids the kit answer may use.</summary>
public sealed record PromptsResponse(string Model, PromptResponse Kit, PromptResponse Boiler, PromptResponse ConsumerUnit, IReadOnlyList<string> KitActions);
