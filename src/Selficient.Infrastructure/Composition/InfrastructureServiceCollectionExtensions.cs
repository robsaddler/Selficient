using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Selficient.Application.Geography;
using Selficient.Infrastructure.Geography;

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
            options.MaximumPayloadBytes = 256 * 1024;
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

        // Read as a string rather than binding the whole object here: binding is reflection-based
        // and the trim analyzer is on. The bound, validated options are still what the clients use.
        bool useFakes = bool.TryParse(configuration[$"{UpstreamOptions.SectionName}:{nameof(UpstreamOptions.UseFakes)}"], out bool flag) && flag;

        if (useFakes)
        {
            services.AddSingleton<IGeocoder, FakeGeocoder>();
            services.AddSingleton<IElevationProvider, FakeElevationProvider>();
            return services;
        }

        AddUpstreamClient<PostcodesIoGeocoder>(services, static o => o.PostcodesBaseUrl);
        AddUpstreamClient<OpenMeteoElevationProvider>(services, static o => o.ElevationBaseUrl);

        services.AddSingleton<IGeocoder>(static provider => new CachedGeocoder(provider.GetRequiredService<PostcodesIoGeocoder>(), provider.GetRequiredService<HybridCache>()));
        services.AddSingleton<IElevationProvider>(static provider => new CachedElevationProvider(provider.GetRequiredService<OpenMeteoElevationProvider>(), provider.GetRequiredService<HybridCache>()));

        return services;
    }

    /// <summary>One typed client per upstream, each with the fixed base address, body cap and resilience pipeline.</summary>
    private static void AddUpstreamClient<TClient>(IServiceCollection services, Func<UpstreamOptions, Uri> baseUrl)
        where TClient : class
    {
        services.AddHttpClient<TClient>((provider, client) => Configure(client, baseUrl(Upstreams(provider))))
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
