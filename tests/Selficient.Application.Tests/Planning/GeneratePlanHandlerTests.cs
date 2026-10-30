using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Composition;
using Selficient.Application.Planning;
using Selficient.Domain.Households;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;
using Selficient.Domain.Risks;

namespace Selficient.Application.Tests.Planning;

public sealed class GeneratePlanHandlerTests
{
    private static readonly GeneratePlanCommand Robs = new(
        People: 4, Children: 2, HasVulnerablePerson: false, HasPets: false, RunsBusinessFromHome: true,
        DwellingType: DwellingType.Detached, Storeys: 3, Heating: HeatingType.GasBoiler, Hob: HobType.Electric,
        HasCar: true, HasGarden: true, HasDecking: true, HasTallTreeNearHouse: true, ElevationMetres: 232m, FloodRisk: FloodRisk.VeryLow);

    private static GeneratePlanHandler Handler() =>
        new(new RiskAssessmentPipeline(ApplicationServiceCollectionExtensions.Assessors()), new PlanBuilder(ApplicationServiceCollectionExtensions.Rules()));

    [Fact]
    public void A_valid_command_produces_a_four_tier_plan()
    {
        Result<Plan> result = Handler().Handle(Robs);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Tiers.Count.ShouldBe(4);
        result.Value.Profile.Dwelling.Elevation!.Value.Metres.ShouldBe(232m);
        result.Value.Risks.Ranked.ShouldNotBeEmpty();
    }

    [Fact]
    public void An_unknown_elevation_is_allowed()
    {
        Result<Plan> result = Handler().Handle(Robs with { ElevationMetres = null });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Profile.Dwelling.Elevation.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, 0, 3, 232, "household.empty")]
    [InlineData(21, 0, 3, 232, "household.too-large")]
    [InlineData(4, 5, 3, 232, "household.children")]
    [InlineData(4, 0, 0, 232, "dwelling.storeys")]
    [InlineData(4, 0, 3, 9000, "dwelling.elevation")]
    public void Bad_inputs_come_back_as_faults_not_exceptions(int people, int children, int storeys, decimal elevation, string code)
    {
        Result<Plan> result = Handler().Handle(Robs with { People = people, Children = children, Storeys = storeys, ElevationMetres = elevation });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    public void The_handler_rejects_missing_collaborators_and_commands()
    {
        Should.Throw<ArgumentNullException>(() => new GeneratePlanHandler(null!, new PlanBuilder([])));
        Should.Throw<ArgumentNullException>(() => new GeneratePlanHandler(new RiskAssessmentPipeline([]), null!));
        Should.Throw<ArgumentNullException>(() => Handler().Handle(null!));
    }

    [Fact]
    public void Composition_registers_a_working_handler_with_every_rule_and_assessor()
    {
        ServiceCollection services = new();

        services.AddSelficientApplication();
        using ServiceProvider provider = services.BuildServiceProvider();

        GeneratePlanHandler handler = provider.GetRequiredService<GeneratePlanHandler>();
        handler.Handle(Robs).IsSuccess.ShouldBeTrue();
        ApplicationServiceCollectionExtensions.Assessors().Count.ShouldBe(9);
        ApplicationServiceCollectionExtensions.Rules().Count.ShouldBe(16);
        ApplicationServiceCollectionExtensions.Rules().Select(r => r.Order).ShouldBeUnique();
        Should.Throw<ArgumentNullException>(() => ApplicationServiceCollectionExtensions.AddSelficientApplication(null!));
    }
}
