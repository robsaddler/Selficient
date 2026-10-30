using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;

namespace Selficient.Domain.Readiness;

/// <summary>
/// What the household has right now, as provisions, and the one honest number that follows from it.
/// Days Ready is the weakest of water, food and light: readiness is always the weakest link.
/// </summary>
public sealed record Inventory(Provisions Provisions)
{
    /// <summary>Drinking water per person per day, the government's figure.</summary>
    public static decimal WaterLitresPerPersonPerDay => Litres.DrinkingPerPersonPerDay;

    /// <summary>Hours of light a household uses on a winter evening without mains power.</summary>
    public static decimal LightHoursPerDay => 5m;

    /// <summary>
    /// What a typical house holds before it does anything: a kettle and some bottles of water, a
    /// fridge of food, and the torches on a couple of phones. About half a day.
    /// </summary>
    public static Inventory Baseline(Household household)
    {
        ArgumentNullException.ThrowIfNull(household);
        int people = household.Size.Count;
        return new Inventory(new Provisions(
            WaterLitres: 1.5m * people,
            FoodDays: 0.5m,
            LightHours: 2.5m));
    }

    /// <summary>The inventory after an action is ticked.</summary>
    public Inventory With(PlanAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new Inventory(Provisions.Plus(action.Provides));
    }

    /// <summary>How many days the household could stand on its own with what it has.</summary>
    public Days DaysReady(Household household)
    {
        ArgumentNullException.ThrowIfNull(household);
        return DaysReadyCalculator.Calculate(Provisions, household.Size);
    }
}

/// <summary>The calculation behind the signature figure. Pure, deterministic, benchmarked.</summary>
public static class DaysReadyCalculator
{
    /// <summary>The minimum across water, food and light, never negative.</summary>
    public static Days Calculate(Provisions provisions, HouseholdSize household)
    {
        ArgumentNullException.ThrowIfNull(provisions);

        decimal water = provisions.WaterLitres / (household.Count * Inventory.WaterLitresPerPersonPerDay);
        decimal food = provisions.FoodDays;
        decimal light = provisions.LightHours / Inventory.LightHoursPerDay;

        decimal weakest = Math.Min(water, Math.Min(food, light));
        return Days.Create(Math.Round(weakest, 1, MidpointRounding.ToZero)).Value;
    }
}
