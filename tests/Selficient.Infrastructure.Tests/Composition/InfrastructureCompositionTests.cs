using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using NSubstitute;
using Selficient.Application.Geography;
using Selficient.Infrastructure.Composition;
using Selficient.Infrastructure.Geography;
using Selficient.Application.Maps;
using Selficient.Infrastructure.Maps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Selficient.Infrastructure.Tests.Composition;

public sealed class InfrastructureCompositionTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    private static IHostEnvironment Environment(string name)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    [Fact]
    public void Registers_the_clock_the_cache_and_real_adapters_by_default()
    {
        ServiceCollection services = new();

        services.AddSelficientInfrastructure(Config(), Environment("Production"));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
        provider.GetRequiredService<HybridCache>().ShouldNotBeNull();
        provider.GetRequiredService<IGeocoder>().ShouldBeOfType<CachedGeocoder>();
        provider.GetRequiredService<IElevationProvider>().ShouldBeOfType<CachedElevationProvider>();
        provider.GetRequiredService<IOptions<UpstreamOptions>>().Value.PostcodesBaseUrl.Host.ShouldBe("api.postcodes.io");
    }

    [Fact]
    public void The_typed_clients_carry_the_fixed_base_address_timeout_and_body_cap()
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config(("Upstreams:TimeoutSeconds", "7")), Environment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PostcodesIoGeocoder));

        client.BaseAddress.ShouldBe(new Uri("https://api.postcodes.io/"));
        client.Timeout.ShouldBe(Timeout.InfiniteTimeSpan, "the resilience pipeline owns the timeouts");
        client.MaxResponseContentBufferSize.ShouldBe(InfrastructureServiceCollectionExtensions.MaxUpstreamBodyBytes);
        HttpStandardResilienceOptions resilience = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get($"{nameof(PostcodesIoGeocoder)}-standard");
        resilience.AttemptTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(7));
        resilience.TotalRequestTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(15));
        resilience.CircuitBreaker.SamplingDuration.ShouldBe(TimeSpan.FromSeconds(30));
        client.DefaultRequestHeaders.UserAgent.ToString().ShouldContain("Selficient");
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenMeteoElevationProvider)).BaseAddress.ShouldBe(new Uri("https://api.open-meteo.com/"));
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenMeteoElevationProvider)).BaseAddress.ShouldBe(new Uri("https://api.open-meteo.com/"), "a second client exercises the cached configuration delegates");
    }

    [Fact]
    public void Fakes_replace_the_adapters_when_switched_on()
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config(("Upstreams:UseFakes", "true")), Environment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IGeocoder>().ShouldBeOfType<FakeGeocoder>();
        provider.GetRequiredService<IElevationProvider>().ShouldBeOfType<FakeElevationProvider>();
    }

    [Fact]
    public void Fakes_are_refused_in_production()
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config(("Upstreams:UseFakes", "true")), Environment("Production"));
        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<UpstreamOptions>>().Value);
        exception.Message.ShouldContain("Upstreams:UseFakes cannot be true in Production.");
    }

    [Theory]
    [InlineData("Upstreams:PostcodesBaseUrl", "http://api.postcodes.io/")]
    [InlineData("Upstreams:ElevationBaseUrl", "ftp://api.open-meteo.com/")]
    [InlineData("Upstreams:TimeoutSeconds", "0")]
    public void Options_refuse_a_non_https_or_wrong_host_base_url(string key, string value)
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config((key, value)), Environment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<UpstreamOptions>>().Value);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5555/")]
    [InlineData("http://localhost:5555/")]
    public void Loopback_http_is_allowed_for_local_stubs(string url)
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config(("Upstreams:PostcodesBaseUrl", url)), Environment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<UpstreamOptions>>().Value.PostcodesBaseUrl.ShouldBe(new Uri(url));
    }

    [Fact]
    public void Relative_urls_are_refused_too()
    {
        UpstreamOptions options = new() { PostcodesBaseUrl = new Uri("postcodes", UriKind.Relative) };

        options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).ShouldHaveSingleItem().MemberNames.ShouldContain("PostcodesBaseUrl");
    }

    [Fact]
    public async Task The_cache_holds_a_value_for_a_second_read()
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config(), Environment("Development"));
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
    public void The_basemap_is_registered_cached_with_its_path_from_configuration()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSelficientInfrastructure(Config(("Map:MbTilesPath", "elsewhere/basemap.mbtiles"), ("Map:RetryAfterSeconds", "5")), Environment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBasemapTiles>().ShouldBeOfType<CachedBasemapTiles>();
        provider.GetRequiredService<MbTilesBasemap>().ArchivePath.ShouldEndWith(Path.Combine("elsewhere", "basemap.mbtiles"));
        provider.GetRequiredService<IOptions<MapOptions>>().Value.RetryAfterSeconds.ShouldBe(5);
        provider.GetRequiredService<IBasemapTiles>().Status().Available.ShouldBeFalse();
    }

    [Fact]
    public void An_empty_basemap_path_is_refused()
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config(("Map:MbTilesPath", "")), Environment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MapOptions>>().Value);
    }

    [Fact]
    public void Rejects_missing_arguments()
    {
        ServiceCollection services = new();
        Should.Throw<ArgumentNullException>(() => InfrastructureServiceCollectionExtensions.AddSelficientInfrastructure(null!, Config(), Environment("Development")));
        Should.Throw<ArgumentNullException>(() => services.AddSelficientInfrastructure(null!, Environment("Development")));
        Should.Throw<ArgumentNullException>(() => services.AddSelficientInfrastructure(Config(), null!));
    }
}
