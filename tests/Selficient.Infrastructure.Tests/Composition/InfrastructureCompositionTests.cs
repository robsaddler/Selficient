using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Infrastructure.Composition;

namespace Selficient.Infrastructure.Tests.Composition;

public sealed class InfrastructureCompositionTests
{
    [Fact]
    public void Registers_the_clock_and_the_cache()
    {
        ServiceCollection services = new();

        services.AddSelficientInfrastructure();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
        provider.GetRequiredService<HybridCache>().ShouldNotBeNull();
    }

    [Fact]
    public async Task The_cache_holds_a_value_for_a_second_read()
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure();
        await using ServiceProvider provider = services.BuildServiceProvider();
        HybridCache cache = provider.GetRequiredService<HybridCache>();
        int calls = 0;

        string first = await cache.GetOrCreateAsync("k", _ => { calls++; return ValueTask.FromResult("v"); });
        string second = await cache.GetOrCreateAsync("k", _ => { calls++; return ValueTask.FromResult("other"); });

        first.ShouldBe("v");
        second.ShouldBe("v");
        calls.ShouldBe(1);
    }

    [Fact]
    public void Rejects_a_missing_service_collection()
    {
        Should.Throw<ArgumentNullException>(() => InfrastructureServiceCollectionExtensions.AddSelficientInfrastructure(null!));
    }
}
