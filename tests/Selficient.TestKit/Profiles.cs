using Selficient.Domain.Households;
using Selficient.Domain.Measures;

namespace Selficient.TestKit;

/// <summary>Ready-made households for tests. Rob's house is the one the review was written for.</summary>
public static class Profiles
{
    /// <summary>Four people, three-storey detached, gas boiler, garden with decking and a tall tree, 232 m up, very low flood risk.</summary>
    public static HouseholdProfile RobsHouse() => Build(
        people: 4, children: 2, vulnerable: false, pets: false, business: true,
        type: DwellingType.Detached, storeys: 3, heating: HeatingType.GasBoiler, hob: HobType.Electric,
        car: true, garden: true, decking: true, tree: true, elevation: 232m, flood: FloodRisk.VeryLow);

    /// <summary>One person in a second-floor flat with electric heating and no car.</summary>
    public static HouseholdProfile Flat() => Build(
        people: 1, children: 0, vulnerable: false, pets: false, business: false,
        type: DwellingType.Flat, storeys: 1, heating: HeatingType.Electric, hob: HobType.Electric,
        car: false, garden: false, decking: false, tree: false, elevation: 12m, flood: FloodRisk.Unknown);

    /// <summary>Two people in a bungalow with a wood stove and a gas hob, in a flood zone.</summary>
    public static HouseholdProfile FloodZoneBungalow() => Build(
        people: 2, children: 0, vulnerable: true, pets: true, business: false,
        type: DwellingType.Bungalow, storeys: 1, heating: HeatingType.Wood, hob: HobType.Gas,
        car: true, garden: true, decking: false, tree: false, elevation: 4m, flood: FloodRisk.High);

    /// <summary>Builds any profile; arguments are assumed valid and the test fails loudly if not.</summary>
    public static HouseholdProfile Build(
        int people, int children, bool vulnerable, bool pets, bool business,
        DwellingType type, int storeys, HeatingType heating, HobType hob,
        bool car, bool garden, bool decking, bool tree, decimal? elevation, FloodRisk flood)
    {
        Household household = Household.Create(people, children, vulnerable, pets, business).Value;
        Elevation? height = elevation is decimal metres ? Elevation.Create(metres).Value : null;
        Dwelling dwelling = Dwelling.Create(type, storeys, heating, hob, car, garden, decking, tree, height, flood).Value;
        return new HouseholdProfile(household, dwelling);
    }
}
