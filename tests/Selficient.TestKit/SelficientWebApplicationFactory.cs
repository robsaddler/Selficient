using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Selficient.TestKit;

/// <summary>
/// Hosts the real application in memory. Defaults to the Production environment so tests exercise
/// the hardened pipeline; <see cref="ForEnvironment"/> switches when a test needs Development.
/// </summary>
public sealed class SelficientWebApplicationFactory : WebApplicationFactory<Program>
{
    private string _environment = "Production";

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
    }
}
