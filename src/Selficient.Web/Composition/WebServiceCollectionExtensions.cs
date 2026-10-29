using Microsoft.AspNetCore.Localization;
using Selficient.Web.Security;

namespace Selficient.Web.Composition;

/// <summary>Registers the web layer: pages, localisation, and the per-request security services.</summary>
public static class WebServiceCollectionExtensions
{
    /// <summary>Adds Razor Pages and the services the pipeline depends on.</summary>
    public static IServiceCollection AddSelficientWeb(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRazorPages();
        services.AddScoped<CspNonce>();

        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
            options.Preload = true;
            // The default excludes localhost, which would also hide the header from the integration
            // tests that prove it is sent. The middleware only runs outside Development anyway.
            options.ExcludedHosts.Clear();
        });

        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(SelficientCulture.EnGb);
            options.SupportedCultures = [SelficientCulture.EnGb];
            options.SupportedUICultures = [SelficientCulture.EnGb];
            // No providers: the Accept-Language header must never switch a household to dollars.
            options.RequestCultureProviders.Clear();
        });

        return services;
    }
}
