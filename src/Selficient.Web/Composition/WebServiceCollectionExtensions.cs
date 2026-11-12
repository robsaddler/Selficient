using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Selficient.Web.Diagnostics;
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
        AddOperatorSignIn(services);
        AddKeyRing(services);
        services.AddScoped<CspPolicy>();
        services.AddSingleton<Services.ShellVersion>();
        services.AddSelficientRateLimiting(configuration);
        services.AddSelficientTelemetry(configuration[Telemetry.EndpointVariable], Telemetry.Version(typeof(WebServiceCollectionExtensions).Assembly.GetName()));

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

    /// <summary>
    /// The operator's sign-in: the cookie scheme, the three policies it carries, the sign-in use
    /// case and the protector the application layer encrypts the second-factor secrets with. The
    /// scheme re-reads the account on every request, so a changed password or a disabled account
    /// ends every live session at once rather than at the next expiry.
    /// </summary>
    private static void AddOperatorSignIn(IServiceCollection services)
    {
        services.AddScoped<Selficient.Application.Security.ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<Selficient.Application.Security.OperatorSignIn>();
        services.AddScoped<OperatorCookieEvents>();
        services.AddHostedService<OperatorBootstrap>();
        services.AddAuthentication(OperatorAuth.Scheme)
            .AddCookie(OperatorAuth.Scheme, options =>
            {
                options.Cookie.Name = OperatorAuth.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/admin";
                options.Cookie.IsEssential = true;
                options.LoginPath = "/admin/sign-in";
                options.LogoutPath = "/admin/sign-out";
                options.AccessDeniedPath = "/admin/sign-in";
                options.SlidingExpiration = true;
                options.EventsType = typeof(OperatorCookieEvents);
            })
            // A household's session: its own scheme and cookie, on every page, for ninety days sliding. Lax, not strict,
            // so a link followed from elsewhere arrives signed in; every change it can make is a post with a token.
            .AddCookie(HouseholdAuth.Scheme, options =>
            {
                options.Cookie.Name = HouseholdAuth.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.IsEssential = true;
                options.LoginPath = HouseholdAuth.SignInPath;
                options.ExpireTimeSpan = HouseholdAuth.SessionLength;
                options.SlidingExpiration = true;
                options.EventsType = typeof(HouseholdCookieEvents);
            });
        services.AddScoped<HouseholdCookieEvents>();
        services.AddSingleton<PendingSignInCookie>();
        services.AddAuthorizationBuilder()
            .AddPolicy(OperatorAuth.Policy, policy => policy
                .AddAuthenticationSchemes(OperatorAuth.Scheme)
                .RequireAuthenticatedUser()
                // A half-signed-in operator has passed the password and nothing else; the reviewer
                // is not theirs until the second factor is done, nor until a password owed is paid.
                .RequireAssertion(context => !OperatorAuth.IsPending(context.User) && !OperatorAuth.MustChangePassword(context.User)))
            .AddPolicy(OperatorAuth.SignedInPolicy, policy => policy
                .AddAuthenticationSchemes(OperatorAuth.Scheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !OperatorAuth.IsPending(context.User)))
            .AddPolicy(OperatorAuth.PendingPolicy, policy => policy
                .AddAuthenticationSchemes(OperatorAuth.Scheme)
                .RequireAuthenticatedUser())
            .AddPolicy(HouseholdAuth.Policy, policy => policy
                .AddAuthenticationSchemes(HouseholdAuth.Scheme)
                .RequireAuthenticatedUser());
    }

    /// <summary>
    /// The key ring that signs the operator's cookie and the antiforgery tokens lives in the platform
    /// database, so it survives a recycle and a redeploy; on Windows the machine's DPAPI wraps every key at
    /// rest, so the table is useless off the box. The platform guard is the analyzer's and runs one way per
    /// operating system, which is why this method is the one composition step outside the gate.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "The DPAPI guard runs one way per operating system; the ring itself is proved by the reviewer's tests, whose cookie and protected secrets round-trip through it.")]
    private static void AddKeyRing(IServiceCollection services)
    {
        Microsoft.AspNetCore.DataProtection.IDataProtectionBuilder protection = services.AddDataProtection().SetApplicationName("Selficient");
        if (OperatingSystem.IsWindows())
        {
            protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>>(static provider =>
            new Microsoft.Extensions.Options.ConfigureOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>(options => options.XmlRepository = new SqlXmlRepository(provider.GetRequiredService<Selficient.Application.Operations.IKeyRingStore>())));
    }
}
