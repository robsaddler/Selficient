using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Selficient.Application.Catalogue;

namespace Selficient.Infrastructure.Catalogue;

/// <summary>
/// The clock for the catalogue's jobs. It loads the catalogue the pages read as soon as the site
/// starts, then wakes every so often and runs the nightly verification when it is due. Everything
/// it does is idempotent, so a restart in the middle costs nothing but the time.
/// </summary>
public sealed partial class CatalogueWorker : BackgroundService
{
    private readonly CatalogueOperations _operations;
    private readonly IOptions<CatalogueOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<CatalogueWorker> _logger;

    /// <summary>Creates the worker over the catalogue's operations.</summary>
    public CatalogueWorker(CatalogueOperations operations, IOptions<CatalogueOptions> options, TimeProvider clock, ILogger<CatalogueWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _operations = operations;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>One wake: the night's verification if it is due, with what it did in the log. Public so a test can turn the clock by hand.</summary>
    public async Task WakeAsync(CancellationToken cancellationToken)
    {
        // Readers' tips first: there are few, they are cheap, and a reader is waiting on the reviewer.
        int proposals = await _operations.JudgeProposalsAsync(cancellationToken);
        if (proposals > 0)
        {
            Log.Proposals(_logger, proposals);
        }

        VerificationReport? report = await _operations.VerifyIfDueAsync(_options.Value.VerifyAtHour, cancellationToken);
        if (report is null)
        {
            return;
        }

        Log.Verified(_logger, report.Checked, report.Landed, report.Walled, report.Hidden.Count);
        foreach (Offer offer in report.Hidden)
        {
            Log.Hidden(_logger, offer.Name, offer.Retailer, offer.Link);
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The clock starts before the first load so that no tick is lost while the store is read.
        using PeriodicTimer? timer = _options.Value.WorkerEnabled ? new PeriodicTimer(TimeSpan.FromMinutes(_options.Value.WakeEveryMinutes), _clock) : null;
        try
        {
            await _operations.ReloadAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The pages fall back to the shipped picks until the store answers; the next wake tries again.
            Log.Unreadable(_logger, exception);
        }

        if (timer is null)
        {
            return;
        }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await WakeAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.RunFailed(_logger, exception);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue verified: {Opened} opened, {Landed} landed, {Walled} behind a robot wall, {Hidden} hidden.")]
        public static partial void Verified(ILogger logger, int opened, int landed, int walled, int hidden);

        [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue proposals opened and judged: {Opened}.")]
        public static partial void Proposals(ILogger logger, int opened);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Catalogue offer hidden after two failures: {Offer} at {Retailer} ({Link}).")]
        public static partial void Hidden(ILogger logger, string offer, string retailer, Uri link);

        [LoggerMessage(Level = LogLevel.Error, Message = "The catalogue store could not be read at start-up; the pages show the shipped picks until it answers.")]
        public static partial void Unreadable(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "The catalogue verification run failed; the next wake tries again.")]
        public static partial void RunFailed(ILogger logger, Exception exception);
    }
}
