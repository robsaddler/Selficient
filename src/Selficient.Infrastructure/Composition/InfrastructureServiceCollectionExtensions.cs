using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Selficient.Infrastructure.Composition;

/// <summary>Registers every Infrastructure adapter against its Application port.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds the adapters. Each upstream client is added in the milestone that introduces it, so
    /// this method grows one line per adapter and the registration is covered by a resolution test.
    /// </summary>
    public static IServiceCollection AddSelficientInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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

        return services;
    }
}
