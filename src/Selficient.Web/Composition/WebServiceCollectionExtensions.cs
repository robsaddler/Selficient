using Microsoft.AspNetCore.Localization;
using Selficient.Web.Security;

namespace Selficient.Web.Composition;

/// <summary>Registers the web layer: pages, localisation, and the per-request security services.</summary>
public static class WebServiceCollectionExtensions
{
    /// <summary>Adds Razor Pages and the services the pipeline depends on.</summary>
    public static IServiceCollection AddSelficientWeb(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddRazorPages().AddMvcOptions(options =>
        {
            // The framework's binding messages are fragments ("The value 'x' is not valid for Heating.").
            // Every sentence a person reads is whole, so they are replaced here.
            options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor(static (_, _) => "That answer is not one of the options offered. Choose one from the list.");
            options.ModelBindingMessageProvider.SetValueIsInvalidAccessor(static _ => "That answer is not one of the options offered. Choose one from the list.");
            options.ModelBindingMessageProvider.SetMissingKeyOrValueAccessor(static () => "This answer is needed to build the plan.");
            options.ModelBindingMessageProvider.SetMissingRequestBodyRequiredValueAccessor(static () => "This answer is needed to build the plan.");
            options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(static _ => "Enter a whole number.");
            options.ModelBindingMessageProvider.SetMissingBindRequiredValueAccessor(static _ => "This answer is needed to build the plan.");
            options.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(static _ => "This answer is needed to build the plan.");
            options.ModelBindingMessageProvider.SetUnknownValueIsInvalidAccessor(static _ => "That answer is not one of the options offered. Choose one from the list.");
            options.ModelBindingMessageProvider.SetNonPropertyAttemptedValueIsInvalidAccessor(static _ => "That answer is not one of the options offered. Choose one from the list.");
            options.ModelBindingMessageProvider.SetNonPropertyValueMustBeANumberAccessor(static () => "Enter a whole number.");
        });
        services.AddScoped<CspNonce>();
        services.AddSelficientRateLimiting(configuration);

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
