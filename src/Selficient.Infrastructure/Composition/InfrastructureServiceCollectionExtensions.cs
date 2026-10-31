using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Selficient.Application.Geography;
using Selficient.Application.Kit;
using Selficient.Application.Maps;
using Selficient.Infrastructure.Geography;
using Selficient.Infrastructure.Kit;
using Selficient.Infrastructure.Maps;
using Selficient.Application.Solar;
using Selficient.Infrastructure.Solar;
using Selficient.Application.Ai;
using Selficient.Infrastructure.Ai;
using Selficient.Infrastructure.Images;
using Selficient.Application.Catalogue;
using Selficient.Infrastructure.Catalogue;
using Selficient.Application.Share;
using Selficient.Infrastructure.Share;
using Anthropic;
using Microsoft.Extensions.Logging;

namespace Selficient.Infrastructure.Composition;

/// <summary>Registers every Infrastructure adapter against its Application port.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Largest upstream body accepted, in bytes. postcodes.io answers in under two kilobytes.</summary>
    public const long MaxUpstreamBodyBytes = 64 * 1024;

    /// <summary>
    /// Adds the adapters. Each upstream client is added in the milestone that introduces it, so
    /// this method grows one line per adapter and the registration is covered by a resolution test.
    /// </summary>
    public static IServiceCollection AddSelficientInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton(TimeProvider.System);

        // Every upstream lookup (geocode, elevation, PVGIS, solar) is cached here, keyed on
        // normalised inputs. In-process only in v1; a distributed backing store can be added
        // without touching the callers.
        services.AddHybridCache(options =>
        {
            // Vector tiles (M5) are the largest entries; Zoomstack tiles stay well under a megabyte.
            options.MaximumPayloadBytes = 2 * 1024 * 1024;
            options.MaximumKeyLength = 256;
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromDays(30),
                LocalCacheExpiration = TimeSpan.FromDays(30),
            };
        });

        services.AddOptions<UpstreamOptions>()
            .Bind(configuration.GetSection(UpstreamOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !(options.UseFakes && environment.IsProduction()), "Upstreams:UseFakes cannot be true in Production.")
            .ValidateOnStart();

        services.AddOptions<AffiliateOptions>()
            .Bind(configuration.GetSection(AffiliateOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // The shipped picks are the seed; the pages read the store's catalogue, which the worker loads and verifies.
        services.AddSingleton(static provider => EmbeddedKitCatalogue.Load(provider.GetRequiredService<IOptions<AffiliateOptions>>().Value));
        services.AddOptions<CatalogueOptions>()
            .Bind(configuration.GetSection(CatalogueOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<ICatalogueStore>(provider => new SqliteCatalogueStore(Path.Combine(environment.ContentRootPath, provider.GetRequiredService<IOptions<CatalogueOptions>>().Value.DatabasePath)));
        services.AddSingleton(static provider => new StoredCatalogue(provider.GetRequiredService<ICatalogueStore>(), provider.GetRequiredService<EmbeddedKitCatalogue>()));
        services.AddSingleton<IKitCatalogue>(static provider => provider.GetRequiredService<StoredCatalogue>());
        services.AddHttpClient<HttpPageReader>(static client =>
            {
                client.Timeout = TimeSpan.FromSeconds(25);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Selficient-catalogue-check/1.0");
            })
            .RemoveAllLoggers()
            .AddStandardResilienceHandler();
        services.AddSingleton<IPageReader>(static provider => new HttpPageReader(provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpPageReader)), provider.GetRequiredService<IOptions<CatalogueOptions>>().Value.PageBytes));
        services.AddSingleton(static provider => new OfferVerifier(provider.GetRequiredService<ICatalogueStore>(), provider.GetRequiredService<IPageReader>(), provider.GetRequiredService<IOfferJudge>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(static provider => new CatalogueOperations(provider.GetRequiredService<StoredCatalogue>(), provider.GetRequiredService<OfferVerifier>(), provider.GetRequiredService<ICatalogueStore>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<CatalogueWorker>();
        services.AddHostedService(static provider => provider.GetRequiredService<CatalogueWorker>());
        // The share card is drawn on demand and cached by the endpoint; it carries a number and a scenario, nothing of a house.
        services.AddSingleton<IShareCardRenderer, SkiaShareCardRenderer>();
        // Readers' tips and the deals page: the allowlist is the catalogue's own, so a tip can never point the checker elsewhere.
        services.AddSingleton<IAllowedShops, AffiliateAllowedShops>();
        services.AddSingleton(static provider => new TipBox(provider.GetRequiredService<ICatalogueStore>(), provider.GetRequiredService<IAllowedShops>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(static provider => new DealsReader(provider.GetRequiredService<StoredCatalogue>(), provider.GetRequiredService<ICatalogueStore>(), provider.GetRequiredService<TimeProvider>()));

        // The basemap has no fake: an absent archive is the degraded state, and the tests build
        // tiny archives of their own.
        services.AddOptions<MapOptions>()
            .Bind(configuration.GetSection(MapOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(provider => new MbTilesBasemap(
            provider.GetRequiredService<IOptions<MapOptions>>(),
            environment,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<MbTilesBasemap>>()));
        services.AddSingleton<IBasemapTiles>(static provider => new CachedBasemapTiles(provider.GetRequiredService<MbTilesBasemap>(), provider.GetRequiredService<HybridCache>()));

        // Read as a string rather than binding the whole object here: binding is reflection-based
        // and the trim analyzer is on. The bound, validated options are still what the clients use.
        bool useFakes = bool.TryParse(configuration[$"{UpstreamOptions.SectionName}:{nameof(UpstreamOptions.UseFakes)}"], out bool flag) && flag;

        services.AddOptions<GoogleOptions>()
            .Bind(configuration.GetSection(GoogleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Photos and AI (M8). The sanitiser and the meter are always real; the analyst is the
        // fake, the null object, or Claude, decided by the fakes flag and the key.
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(static o => o.BaseUrl.IsAbsoluteUri && (o.BaseUrl.Scheme == Uri.UriSchemeHttps || (o.BaseUrl.Scheme == Uri.UriSchemeHttp && o.BaseUrl.IsLoopback)), "Anthropic:BaseUrl must be an absolute https URL.")
            .ValidateOnStart();
        services.AddSingleton<IImageSanitiser, SkiaImageSanitiser>();
        services.AddSingleton<IAnalysisMeter, InMemoryAnalysisMeter>();

        if (useFakes)
        {
            services.AddSingleton<IGeocoder, FakeGeocoder>();
            services.AddSingleton<IReverseGeocoder, FakeReverseGeocoder>();
            services.AddSingleton<IElevationProvider, FakeElevationProvider>();
            services.AddSingleton<ISolarYieldProvider, FakeSolarYieldProvider>();
            services.AddSingleton<IRoofInsightsProvider, FakeRoofInsights>();
            services.AddSingleton<FakeAssistant>();
            services.AddSingleton<IPhotoAnalyst>(static provider => provider.GetRequiredService<FakeAssistant>());
            services.AddSingleton<IPlanNarrator>(static provider => provider.GetRequiredService<FakeAssistant>());
            services.AddSingleton<IOfferJudge, FakeOfferJudge>();
            return services;
        }

        string? anthropicKey = configuration[$"{AnthropicOptions.SectionName}:{nameof(AnthropicOptions.ApiKey)}"];
        if (string.IsNullOrEmpty(anthropicKey))
        {
            services.AddSingleton<NoAssistant>();
            services.AddSingleton<IPhotoAnalyst>(static provider => provider.GetRequiredService<NoAssistant>());
            services.AddSingleton<IPlanNarrator>(static provider => provider.GetRequiredService<NoAssistant>());
            services.AddSingleton<IOfferJudge, NoJudge>();
        }
        else
        {
            // The SDK owns its retries and timeout; the key goes in as the SDK sends it, a header.
            services.AddSingleton(static provider =>
            {
                AnthropicOptions options = provider.GetRequiredService<IOptions<AnthropicOptions>>().Value;
                AnthropicClient client = new()
                {
                    ApiKey = options.ApiKey,
                    BaseUrl = options.BaseUrl.ToString().TrimEnd('/'),
                    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                    MaxRetries = 1,
                };
                return new AnthropicAssistant(client, options.Model);
            });
            services.AddSingleton<IPhotoAnalyst>(static provider => provider.GetRequiredService<AnthropicAssistant>());
            services.AddSingleton<IPlanNarrator>(static provider => provider.GetRequiredService<AnthropicAssistant>());
            services.AddSingleton<IOfferJudge>(static provider =>
            {
                AnthropicOptions options = provider.GetRequiredService<IOptions<AnthropicOptions>>().Value;
                return new AnthropicOfferJudge(new AnthropicClient { ApiKey = options.ApiKey, BaseUrl = options.BaseUrl.ToString().TrimEnd('/'), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds), MaxRetries = 1 }, options.Model);
            });
        }

        AddUpstreamClient<PostcodesIoGeocoder>(services, static o => o.PostcodesBaseUrl);
        AddUpstreamClient<PostcodesIoReverseGeocoder>(services, static o => o.PostcodesBaseUrl);
        AddUpstreamClient<OpenMeteoElevationProvider>(services, static o => o.ElevationBaseUrl);
        AddUpstreamClient<PvgisYieldProvider>(services, static o => o.PvgisBaseUrl);

        services.AddSingleton<IGeocoder>(static provider => new CachedGeocoder(provider.GetRequiredService<PostcodesIoGeocoder>(), provider.GetRequiredService<HybridCache>()));
        // Not cached: a location is a point, not a key anyone types twice, and keeping it anywhere is the thing we promised not to do.
        services.AddSingleton<IReverseGeocoder>(static provider => provider.GetRequiredService<PostcodesIoReverseGeocoder>());
        services.AddSingleton<IElevationProvider>(static provider => new CachedElevationProvider(provider.GetRequiredService<OpenMeteoElevationProvider>(), provider.GetRequiredService<HybridCache>()));
        services.AddSingleton<ISolarYieldProvider>(static provider => new CachedSolarYieldProvider(provider.GetRequiredService<PvgisYieldProvider>(), provider.GetRequiredService<HybridCache>()));

        // The Google key is read here as a string for the same reason as the fakes flag; the bound
        // options still validate its shape. No key means the null object, and the page never offers
        // the roof lookup.
        string? googleKey = configuration[$"{GoogleOptions.SectionName}:{nameof(GoogleOptions.SolarApiKey)}"];
        if (string.IsNullOrEmpty(googleKey))
        {
            services.AddSingleton<IRoofInsightsProvider, NoRoofInsights>();
            return services;
        }

        // The key travels as a header, never in the URL, so it is in no log and no referrer.
        AddUpstreamClient<GoogleSolarRoofInsights>(services, static o => o.GoogleSolarBaseUrl, (provider, client) =>
            client.DefaultRequestHeaders.Add("X-Goog-Api-Key", provider.GetRequiredService<IOptions<GoogleOptions>>().Value.SolarApiKey));
        services.AddSingleton<IRoofInsightsProvider>(static provider => provider.GetRequiredService<GoogleSolarRoofInsights>());

        return services;
    }

    /// <summary>One typed client per upstream, each with the fixed base address, body cap and resilience pipeline.</summary>
    private static void AddUpstreamClient<TClient>(IServiceCollection services, Func<UpstreamOptions, Uri> baseUrl, Action<IServiceProvider, HttpClient>? extra = null)
        where TClient : class
    {
        services.AddHttpClient<TClient>((provider, client) =>
            {
                Configure(client, baseUrl(Upstreams(provider)));
                extra?.Invoke(provider, client);
            })
            // The factory's default handlers log every request URL at Information, and the URL
            // carries the postcode. Nothing about these calls is logged below Warning.
            .RemoveAllLoggers()
            .AddStandardResilienceHandler()
            .Configure(Resilience);
    }

    private static UpstreamOptions Upstreams(IServiceProvider provider) => provider.GetRequiredService<IOptions<UpstreamOptions>>().Value;

    private static void Configure(HttpClient client, Uri baseUrl)
    {
        client.BaseAddress = baseUrl;
        // The resilience pipeline owns every timeout (below); the client itself must not race it.
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.MaxResponseContentBufferSize = MaxUpstreamBodyBytes;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Selficient/1.0 (+https://selficient.app)");
    }

    /// <summary>
    /// One attempt may take the configured timeout; the whole call, retries included, twice that
    /// plus a second. The circuit breaker needs a sampling window of at least twice the attempt.
    /// </summary>
    private static void Resilience(HttpStandardResilienceOptions resilience, IServiceProvider provider)
    {
        int seconds = provider.GetRequiredService<IOptions<UpstreamOptions>>().Value.TimeoutSeconds;
        resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(seconds);
        resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds((seconds * 2) + 1);
        resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, seconds * 2));
    }
}
