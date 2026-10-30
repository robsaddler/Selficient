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

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .WriteTo.Sink(LogTap.Sink)
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

builder.Services.AddSelficientApplication();
builder.Services.AddSelficientInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSelficientWeb(builder.Configuration);

WebApplication app = builder.Build();

app.UseSelficientPipeline();

app.Run();

/// <summary>Exposed so the integration tests can host the real application.</summary>
public partial class Program;
