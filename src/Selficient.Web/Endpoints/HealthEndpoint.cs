using Microsoft.AspNetCore.Http.HttpResults;

namespace Selficient.Web.Endpoints;

/// <summary>
/// The liveness probe the deploy workflow polls. It says only that the process answers; it never
/// describes versions, dependencies or configuration to an anonymous caller.
/// </summary>
public static class HealthEndpoint
{
    /// <summary>Maps <c>GET /health</c>.</summary>
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health", static () => Handle())
            .WithName("Health")
            .ExcludeFromDescription();

        return endpoints;
    }

    private static ContentHttpResult Handle() => TypedResults.Text("ok");
}
