using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Ai;
using Selficient.Application.Planning;
using Selficient.Application.Solar;
using Selficient.Domain.Plans;
using Selficient.Domain.Plans.Rules;
using Selficient.Domain.Risks;

namespace Selficient.Application.Composition;

/// <summary>Registers the use cases and the fixed set of domain rules behind them.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// The rules and assessors are listed here explicitly rather than discovered by scanning, so
    /// the set that ships is the set that is reviewed, and so the plan never depends on load order.
    /// </summary>
    public static IServiceCollection AddSelficientApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(static _ => new RiskAssessmentPipeline(Assessors()));
        services.AddSingleton(static _ => new PlanBuilder(Rules()));
        services.AddSingleton<GeneratePlanHandler>();
        services.AddSingleton<PlanHouseholdHandler>();
        services.AddSingleton<EstimateSolarHandler>();
        services.AddSingleton<AnalysePhotoHandler>();
        services.AddSingleton<ExplainPlanHandler>();

        return services;
    }

    /// <summary>Every assessor that ships.</summary>
    public static IReadOnlyList<IRiskAssessor> Assessors() =>
    [
        new StormPowerCutAssessor(),
        new FallingTreeAssessor(),
        new WaterInterruptionAssessor(),
        new WinterAccessAssessor(),
        new CommsLossAssessor(),
        new GardenFireAssessor(),
        new FloodingAssessor(),
        new RegionalDisruptionAssessor(),
        new IndoorFireAssessor(),
    ];

    /// <summary>Every rule that ships.</summary>
    public static IReadOnlyList<IPlanRule> Rules() =>
    [
        new BaselineChecklistRule(),
        new PriorityServicesRule(),
        new InfantsRule(),
        new TallTreeRule(),
        new GardenFireRule(),
        new CarRule(),
        new FloodRule(),
        new WaterStoreRule(),
        new FoodStoreRule(),
        new CookingRule(),
        new WoodStoveRule(),
        new LanternRule(),
        new DocumentsRule(),
        new SolarQuotesRule(),
        new PowerStationRule(),
        new WaterFilterRule(),
        new BusinessCommsRule(),
        new DrillRule(),
    ];
}
