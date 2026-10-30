using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;
using Selficient.Domain.Risks;

namespace Selficient.Application.Planning;

/// <summary>Everything a plan needs, as closed values. No free text anywhere.</summary>
public sealed record GeneratePlanCommand(
    int People,
    int Children,
    bool HasVulnerablePerson,
    bool HasPets,
    bool RunsBusinessFromHome,
    DwellingType DwellingType,
    int Storeys,
    HeatingType Heating,
    HobType Hob,
    bool HasCar,
    bool HasGarden,
    bool HasDecking,
    bool HasTallTreeNearHouse,
    decimal? ElevationMetres,
    FloodRisk FloodRisk);

/// <summary>Turns a command into a profile, assesses the risks and builds the plan.</summary>
public sealed class GeneratePlanHandler
{
    private readonly RiskAssessmentPipeline _risks;
    private readonly PlanBuilder _builder;

    /// <summary>Creates the handler.</summary>
    public GeneratePlanHandler(RiskAssessmentPipeline risks, PlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(risks);
        ArgumentNullException.ThrowIfNull(builder);
        _risks = risks;
        _builder = builder;
    }

    /// <summary>Builds the plan, or explains which input was wrong.</summary>
    public Result<Plan> Handle(GeneratePlanCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ToProfile(command).Bind(profile =>
        {
            RiskAssessment risks = _risks.Run(profile);
            return _builder.Build(profile, risks);
        });
    }

    internal static Result<HouseholdProfile> ToProfile(GeneratePlanCommand command)
    {
        Result<Elevation?> elevation = command.ElevationMetres is decimal metres
            ? Elevation.Create(metres).Map(static e => (Elevation?)e)
            : Result.Success<Elevation?>(null);

        return elevation.Bind(e =>
            Household.Create(command.People, command.Children, command.HasVulnerablePerson, command.HasPets, command.RunsBusinessFromHome).Bind(household =>
                Dwelling.Create(command.DwellingType, command.Storeys, command.Heating, command.Hob, command.HasCar, command.HasGarden, command.HasDecking, command.HasTallTreeNearHouse, e, command.FloodRisk)
                    .Map(dwelling => new HouseholdProfile(household, dwelling))));
    }
}
