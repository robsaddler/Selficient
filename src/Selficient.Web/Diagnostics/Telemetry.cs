using System.Diagnostics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Selficient.Web.Diagnostics;

/// <summary>
/// Traces and metrics through OpenTelemetry: every request, every upstream call, the runtime's
/// counters. They leave through OTLP to whatever <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> names; with
/// nothing named, nothing is exported and nothing is lost but the view. Two things never reach a
/// collector: the probes and the tiles, which would drown everything else, and any address that
/// could carry a postcode, which is cut to its host before the span is finished.
/// </summary>
public static class Telemetry
{
    /// <summary>The service's name on every signal.</summary>
    public const string ServiceName = "selficient";

    /// <summary>The standard variable that names the collector.</summary>
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>The build's version as the signals carry it: three parts, or nought when the assembly has none.</summary>
    public static string Version(System.Reflection.AssemblyName assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.Version is { } version ? version.ToString(3) : "0.0.0";
    }

    /// <summary>Whether a collector is named.</summary>
    public static bool Exporting(string? endpoint) => !string.IsNullOrWhiteSpace(endpoint);

    /// <summary>Adds the pipelines.</summary>
    public static IServiceCollection AddSelficientTelemetry(this IServiceCollection services, string? endpoint, string version)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(version);
        if (!Exporting(endpoint))
        {
            // No collector, no instrumentation: an activity per request that nobody reads is a cost with no reader.
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName, serviceVersion: version))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options => options.Filter = context => Traced(context.Request.Path))
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.EnrichWithHttpRequestMessage = Redact;
                        options.EnrichWithHttpResponseMessage = static (activity, _) => RedactUrl(activity);
                    });
                tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                metrics.AddOtlpExporter();
            });
        return services;
    }

    /// <summary>Whether a request path is worth a span: not the probes, not the tiles.</summary>
    public static bool Traced(PathString path) =>
        !path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/api/tiles", StringComparison.OrdinalIgnoreCase);

    /// <summary>Cuts an upstream span's address to its origin: the postcodes.io path is a postcode and the elevation query is a point.</summary>
    public static void Redact(Activity activity, HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is { IsAbsoluteUri: true } uri)
        {
            activity.SetTag("url.full", uri.GetLeftPart(UriPartial.Authority) + "/");
            activity.SetTag("url.path", "/");
            activity.SetTag("url.query", null);
            activity.DisplayName = $"{request.Method} {uri.Host}";
        }
    }

    /// <summary>The same cut after the response, in case the instrumentation wrote the address again.</summary>
    public static void RedactUrl(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.GetTagItem("url.full") is string full && Uri.TryCreate(full, UriKind.Absolute, out Uri? uri))
        {
            activity.SetTag("url.full", uri.GetLeftPart(UriPartial.Authority) + "/");
            activity.SetTag("url.query", null);
        }
    }
}
