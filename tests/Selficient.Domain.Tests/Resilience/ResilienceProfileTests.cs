using Selficient.Domain.Households;
using Selficient.Domain.Resilience;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Resilience;

public sealed class ResilienceProfileTests
{
    [Fact]
    public void A_dependency_is_range_checked_because_it_comes_from_a_form()
    {
        Dependency device = Dependency.Create("  Oxygen concentrator ", Rung.Survive, DependencyKind.PoweredDevice, 300m, 24m, 99, mustStayCold: false).Value;
        Dependency medicine = Dependency.Create("Insulin", Rung.Survive, DependencyKind.Medicine, 500m, 5m, 28, mustStayCold: true).Value;

        device.Name.ShouldBe("Oxygen concentrator");
        device.WhPerDay.ShouldBe(7200m);
        device.DaysOfStock.ShouldBe(0, "a device has no stock");
        medicine.Watts.ShouldBe(0m, "a medicine draws nothing");
        medicine.HoursPerDay.ShouldBe(0m);
        medicine.DaysOfStock.ShouldBe(28);
        medicine.MustStayCold.ShouldBeTrue();

        Dependency.Create(string.Empty, Rung.Survive, DependencyKind.Supply, 0, 0, 3, false).Error.Code.ShouldBe("dependency.name");
        Dependency.Create(new string('x', 41), Rung.Survive, DependencyKind.Supply, 0, 0, 3, false).Error.Code.ShouldBe("dependency.name");
        Dependency.Create("Thing", (Rung)9, DependencyKind.Supply, 0, 0, 3, false).Error.Code.ShouldBe("dwelling.unknown-choice");
        Dependency.Create("Thing", Rung.Survive, (DependencyKind)9, 0, 0, 3, false).Error.Code.ShouldBe("dwelling.unknown-choice");
        Dependency.Create("Pump", Rung.Survive, DependencyKind.PoweredDevice, 0m, 8m, 0, false).Error.Code.ShouldBe("dependency.power");
        Dependency.Create("Pump", Rung.Survive, DependencyKind.PoweredDevice, 3001m, 8m, 0, false).Error.Code.ShouldBe("dependency.power");
        Dependency.Create("Pump", Rung.Survive, DependencyKind.PoweredDevice, 50m, 0m, 0, false).Error.Code.ShouldBe("dependency.power");
        Dependency.Create("Pump", Rung.Survive, DependencyKind.PoweredDevice, 50m, 25m, 0, false).Error.Code.ShouldBe("dependency.power");
        Dependency.Create("Bags", Rung.Survive, DependencyKind.Supply, 0, 0, -1, false).Error.Code.ShouldBe("dependency.stock");
        Dependency.Create("Bags", Rung.Survive, DependencyKind.Supply, 0, 0, 366, false).Error.Code.ShouldBe("dependency.stock");
        Should.Throw<ArgumentNullException>(() => Dependency.Create(null!, Rung.Survive, DependencyKind.Supply, 0, 0, 3, false));
    }

    [Fact]
    public void The_profile_from_the_plan_takes_sensible_defaults()
    {
        ResilienceProfile rob = ResilienceProfile.FromProfile(Profiles.RobsHouse());
        ResilienceProfile woodStove = ResilienceProfile.FromProfile(Profiles.FloodZoneBungalow());
        ResilienceProfile flat = ResilienceProfile.FromProfile(Profiles.Flat());

        rob.Car.ShouldBe(CarKind.Petrol, "the plan said there is a car");
        rob.HasWoodStove.ShouldBeFalse();
        rob.MedicineDaysInStock.ShouldBe(7);
        rob.Dependencies.ShouldBeEmpty();
        rob.Household.ShouldBe(Profiles.RobsHouse().Household);
        rob.Dwelling.ShouldBe(Profiles.RobsHouse().Dwelling);
        woodStove.HasWoodStove.ShouldBeTrue("the house is heated by wood");
        flat.Car.ShouldBe(CarKind.None);
        flat.WaterIsPumped.ShouldBeFalse("the ground floor is not pumped");
        Should.Throw<ArgumentNullException>(() => ResilienceProfile.FromProfile(null!));
    }

    [Fact]
    public void The_full_profile_is_validated()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        HouseholdProfile flat = Profiles.Flat();
        Dependency dependency = Dependency.Create("Thing", Rung.Function, DependencyKind.Supply, 0, 0, 3, false).Value;

        ResilienceProfile.Create(rob, true, true, true, Growing.Allotment, CarKind.ElectricWithOutput, 1, 28, 0, [dependency]).IsSuccess.ShouldBeTrue();
        ResilienceProfile.Create(rob, false, false, false, (Growing)9, CarKind.Petrol, 0, 7, 0, []).Error.Code.ShouldBe("dwelling.unknown-choice");
        ResilienceProfile.Create(rob, false, false, false, Growing.None, (CarKind)9, 0, 7, 0, []).Error.Code.ShouldBe("dwelling.unknown-choice");
        ResilienceProfile.Create(rob, false, false, false, Growing.None, CarKind.Petrol, 3, 7, 0, []).Error.Code.ShouldBe("household.infants", "two children, three infants");
        ResilienceProfile.Create(rob, false, false, false, Growing.None, CarKind.Petrol, -1, 7, 0, []).Error.Code.ShouldBe("household.infants");
        ResilienceProfile.Create(rob, false, false, false, Growing.None, CarKind.Petrol, 0, 366, 0, []).Error.Code.ShouldBe("dependency.stock");
        ResilienceProfile.Create(rob, false, false, false, Growing.None, CarKind.Petrol, 0, 7, 41, []).Error.Code.ShouldBe("dwelling.floor");
        ResilienceProfile.Create(rob, false, false, false, Growing.None, CarKind.Petrol, 0, 7, 0, Enumerable.Repeat(dependency, 13).ToList()).Error.Code.ShouldBe("dependency.too-many");
        ResilienceProfile.Create(flat, false, false, false, Growing.None, CarKind.Petrol, 0, 7, 0, []).Error.Code.ShouldBe("dwelling.car", "the plan said no car");
        Should.Throw<ArgumentNullException>(() => ResilienceProfile.Create(null!, false, false, false, Growing.None, CarKind.None, 0, 7, 0, []));
        Should.Throw<ArgumentNullException>(() => ResilienceProfile.Create(rob, false, false, false, Growing.None, CarKind.None, 0, 7, 0, null!));
    }

    [Fact]
    public void Context_flags_follow_the_household()
    {
        HouseholdProfile fifthFloor = Profiles.Build(1, 0, false, false, false, DwellingType.Flat, 1, HeatingType.Electric, HobType.Electric, false, false, false, false, 20m, FloodRisk.Unknown);
        HouseholdProfile frail = Profiles.Build(2, 0, true, false, false, DwellingType.Bungalow, 1, HeatingType.GasBoiler, HobType.Gas, true, true, false, false, 50m, FloodRisk.Low);

        ResilienceProfile.Create(fifthFloor, false, false, false, Growing.None, CarKind.None, 0, 7, 5, []).Value.WaterIsPumped.ShouldBeTrue();
        ResilienceProfile.Create(fifthFloor, false, false, false, Growing.None, CarKind.None, 0, 7, 3, []).Value.WaterIsPumped.ShouldBeFalse("the third floor is gravity fed");
        ResilienceProfile.Create(Profiles.RobsHouse(), false, false, false, Growing.None, CarKind.Petrol, 0, 7, 5, []).Value.WaterIsPumped.ShouldBeFalse("a house is not a block");
        ResilienceProfile.FromProfile(frail).NeedsFullWarmth.ShouldBeTrue();
        ResilienceProfile.Create(Profiles.RobsHouse(), false, false, false, Growing.None, CarKind.Petrol, 1, 7, 0, []).Value.NeedsFullWarmth.ShouldBeTrue("an infant");
        ResilienceProfile.FromProfile(Profiles.RobsHouse()).NeedsFullWarmth.ShouldBeFalse();
    }

    [Fact]
    public void Placement_decides_what_a_house_can_have()
    {
        ResilienceProfile rob = ResilienceProfile.Create(Profiles.RobsHouse(), false, false, false, Growing.None, CarKind.ElectricWithOutput, 0, 7, 0, []).Value;
        ResilienceProfile flat = ResilienceProfile.FromProfile(Profiles.Flat());
        HouseholdProfile terraceNoGarden = Profiles.Build(2, 0, false, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, true, false, false, false, 30m, FloodRisk.Low);
        ResilienceProfile terrace = ResilienceProfile.FromProfile(terraceNoGarden);

        foreach (Placement where in Enum.GetValues<Placement>())
        {
            rob.CanHave(where).ShouldBeTrue($"Rob's house can have {where}");
        }

        flat.CanHave(Placement.Anywhere).ShouldBeTrue();
        flat.CanHave(Placement.NeedsGarden).ShouldBeFalse();
        flat.CanHave(Placement.OutdoorsOnly).ShouldBeFalse("a flat with no garden has nowhere to run a generator");
        flat.CanHave(Placement.NeedsOwnRoof).ShouldBeFalse();
        flat.CanHave(Placement.NeedsCar).ShouldBeFalse();
        flat.CanHave(Placement.NeedsElectricCarWithOutput).ShouldBeFalse();
        terrace.CanHave(Placement.OutdoorsOnly).ShouldBeTrue("a house without a garden still has a yard or a doorstep");
        terrace.CanHave(Placement.NeedsGarden).ShouldBeFalse();
        terrace.CanHave(Placement.NeedsElectricCarWithOutput).ShouldBeFalse("a petrol car");
    }

    [Theory]
    [InlineData(HeatingType.GasBoiler, "gas-boiler")]
    [InlineData(HeatingType.HeatPump, "heat-pump")]
    [InlineData(HeatingType.Electric, "electric-heating")]
    [InlineData(HeatingType.Wood, "wood-stove")]
    [InlineData(HeatingType.Oil, "oil-or-lpg-boiler")]
    [InlineData(HeatingType.Lpg, "oil-or-lpg-boiler")]
    public void The_fixtures_follow_the_heating(HeatingType heating, string expected)
    {
        HouseholdProfile house = Profiles.Build(2, 0, false, false, false, DwellingType.SemiDetached, 2, heating, HobType.None, false, false, false, false, 30m, FloodRisk.Low);

        IReadOnlySet<string> fixtures = ResilienceProfile.FromProfile(house).Fixtures;

        fixtures.ShouldContain(expected);
        fixtures.Contains("immersion-heater").ShouldBe(heating is HeatingType.Electric or HeatingType.Wood, "electric and wood heating come with an immersion heater");
        fixtures.ShouldNotContain("gas-hob");
        fixtures.ShouldNotContain("electric-hob");
        fixtures.ShouldContain("mains-tap");
        fixtures.ShouldContain("phone-charging");
    }

    [Fact]
    public void The_fixtures_follow_the_rest_of_the_house()
    {
        HouseholdProfile gasHob = Profiles.Build(2, 0, false, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, true, true, false, false, 30m, FloodRisk.Low);
        ResilienceProfile everything = ResilienceProfile.Create(gasHob, true, true, true, Growing.Allotment, CarKind.ElectricWithOutput, 0, 7, 0, []).Value;
        ResilienceProfile beds = ResilienceProfile.Create(gasHob, false, false, false, Growing.RaisedBeds, CarKind.Electric, 0, 7, 0, []).Value;
        ResilienceProfile rob = ResilienceProfile.FromProfile(Profiles.RobsHouse());
        ResilienceProfile flat = ResilienceProfile.FromProfile(Profiles.Flat());

        foreach (string id in new[] { "gas-hob", "chest-freezer", "water-butt", "allotment", "electric-car", "ev-charging", "ev-output", "wood-stove" }) { everything.Fixtures.ShouldContain(id); }
        everything.Fixtures.ShouldNotContain("petrol-car");
        foreach (string id in new[] { "raised-beds", "electric-car", "ev-charging" }) { beds.Fixtures.ShouldContain(id); }
        beds.Fixtures.ShouldNotContain("ev-output");
        beds.Fixtures.ShouldNotContain("allotment");
        foreach (string id in new[] { "electric-hob", "petrol-car", "forecourt" }) { rob.Fixtures.ShouldContain(id); }
        rob.Fixtures.ShouldNotContain("chest-freezer");
        flat.Fixtures.ShouldNotContain("petrol-car");
        flat.Fixtures.ShouldNotContain("electric-car");
    }
}
