using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Selficient.TestKit;
using Selficient.Web.Diagnostics;
using Selficient.Web.Endpoints;

namespace Selficient.Web.Tests.Diagnostics;

public sealed class TelemetryTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Theory]
    [InlineData("/", true)]
    [InlineData("/plan", true)]
    [InlineData("/out/3", true)]
    [InlineData("/health", false)]
    [InlineData("/health/ready", false)]
    [InlineData("/HEALTH", false)]
    [InlineData("/api/tiles/14/8000/5000.pbf", false)]
    [InlineData("/api/geo/nearest", true)]
    public void Probes_and_tiles_are_not_traced_and_everything_else_is(string path, bool traced) => Telemetry.Traced(path).ShouldBe(traced);

    [Fact]
    public void An_upstream_span_keeps_the_host_and_loses_the_postcode()
    {
        using Activity activity = new("HTTP GET");
        activity.Start();
        activity.SetTag("url.full", "https://api.postcodes.io/postcodes/NP12%201GW");
        activity.SetTag("url.path", "/postcodes/NP12%201GW");
        activity.SetTag("url.query", "?q=1");
        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.open-meteo.com/v1/elevation?latitude=51.6655&longitude=-3.2031");

        Telemetry.Redact(activity, request);

        activity.GetTagItem("url.full").ShouldBe("https://api.open-meteo.com/");
        activity.GetTagItem("url.path").ShouldBe("/");
        activity.GetTagItem("url.query").ShouldBeNull();
        activity.DisplayName.ShouldBe("GET api.open-meteo.com");

        activity.SetTag("url.full", "https://api.postcodes.io/postcodes/NP12%201GW");
        activity.SetTag("url.query", "?x");
        Telemetry.RedactUrl(activity);
        activity.GetTagItem("url.full").ShouldBe("https://api.postcodes.io/");
        activity.GetTagItem("url.query").ShouldBeNull();

        activity.SetTag("url.full", "not a url");
        Telemetry.RedactUrl(activity);
        activity.GetTagItem("url.full").ShouldBe("not a url", "a tag that is not an address is left alone");
        using HttpRequestMessage relative = new(HttpMethod.Get, new Uri("/relative", UriKind.Relative));
        Telemetry.Redact(activity, relative);
        activity.GetTagItem("url.full").ShouldBe("not a url", "a relative request names no host to keep");
        Should.Throw<ArgumentNullException>(() => Telemetry.Redact(null!, request));
        Should.Throw<ArgumentNullException>(() => Telemetry.Redact(activity, null!));
        Should.Throw<ArgumentNullException>(() => Telemetry.RedactUrl(null!));
    }

    [Fact]
    public void Exporting_needs_an_endpoint_and_the_pipelines_register_either_way()
    {
        Telemetry.Exporting(null).ShouldBeFalse();
        Telemetry.Exporting(" ").ShouldBeFalse();
        Telemetry.Exporting("http://localhost:4317").ShouldBeTrue();
        Telemetry.Version(new System.Reflection.AssemblyName { Version = new Version(1, 2, 3, 4) }).ShouldBe("1.2.3");
        Telemetry.Version(new System.Reflection.AssemblyName()).ShouldBe("0.0.0");
        Should.Throw<ArgumentNullException>(() => Telemetry.Version(null!));

        ServiceCollection quiet = new();
        quiet.AddSelficientTelemetry(null, "1.0");
        ServiceCollection loud = new();
        loud.AddSelficientTelemetry("http://localhost:4317", "1.0");

        quiet.Count.ShouldBe(0, "with no collector named nothing is instrumented, so no request pays for a span nobody reads");
        loud.Count.ShouldBeGreaterThan(0, "a named collector gets the whole pipeline");
        // Registering only stores the two configuration delegates; they run when the providers are
        // built, so the traces and the metrics are only proved to be wired by building them.
        loud.AddLogging();
        using (ServiceProvider built = loud.BuildServiceProvider())
        {
            built.GetRequiredService<OpenTelemetry.Trace.TracerProvider>().ShouldNotBeNull();
            built.GetRequiredService<OpenTelemetry.Metrics.MeterProvider>().ShouldNotBeNull();
        }

        Should.Throw<ArgumentNullException>(() => Telemetry.AddSelficientTelemetry(null!, null, "1.0"));
        Should.Throw<ArgumentException>(() => quiet.AddSelficientTelemetry(null, ""));
    }

    [Fact]
    public async Task Readiness_says_ready_or_not_and_nothing_else_while_liveness_says_ok()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage live = await client.GetAsync(new Uri("/health", UriKind.Relative));
        using HttpResponseMessage ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        string verdict = await ready.Content.ReadAsStringAsync();

        (await live.Content.ReadAsStringAsync()).ShouldBe("ok");
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        verdict.ShouldBe("ready");
        ready.Headers.CacheControl!.NoStore.ShouldBeTrue();
        ready.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        HealthEndpoint.Verdict(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded).ShouldBe("ready", "a degraded site still serves");
        HealthEndpoint.Verdict(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy).ShouldBe("not ready");
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService checks = _factory.Services.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report = await checks.CheckHealthAsync(TestContext.Current.CancellationToken);
        report.Entries.Keys.ShouldBe(["database", "catalogue", "basemap"], ignoreOrder: true);
        report.Entries["database"].Status.ShouldBe(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);
    }

    public void Dispose() => _factory.Dispose();
}
