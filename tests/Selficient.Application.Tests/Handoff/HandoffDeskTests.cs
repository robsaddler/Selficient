using NSubstitute;
using Selficient.Application.Handoff;
using Selficient.Domain.Primitives;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Handoff;

/// <summary>
/// The desk's own rules: what counts as a copy, what an id is, and that nothing a stranger types
/// reaches the store unless it could be one. The store's promises (one read, expiry) are proved
/// against a real database in the Infrastructure tests.
/// </summary>
public sealed class HandoffDeskTests
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);

    private readonly IHandoffStore _store = Substitute.For<IHandoffStore>();
    private readonly ManualClock _clock = new(Evening);

    [Fact]
    public async Task A_copy_is_kept_for_ten_minutes_under_an_id_nobody_chose()
    {
        HandoffDesk desk = new(_store, _clock);
        byte[] copy = new byte[HandoffDesk.MinBytes];

        Result<string> first = await desk.LeaveAsync(copy, CancellationToken.None);
        Result<string> second = await desk.LeaveAsync(copy, CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        first.Value.Length.ShouldBe(22, "sixteen bytes as a link writes them");
        first.Value.ShouldMatch("^[A-Za-z0-9_-]{22}$");
        second.Value.ShouldNotBe(first.Value, "each copy has an id of its own");
        await _store.Received(2).PutAsync(Arg.Is<byte[]>(id => id.Length == 16), copy, Evening, Evening.AddMinutes(10), Arg.Any<CancellationToken>());
        HandoffDesk.Lifetime.ShouldBe(TimeSpan.FromMinutes(10));
        HandoffDesk.IdBytes.ShouldBe(16);
    }

    [Fact]
    public async Task What_is_too_small_or_too_large_to_be_a_copy_never_reaches_the_store()
    {
        HandoffDesk desk = new(_store, _clock);

        Result<string> small = await desk.LeaveAsync(new byte[HandoffDesk.MinBytes - 1], CancellationToken.None);
        Result<string> large = await desk.LeaveAsync(new byte[HandoffDesk.MaxBytes + 1], CancellationToken.None);
        Result<string> largest = await desk.LeaveAsync(new byte[HandoffDesk.MaxBytes], CancellationToken.None);

        small.Error.ShouldBe(HandoffDesk.Malformed);
        small.Error.Message.ShouldBe("That is not a copy of a plan. Send a copy again from the other device.");
        large.Error.ShouldBe(HandoffDesk.TooLarge);
        large.Error.Message.ShouldBe("That copy is too large to send. Use \"Export as a file\" on the plan instead.");
        largest.IsSuccess.ShouldBeTrue("the cap itself is allowed");
        HandoffDesk.MaxBytes.ShouldBe(65_536);
        await _store.Received(1).PutAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await Should.ThrowAsync<ArgumentNullException>(() => desk.LeaveAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task The_desk_refuses_when_it_is_full()
    {
        _store.CountAsync(Arg.Any<CancellationToken>()).Returns(HandoffDesk.MaxWaiting);
        HandoffDesk desk = new(_store, _clock);

        Result<string> refused = await desk.LeaveAsync(new byte[64], CancellationToken.None);

        refused.Error.ShouldBe(HandoffDesk.Full);
        refused.Error.Message.ShouldBe("Too many copies are waiting to be collected at the moment. Try again in a few minutes.");
        HandoffDesk.MaxWaiting.ShouldBe(5_000);
        await _store.DidNotReceive().PutAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAA+/")]
    [InlineData("'; DROP TABLE handoffs")]
    public async Task An_id_that_is_not_one_is_never_looked_up(string? id)
    {
        HandoffDesk desk = new(_store, _clock);

        (await desk.CollectAsync(id, CancellationToken.None)).ShouldBeNull();
        (await desk.IsWaitingAsync(id, CancellationToken.None)).ShouldBeFalse();

        HandoffDesk.Parse(id).ShouldBeNull();
        await _store.DidNotReceive().TakeAsync(Arg.Any<byte[]>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().IsWaitingAsync(Arg.Any<byte[]>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_real_id_is_asked_of_the_store_as_of_now()
    {
        byte[] copy = [1, 2, 3];
        _store.TakeAsync(Arg.Any<byte[]>(), Evening, Arg.Any<CancellationToken>()).Returns(copy);
        _store.IsWaitingAsync(Arg.Any<byte[]>(), Evening, Arg.Any<CancellationToken>()).Returns(true);
        _store.SweepAsync(Evening, Arg.Any<CancellationToken>()).Returns(3);
        HandoffDesk desk = new(_store, _clock);
        string id = (await desk.LeaveAsync(new byte[64], CancellationToken.None)).Value;

        (await desk.CollectAsync(id, CancellationToken.None)).ShouldBe(copy);
        (await desk.IsWaitingAsync(id, CancellationToken.None)).ShouldBeTrue();
        (await desk.SweepAsync(Evening, CancellationToken.None)).ShouldBe(3);

        HandoffDesk.Parse(id)!.Length.ShouldBe(16);
        desk.Describe(1).ShouldBe("1 copy of a plan that was never collected");
        desk.Describe(3).ShouldBe("3 copies of a plan that were never collected");
        Should.Throw<ArgumentNullException>(() => new HandoffDesk(null!, _clock));
        Should.Throw<ArgumentNullException>(() => new HandoffDesk(_store, null!));
    }
}
