using BenchmarkDotNet.Attributes;
using Selficient.Domain.Resilience;
using Selficient.TestKit;

namespace Selficient.Benchmarks;

/// <summary>The board for one house: the storm to a month, and the long shortages scenario to six months.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class BoardBenchmark
{
    private static readonly HashSet<string> ThisWeek = new(["water-bottled-three-days", "food-no-cook-three-days", "headtorches", "radio-dab-fm-with-crank", "power-banks", "cash-in-small-notes", "rooftop-solar-and-battery"], StringComparer.Ordinal);
    private readonly ResilienceProfile _profile = ResilienceProfile.FromProfile(Profiles.RobsHouse());

    /// <summary>A winter storm to a month, with this week's shelf and a battery.</summary>
    [Benchmark(Baseline = true)]
    public Board Storm() => OutageBoard.Simulate(_profile, ThisWeek, Scenarios.Storm, Season.Winter);

    /// <summary>Six months of shortages, the longest board there is.</summary>
    [Benchmark]
    public Board Shortages() => OutageBoard.Simulate(_profile, ThisWeek, Scenarios.Shortages, Season.Winter);

    /// <summary>Every scenario for every source, as the Board page will render them.</summary>
    [Benchmark]
    public decimal EveryScenarioEverySource()
    {
        decimal total = 0m;
        foreach (Scenario scenario in Scenarios.All)
        {
            foreach (string source in new[] { "power-banks", "portable-power-station", "large-power-station", "rooftop-solar-and-battery", "petrol-generator" })
            {
                HashSet<string> held = new(ThisWeek, StringComparer.Ordinal) { source };
                total += OutageBoard.Simulate(_profile, held, scenario, scenario.DefaultSeason).DaysReady;
            }
        }

        return total;
    }
}
