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

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = SetStaticCachePolicy,
        });

        app.UseRouting();
        app.UseRateLimiter();
        app.UseAntiforgery();

        app.MapRazorPages();
        app.MapHealthEndpoint();

        return app;
    }

    private static void SetStaticCachePolicy(StaticFileResponseContext context)
    {
        PathString path = context.Context.Request.Path;
        IHeaderDictionary headers = context.Context.Response.Headers;

        if (path.StartsWithSegments("/fonts"))
        {
            // Fonts are versioned by filename and never change in place.
            headers[HeaderNames.CacheControl] = ImmutableYear;
        }
        else if (path.StartsWithSegments("/css") || path.StartsWithSegments("/js"))
        {
            // Scripts and styles are cache-busted by the asp-append-version hash, so the browser
            // may keep them but must revalidate; this is what lets a deploy take effect at once.
            headers[HeaderNames.CacheControl] = Revalidate;
        }
    }
}
