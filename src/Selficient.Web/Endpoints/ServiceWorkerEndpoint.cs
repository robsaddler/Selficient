using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Selficient.Web.Services;

namespace Selficient.Web.Endpoints;

/// <summary>
/// Serves <c>/sw.js</c> from the <c>wwwroot/sw.source.js</c> template with the precache list
/// generated from every static asset on disk and the version derived from their fingerprints.
/// Both are substituted as JSON, never concatenated, so nothing but a string and an array can
/// reach the script.
/// </summary>
public static class ServiceWorkerEndpoint
{
    private const string TemplateFile = "sw.source.js";
    private const string VersionPlaceholder = "'__VERSION__'";
    private const string ShellPlaceholder = "__SHELL__";

    /// <summary>Maps <c>GET /sw.js</c>.</summary>
    public static IEndpointRouteBuilder MapServiceWorkerEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/sw.js", Handle)
            .WithName("ServiceWorker")
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<ContentHttpResult> Handle(ShellVersion shell, IWebHostEnvironment environment, HttpContext context, CancellationToken cancellationToken)
    {
        ShellVersion.Shell current = shell.Current();
        string template = await File.ReadAllTextAsync(Path.Combine(environment.WebRootPath, TemplateFile), cancellationToken);
        string script = template
            .Replace(VersionPlaceholder, JsonSerializer.Serialize(current.Version), StringComparison.Ordinal)
            .Replace(ShellPlaceholder, JsonSerializer.Serialize(current.Urls), StringComparison.Ordinal);

        // Browsers re-check the worker on navigation; no-cache keeps that check honest.
        context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
        return TypedResults.Text(script, "text/javascript; charset=utf-8");
    }
}
