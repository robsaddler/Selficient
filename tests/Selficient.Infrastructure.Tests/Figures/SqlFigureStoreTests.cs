using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Figures;
using Selficient.Infrastructure.Data;
using Selficient.Infrastructure.Figures;
using Selficient.Infrastructure.Security;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Figures;

public sealed class SqlFigureStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = TestDatabase.Create();
    private SqlFigureStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);
        _store = new SqlFigureStore(_database.ConnectionString);
    }

    private static PublishedFigures Set(int version, int? restores = null, params (string Id, decimal Value)[] corrections) =>
        new(version, Morning.AddDays(version), "rob", $"Set {version}.", restores, corrections.ToDictionary(static c => c.Id, static c => c.Value));

    private static FigureChange Change(int version, string id, decimal before, decimal after) =>
        new(version, id, before, after, "An engineer measured it.", "Stannah service manual, section 4", "expert", Morning);

    [Fact]
    public void The_store_needs_a_connection_string()
    {
        Should.Throw<ArgumentException>(() => new SqlFigureStore(" "));
        Should.Throw<ArgumentException>(() => new SqlFigureStore(null!));
    }

    [Fact]
    public async Task Nothing_is_in_force_until_a_set_is_published_and_then_the_latest_is()
    {
        (await _store.CurrentAsync(CancellationToken.None)).ShouldBeNull();
        (await _store.HistoryAsync(10, CancellationToken.None)).ShouldBeEmpty();

        (await _store.PublishAsync(Set(1, null, ("stairlift-batteries.stock", 3m), ("demand.drinking-litres", 2.5m)), [Change(1, "stairlift-batteries.stock", 2m, 3m), Change(1, "demand.drinking-litres", 3m, 2.5m)], clearDrafts: false, CancellationToken.None)).ShouldBeTrue();
        (await _store.PublishAsync(Set(2, 0), [Change(2, "stairlift-batteries.stock", 3m, 2m)], clearDrafts: false, CancellationToken.None)).ShouldBeTrue();

        PublishedFigures current = (await _store.CurrentAsync(CancellationToken.None))!;
        current.Version.ShouldBe(2);
        current.Restores.ShouldBe(0);
        current.Corrections.ShouldBeEmpty();
        PublishedFigures first = (await _store.FindAsync(1, CancellationToken.None))!;
        first.At.ShouldBe(Morning.AddDays(1));
        (first.By, first.Note, first.Restores).ShouldBe(("rob", "Set 1.", null));
        first.Corrections.ShouldBe(new Dictionary<string, decimal> { ["stairlift-batteries.stock"] = 3m, ["demand.drinking-litres"] = 2.5m }, ignoreOrder: true);
        (await _store.FindAsync(9, CancellationToken.None)).ShouldBeNull();
        (await _store.HistoryAsync(10, CancellationToken.None)).Select(set => set.Version).ShouldBe([2, 1]);
        (await _store.HistoryAsync(1, CancellationToken.None)).Select(set => set.Version).ShouldBe([2]);
    }

    [Fact]
    public async Task Every_change_to_a_figure_is_kept_with_why_and_on_what_authority_newest_first()
    {
        await _store.PublishAsync(Set(1), [Change(1, "stairlift-batteries.stock", 2m, 3m), Change(1, "demand.drinking-litres", 3m, 2.5m)], false, CancellationToken.None);
        await _store.PublishAsync(Set(2), [Change(2, "stairlift-batteries.stock", 3m, 2.25m)], false, CancellationToken.None);

        IReadOnlyList<FigureChange> lift = await _store.ChangesAsync("stairlift-batteries.stock", 10, CancellationToken.None);

        lift.Select(change => (change.Version, change.Before, change.After)).ShouldBe([(2, 3m, 2.25m), (1, 2m, 3m)]);
        lift[0].ShouldBe(new FigureChange(2, "stairlift-batteries.stock", 3m, 2.25m, "An engineer measured it.", "Stannah service manual, section 4", "expert", Morning));
        (await _store.ChangesAsync("stairlift-batteries.stock", 1, CancellationToken.None)).Count.ShouldBe(1);
        (await _store.ChangesAsync("no-such-figure", 10, CancellationToken.None)).ShouldBeEmpty();
        (await _store.ChangesInAsync(1, CancellationToken.None)).Select(change => (change.ParameterId, change.Before, change.After)).ShouldBe([("demand.drinking-litres", 3m, 2.5m), ("stairlift-batteries.stock", 2m, 3m)], "everything one publication changed, by figure");
        (await _store.ChangesInAsync(2, CancellationToken.None)).ShouldHaveSingleItem().ShouldBe(lift[0]);
        (await _store.ChangesInAsync(0, CancellationToken.None)).ShouldBeEmpty("the shipped figures were made by no publication");
        await Should.ThrowAsync<ArgumentNullException>(() => _store.ChangesAsync(null!, 1, CancellationToken.None));
    }

    [Fact]
    public async Task A_draft_is_one_value_a_figure_replaced_when_drafted_again_and_gone_when_discarded()
    {
        FigureDraft first = new("stairlift-batteries.stock", 3m, "A guess.", "Nobody.", "expert", Morning);
        FigureDraft second = first with { Value = 2.5m, Reason = "An engineer measured it.", Source = "A manual.", DraftedBy = "rob", DraftedAt = Morning.AddHours(1) };

        await _store.SaveDraftAsync(first, CancellationToken.None);
        await _store.SaveDraftAsync(new FigureDraft("demand.drinking-litres", 2.5m, "Why.", "Where.", "expert", Morning), CancellationToken.None);
        await _store.SaveDraftAsync(second, CancellationToken.None);

        (await _store.DraftsAsync(CancellationToken.None)).ShouldBe([new FigureDraft("demand.drinking-litres", 2.5m, "Why.", "Where.", "expert", Morning), second]);
        (await _store.DiscardDraftAsync("demand.drinking-litres", CancellationToken.None)).ShouldBeTrue();
        (await _store.DiscardDraftAsync("demand.drinking-litres", CancellationToken.None)).ShouldBeFalse();
        (await _store.DraftsAsync(CancellationToken.None)).ShouldHaveSingleItem().ShouldBe(second);
        await Should.ThrowAsync<ArgumentNullException>(() => _store.SaveDraftAsync(null!, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.DiscardDraftAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Publishing_empties_the_draft_when_asked_and_a_number_already_taken_writes_nothing()
    {
        await _store.SaveDraftAsync(new FigureDraft("stairlift-batteries.stock", 3m, "Why.", "Where.", "expert", Morning), CancellationToken.None);
        (await _store.PublishAsync(Set(1, null, ("stairlift-batteries.stock", 3m)), [Change(1, "stairlift-batteries.stock", 2m, 3m)], clearDrafts: true, CancellationToken.None)).ShouldBeTrue();
        (await _store.DraftsAsync(CancellationToken.None)).ShouldBeEmpty();

        // Two people publish from the same starting point: the second finds the number taken.
        await _store.SaveDraftAsync(new FigureDraft("demand.drinking-litres", 2.5m, "Why.", "Where.", "expert", Morning), CancellationToken.None);
        bool second = await _store.PublishAsync(Set(1, null, ("demand.drinking-litres", 2.5m)) with { By = "somebody-else" }, [Change(1, "demand.drinking-litres", 3m, 2.5m)], clearDrafts: true, CancellationToken.None);

        second.ShouldBeFalse();
        (await _store.CurrentAsync(CancellationToken.None))!.By.ShouldBe("rob");
        (await _store.ChangesAsync("demand.drinking-litres", 10, CancellationToken.None)).ShouldBeEmpty("no change of the refused publication is kept");
        (await _store.DraftsAsync(CancellationToken.None)).ShouldHaveSingleItem("and the draft is still there to try again");
        await Should.ThrowAsync<ArgumentNullException>(() => _store.PublishAsync(null!, [], false, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.PublishAsync(Set(5), null!, false, CancellationToken.None));
        await Should.ThrowAsync<SqlException>(() => _store.PublishAsync(Set(0), [], false, CancellationToken.None));
    }

    [Fact]
    public async Task The_first_operator_may_publish_until_somebody_is_named_and_then_only_the_named_may()
    {
        SqlOperatorStore operators = new(_database.ConnectionString);
        long rob = await operators.CreateAsync("rob", "Rob", "hash", false, Morning, CancellationToken.None);
        long expert = await operators.CreateAsync("expert", "An expert", "hash", false, Morning, CancellationToken.None);

        (await _store.MayPublishAsync("rob", CancellationToken.None)).ShouldBeTrue("the first account made");
        (await _store.MayPublishAsync("ROB", CancellationToken.None)).ShouldBeTrue("a username is not compared by case");
        (await _store.MayPublishAsync("expert", CancellationToken.None)).ShouldBeFalse("an expert drafts");
        (await _store.MayPublishAsync("nobody", CancellationToken.None)).ShouldBeFalse();

        await using (SqlConnection connection = new(_database.ConnectionString))
        {
            await connection.ExecuteAsync("INSERT INTO dbo.figure_publishers (operator_id) VALUES (@expert)", new { expert });
        }

        (await _store.MayPublishAsync("expert", CancellationToken.None)).ShouldBeTrue();
        (await _store.MayPublishAsync("rob", CancellationToken.None)).ShouldBeFalse("once publishers are named, being first is not enough");

        await operators.SetEnabledAsync(expert, false, Guid.NewGuid(), CancellationToken.None);
        (await _store.MayPublishAsync("expert", CancellationToken.None)).ShouldBeFalse("a disabled account publishes nothing");
        rob.ShouldBeLessThan(expert);
        await Should.ThrowAsync<ArgumentNullException>(() => _store.MayPublishAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task The_figures_in_force_are_read_when_the_host_starts()
    {
        await _store.PublishAsync(Set(1, null, ("stairlift-batteries.stock", 3m)), [], false, CancellationToken.None);
        CurrentFigures current = new(_store);
        FiguresStartup startup = new(current);

        current.IsLoaded.ShouldBeFalse();
        await startup.StartAsync(CancellationToken.None);
        await startup.StopAsync(CancellationToken.None);

        current.IsLoaded.ShouldBeTrue();
        current.Current.Version.ShouldBe(1);
        current.Current["stairlift-batteries.stock"].ShouldBe(3m);
        Should.Throw<ArgumentNullException>(() => new FiguresStartup(null!));
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }
}
