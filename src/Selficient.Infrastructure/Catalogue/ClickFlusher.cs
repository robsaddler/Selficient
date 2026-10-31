using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Selficient.Application.Catalogue;

namespace Selficient.Infrastructure.Catalogue;

/// <summary>
/// Writes the click counter to the store once a second, and once more when the host stops, so the
/// hop never waits on the database and a day's clicks are never more than a second behind.
/// </summary>
public sealed partial class ClickFlusher : BackgroundService
{
    /// <summary>How long a click waits in memory at most.</summary>
    public static TimeSpan Every => TimeSpan.FromSeconds(1);

    private readonly ClickCounter _counter;
    private readonly ICatalogueStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<ClickFlusher> _logger;

    /// <summary>Creates the flusher.</summary>
    public ClickFlusher(ClickCounter counter, ICatalogueStore store, TimeProvider clock, ILogger<ClickFlusher> logger)
    {
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _counter = counter;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>One flush, with a failure logged and the clicks kept for the next.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _counter.FlushAsync(_store, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.FlushFailed(_logger, _counter.Pending, exception);
        }
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // The clock is made here, on the caller's thread, because the host runs ExecuteAsync off it and a tick before the clock exists would be lost.
        _timer = new PeriodicTimer(Every, _clock);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _timer!.WaitForNextTickAsync(stoppingToken))
        {
            await FlushAsync(stoppingToken);
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Closing the clock ends the loop on its own terms; the host then waits for it rather than cancelling it.
        _timer?.Dispose();
        await base.StopAsync(cancellationToken);
        // The last second's clicks go with the process rather than being lost with it.
        await FlushAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    private PeriodicTimer? _timer;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "The click counter could not be written; {Pending} clicks wait for the next second.")]
        public static partial void FlushFailed(ILogger logger, long pending, Exception exception);
    }
}
