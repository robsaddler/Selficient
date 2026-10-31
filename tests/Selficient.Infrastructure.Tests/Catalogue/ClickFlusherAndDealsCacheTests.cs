using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Selficient.Application.Catalogue;
using Selficient.Application.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Infrastructure.Catalogue;

namespace Selficient.Infrastructure.Tests.Catalogue;

public sealed class ClickFlusherAndDealsCacheTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_flusher_writes_the_counter_every_second_and_once_more_when_the_host_stops()
    {
        ClickCounter counter = new();
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        FakeTimeProvider clock = new(Noon);
        ListLogger logger = new();
        ClickFlusher flusher = new(counter, store, clock, logger);
        counter.Record(1, new DateOnly(2026, 10, 31));

        await flusher.StartAsync(CancellationToken.None);
        clock.Advance(ClickFlusher.Every);
        await Until(() => counter.Pending == 0);
        counter.Record(2, new DateOnly(2026, 10, 31));
        await flusher.StopAsync(CancellationToken.None);

        await store.Received(1).RecordClicksAsync(Arg.Is<IReadOnlyList<ClickBatch>>(b => b.Single().OfferId == 1), Arg.Any<CancellationToken>());
        await store.Received(1).RecordClicksAsync(Arg.Is<IReadOnlyList<ClickBatch>>(b => b.Single().OfferId == 2), Arg.Any<CancellationToken>());
        counter.Pending.ShouldBe(0);
        logger.Lines.ShouldBeEmpty();
        flusher.ExecuteTask.ShouldNotBeNull().IsCompletedSuccessfully.ShouldBeTrue("the loop ended when the clock closed, not by cancellation");
        flusher.Dispose();
        using ClickFlusher unstarted = new(counter, store, clock, logger);
        await unstarted.StopAsync(CancellationToken.None);
        Should.Throw<ArgumentNullException>(() => new ClickFlusher(null!, store, clock, logger));
        Should.Throw<ArgumentNullException>(() => new ClickFlusher(counter, null!, clock, logger));
        Should.Throw<ArgumentNullException>(() => new ClickFlusher(counter, store, null!, logger));
        Should.Throw<ArgumentNullException>(() => new ClickFlusher(counter, store, clock, null!));
    }

    [Fact]
    public async Task A_store_that_is_down_is_logged_and_the_clicks_wait_for_the_next_second()
    {
        ClickCounter counter = new();
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        store.RecordClicksAsync(Arg.Any<IReadOnlyList<ClickBatch>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The database is locked."));
        ListLogger logger = new();
        ClickFlusher flusher = new(counter, store, new FakeTimeProvider(Noon), logger);
        counter.Record(1, new DateOnly(2026, 10, 31));

        await flusher.FlushAsync(CancellationToken.None);

        counter.Pending.ShouldBe(1);
        logger.Lines.ShouldBe(["Error: The click counter could not be written; 1 clicks wait for the next second."]);
    }

    [Fact]
    public async Task The_deals_are_read_once_per_catalogue_version_and_day()
    {
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IKitCatalogue shipped = Substitute.For<IKitCatalogue>();
        shipped.All.Returns([]);
        store.CountAsync(Arg.Any<CancellationToken>()).Returns(1);
        store.ShownAsync(Arg.Any<CancellationToken>()).Returns([new Offer(1, ActionId.From("headtorches"), "Petzl Tikkina", "Amazon", Money.FromPence(2200), new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=Petzl"), "", OfferStatus.Live, 0)]);
        store.PriceSpansAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<long, PriceSpan>());
        using StoredCatalogue catalogue = new(store, shipped);
        await catalogue.ReloadAsync(CancellationToken.None);
        FakeTimeProvider clock = new(Noon);
        CachedDealsReader reader = new(new DealsReader(catalogue, store, clock), clock);

        IReadOnlyList<Deal> first = await reader.ReadAsync(CancellationToken.None);
        IReadOnlyList<Deal> again = await reader.ReadAsync(CancellationToken.None);
        await catalogue.ReloadAsync(CancellationToken.None);
        IReadOnlyList<Deal> afterReload = await reader.ReadAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(1));
        IReadOnlyList<Deal> nextDay = await reader.ReadAsync(CancellationToken.None);

        first.Count.ShouldBe(1);
        again.ShouldBeSameAs(first, "the second visitor reads the kept answer");
        afterReload.ShouldNotBeSameAs(first, "a reload moves the version and the key");
        nextDay.ShouldNotBeSameAs(afterReload, "a new day is a new window");
        await store.Received(3).PriceSpansAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        Should.Throw<ArgumentNullException>(() => new CachedDealsReader(null!, clock));
        Should.Throw<ArgumentNullException>(() => new CachedDealsReader(new DealsReader(catalogue, store, clock), null!));
    }

    [Fact]
    public async Task A_read_that_failed_is_not_kept_and_the_next_visitor_tries_again()
    {
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IKitCatalogue shipped = Substitute.For<IKitCatalogue>();
        shipped.All.Returns([]);
        store.CountAsync(Arg.Any<CancellationToken>()).Returns(1);
        store.ShownAsync(Arg.Any<CancellationToken>()).Returns([]);
        store.PriceSpansAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The database is locked."));
        using StoredCatalogue catalogue = new(store, shipped);
        await catalogue.ReloadAsync(CancellationToken.None);
        FakeTimeProvider clock = new(Noon);
        CachedDealsReader reader = new(new DealsReader(catalogue, store, clock), clock);

        await Should.ThrowAsync<InvalidOperationException>(() => reader.ReadAsync(CancellationToken.None));
        store.PriceSpansAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<long, PriceSpan>());
        IReadOnlyList<Deal> deals = await reader.ReadAsync(CancellationToken.None);

        deals.ShouldBeEmpty();
        await store.Received(2).PriceSpansAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue("the flusher did not get there in time");
    }

    private sealed class ListLogger : ILogger<ClickFlusher>
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
