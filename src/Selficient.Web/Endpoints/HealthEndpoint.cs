using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Selficient.Web.Endpoints;

/// <summary>
/// Two probes. <c>/health</c> is liveness: the process answers, nothing more. <c>/health/ready</c> is
/// readiness: the database answers, the catalogue has loaded, the basemap is present; it says
/// "ready" or "not ready" with the status code and never which, because an anonymous caller is
/// owed the verdict and not the diagnosis. The reviewer page shows the detail to the operator.
/// </summary>
public static class HealthEndpoint
{
    /// <summary>Maps both probes.</summary>
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health", static () => Handle())
            .WithName("Health")
            .ExcludeFromDescription();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteVerdictAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).WithName("Ready").ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>The word for a status: a degraded site still serves.</summary>
    public static string Verdict(HealthStatus status) => status == HealthStatus.Unhealthy ? "not ready" : "ready";

    private static ContentHttpResult Handle() => TypedResults.Text("ok");

    private static Task WriteVerdictAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(Verdict(report.Status));
    }
}
