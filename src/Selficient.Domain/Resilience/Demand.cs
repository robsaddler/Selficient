using Selficient.Domain.Measures;

namespace Selficient.Domain.Resilience;

/// <summary>
/// What a household needs of each thing on a given day. The arithmetic the engine owns: people,
/// season, the scenario's weather, and who lives there. A demand of zero means the need does not
/// arise for this household on this day, and the board shows it held.
/// </summary>
public static class Demand
{
    /// <summary>Litres a day for the pets of a household, over the people.</summary>
    public static decimal PetWaterLitresPerDay => 2m;

    /// <summary>Extra litres a person a day in a heatwave.</summary>
    public static decimal HeatwaveExtraLitresPerPersonPerDay => 1m;

    /// <summary>Lit hours needed in a winter day: the living room and the stairs from dusk.</summary>
    public static decimal WinterLitHours => 8m;

    /// <summary>Lit hours needed in a summer day.</summary>
    public static decimal SummerLitHours => 4m;

    /// <summary>Days a household goes before it needs to wash clothes.</summary>
    public static decimal LaundryAfterDays => 7m;

    /// <summary>Flushing and hand washing, litres a person a day, when the taps are dry.</summary>
    public static decimal SanitationLitresPerPersonPerDay => 12m;

    /// <summary>The demand for a need on a day.</summary>
    public static decimal For(ResilienceProfile profile, NeedId need, Scenario scenario, Season season, int day)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(scenario);
        int people = profile.Household.Size.Count;
        DemandFigures figures = DemandFigures.Of(profile.Figures);
        return need switch
        {
            NeedId.DrinkingWater => people * (figures.DrinkingLitres + (scenario.Heatwave ? figures.HeatwaveExtraLitres : 0m)) + (profile.Household.HasPets ? figures.PetWaterLitres : 0m),
            NeedId.Food => people,
            NeedId.Warmth => season == Season.Winter && !scenario.Heatwave ? 1m : 0m,
            NeedId.Health => 1m,
            NeedId.Dependants => profile.Infants > 0 || profile.Household.HasPets ? 1m : 0m,
            NeedId.Light => season == Season.Winter ? figures.WinterLitHours : figures.SummerLitHours,
            NeedId.Information => 1m,
            NeedId.Contact => 1m,
            NeedId.Cooking => 1m,
            NeedId.Sanitation => people * figures.SanitationLitres,
            NeedId.Cash => 1m,
            NeedId.ColdStorage => profile.HasChestFreezer ? 2m : 1m,
            NeedId.HeatingSystem => season == Season.Winter && !scenario.Heatwave ? 1m : 0m,
            NeedId.HotWater => 1m,
            NeedId.Internet => 1m,
            NeedId.Mobility => profile.Car == CarKind.None ? 0m : 1m,
            NeedId.WorkingFromHome => profile.Household.RunsBusinessFromHome ? 1m : 0m,
            NeedId.Laundry => day > figures.LaundryAfterDays ? 1m : 0m,
            NeedId.Entertainment => 1m,
            // The last need is the default arm, so the switch has no branch that no need can take.
            _ => profile.Profile.HasStairlift ? 1m : 0m,
        };
    }
}
