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
using Selficient.Infrastructure.Data;
using Selficient.Infrastructure.Health;
using Selficient.Application.Operations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.SqlServer;
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
        // The platform database (M19): SQL Server through Dapper, migrated before anything else starts.
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(static provider => new SqlMigrator(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddHostedService<MigrationStartup>();

        // The figures in force: read once the schema is current, held in memory, and read again after every publication.
        services.AddSingleton<Selficient.Application.Figures.IFigureStore>(static provider => new Selficient.Infrastructure.Figures.SqlFigureStore(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddSingleton(static provider => new Selficient.Application.Figures.CurrentFigures(provider.GetRequiredService<Selficient.Application.Figures.IFigureStore>()));
        services.AddSingleton<Selficient.Application.Figures.ICurrentFigures>(static provider => provider.GetRequiredService<Selficient.Application.Figures.CurrentFigures>());
        services.AddSingleton(static provider => new Selficient.Application.Figures.FigureDesk(
            provider.GetRequiredService<Selficient.Application.Figures.IFigureStore>(),
            provider.GetRequiredService<Selficient.Application.Figures.CurrentFigures>(),
            provider.GetRequiredService<IOperationsLog>(),
            provider.GetRequiredService<Selficient.Domain.Risks.RiskAssessmentPipeline>(),
            provider.GetRequiredService<Selficient.Domain.Plans.PlanBuilder>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddHostedService<Selficient.Infrastructure.Figures.FiguresStartup>();
        services.AddSingleton<ICatalogueStore>(static provider => new SqlServerCatalogueStore(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        // The operations log: every hand the operator takes and every run of every job, in the database.
        services.AddSingleton<IOperationsLog>(static provider => new SqlOperationsLog(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<Selficient.Application.Security.IOperatorStore>(static provider => new Selficient.Infrastructure.Security.SqlOperatorStore(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        // The web layer's key ring lives in the database too; the web composition root wires it to Data Protection.
        services.AddSingleton<IKeyRingStore>(static provider => new SqlKeyRingStore(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        // Sending a copy to another device: ciphertext we cannot read, for ten minutes, taken once.
        services.AddSingleton<Selficient.Application.Handoff.IHandoffStore>(static provider => new Selficient.Infrastructure.Handoff.SqlHandoffStore(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddSingleton(static provider => new Selficient.Application.Handoff.HandoffDesk(provider.GetRequiredService<Selficient.Application.Handoff.IHandoffStore>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IExpiring>(static provider => provider.GetRequiredService<Selficient.Application.Handoff.HandoffDesk>());
        // Household accounts (M23): the store, the mailer chosen by configuration, and the sign-in over both.
        services.AddOptions<Selficient.Infrastructure.Email.EmailOptions>()
            .Bind(configuration.GetSection(Selficient.Infrastructure.Email.EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(static o => o.PostmarkIsComplete, "Email:PostmarkServerToken is required when Email:Provider is Postmark. Set the Email__PostmarkServerToken environment variable.")
            .Validate(static o => o.PostmarkUrlIsSafe, "Email:PostmarkBaseUrl must be an absolute https URL.")
            .ValidateOnStart();
        AddEmailSender(services, Selficient.Infrastructure.Email.EmailOptions.Resolve(configuration[$"{Selficient.Infrastructure.Email.EmailOptions.SectionName}:{nameof(Selficient.Infrastructure.Email.EmailOptions.Provider)}"], environment.IsDevelopment()));
        services.AddSingleton<Selficient.Application.Accounts.IAccountStore>(static provider => new Selficient.Infrastructure.Accounts.SqlAccountStore(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddSingleton(static provider => new Selficient.Application.Accounts.AccountSignIn(provider.GetRequiredService<Selficient.Application.Accounts.IAccountStore>(), provider.GetRequiredService<Selficient.Application.Accounts.IEmailSender>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IExpiring>(static provider => provider.GetRequiredService<Selficient.Application.Accounts.AccountSignIn>());
        // Passkeys (M25): the store, the WebAuthn ceremonies for this site's own address, and the use case over both.
        services.AddOptions<Selficient.Infrastructure.Accounts.PasskeyOptions>()
            .Bind(configuration.GetSection(Selficient.Infrastructure.Accounts.PasskeyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<Selficient.Application.Accounts.IPasskeyStore>(static provider => new Selficient.Infrastructure.Accounts.SqlPasskeyStore(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddSingleton<Selficient.Application.Accounts.IPasskeyCeremony, Selficient.Infrastructure.Accounts.Fido2PasskeyCeremony>();
        services.AddSingleton(static provider => new Selficient.Application.Accounts.Passkeys(
            provider.GetRequiredService<Selficient.Application.Accounts.IPasskeyStore>(),
            provider.GetRequiredService<Selficient.Application.Accounts.IAccountStore>(),
            provider.GetRequiredService<Selficient.Application.Accounts.IPasskeyCeremony>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<Selficient.Application.Accounts.IPlanStore>(static provider => new Selficient.Infrastructure.Accounts.SqlPlanStore(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        // Street groups: who is in what, and a record for each that only its members can read.
        services.AddSingleton<Selficient.Application.Streets.IStreetStore>(static provider => new Selficient.Infrastructure.Streets.SqlStreetStore(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddSingleton(static provider => new Selficient.Application.Streets.StreetGroups(provider.GetRequiredService<Selficient.Application.Streets.IStreetStore>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IExpiring>(static provider => provider.GetRequiredService<Selficient.Application.Streets.StreetGroups>());
        // The housekeeper removes whatever has run out, once a minute, and tells the run log once a day.
        services.AddSingleton<Housekeeper>();
        services.AddHostedService(static provider => provider.GetRequiredService<Housekeeper>());
        // The distributed cache behind HybridCache, in the same database, so a warm cache outlives the process. Tiles stay in process only.
        services.AddOptions<SqlServerCacheOptions>().Configure<IOptions<DatabaseOptions>>(static (cache, database) =>
        {
            cache.ConnectionString = database.Value.ConnectionString;
            cache.SchemaName = "dbo";
            cache.TableName = "cache";
            cache.ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(30);
        });
        services.AddDistributedSqlServerCache(static _ => { });
        // What the readiness probe asks: the database answers, the catalogue has loaded, the basemap is present.
        services.AddSingleton(static provider => new DatabaseHealthCheck(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
            .AddCheck<CatalogueHealthCheck>("catalogue", tags: ["ready"])
            .AddCheck<BasemapHealthCheck>("basemap", tags: ["ready"]);
        services.AddSingleton(static provider => new StoredCatalogue(provider.GetRequiredService<ICatalogueStore>(), provider.GetRequiredService<EmbeddedKitCatalogue>(), EmbeddedKitCatalogue.Retired(provider.GetRequiredService<IOptions<AffiliateOptions>>().Value)));
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
        services.AddSingleton<IDealsReader>(static provider => new CachedDealsReader(provider.GetRequiredService<DealsReader>(), provider.GetRequiredService<TimeProvider>()));
        // The click counter: the hop increments memory, the flusher writes the store once a second and once more at stop.
        services.AddSingleton<ClickCounter>();
        services.AddSingleton<ClickFlusher>();
        services.AddHostedService(static provider => provider.GetRequiredService<ClickFlusher>());

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
            services.AddSingleton<IFloodRiskProvider, FakeFloodRiskProvider>();
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
        // The three flood maps there are. Northern Ireland has none we may ask, and the plan says so by sending a household there to look.
        AddUpstreamClient<EnvironmentAgencyFloodMap>(services, static o => o.EnvironmentAgencyBaseUrl);
        AddUpstreamClient<NaturalResourcesWalesFloodMap>(services, static o => o.DataMapWalesBaseUrl);
        AddUpstreamClient<SepaFloodMap>(services, static o => o.SepaBaseUrl);
        services.AddSingleton<IFloodRiskProvider>(static provider => new CachedFloodRiskProvider(
            new NationalFloodMaps([provider.GetRequiredService<EnvironmentAgencyFloodMap>(), provider.GetRequiredService<NaturalResourcesWalesFloodMap>(), provider.GetRequiredService<SepaFloodMap>()]),
            provider.GetRequiredService<HybridCache>()));

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

    /// <summary>
    /// The one mailer this server uses. SMTP for the local machine's Papercut, the Postmark API for
    /// production, and none at all when nothing is configured, in which case the site offers no
    /// accounts. The tests put a recorder of their own in its place.
    /// </summary>
    private static void AddEmailSender(IServiceCollection services, string provider)
    {
        if (provider == Selficient.Infrastructure.Email.EmailOptions.Smtp)
        {
            services.AddSingleton<Selficient.Application.Accounts.IEmailSender, Selficient.Infrastructure.Email.SmtpEmailSender>();
        }
        else if (provider == Selficient.Infrastructure.Email.EmailOptions.Postmark)
        {
            // No retries: a code sent twice is two emails. The token is a header, so it is in no log and no address.
            services.AddHttpClient<Selficient.Infrastructure.Email.PostmarkEmailSender>(static (provider, client) =>
                {
                    Selficient.Infrastructure.Email.EmailOptions options = provider.GetRequiredService<IOptions<Selficient.Infrastructure.Email.EmailOptions>>().Value;
                    client.BaseAddress = options.PostmarkBaseUrl;
                    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                    client.DefaultRequestHeaders.Add(Selficient.Infrastructure.Email.PostmarkEmailSender.TokenHeader, options.PostmarkServerToken);
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                })
                .RemoveAllLoggers();
            services.AddSingleton<Selficient.Application.Accounts.IEmailSender>(static provider => provider.GetRequiredService<Selficient.Infrastructure.Email.PostmarkEmailSender>());
        }
        else
        {
            services.AddSingleton<Selficient.Application.Accounts.IEmailSender, Selficient.Infrastructure.Email.NoEmailSender>();
        }
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
