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
    FloodRisk FloodRisk,
    bool CarIsElectric = false,
    bool HasWoodStove = false,
    bool HasWaterButt = false,
    int Infants = 0,
    Nation Nation = Nation.Unknown,
    FloodWater FloodFrom = FloodWater.NotSaid,
    Tenure Tenure = Tenure.Owner,
    WaterSupply Water = WaterSupply.Mains,
    bool OwnDrains = false,
    bool PrepaymentMeter = false,
    bool NeedsHelpToLeave = false,
    bool HasCellar = false,
    bool SleepsDownstairs = false,
    int Floor = 0,
    Stairlift Stairlift = Stairlift.None);

/// <summary>Turns a command into a profile, assesses the risks and builds the plan.</summary>
public sealed class GeneratePlanHandler
{
    private readonly RiskAssessmentPipeline _risks;
    private readonly PlanBuilder _builder;
    private readonly Selficient.Application.Figures.ICurrentFigures _figures;

    /// <summary>Creates the handler, working from the shipped figures.</summary>
    public GeneratePlanHandler(RiskAssessmentPipeline risks, PlanBuilder builder)
        : this(risks, builder, new Selficient.Application.Figures.ShippedFigures())
    {
    }

    /// <summary>Creates the handler, working from whatever figures are in force when each plan is built.</summary>
    public GeneratePlanHandler(RiskAssessmentPipeline risks, PlanBuilder builder, Selficient.Application.Figures.ICurrentFigures figures)
    {
        ArgumentNullException.ThrowIfNull(risks);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(figures);
        _risks = risks;
        _builder = builder;
        _figures = figures;
    }

    /// <summary>Builds the plan, or explains which input was wrong.</summary>
    public Result<Plan> Handle(GeneratePlanCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ToProfile(command, _figures.Current).Bind(profile =>
        {
            RiskAssessment risks = _risks.Run(profile);
            return _builder.Build(profile, risks);
        });
    }

    internal static Result<HouseholdProfile> ToProfile(GeneratePlanCommand command) => ToProfile(command, Selficient.Domain.Parameters.ParameterSet.Default);

    internal static Result<HouseholdProfile> ToProfile(GeneratePlanCommand command, Selficient.Domain.Parameters.ParameterSet figures)
    {
        Result<Elevation?> elevation = command.ElevationMetres is decimal metres
            ? Elevation.Create(metres).Map(static e => (Elevation?)e)
            : Result.Success<Elevation?>(null);

        return elevation.Bind(e =>
            Household.Create(command.People, command.Children, command.HasVulnerablePerson, command.HasPets, command.RunsBusinessFromHome).Bind(household =>
                Dwelling.Create(command.DwellingType, command.Storeys, command.Heating, command.Hob, command.HasCar, command.HasGarden, command.HasDecking, command.HasTallTreeNearHouse, e, command.FloodRisk).Bind(dwelling =>
                    Particulars.Create(household, command.CarIsElectric, command.HasWoodStove, command.HasWaterButt, command.Infants).Bind(particulars =>
                        Circumstances.Create(command.Tenure, command.Water, command.OwnDrains, command.PrepaymentMeter, command.NeedsHelpToLeave, command.HasCellar, command.SleepsDownstairs, command.Floor, command.Stairlift)
                            .Map(circumstances => new HouseholdProfile(household, dwelling) { Nation = command.Nation, FloodFrom = command.FloodFrom, Particulars = particulars, Circumstances = circumstances, Figures = figures })))));
    }
}
