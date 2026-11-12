using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Selficient.Application.Operations;

namespace Selficient.Infrastructure.Data;

/// <summary>
/// Removes what the platform promised to keep only for a while: copies of a plan nobody collected,
/// sign-in codes that ran out. Each of those is already refused by its own reader the moment its
/// time is up; this is what makes the rows go too. It sweeps once a minute, and at the first sweep
/// of a new day it writes one line in the run log saying how many went the day before, so the
/// reviewer shows it without a line a minute.
/// </summary>
public sealed partial class Housekeeper : BackgroundService
{
    /// <summary>How often it sweeps.</summary>
    public static TimeSpan Every => TimeSpan.FromMinutes(1);

    /// <summary>The job's name in the run log.</summary>
    public const string JobName = "housekeeping";

    private readonly IReadOnlyList<IExpiring> _expiring;
    private readonly IOperationsLog _log;
    private readonly TimeProvider _clock;
    private readonly ILogger<Housekeeper> _logger;
    private readonly int[] _removed;
    private DateOnly _day;
    private PeriodicTimer? _timer;

    /// <summary>Creates the housekeeper over everything that expires.</summary>
    public Housekeeper(IEnumerable<IExpiring> expiring, IOperationsLog log, TimeProvider clock, ILogger<Housekeeper> logger)
    {
        ArgumentNullException.ThrowIfNull(expiring);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _expiring = [.. expiring];
        _log = log;
        _clock = clock;
        _logger = logger;
        _removed = new int[_expiring.Count];
        _day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>One sweep of everything, with a failure logged and left for the next minute. Public so a test can turn the clock by hand.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        try
        {
            DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
            if (today != _day)
            {
                await ReportAsync(cancellationToken).ConfigureAwait(false);
                _day = today;
            }

            for (int i = 0; i < _expiring.Count; i++)
            {
                _removed[i] += await _expiring[i].SweepAsync(now, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.SweepFailed(_logger, exception);
        }
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Made here, on the caller's thread: the host runs ExecuteAsync off it and a tick before the timer exists would be lost.
        _timer = new PeriodicTimer(Every, _clock);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _timer!.WaitForNextTickAsync(stoppingToken))
        {
            await SweepAsync(stoppingToken);
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Closing the timer ends the loop on its own terms; the host then waits for it rather than cancelling it.
        _timer?.Dispose();
        await base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    /// <summary>Yesterday's count as one line in the run log, when anything went.</summary>
    private async Task ReportAsync(CancellationToken cancellationToken)
    {
        List<string> parts = [];
        for (int i = 0; i < _expiring.Count; i++)
        {
            if (_removed[i] > 0)
            {
                parts.Add(_expiring[i].Describe(_removed[i]));
                _removed[i] = 0;
            }
        }

        if (parts.Count == 0)
        {
            return;
        }

        long run = await _log.StartJobAsync(JobName, cancellationToken).ConfigureAwait(false);
        await _log.FinishJobAsync(run, JobOutcome.Succeeded, $"Removed {string.Join(" and ", parts)} on {_day:yyyy-MM-dd}.", cancellationToken).ConfigureAwait(false);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "The housekeeper could not sweep; what has run out is still refused and goes at the next minute.")]
        public static partial void SweepFailed(ILogger logger, Exception exception);
    }
}
