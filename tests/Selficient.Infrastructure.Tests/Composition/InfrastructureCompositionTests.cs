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
    // Every composition carries a database address, as the host must; these tests never open it unless they say so.
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection((pairs.Any(p => p.Key == "Database:ConnectionString") ? pairs : pairs.Prepend((Key: "Database:ConnectionString", Value: "Server=localhost;Database=selficient_never_opened;Integrated Security=true;TrustServerCertificate=true"))).Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

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
    public async Task The_cache_holds_a_value_for_a_second_read_and_for_a_second_process()
    {
        using Selficient.TestKit.TestDatabase database = Selficient.TestKit.TestDatabase.Create();
        await new Selficient.Infrastructure.Data.SqlMigrator(database.ConnectionString).MigrateAsync(CancellationToken.None);
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config(("Database:ConnectionString", database.ConnectionString)), Environment("Development"));
        await using ServiceProvider provider = services.BuildServiceProvider();
        HybridCache cache = provider.GetRequiredService<HybridCache>();
        int calls = 0;

        string first = await cache.GetOrCreateAsync("k", _ => { calls++; return ValueTask.FromResult("v"); });
        string second = await cache.GetOrCreateAsync("k", _ => { calls++; return ValueTask.FromResult("other"); });
        // The backend itself, driven directly: HybridCache logs and swallows a failing write, so this is where a broken table shows.
        Microsoft.Extensions.Caching.Distributed.IDistributedCache backend = provider.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        await backend.SetAsync("probe", [1, 2, 3], new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        (await backend.GetAsync("probe")).ShouldBe([1, 2, 3]);
        // HybridCache writes the backend after it has answered the caller, so the row is waited for rather than assumed.
        await using Microsoft.Data.SqlClient.SqlConnection connection = new(database.ConnectionString);
        int rows = 0;
        for (int attempt = 0; attempt < 50 && rows < 2; attempt++)
        {
            rows = await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection, "SELECT COUNT(*) FROM dbo.cache");
            await Task.Delay(100);
        }

        rows.ShouldBe(2, "the value is written through to the database");
        // Another process over the same database finds the value in the shared cache and never calls the factory.
        ServiceCollection other = new();
        other.AddSelficientInfrastructure(Config(("Database:ConnectionString", database.ConnectionString)), Environment("Development"));
        await using ServiceProvider otherProvider = other.BuildServiceProvider();
        string elsewhere = await otherProvider.GetRequiredService<HybridCache>().GetOrCreateAsync("k", _ => { calls++; return ValueTask.FromResult("cold"); });

        first.ShouldBe("v");
        second.ShouldBe("v");
        elsewhere.ShouldBe("v", "the cache outlives the process because it is in the database");
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
    public void The_local_machine_sends_mail_through_papercut_without_being_told_to()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSelficientInfrastructure(Config(), Environment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        Selficient.Application.Accounts.IEmailSender mail = provider.GetRequiredService<Selficient.Application.Accounts.IEmailSender>();
        Selficient.Infrastructure.Email.EmailOptions options = provider.GetRequiredService<IOptions<Selficient.Infrastructure.Email.EmailOptions>>().Value;

        mail.ShouldBeOfType<Selficient.Infrastructure.Email.SmtpEmailSender>();
        options.SmtpHost.ShouldBe("localhost");
        options.SmtpPort.ShouldBe(25);
        provider.GetRequiredService<Selficient.Application.Accounts.AccountSignIn>().IsAvailable.ShouldBeTrue();
        provider.GetRequiredService<Selficient.Application.Accounts.IAccountStore>().ShouldBeOfType<Selficient.Infrastructure.Accounts.SqlAccountStore>();
        provider.GetRequiredService<Selficient.Application.Accounts.IPlanStore>().ShouldBeOfType<Selficient.Infrastructure.Accounts.SqlPlanStore>();
        provider.GetRequiredService<Selficient.Application.Accounts.IPasskeyStore>().ShouldBeOfType<Selficient.Infrastructure.Accounts.SqlPasskeyStore>();
        provider.GetRequiredService<Selficient.Application.Accounts.IPasskeyCeremony>().ShouldBeOfType<Selficient.Infrastructure.Accounts.Fido2PasskeyCeremony>();
        provider.GetRequiredService<Selficient.Application.Accounts.Passkeys>().ShouldNotBeNull();
        Selficient.Infrastructure.Accounts.PasskeyOptions passkeys = provider.GetRequiredService<IOptions<Selficient.Infrastructure.Accounts.PasskeyOptions>>().Value;
        passkeys.RelyingPartyId.ShouldBe("selficient.app", "a passkey is bound to the site's own address unless a server is told otherwise");
        passkeys.RelyingPartyName.ShouldBe("Selficient");
        passkeys.Origins.ShouldBe(["https://selficient.app"]);
        provider.GetServices<Selficient.Application.Operations.IExpiring>().Select(e => e.GetType().Name).ShouldBe(["HandoffDesk", "AccountSignIn"], "everything that expires is swept by the one housekeeper");
        provider.GetRequiredService<Selficient.Infrastructure.Data.Housekeeper>().ShouldNotBeNull();
    }

    [Fact]
    public void A_server_told_nothing_about_mail_sends_none_and_offers_no_account()
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config(), Environment("Production"));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<Selficient.Application.Accounts.IEmailSender>().ShouldBeOfType<Selficient.Infrastructure.Email.NoEmailSender>();
        provider.GetRequiredService<Selficient.Application.Accounts.AccountSignIn>().IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void Production_sends_through_postmark_with_the_token_as_a_header()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSelficientInfrastructure(Config(("Email:Provider", "Postmark"), ("Email:PostmarkServerToken", "a-server-token"), ("Email:TimeoutSeconds", "7")), Environment("Production"));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<Selficient.Application.Accounts.IEmailSender>().ShouldBeOfType<Selficient.Infrastructure.Email.PostmarkEmailSender>();
        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Selficient.Infrastructure.Email.PostmarkEmailSender));
        client.BaseAddress.ShouldBe(new Uri("https://api.postmarkapp.com/"));
        client.Timeout.ShouldBe(TimeSpan.FromSeconds(7));
        client.DefaultRequestHeaders.GetValues(Selficient.Infrastructure.Email.PostmarkEmailSender.TokenHeader).ShouldBe(["a-server-token"]);
        client.DefaultRequestHeaders.Accept.ToString().ShouldBe("application/json");
    }

    [Theory]
    [InlineData("Email:Provider", "Postmark", "Email:PostmarkServerToken is required when Email:Provider is Postmark. Set the Email__PostmarkServerToken environment variable.")]
    [InlineData("Email:Provider", "Recording", "Email:Provider must be None, Smtp or Postmark.")]
    [InlineData("Email:Provider", "Sendmail", "Email:Provider must be None, Smtp or Postmark.")]
    [InlineData("Email:PostmarkBaseUrl", "http://api.postmarkapp.com/", "Email:PostmarkBaseUrl must be an absolute https URL.")]
    public void Mail_settings_that_cannot_work_stop_the_server_starting(string key, string value, string sentence)
    {
        ServiceCollection services = new();
        services.AddSelficientInfrastructure(Config((key, value)), Environment("Production"));
        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException refused = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<Selficient.Infrastructure.Email.EmailOptions>>().Value);

        refused.Message.ShouldContain(sentence);
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
