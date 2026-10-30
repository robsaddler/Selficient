using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Selficient.Application.Ai;

namespace Selficient.Infrastructure.Ai;

/// <summary>
/// Counts in memory: per household per calendar month, and for the site per calendar day. There
/// are no accounts in v1, so the household key is whatever the browser sends, with the address as
/// the fallback; the rate limit and the daily cap bound what a forged key can cost. Counts reset
/// when the process restarts, which errs in the household's favour.
/// </summary>
public sealed class InMemoryAnalysisMeter : IAnalysisMeter
{
    private readonly ConcurrentDictionary<string, int> _households = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _days = new(StringComparer.Ordinal);
    private readonly AiOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the meter.</summary>
    public InMemoryAnalysisMeter(IOptions<AiOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _options = options.Value;
        _clock = clock;
    }

    /// <inheritdoc />
    public int FreePerMonth => _options.FreeAnalysesPerMonth;

    /// <inheritdoc />
    public MeterOutcome TryTake(string householdKey)
    {
        ArgumentNullException.ThrowIfNull(householdKey);
        DateTimeOffset now = _clock.GetUtcNow();
        string month = $"{now:yyyy-MM}:{householdKey}";
        string day = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        // The day is checked first so an exhausted site does not burn a household's count.
        int siteCount = _days.AddOrUpdate(day, 1, static (_, count) => count + 1);
        if (siteCount > _options.DailyGlobalCap)
        {
            return MeterOutcome.SiteBusy;
        }

        int householdCount = _households.AddOrUpdate(month, 1, static (_, count) => count + 1);
        if (householdCount > _options.FreeAnalysesPerMonth)
        {
            // Give the site's count back: nothing ran.
            _days.AddOrUpdate(day, 0, static (_, count) => count - 1);
            return MeterOutcome.AllowanceUsed;
        }

        Forget(now);
        return MeterOutcome.Allowed;
    }

    /// <summary>Drops keys from earlier months and days so the dictionaries never grow without bound.</summary>
    private void Forget(DateTimeOffset now)
    {
        string monthPrefix = now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        foreach (string key in _households.Keys)
        {
            if (!key.StartsWith(monthPrefix, StringComparison.Ordinal))
            {
                _households.TryRemove(key, out _);
            }
        }

        string today = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        foreach (string key in _days.Keys)
        {
            if (key != today)
            {
                _days.TryRemove(key, out _);
            }
        }
    }
}
