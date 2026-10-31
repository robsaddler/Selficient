using System.Globalization;
using Selficient.Application.Composition;
using Selficient.Infrastructure.Composition;
using Selficient.Web.Composition;
using Selficient.Web.Diagnostics;
using Serilog;

// The composition root. Everything it wires is covered through WebApplicationFactory in
// Selficient.Web.Tests; the entry point itself is the one documented coverage exclusion.
CultureInfo.DefaultThreadCurrentCulture = SelficientCulture.EnGb;
CultureInfo.DefaultThreadCurrentUICulture = SelficientCulture.EnGb;

// A burst of households after a storm warning must not queue behind thread-pool growth.
System.Threading.ThreadPool.SetMinThreads(Math.Max(64, Environment.ProcessorCount * 8), Math.Max(64, Environment.ProcessorCount * 8));

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .WriteTo.Sink(LogTap.Sink)
    // The logs go to the same collector as the traces and metrics when one is named, and nowhere else otherwise.
    .WriteTo.Conditional(
        _ => Telemetry.Exporting(context.Configuration[Telemetry.EndpointVariable]),
        sink => sink.OpenTelemetry(options =>
        {
            options.Endpoint = context.Configuration[Telemetry.EndpointVariable];
            options.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = Telemetry.ServiceName };
        }))
    .MinimumLevel.Information()
    // The framework narrates every request eight lines deep at Information; the one request line from the
    // request-logging middleware is the record, and the rest is noise that under load becomes the cost.
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    // Belt and braces with RemoveAllLoggers(): the HTTP client categories would log full URLs.
    .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Polly", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .WriteTo.File(
        Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "selficient-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        formatProvider: CultureInfo.InvariantCulture));

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
    // A household's request is small: a form, a photo, a tile address. Anything larger is noise.
    options.Limits.MaxRequestHeaderCount = 60;
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
    options.Limits.MaxRequestLineSize = 8 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddSelficientApplication();
builder.Services.AddSelficientInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSelficientWeb(builder.Configuration);

WebApplication app = builder.Build();

app.UseSelficientPipeline();

app.Run();

/// <summary>Exposed so the integration tests can host the real application.</summary>
public partial class Program;
