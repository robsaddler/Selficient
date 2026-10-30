using BenchmarkDotNet.Attributes;
using Selficient.Application.Composition;
using Selficient.Application.Resilience;
using Selficient.Domain.Households;
using Selficient.Domain.Plans;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Benchmarks;

/// <summary>The whole set the plan page posts for on every tick: every source, both seasons, and the cells each untaken action would turn.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class BoardSetBenchmark
{
    private static readonly HashSet<string> ThisWeek = new(["water-bottled-three-days", "food-no-cook-three-days", "headtorches", "cash-in-small-notes"], StringComparer.Ordinal);
    private readonly ResilienceProfile _profile = ResilienceProfile.FromProfile(Profiles.RobsHouse());
    private readonly Plan _plan;

    /// <summary>Builds Rob's plan once; the set is what is measured.</summary>
    public BoardSetBenchmark()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        RiskAssessmentPipeline risks = new(ApplicationServiceCollectionExtensions.Assessors());
        _plan = new PlanBuilder(ApplicationServiceCollectionExtensions.Rules()).Build(rob, risks.Run(rob)).Value;
    }

    /// <summary>The set for the storm with this week ticked: six sources by two seasons, plus a board per untaken provider action.</summary>
    [Benchmark]
    public BoardSet Compose() => BoardComposer.Compose(new BoardRequest(_profile, ThisWeek, _plan, "storm", null));
}
