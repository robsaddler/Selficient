using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Selficient.TestKit;

/// <summary>
/// Hosts the real application in memory. Defaults to the Production environment so tests exercise
/// the hardened pipeline; <see cref="ForEnvironment"/> switches when a test needs Development.
/// </summary>
public sealed class SelficientWebApplicationFactory : WebApplicationFactory<Program>
{
    private string _environment = "Testing";
    private bool _useFakes = true;

    private readonly List<(string Key, string Value)> _settings = [];
    private readonly List<Action<IWebHostBuilder>> _configure = [];

    /// <summary>Use the real adapters (only for tests that point them at a local stub server).</summary>
    public SelficientWebApplicationFactory WithRealUpstreams()
    {
        _useFakes = false;
        return this;
    }

    /// <summary>Overrides one configuration value, for example a rate limit.</summary>
    public SelficientWebApplicationFactory WithSetting(string key, string value)
    {
        _settings.Add((key, value));
        return this;
    }

    /// <summary>Adds arbitrary host configuration, for example a capturing log sink.</summary>
    public SelficientWebApplicationFactory Configuring(Action<IWebHostBuilder> configure)
    {
        _configure.Add(configure);
        return this;
    }

    /// <summary>Hosts over HTTPS so HSTS and the redirect middleware behave as in production.</summary>
    public SelficientWebApplicationFactory()
    {
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    /// <summary>A factory that hosts the application under <paramref name="environment"/>.</summary>
    public SelficientWebApplicationFactory ForEnvironment(string environment)
    {
        _environment = environment;
        return this;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(_environment);
        // Tests never touch the network: the deterministic fakes stand in for every upstream. The
        // Production check on this flag is covered separately in the Infrastructure tests.
        builder.UseSetting("Upstreams:UseFakes", _useFakes ? "true" : "false");
        foreach ((string key, string value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        foreach (Action<IWebHostBuilder> configure in _configure)
        {
            configure(builder);
        }
    }
}
