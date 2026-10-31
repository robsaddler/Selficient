using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Selficient.Application.Catalogue;
using Selficient.Application.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Infrastructure.Catalogue;
using Selficient.Infrastructure.Composition;
using Selficient.Infrastructure.Kit;

namespace Selficient.Infrastructure.Tests.Catalogue;

public sealed class CatalogueWorkerTests
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 30, 22, 0, 0, TimeSpan.Zero);

    private static Offer AnOffer(long id, string name) =>
        new(id, ActionId.From("headtorches"), name, "Amazon", Money.FromPence(2200), new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=" + Uri.EscapeDataString(name)), "Why.", OfferStatus.Live, 0);

    private sealed class Rig
    {
        public ICatalogueStore Store { get; } = Substitute.For<ICatalogueStore>();

        public IPageReader Reader { get; } = Substitute.For<IPageReader>();

        public Selficient.Application.Operations.IOperationsLog Log { get; } = Substitute.For<Selficient.Application.Operations.IOperationsLog>();

        public FakeTimeProvider Clock { get; } = new(Evening);

        public ListLogger Logger { get; } = new();

        public CatalogueOptions Options { get; init; } = new();

        public StoredCatalogue Catalogue { get; }

        public CatalogueWorker Worker { get; }

        public Rig(bool enabled)
        {
            Options = new CatalogueOptions { WorkerEnabled = enabled, VerifyAtHour = 2, WakeEveryMinutes = 30 };
            IKitCatalogue shipped = Substitute.For<IKitCatalogue>();
            shipped.All.Returns([]);
            Store.CountAsync(Arg.Any<CancellationToken>()).Returns(1);
            Store.ShownAsync(Arg.Any<CancellationToken>()).Returns([]);
            Store.LastVerifiedAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
            Catalogue = new StoredCatalogue(Store, shipped);
            OfferVerifier verifier = new(Store, Reader, new NoJudge(), Clock);
            Log.StartJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(41L);
            Worker = new CatalogueWorker(new CatalogueOperations(Catalogue, verifier, Store, Clock), Log, Microsoft.Extensions.Options.Options.Create(Options), Clock, Logger);
        }
    }

    [Fact]
    public void The_worker_checks_its_arguments()
    {
        Rig rig = new(false);
        CatalogueOperations operations = new(rig.Catalogue, new OfferVerifier(rig.Store, rig.Reader, new NoJudge(), rig.Clock), rig.Store, rig.Clock);
        IOptions<CatalogueOptions> options = Microsoft.Extensions.Options.Options.Create(new CatalogueOptions());

        Should.Throw<ArgumentNullException>(() => new CatalogueWorker(null!, rig.Log, options, rig.Clock, rig.Logger));
        Should.Throw<ArgumentNullException>(() => new CatalogueWorker(operations, null!, options, rig.Clock, rig.Logger));
        Should.Throw<ArgumentNullException>(() => new CatalogueWorker(operations, rig.Log, null!, rig.Clock, rig.Logger));
        Should.Throw<ArgumentNullException>(() => new CatalogueWorker(operations, rig.Log, options, null!, rig.Logger));
        Should.Throw<ArgumentNullException>(() => new CatalogueWorker(operations, rig.Log, options, rig.Clock, null!));
    }

    [Fact]
    public async Task A_wake_before_the_hour_does_nothing_and_one_after_it_verifies_and_says_what_moved()
    {
        Rig rig = new(false);
        Offer landed = AnOffer(1, "Petzl Tikkina headtorch");
        Offer gone = AnOffer(2, "Kidde fire blanket");
        rig.Store.ShownAsync(Arg.Any<CancellationToken>()).Returns([landed, gone]);
        rig.Reader.ReadAsync(landed.Link, Arg.Any<CancellationToken>()).Returns(new PageRead(200, "Petzl", "Petzl Tikkina headtorch £22", landed.Link));
        rig.Reader.ReadAsync(gone.Link, Arg.Any<CancellationToken>()).Returns(new PageRead(404, "", "Page not found", gone.Link));
        rig.Store.FindAsync(2, Arg.Any<CancellationToken>()).Returns(gone with { Status = OfferStatus.Hidden, Failures = 2 });
        rig.Store.LastVerifiedAsync(Arg.Any<CancellationToken>()).Returns(new DateTimeOffset(2026, 10, 30, 2, 30, 0, TimeSpan.Zero));

        await rig.Worker.WakeAsync(CancellationToken.None);
        rig.Clock.Advance(TimeSpan.FromHours(5));
        await rig.Worker.WakeAsync(CancellationToken.None);

        rig.Logger.Lines.ShouldBe([
            "Information: Catalogue verified: 2 opened, 1 landed, 0 behind a robot wall, 1 hidden.",
            "Warning: Catalogue offer hidden after two failures: Kidde fire blanket at Amazon (https://www.amazon.co.uk/s?k=Kidde fire blanket).",
        ]);
        await rig.Store.Received(1).MarkVerifiedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await rig.Log.Received().FinishJobAsync(41, Selficient.Application.Operations.JobOutcome.Succeeded, "No tips waited. Verified 2: 1 landed, 0 behind a robot wall, 1 hidden.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_wake_opens_the_readers_proposals_first_and_says_how_many()
    {
        Rig rig = new(false);
        Offer proposal = AnOffer(9, "Suggested link") with { Status = OfferStatus.Proposed, Price = Money.FromPence(0) };
        rig.Store.ProposedAsync(Arg.Any<CancellationToken>()).Returns([proposal]);
        rig.Store.ChecksAsync(9, 1, Arg.Any<CancellationToken>()).Returns([]);
        rig.Reader.ReadAsync(proposal.Link, Arg.Any<CancellationToken>()).Returns(new PageRead(200, "Head torch", "Headtorches: a head torch for £19", proposal.Link));
        rig.Store.LastVerifiedAsync(Arg.Any<CancellationToken>()).Returns(new DateTimeOffset(2026, 10, 30, 2, 30, 0, TimeSpan.Zero));

        await rig.Worker.WakeAsync(CancellationToken.None);

        rig.Logger.Lines.ShouldBe(["Information: Catalogue proposals opened and judged: 1."]);
        await rig.Store.Received().DescribeAsync(9, "Head torch", null, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await rig.Log.Received().FinishJobAsync(41, Selficient.Application.Operations.JobOutcome.Succeeded, "Opened one tip. The night's run was not due.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Every_wake_is_a_run_in_the_log_with_how_it_went()
    {
        Rig rig = new(false);
        rig.Store.LastVerifiedAsync(Arg.Any<CancellationToken>()).Returns(new DateTimeOffset(2026, 10, 30, 2, 30, 0, TimeSpan.Zero));

        await rig.Worker.WakeAsync(CancellationToken.None);
        rig.Store.ShownAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The disk is full."));
        rig.Clock.Advance(TimeSpan.FromHours(5));
        await Should.ThrowAsync<InvalidOperationException>(() => rig.Worker.WakeAsync(CancellationToken.None));

        await rig.Log.Received(2).StartJobAsync(CatalogueWorker.JobName, Arg.Any<CancellationToken>());
        await rig.Log.Received().FinishJobAsync(41, Selficient.Application.Operations.JobOutcome.Skipped, "No tips waited. The night's run was not due.", Arg.Any<CancellationToken>());
        await rig.Log.Received().FinishJobAsync(41, Selficient.Application.Operations.JobOutcome.Failed, "The disk is full.", Arg.Any<CancellationToken>());
        rig.Store.ShownAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        await Should.ThrowAsync<OperationCanceledException>(() => rig.Worker.WakeAsync(CancellationToken.None));
        await rig.Log.Received(1).FinishJobAsync(41, Selficient.Application.Operations.JobOutcome.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
        CatalogueWorker.Detail(1, null).ShouldBe("Opened one tip. The night's run was not due.");
        CatalogueWorker.Detail(3, new VerificationReport(38, 36, [], 2)).ShouldBe("Opened 3 tips. Verified 38: 36 landed, 2 behind a robot wall, 0 hidden.");
        CatalogueWorker.Detail(0, new VerificationReport(1, 0, [], 0)).ShouldBe("No tips waited. Verified 1: 0 landed, 0 behind a robot wall, 0 hidden.");
    }

    [Fact]
    public async Task Disabled_the_worker_loads_the_catalogue_at_start_and_stops_there()
    {
        Rig rig = new(false);

        await rig.Worker.StartAsync(CancellationToken.None);
        await (rig.Worker.ExecuteTask ?? Task.CompletedTask);
        await rig.Worker.StopAsync(CancellationToken.None);

        rig.Catalogue.IsLoaded.ShouldBeTrue();
        rig.Logger.Lines.ShouldBeEmpty();
        await rig.Store.DidNotReceive().LastVerifiedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_at_start_is_logged_and_the_pages_keep_the_shipped_picks()
    {
        Rig rig = new(false);
        rig.Store.CountAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The disk is full."));

        await rig.Worker.StartAsync(CancellationToken.None);
        await (rig.Worker.ExecuteTask ?? Task.CompletedTask);

        rig.Catalogue.IsLoaded.ShouldBeFalse();
        rig.Logger.Lines.ShouldBe(["Error: The catalogue store could not be read at start-up; the pages show the shipped picks until it answers."]);
    }

    [Fact]
    public async Task Enabled_the_worker_wakes_on_the_clock_survives_a_failed_run_and_stops_when_told()
    {
        Rig rig = new(true);
        rig.Store.LastVerifiedAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new InvalidOperationException("The database is locked."),
            _ => Task.FromResult<DateTimeOffset?>(null));
        rig.Clock.Advance(TimeSpan.FromHours(5));

        await rig.Worker.StartAsync(CancellationToken.None);
        // The host runs ExecuteAsync off the calling thread; the clock exists once the first load has happened.
        await Until(() => rig.Catalogue.IsLoaded, rig);
        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        await Until(() => rig.Logger.Lines.Count >= 1, rig);
        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        await Until(() => rig.Logger.Lines.Count >= 2, rig);
        await rig.Worker.StopAsync(CancellationToken.None);

        rig.Logger.Lines.ShouldBe([
            "Error: The catalogue verification run failed; the next wake tries again.",
            "Information: Catalogue verified: 0 opened, 0 landed, 0 behind a robot wall, 0 hidden.",
        ]);
        rig.Worker.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public void The_composition_root_wires_the_store_the_catalogue_the_reader_and_the_worker()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        services.AddSelficientInfrastructure(
            new ConfigurationBuilder().AddInMemoryCollection([new KeyValuePair<string, string?>("Database:ConnectionString", "Server=localhost;Database=selficient_never_opened;Integrated Security=true")]).Build(),
            environment);
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICatalogueStore>().ShouldBeOfType<SqlServerCatalogueStore>();
        provider.GetRequiredService<Selficient.Infrastructure.Data.SqlMigrator>().ShouldNotBeNull();
        provider.GetServices<IHostedService>().First().ShouldBeOfType<Selficient.Infrastructure.Data.MigrationStartup>("the schema is migrated before any other service starts");
        provider.GetRequiredService<Selficient.Application.Operations.IOperationsLog>().ShouldBeOfType<Selficient.Infrastructure.Data.SqlOperationsLog>();
        provider.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>().ShouldBeOfType<Microsoft.Extensions.Caching.SqlServer.SqlServerCache>();
        provider.GetRequiredService<IOptions<Microsoft.Extensions.Caching.SqlServer.SqlServerCacheOptions>>().Value.TableName.ShouldBe("cache");
        provider.GetRequiredService<Selficient.Application.Operations.IKeyRingStore>().ShouldBeOfType<Selficient.Infrastructure.Data.SqlKeyRingStore>();
        provider.GetRequiredService<IDealsReader>().ShouldBeOfType<CachedDealsReader>();
        provider.GetServices<IHostedService>().ShouldContain(s => s is ClickFlusher);
        provider.GetRequiredService<ClickCounter>().Pending.ShouldBe(0);
        provider.GetRequiredService<IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>().Value.Registrations.Select(r => r.Name).ShouldBe(["database", "catalogue", "basemap"]);
        provider.GetRequiredService<IKitCatalogue>().ShouldBeOfType<StoredCatalogue>().ShouldBeSameAs(provider.GetRequiredService<StoredCatalogue>());
        provider.GetRequiredService<EmbeddedKitCatalogue>().All.Count.ShouldBeGreaterThan(30);
        provider.GetRequiredService<IPageReader>().ShouldBeOfType<HttpPageReader>();
        provider.GetRequiredService<OfferVerifier>().ShouldNotBeNull();
        provider.GetRequiredService<CatalogueOperations>().ShouldNotBeNull();
        provider.GetServices<IHostedService>().ShouldContain(s => s is CatalogueWorker);
        provider.GetRequiredService<IOfferJudge>().ShouldBeOfType<NoJudge>();
        provider.GetRequiredService<IOptions<CatalogueOptions>>().Value.WorkerEnabled.ShouldBeFalse();
        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpPageReader));
        client.DefaultRequestHeaders.UserAgent.ToString().ShouldContain("Selficient-catalogue-check/1.0");
    }

    private static async Task Until(Func<bool> condition, Rig rig)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue($"the worker did not get there in time: task {rig.Worker.ExecuteTask?.Status}, {rig.Worker.ExecuteTask?.Exception}, lines {string.Join(" | ", rig.Logger.Lines)}");
    }

    /// <summary>A logger that keeps the level and the rendered message of every line.</summary>
    internal sealed class ListLogger : ILogger<CatalogueWorker>
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lines)
            {
                _lines.Add($"{logLevel}: {formatter(state, exception)}");
            }
        }
    }
}
