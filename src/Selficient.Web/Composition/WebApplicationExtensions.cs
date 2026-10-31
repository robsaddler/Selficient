using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using Selficient.Web.Endpoints;
using Selficient.Web.Security;
using Serilog;

namespace Selficient.Web.Composition;

/// <summary>The request pipeline, in the order that makes each layer's defence independent.</summary>
public static class WebApplicationExtensions
{
    private const string ImmutableYear = "public, max-age=31536000, immutable";
    private const string Revalidate = "no-cache";

    /// <summary>Configures the pipeline and maps every endpoint.</summary>
    public static WebApplication UseSelficientPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/error/500");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/error/{0}");
        app.UseHttpsRedirection();
        app.UseSerilogRequestLogging();
        app.UseSecurityHeaders();
        app.UseRequestLocalization();
        // Before the static files so the sheets and scripts are compressed too. Tiles arrive
        // already gzipped and are left alone: the middleware never re-encodes an encoded body.
        app.UseResponseCompression();

        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = ContentTypes(),
            OnPrepareResponse = SetStaticCachePolicy,
        });

        app.UseRouting();
        app.UseRateLimiter();
        // Authentication before authorisation, and both before antiforgery, so a reviewer page that
        // refuses an unsigned caller does so before anything reads a form.
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapRazorPages();
        app.MapHealthEndpoint();
        app.MapTilesEndpoints();
        app.MapServiceWorkerEndpoint();
        app.MapAiPromptsEndpoint();
        app.MapGeoEndpoints();
        app.MapOutEndpoint();
        app.MapSeoEndpoints();
        app.MapShareEndpoints();

        return app;
    }

    /// <summary>The framework's map plus the map formats: glyph files are protobuf, the library is a module.</summary>
    private static FileExtensionContentTypeProvider ContentTypes()
    {
        FileExtensionContentTypeProvider provider = new();
        provider.Mappings[".pbf"] = TilesEndpoints.ProtobufContentType;
        provider.Mappings[".mjs"] = "text/javascript";
        provider.Mappings[".webmanifest"] = "application/manifest+json";
        return provider;
    }

    private static void SetStaticCachePolicy(StaticFileResponseContext context)
    {
        PathString path = context.Context.Request.Path;
        IHeaderDictionary headers = context.Context.Response.Headers;

        if (path.StartsWithSegments("/fonts"))
        {
            // Fonts and glyph ranges are versioned by filename and never change in place.
            headers[HeaderNames.CacheControl] = ImmutableYear;
        }
        else if (path.StartsWithSegments("/css") || path.StartsWithSegments("/js") || path.StartsWithSegments("/lib") || path.StartsWithSegments("/img") || path == "/manifest.webmanifest")
        {
            // Scripts and styles are cache-busted by the asp-append-version hash, so the browser
            // may keep them but must revalidate; this is what lets a deploy take effect at once.
            // The vendored map library is imported by bare specifier and gets the same treatment.
            headers[HeaderNames.CacheControl] = Revalidate;
        }
    }
}
