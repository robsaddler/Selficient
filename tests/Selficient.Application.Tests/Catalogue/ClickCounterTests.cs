using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Selficient.Application.Catalogue;

namespace Selficient.Application.Tests.Catalogue;

public sealed class ClickCounterTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);

    [Fact]
    public async Task Clicks_add_up_per_offer_per_day_and_leave_in_one_batch()
    {
        ClickCounter counter = new();
        ICatalogueStore store = Substitute.For<ICatalogueStore>();

        counter.Record(1, Today);
        counter.Record(1, Today);
        counter.Record(1, Today.AddDays(-1));
        counter.Record(2, Today);
        long pending = counter.Pending;
        await counter.FlushAsync(store, CancellationToken.None);
        await counter.FlushAsync(store, CancellationToken.None);

        pending.ShouldBe(4);
        counter.Pending.ShouldBe(0);
        await store.Received(1).RecordClicksAsync(Arg.Is<IReadOnlyList<ClickBatch>>(b => b.Count == 3 && b.Sum(x => x.Count) == 4 && b.Single(x => x.OfferId == 1 && x.Day == Today).Count == 2), Arg.Any<CancellationToken>());
        await Should.ThrowAsync<ArgumentNullException>(() => counter.FlushAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task A_store_that_refuses_the_batch_gives_the_clicks_back_to_the_counter()
    {
        ClickCounter counter = new();
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        store.RecordClicksAsync(Arg.Any<IReadOnlyList<ClickBatch>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The database is locked."));
        counter.Record(1, Today);
        counter.Record(1, Today);

        await Should.ThrowAsync<InvalidOperationException>(() => counter.FlushAsync(store, CancellationToken.None));
        counter.Record(1, Today);

        counter.Pending.ShouldBe(3, "nothing is lost when the store is down; the next flush carries it");
        counter.Drain().ShouldBe([new ClickBatch(1, Today, 3)]);
        store.RecordClicksAsync(Arg.Any<IReadOnlyList<ClickBatch>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        counter.Record(2, Today);
        await Should.ThrowAsync<OperationCanceledException>(() => counter.FlushAsync(store, CancellationToken.None));
        counter.Pending.ShouldBe(1, "a cancellation is the host stopping; the click waits for the flush at stop");
    }
}
