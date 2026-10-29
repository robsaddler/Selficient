using System.Globalization;
using Selficient.Infrastructure.Composition;
using Selficient.Web.Composition;
using Serilog;

// The composition root. Everything it wires is covered through WebApplicationFactory in
// Selficient.Web.Tests; the entry point itself is the one documented coverage exclusion.
CultureInfo.DefaultThreadCurrentCulture = SelficientCulture.EnGb;
CultureInfo.DefaultThreadCurrentUICulture = SelficientCulture.EnGb;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .MinimumLevel.Information()
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
});

builder.Services.AddSelficientInfrastructure();
builder.Services.AddSelficientWeb();

WebApplication app = builder.Build();

app.UseSelficientPipeline();

app.Run();

/// <summary>Exposed so the integration tests can host the real application.</summary>
public partial class Program;
