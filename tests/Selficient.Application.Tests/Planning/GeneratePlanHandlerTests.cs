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
        ApplicationServiceCollectionExtensions.Rules().Count.ShouldBe(18);
        ApplicationServiceCollectionExtensions.Rules().Select(r => r.Order).ShouldBeUnique();
        Should.Throw<ArgumentNullException>(() => ApplicationServiceCollectionExtensions.AddSelficientApplication(null!));
    }

    [Fact]
    public void What_the_later_questions_said_reaches_the_rules_so_the_plan_fits_the_answers()
    {
        GeneratePlanCommand answered = Robs with { HasPets = true, CarIsElectric = true, HasWoodStove = true, HasWaterButt = true, Infants = 1, Nation = Selficient.Domain.Households.Nation.Scotland, FloodFrom = Selficient.Domain.Households.FloodWater.SurfaceWater };

        Selficient.Domain.Plans.Plan plan = Handler().Handle(answered).Value;
        HashSet<string> ids = [.. plan.Actions.Select(a => a.Id.Value)];

        plan.Profile.Particulars.ShouldBe(new Selficient.Domain.Households.Particulars(ElectricCar: true, WoodStove: true, WaterButt: true, Infants: 1));
        plan.Profile.Nation.ShouldBe(Selficient.Domain.Households.Nation.Scotland);
        plan.Profile.FloodFrom.ShouldBe(Selficient.Domain.Households.FloodWater.SurfaceWater);
        ids.ShouldContain("car-above-half-charge");
        ids.ShouldNotContain("car-above-half-a-tank", "an electric car has no tank");
        ids.ShouldNotContain("water-butt", "the garden has one");
        ids.ShouldContain("wood-stove-ready");
        ids.ShouldContain("formula-and-nappies");
        ids.ShouldContain("pet-food-two-weeks");

        HashSet<string> plain = [.. Handler().Handle(Robs).Value.Actions.Select(a => a.Id.Value)];
        plain.ShouldContain("car-above-half-a-tank");
        plain.ShouldContain("water-butt");
        plain.ShouldNotContain("wood-stove-ready");
        plain.ShouldNotContain("formula-and-nappies");
        plain.ShouldNotContain("pet-food-two-weeks");

        Handler().Handle(Robs with { Infants = 3 }).Error.Code.ShouldBe("household.infants", "more infants than children is refused before any rule runs");
    }
}
