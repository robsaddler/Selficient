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
        // Pounds, degrees and squared metres are everywhere on these pages. The default encoder
        // turns every one into a numeric entity; Latin-1 carries no HTML-significant character, so
        // letting it through is safe and the pages read as written.
        services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(static options =>
            options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.BasicLatin, System.Text.Unicode.UnicodeRanges.Latin1Supplement));
        // Brotli, then gzip, for everything textual including the manifest and the icon. Over
        // HTTPS on purpose: no page carries a secret that an attacker's input sits beside, the
        // antiforgery field is re-encrypted on every render and the nonce changes every request,
        // so the compression side channel has nothing to measure (docs/threat-models/launch.md).
        services.AddResponseCompression(static options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
            options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
            options.MimeTypes =
            [
                .. Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes,
                "application/manifest+json",
                "image/svg+xml",
                "text/javascript",
            ];
        });
        services.AddScoped<CspNonce>();
        services.AddOptions<OperatorOptions>()
            .Bind(configuration.GetSection(OperatorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<OperatorSession>();
        services.AddScoped<CspPolicy>();
        services.AddSingleton<Services.ShellVersion>();
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
