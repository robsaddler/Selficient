using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Selficient.Application.Catalogue;
using Selficient.Application.Kit;
using Selficient.Application.Maps;
using Selficient.Application.Operations;
using Selficient.Infrastructure.Data;
using Selficient.Infrastructure.Health;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Data;

public sealed class PlatformStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = TestDatabase.Create();

    public async ValueTask InitializeAsync() => await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);

    [Fact]
    public void The_key_ring_is_kept_whole_in_order_and_named()
    {
        SqlKeyRingStore store = new(_database.ConnectionString);

        store.ReadAll().ShouldBeEmpty();
        store.Write("key-a", "<key id=\"a\" />");
        store.Write("key-b", "<key id=\"b\" />");

        store.ReadAll().ShouldBe(["<key id=\"a\" />", "<key id=\"b\" />"]);
        Should.Throw<Microsoft.Data.SqlClient.SqlException>(() => store.Write("key-a", "<key />"), "a name is written once");
        Should.Throw<ArgumentException>(() => store.Write(" ", "<key />"));
        Should.Throw<ArgumentException>(() => store.Write("key-c", " "));
        Should.Throw<ArgumentException>(() => new SqlKeyRingStore(" "));
    }

    [Fact]
    public async Task The_operations_log_keeps_hands_and_runs_newest_first_and_clips_long_text()
    {
        ManualClock clock = new(Noon);
        SqlOperationsLog log = new(_database.ConnectionString, clock);

        await log.RecordHandAsync("hide", 7, "Petzl Tikkina is hidden.", "rob", CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        await log.RecordHandAsync("verify", null, new string('o', 500), null, CancellationToken.None);
        long first = await log.StartJobAsync(CatalogueWorker_JobName, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        await log.FinishJobAsync(first, JobOutcome.Succeeded, "Verified 38.", CancellationToken.None);
        long second = await log.StartJobAsync(CatalogueWorker_JobName, CancellationToken.None);
        IReadOnlyList<OperatorAction> hands = await log.RecentHandsAsync(5, CancellationToken.None);
        IReadOnlyList<JobRun> runs = await log.RecentRunsAsync(5, CancellationToken.None);
        IReadOnlyList<JobRun> one = await log.RecentRunsAsync(1, CancellationToken.None);

        hands.Count.ShouldBe(2);
        hands[0].Hand.ShouldBe("verify");
        hands[0].OfferId.ShouldBeNull();
        hands[0].Outcome.Length.ShouldBe(400);
        hands[0].At.ShouldBe(Noon.AddMinutes(1));
        hands[1].Hand.ShouldBe("hide");
        hands[1].OfferId.ShouldBe(7L);
        runs.Count.ShouldBe(2);
        runs[0].Id.ShouldBe(second);
        runs[0].Outcome.ShouldBe(JobOutcome.Running);
        runs[0].FinishedAt.ShouldBeNull();
        runs[1].Id.ShouldBe(first);
        runs[1].Outcome.ShouldBe(JobOutcome.Succeeded);
        runs[1].Detail.ShouldBe("Verified 38.");
        runs[1].StartedAt.ShouldBe(Noon.AddMinutes(1));
        runs[1].FinishedAt.ShouldBe(Noon.AddMinutes(3));
        one.Count.ShouldBe(1);
        await Should.ThrowAsync<ArgumentException>(() => log.RecordHandAsync(" ", null, "x", "rob", CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => log.RecordHandAsync("hide", null, null!, "rob", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => log.StartJobAsync(" ", CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => log.FinishJobAsync(1, JobOutcome.Failed, null!, CancellationToken.None));
        Should.Throw<ArgumentException>(() => new SqlOperationsLog(" ", clock));
        Should.Throw<ArgumentNullException>(() => new SqlOperationsLog(_database.ConnectionString, null!));
    }

    [Fact]
    public async Task The_readiness_checks_say_what_stands_and_what_is_missing()
    {
        DatabaseHealthCheck database = new(_database.ConnectionString);
        DatabaseHealthCheck nowhere = new("Server=localhost,1;Database=nothing;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=1");
        DatabaseHealthCheck malformed = new("Server=localhost;Database=x;Integrated Security=true;Not A Keyword=1");
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IKitCatalogue shipped = Substitute.For<IKitCatalogue>();
        shipped.All.Returns([]);
        store.ShownAsync(Arg.Any<CancellationToken>()).Returns([]);
        using StoredCatalogue catalogue = new(store, shipped);
        IBasemapTiles present = Substitute.For<IBasemapTiles>();
        present.Status().Returns(new BasemapStatus(true, 0, 14));
        IBasemapTiles absent = Substitute.For<IBasemapTiles>();
        absent.Status().Returns(new BasemapStatus(false, 0, 0));
        HealthCheckContext context = new();

        HealthCheckResult up = await database.CheckHealthAsync(context);
        HealthCheckResult down = await nowhere.CheckHealthAsync(context);
        HealthCheckResult broken = await malformed.CheckHealthAsync(context);
        HealthCheckResult unloaded = await new CatalogueHealthCheck(catalogue).CheckHealthAsync(context);
        await catalogue.ReloadAsync(CancellationToken.None);
        HealthCheckResult loaded = await new CatalogueHealthCheck(catalogue).CheckHealthAsync(context);
        HealthCheckResult map = await new BasemapHealthCheck(present).CheckHealthAsync(context);
        HealthCheckResult noMap = await new BasemapHealthCheck(absent).CheckHealthAsync(context);

        up.Status.ShouldBe(HealthStatus.Healthy);
        up.Description.ShouldBe($"The database answers and holds {SqlMigrator.Shipped.Count} migrations.");
        down.Status.ShouldBe(HealthStatus.Unhealthy);
        down.Description.ShouldBe("The database did not answer.");
        broken.Status.ShouldBe(HealthStatus.Unhealthy);
        broken.Description.ShouldBe("The database connection string is not valid.");
        unloaded.Status.ShouldBe(HealthStatus.Degraded);
        loaded.Status.ShouldBe(HealthStatus.Healthy);
        loaded.Description.ShouldBe("The catalogue is loaded with 0 picks.");
        map.Status.ShouldBe(HealthStatus.Healthy);
        noMap.Status.ShouldBe(HealthStatus.Degraded);
        Should.Throw<ArgumentException>(() => new DatabaseHealthCheck(" "));
        Should.Throw<ArgumentNullException>(() => new CatalogueHealthCheck(null!));
        Should.Throw<ArgumentNullException>(() => new BasemapHealthCheck(null!));
    }

    private const string CatalogueWorker_JobName = "wake";

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }
}
