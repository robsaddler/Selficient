using Selficient.Application.Catalogue;
using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Infrastructure.Catalogue;
using Selficient.Infrastructure.Data;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Catalogue;

public sealed class SqlServerCatalogueStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Night = new(2026, 10, 31, 2, 15, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = TestDatabase.Create();
    private SqlServerCatalogueStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);
        _store = new SqlServerCatalogueStore(_database.ConnectionString);
    }

    private static KitPick Pick(string action, string name, decimal pounds, string retailer = "Amazon") =>
        KitPick.Create(action, name, retailer, pounds, new DateOnly(2026, 10, 29), new Uri($"https://www.amazon.co.uk/s?k={Uri.EscapeDataString(name)}"), "Why.").Value;

    private static OfferCheck Check(Verdict verdict, long? pricePence = null, DateTimeOffset? at = null) =>
        new(at ?? Night, verdict, verdict == Verdict.Landed ? 200 : 404, "Evidence.", pricePence is { } p ? Money.FromPence(p) : null, "rule");

    private async Task<SqlServerCatalogueStore> SeededAsync()
    {
        await _store.SeedAsync([Pick("headtorches", "Petzl Tikkina", 22m), Pick("headtorches", "Ledlenser MH3", 26m, "Ledlenser"), Pick("water-cans", "Water carrier 10 l", 9.5m)], CancellationToken.None);
        return _store;
    }

    [Fact]
    public void The_store_needs_a_connection_string()
    {
        Should.Throw<ArgumentException>(() => new SqlServerCatalogueStore(" "));
        Should.Throw<ArgumentException>(() => new SqlServerCatalogueStore(null!));
    }

    [Fact]
    public async Task Seeding_fills_an_empty_store_and_the_shown_offers_are_the_live_and_failing_ones()
    {
        (await _store.AllAsync(CancellationToken.None)).ShouldBeEmpty();
        await Should.ThrowAsync<ArgumentNullException>(() => _store.SeedAsync(null!, CancellationToken.None));

        SqlServerCatalogueStore store = await SeededAsync();

        (await store.AllAsync(CancellationToken.None)).Count.ShouldBe(3);
        IReadOnlyList<Offer> shown = await store.ShownAsync(CancellationToken.None);
        shown.Select(o => o.Name).ShouldBe(["Petzl Tikkina", "Ledlenser MH3", "Water carrier 10 l"], "by action, then price");
        Offer first = shown[0];
        first.Id.ShouldBe(1L);
        first.Action.ShouldBe(ActionId.From("headtorches"));
        first.Retailer.ShouldBe("Amazon");
        first.Price.ShouldBe(Money.FromPence(2200));
        first.Seen.ShouldBe(new DateOnly(2026, 10, 29));
        first.Link.ShouldBe(new Uri("https://www.amazon.co.uk/s?k=Petzl%20Tikkina"));
        first.Note.ShouldBe("Why.");
        first.Status.ShouldBe(OfferStatus.Live);
        first.Failures.ShouldBe(0);
        (await store.FindAsync(2, CancellationToken.None)).ShouldNotBeNull().Name.ShouldBe("Ledlenser MH3");
        (await store.FindAsync(99, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_corrected_pick_rewrites_the_offer_and_leaves_where_it_stands_alone()
    {
        SqlServerCatalogueStore store = await SeededAsync();
        await store.SetStatusAsync(2, OfferStatus.Hidden, CancellationToken.None);
        KitPick corrected = KitPick.Create("headtorches", "Ledlenser MH3 head torch, 200 lumens", "Ledlenser UK", 44.95m, new DateOnly(2026, 11, 13), new Uri("https://example.test/ignored"), "Brighter and heavier.").Value;

        await store.ReviseAsync(2, corrected, CancellationToken.None);
        await store.ReviseAsync(99, corrected, CancellationToken.None);

        Offer revised = (await store.FindAsync(2, CancellationToken.None))!;
        revised.Name.ShouldBe("Ledlenser MH3 head torch, 200 lumens");
        revised.Retailer.ShouldBe("Ledlenser UK");
        revised.Price.ShouldBe(Money.FromPence(4495));
        revised.Seen.ShouldBe(new DateOnly(2026, 11, 13));
        revised.Note.ShouldBe("Brighter and heavier.");
        revised.Status.ShouldBe(OfferStatus.Hidden, "a hand that hid it is not undone by a correction");
        revised.Link.ShouldBe(new Uri("https://www.amazon.co.uk/s?k=Ledlenser%20MH3"), "the link is what the offer is, and is never rewritten");
        (await store.FindAsync(1, CancellationToken.None))!.Name.ShouldBe("Petzl Tikkina");
        await Should.ThrowAsync<ArgumentNullException>(() => store.ReviseAsync(1, null!, CancellationToken.None));
    }

    [Fact]
    public async Task A_landing_refreshes_the_offer_and_two_failures_running_hide_it()
    {
        SqlServerCatalogueStore store = await SeededAsync();

        await store.RecordCheckAsync(1, Check(Verdict.Dead), CancellationToken.None);
        Offer failing = (await store.FindAsync(1, CancellationToken.None))!;
        await store.RecordCheckAsync(1, Check(Verdict.RobotWall), CancellationToken.None);
        Offer walled = (await store.FindAsync(1, CancellationToken.None))!;
        await store.RecordCheckAsync(1, Check(Verdict.Landed, 2150, Night.AddDays(1)), CancellationToken.None);
        Offer revived = (await store.FindAsync(1, CancellationToken.None))!;
        await store.RecordCheckAsync(1, Check(Verdict.Landed, null, Night.AddDays(2)), CancellationToken.None);
        Offer unpriced = (await store.FindAsync(1, CancellationToken.None))!;
        await store.RecordCheckAsync(1, Check(Verdict.WrongItem), CancellationToken.None);
        await store.RecordCheckAsync(1, Check(Verdict.NoResults), CancellationToken.None);
        Offer hidden = (await store.FindAsync(1, CancellationToken.None))!;
        await store.RecordCheckAsync(1, Check(Verdict.Dead), CancellationToken.None);
        Offer stillHidden = (await store.FindAsync(1, CancellationToken.None))!;

        failing.Status.ShouldBe(OfferStatus.Failing);
        failing.Failures.ShouldBe(1);
        walled.ShouldBe(failing, "a robot wall is not evidence either way");
        revived.Status.ShouldBe(OfferStatus.Live);
        revived.Failures.ShouldBe(0);
        revived.Price.ShouldBe(Money.FromPence(2150));
        revived.Seen.ShouldBe(new DateOnly(2026, 11, 1));
        unpriced.Price.ShouldBe(Money.FromPence(2150), "a landing without a readable price keeps the last one");
        unpriced.Seen.ShouldBe(new DateOnly(2026, 11, 2));
        hidden.Status.ShouldBe(OfferStatus.Hidden);
        hidden.Failures.ShouldBe(2);
        stillHidden.Status.ShouldBe(OfferStatus.Hidden);
        stillHidden.Failures.ShouldBe(3);
        (await store.ShownAsync(CancellationToken.None)).Select(o => o.Id).ShouldBe([2L, 3L]);
        (await store.AllAsync(CancellationToken.None)).Select(o => o.Id).ShouldBe([2L, 3L, 1L], "the hidden come last");
        await Should.ThrowAsync<ArgumentNullException>(() => store.RecordCheckAsync(1, null!, CancellationToken.None));
    }

    [Fact]
    public async Task A_landing_on_a_hidden_offer_does_not_show_it_but_a_hand_does_and_forgives_the_failures()
    {
        SqlServerCatalogueStore store = await SeededAsync();
        await store.RecordCheckAsync(2, Check(Verdict.Dead), CancellationToken.None);
        await store.RecordCheckAsync(2, Check(Verdict.Dead), CancellationToken.None);

        await store.RecordCheckAsync(2, Check(Verdict.Landed, 2600), CancellationToken.None);
        Offer landedButHidden = (await store.FindAsync(2, CancellationToken.None))!;
        await store.SetStatusAsync(2, OfferStatus.Live, CancellationToken.None);
        Offer shown = (await store.FindAsync(2, CancellationToken.None))!;
        await store.RecordCheckAsync(3, Check(Verdict.Dead), CancellationToken.None);
        await store.SetStatusAsync(3, OfferStatus.Hidden, CancellationToken.None);
        Offer hiddenByHand = (await store.FindAsync(3, CancellationToken.None))!;

        landedButHidden.Status.ShouldBe(OfferStatus.Hidden, "a hidden offer waits for a hand");
        landedButHidden.Failures.ShouldBe(0, "the landing still clears the count");
        shown.Status.ShouldBe(OfferStatus.Live);
        shown.Failures.ShouldBe(0);
        hiddenByHand.Status.ShouldBe(OfferStatus.Hidden);
        hiddenByHand.Failures.ShouldBe(1, "hiding by hand keeps the record");
    }

    [Fact]
    public async Task Checks_come_back_newest_first_with_their_prices_and_only_as_many_as_asked()
    {
        SqlServerCatalogueStore store = await SeededAsync();
        await store.RecordCheckAsync(1, Check(Verdict.Dead, null, Night), CancellationToken.None);
        await store.RecordCheckAsync(1, Check(Verdict.Landed, 2150, Night.AddDays(1)), CancellationToken.None);
        await store.RecordCheckAsync(1, Check(Verdict.RobotWall, null, Night.AddDays(2)), CancellationToken.None);
        await store.RecordCheckAsync(1, new OfferCheck(Night.AddDays(3), Verdict.RobotWall, 403, new string('e', 500), null, "rule"), CancellationToken.None);

        IReadOnlyList<OfferCheck> three = await store.ChecksAsync(1, 3, CancellationToken.None);
        IReadOnlyList<OfferCheck> none = await store.ChecksAsync(2, 5, CancellationToken.None);

        three.Count.ShouldBe(3);
        three[0].Evidence.Length.ShouldBe(400, "evidence is clipped to the column");
        three[1].Verdict.ShouldBe(Verdict.RobotWall);
        three[1].At.ShouldBe(Night.AddDays(2));
        three[1].PriceSeen.ShouldBeNull();
        three[1].Status.ShouldBe(404);
        three[1].Evidence.ShouldBe("Evidence.");
        three[1].JudgedBy.ShouldBe("rule");
        three[2].Verdict.ShouldBe(Verdict.Landed);
        three[2].PriceSeen.ShouldBe(Money.FromPence(2150));
        none.ShouldBeEmpty();
    }

    [Fact]
    public async Task Clicks_count_per_offer_per_day_and_the_window_leaves_old_days_out()
    {
        SqlServerCatalogueStore store = await SeededAsync();
        DateOnly today = new(2026, 10, 31);

        await store.RecordClicksAsync([new ClickBatch(1, today, 1), new ClickBatch(1, today.AddDays(-29), 1), new ClickBatch(2, today.AddDays(-30), 1)], CancellationToken.None);
        await store.RecordClicksAsync([new ClickBatch(1, today, 1), new ClickBatch(3, today.AddDays(-1), 1)], CancellationToken.None);
        await store.RecordClicksAsync([], CancellationToken.None);
        await Should.ThrowAsync<ArgumentNullException>(() => store.RecordClicksAsync(null!, CancellationToken.None));

        IReadOnlyDictionary<long, long> month = await store.ClicksAsync(30, today, CancellationToken.None);
        IReadOnlyDictionary<long, long> day = await store.ClicksAsync(1, today, CancellationToken.None);

        month.ShouldBe(new Dictionary<long, long> { [1] = 3, [3] = 1 }, ignoreOrder: true);
        day.ShouldBe(new Dictionary<long, long> { [1] = 2 });
    }

    [Fact]
    public async Task The_last_run_is_remembered_and_overwritten()
    {
        (await _store.LastVerifiedAsync(CancellationToken.None)).ShouldBeNull();

        await _store.MarkVerifiedAsync(Night, CancellationToken.None);
        DateTimeOffset? first = await _store.LastVerifiedAsync(CancellationToken.None);
        await _store.MarkVerifiedAsync(Night.AddDays(1).ToOffset(TimeSpan.FromHours(1)), CancellationToken.None);
        DateTimeOffset? second = await _store.LastVerifiedAsync(CancellationToken.None);

        first.ShouldBe(Night);
        second.ShouldBe(Night.AddDays(1));
        second!.Value.Offset.ShouldBe(TimeSpan.Zero, "stamps are kept in UTC");
    }

    [Fact]
    public async Task Many_calls_at_once_all_succeed()
    {
        IReadOnlyList<Offer>[] reads = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => _store.AllAsync(CancellationToken.None))));

        reads.ShouldAllBe(read => read.Count == 0);
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }
}
