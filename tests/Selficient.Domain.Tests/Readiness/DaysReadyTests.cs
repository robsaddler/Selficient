using FsCheck;
using FsCheck.Xunit;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Readiness;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Readiness;

public sealed class DaysReadyTests
{
    [Fact]
    public void A_household_with_nothing_still_has_the_baseline_half_day()
    {
        Household four = Profiles.RobsHouse().Household;
        Household one = Profiles.Flat().Household;

        Inventory.Baseline(four).DaysReady(four).Value.ShouldBe(0.5m);
        Inventory.Baseline(one).DaysReady(one).Value.ShouldBe(0.5m);
    }

    [Fact]
    public void Days_ready_is_the_weakest_dimension()
    {
        HouseholdSize four = HouseholdSize.Create(4).Value;

        DaysReadyCalculator.Calculate(new Provisions(WaterLitres: 36m, FoodDays: 30m, LightHours: 500m), four).Value.ShouldBe(3m);
        DaysReadyCalculator.Calculate(new Provisions(WaterLitres: 360m, FoodDays: 2.25m, LightHours: 500m), four).Value.ShouldBe(2.2m);
        DaysReadyCalculator.Calculate(new Provisions(WaterLitres: 360m, FoodDays: 30m, LightHours: 10m), four).Value.ShouldBe(2m);
    }

    [Fact]
    public void Ticking_the_baseline_water_takes_four_people_from_half_a_day_towards_three()
    {
        Household household = Profiles.RobsHouse().Household;
        PlanAction water = new PlanBuilderFixture().Action("water-bottled-three-days");

        Inventory after = Inventory.Baseline(household).With(water);

        after.Provisions.WaterLitres.ShouldBe(42m);
        after.DaysReady(household).Value.ShouldBe(0.5m, "food and light are still the weakest links");
    }

    [Fact]
    public void Inventory_rejects_missing_arguments()
    {
        Should.Throw<ArgumentNullException>(() => Inventory.Baseline(null!));
        Should.Throw<ArgumentNullException>(() => Inventory.Baseline(Profiles.Flat().Household).With(null!));
        Should.Throw<ArgumentNullException>(() => Inventory.Baseline(Profiles.Flat().Household).DaysReady(null!));
        Should.Throw<ArgumentNullException>(() => DaysReadyCalculator.Calculate(null!, HouseholdSize.Create(1).Value));
    }

    [Property]
    public bool Days_ready_is_never_negative(NonNegativeInt water, NonNegativeInt food, NonNegativeInt light, PositiveInt people)
    {
        HouseholdSize size = HouseholdSize.Create(1 + (people.Get % 20)).Value;
        Provisions provisions = new(water.Get, food.Get, light.Get);

        return DaysReadyCalculator.Calculate(provisions, size).Value >= 0m;
    }

    [Property]
    public bool Ticking_an_action_never_lowers_days_ready(NonNegativeInt water, NonNegativeInt food, NonNegativeInt light, PositiveInt people)
    {
        Household household = Household.Create(1 + (people.Get % 20), 0, false, false, false).Value;
        Inventory before = Inventory.Baseline(household);
        PlanAction action = new(ActionId.From("x"), Tier.ThisWeek, "T.", "W.", MoneyRange.Free, new HashSet<RiskKind>(), new Provisions(water.Get, food.Get, light.Get));

        return before.With(action).DaysReady(household).Value >= before.DaysReady(household).Value;
    }

    [Property]
    public bool Days_ready_never_exceeds_any_single_dimension(NonNegativeInt water, NonNegativeInt food, NonNegativeInt light, PositiveInt people)
    {
        HouseholdSize size = HouseholdSize.Create(1 + (people.Get % 20)).Value;
        Provisions provisions = new(water.Get, food.Get, light.Get);

        decimal days = DaysReadyCalculator.Calculate(provisions, size).Value;
        return days <= provisions.FoodDays
            && days <= provisions.WaterLitres / (size.Count * 3m)
            && days <= provisions.LightHours / 5m;
    }

    private sealed class PlanBuilderFixture
    {
        private readonly Plan _plan = new PlanBuilder([new Selficient.Domain.Plans.Rules.BaselineChecklistRule()])
            .Build(Profiles.RobsHouse(), new RiskAssessmentPipeline([]).Run(Profiles.RobsHouse()))
            .Value;

        public PlanAction Action(string id) => _plan.Actions.Single(a => a.Id.Value == id);
    }
}
