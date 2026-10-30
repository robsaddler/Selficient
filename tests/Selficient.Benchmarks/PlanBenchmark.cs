using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Selficient.Application.Composition;
using Selficient.Domain.Households;
using Selficient.Domain.Plans;
using Selficient.Domain.Readiness;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Benchmarks;

/// <summary>Pins the build plan's target: under a millisecond and under ten kilobytes per plan.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class PlanBenchmark
{
    private readonly RiskAssessmentPipeline _risks = new(ApplicationServiceCollectionExtensions.Assessors());
    private readonly PlanBuilder _builder = new(ApplicationServiceCollectionExtensions.Rules());
    private readonly HouseholdProfile _profile = Profiles.RobsHouse();
    private Plan _plan = null!;

    /// <summary>Builds one plan to tick against.</summary>
    [GlobalSetup]
    public void Setup() => _plan = _builder.Build(_profile, _risks.Run(_profile)).Value;

    /// <summary>Assess risks and build the full plan for a four-person house.</summary>
    [Benchmark(Baseline = true)]
    public Plan GeneratePlan() => _builder.Build(_profile, _risks.Run(_profile)).Value;

    /// <summary>Tick every action and recompute Days Ready after each, as the checklist page will.</summary>
    [Benchmark]
    public decimal TickEverything()
    {
        Inventory inventory = Inventory.Baseline(_profile.Household);
        decimal last = 0m;
        foreach (PlanAction action in _plan.Actions)
        {
            inventory = inventory.With(action);
            last = inventory.DaysReady(_profile.Household).Value;
        }

        return last;
    }
}

/// <summary>Entry point.</summary>
public static class Program
{
    /// <summary>Runs every benchmark in the assembly.</summary>
    public static void Main(string[] args) => BenchmarkRunner.Run<PlanBenchmark>(args: args);
}
