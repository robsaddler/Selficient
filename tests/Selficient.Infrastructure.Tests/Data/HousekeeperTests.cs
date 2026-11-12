using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Selficient.Application.Operations;
using Selficient.Infrastructure.Data;

namespace Selficient.Infrastructure.Tests.Data;

/// <summary>
/// The housekeeper: it sweeps everything that expires once a minute, tells the run log once a day
/// and only when something went, and a sweep that fails is logged and tried again.
/// </summary>
public sealed class HousekeeperTests
{
    private static readonly DateTimeOffset LateEvening = new(2026, 11, 12, 23, 58, 30, TimeSpan.Zero);

    private readonly IOperationsLog _log = Substitute.For<IOperationsLog>();
    private readonly FakeTimeProvider _clock = new(LateEvening);
    private readonly ListLogger _logger = new();

    [Fact]
    public async Task What_went_in_a_day_is_one_line_in_the_run_log_the_next_day()
    {
        IExpiring copies = Expiring("copies", 2, 0, 1, 0);
        IExpiring codes = Expiring("codes", 0, 0, 0, 0);
        _log.StartJobAsync(Housekeeper.JobName, Arg.Any<CancellationToken>()).Returns(41L);
        using Housekeeper keeper = new([copies, codes], _log, _clock, _logger);

        await keeper.SweepAsync(CancellationToken.None);
        await keeper.SweepAsync(CancellationToken.None);
        await _log.DidNotReceive().StartJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _clock.Advance(TimeSpan.FromMinutes(2));
        await keeper.SweepAsync(CancellationToken.None);
        await keeper.SweepAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(1));
        await keeper.SweepAsync(CancellationToken.None);

        await _log.Received(1).FinishJobAsync(41L, JobOutcome.Succeeded, "Removed 2 copies on 2026-11-12.", Arg.Any<CancellationToken>());
        await _log.Received(1).FinishJobAsync(41L, JobOutcome.Succeeded, "Removed 1 copies on 2026-11-13.", Arg.Any<CancellationToken>());
        await _log.Received(2).StartJobAsync(Housekeeper.JobName, Arg.Any<CancellationToken>());
        await copies.Received(2).SweepAsync(LateEvening, Arg.Any<CancellationToken>());
        _logger.Lines.ShouldBeEmpty();
        Housekeeper.JobName.ShouldBe("housekeeping");
    }

    [Fact]
    public async Task Several_kinds_are_said_together_and_a_quiet_day_is_not_a_line()
    {
        IExpiring copies = Expiring("copies", 3);
        IExpiring codes = Expiring("codes", 5);
        using Housekeeper keeper = new([copies, codes], _log, _clock, _logger);

        await keeper.SweepAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(1));
        await keeper.SweepAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(1));
        await keeper.SweepAsync(CancellationToken.None);

        await _log.Received(1).FinishJobAsync(Arg.Any<long>(), JobOutcome.Succeeded, "Removed 3 copies and 5 codes on 2026-11-12.", Arg.Any<CancellationToken>());
        await _log.Received(1).StartJobAsync(Housekeeper.JobName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_sweep_that_fails_is_logged_and_the_next_minute_tries_again()
    {
        IExpiring copies = Substitute.For<IExpiring>();
        copies.SweepAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The database is away."));
        using Housekeeper keeper = new([copies], _log, _clock, _logger);

        await keeper.SweepAsync(CancellationToken.None);

        _logger.Lines.ShouldBe(["Error: The housekeeper could not sweep; what has run out is still refused and goes at the next minute."]);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        copies.SweepAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        await Should.ThrowAsync<OperationCanceledException>(() => keeper.SweepAsync(cancelled.Token));
    }

    [Fact]
    public async Task It_sweeps_every_minute_while_the_host_runs_and_stops_with_it()
    {
        IExpiring copies = Substitute.For<IExpiring>();
        int swept = 0;
        copies.SweepAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => Interlocked.Increment(ref swept));
        Housekeeper keeper = new([copies], _log, _clock, _logger);

        await keeper.StartAsync(CancellationToken.None);
        _clock.Advance(Housekeeper.Every);
        await Until(() => Volatile.Read(ref swept) == 1);
        _clock.Advance(Housekeeper.Every);
        await Until(() => Volatile.Read(ref swept) == 2);
        await keeper.StopAsync(CancellationToken.None);
        keeper.ExecuteTask.ShouldNotBeNull().IsCompletedSuccessfully.ShouldBeTrue("the loop ended when the timer closed, not by cancellation");
        keeper.Dispose();

        Housekeeper.Every.ShouldBe(TimeSpan.FromMinutes(1));
        using Housekeeper unstarted = new([copies], _log, _clock, _logger);
        await unstarted.StopAsync(CancellationToken.None);
        Should.Throw<ArgumentNullException>(() => new Housekeeper(null!, _log, _clock, _logger));
        Should.Throw<ArgumentNullException>(() => new Housekeeper([copies], null!, _clock, _logger));
        Should.Throw<ArgumentNullException>(() => new Housekeeper([copies], _log, null!, _logger));
        Should.Throw<ArgumentNullException>(() => new Housekeeper([copies], _log, _clock, null!));
    }

    private static IExpiring Expiring(string noun, params int[] removed)
    {
        IExpiring expiring = Substitute.For<IExpiring>();
        expiring.SweepAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(removed[0], [.. removed.Skip(1), 0]);
        expiring.Describe(Arg.Any<int>()).Returns(call => $"{call.Arg<int>()} {noun}");
        return expiring;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue("the housekeeper did not get there in four seconds");
    }

    private sealed class ListLogger : ILogger<Housekeeper>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{logLevel}: {formatter(state, exception)}");
    }
}
