using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Tests.Households;

public sealed class HouseholdTests
{
    [Fact]
    public void Household_rejects_more_than_twenty_people()
    {
        Result<Household> result = Household.Create(21, 0, false, false, false);

        result.Error.Code.ShouldBe("household.too-large");
        result.Error.Message.ShouldBe("A plan covers up to twenty people. For more than that, make a plan for each household.");
    }

    [Fact]
    public void Household_rejects_nobody_at_all()
    {
        Household.Create(0, 0, false, false, false).Error.Code.ShouldBe("household.empty");
    }

    [Theory]
    [InlineData(4, -1)]
    [InlineData(4, 5)]
    public void Household_rejects_negative_children_or_more_children_than_people(int people, int children)
    {
        Result<Household> result = Household.Create(people, children, false, false, false);

        result.Error.Code.ShouldBe("household.children");
        result.Error.Message.ShouldBe("The number of children must be between zero and the number of people.");
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, true)]
    [InlineData(false, 1, true)]
    public void Children_or_a_vulnerable_person_qualify_for_priority_services(bool vulnerable, int children, bool qualifies)
    {
        Household household = Household.Create(4, children, vulnerable, hasPets: true, runsBusinessFromHome: true).Value;

        household.QualifiesForPriorityServices.ShouldBe(qualifies);
        household.Size.Count.ShouldBe(4);
        household.Children.ShouldBe(children);
        household.HasPets.ShouldBeTrue();
        household.RunsBusinessFromHome.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Property_rejects_storeys_outside_one_to_six(int storeys)
    {
        Result<Dwelling> result = Dwelling.Create(DwellingType.Detached, storeys, HeatingType.GasBoiler, HobType.Gas, true, true, false, false, null, FloodRisk.Unknown);

        result.Error.Code.ShouldBe("dwelling.storeys");
        result.Error.Message.ShouldBe("A home has between one and six storeys.");
    }

    [Theory]
    [InlineData(DwellingType.Flat, false)]
    [InlineData(DwellingType.Terrace, true)]
    [InlineData(DwellingType.SemiDetached, true)]
    [InlineData(DwellingType.Detached, true)]
    [InlineData(DwellingType.Bungalow, true)]
    public void Only_flats_lack_a_roof_of_their_own(DwellingType type, bool hasRoof)
    {
        Dwelling dwelling = Dwelling.Create(type, 1, HeatingType.Wood, HobType.None, false, false, false, false, null, FloodRisk.Unknown).Value;

        dwelling.HasOwnRoof.ShouldBe(hasRoof);
        dwelling.Type.ShouldBe(type);
    }

    [Theory]
    [InlineData(HeatingType.GasBoiler, true)]
    [InlineData(HeatingType.Electric, true)]
    [InlineData(HeatingType.HeatPump, true)]
    [InlineData(HeatingType.Oil, true)]
    [InlineData(HeatingType.Lpg, true)]
    [InlineData(HeatingType.Wood, false)]
    public void Every_heating_but_wood_stops_with_the_electricity(HeatingType heating, bool needsElectricity)
    {
        Dwelling dwelling = Dwelling.Create(DwellingType.Terrace, 2, heating, HobType.Electric, true, true, true, true, null, FloodRisk.Low).Value;

        dwelling.HeatingNeedsElectricity.ShouldBe(needsElectricity);
        dwelling.Heating.ShouldBe(heating);
        dwelling.Hob.ShouldBe(HobType.Electric);
        dwelling.HasCar.ShouldBeTrue();
        dwelling.HasGarden.ShouldBeTrue();
        dwelling.HasDecking.ShouldBeTrue();
        dwelling.HasTallTreeNearHouse.ShouldBeTrue();
        dwelling.FloodRisk.ShouldBe(FloodRisk.Low);
        dwelling.Storeys.ShouldBe(2);
    }

    [Fact]
    public void A_house_is_exposed_only_when_its_elevation_says_so()
    {
        Dwelling unknown = Dwelling.Create(DwellingType.Detached, 2, HeatingType.GasBoiler, HobType.Gas, true, true, false, false, null, FloodRisk.Unknown).Value;
        Dwelling high = Dwelling.Create(DwellingType.Detached, 2, HeatingType.GasBoiler, HobType.Gas, true, true, false, false, Elevation.Create(232m).Value, FloodRisk.Unknown).Value;
        Dwelling low = Dwelling.Create(DwellingType.Detached, 2, HeatingType.GasBoiler, HobType.Gas, true, true, false, false, Elevation.Create(20m).Value, FloodRisk.Unknown).Value;

        unknown.IsExposed.ShouldBeFalse();
        unknown.Elevation.ShouldBeNull();
        high.IsExposed.ShouldBeTrue();
        low.IsExposed.ShouldBeFalse();
    }
}
