using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Solar;

/// <summary>The twelve months, as the model walks them.</summary>
public static class Months
{
    /// <summary>Days in each month of a common year.</summary>
    public static IReadOnlyList<int> Days { get; } = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    /// <summary>
    /// Share of a year's electricity used in each month for a house without electric heating: heavier
    /// in winter for lighting and the heating pump, lighter in summer. Sums to one.
    /// </summary>
    public static IReadOnlyList<decimal> DemandShare { get; } = [0.105m, 0.09m, 0.09m, 0.08m, 0.075m, 0.07m, 0.07m, 0.07m, 0.075m, 0.085m, 0.09m, 0.10m];

    /// <summary>Three-letter names for the chart.</summary>
    public static IReadOnlyList<string> Abbreviated { get; } = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
}

/// <summary>What a kilowatt of panels on a given roof produces each month, from PVGIS.</summary>
public sealed class SolarYield
{
    private SolarYield(IReadOnlyList<decimal> monthlyKwhPerKwp) => MonthlyKwhPerKwp = monthlyKwhPerKwp;

    /// <summary>Kilowatt-hours per kilowatt-peak for each of the twelve months.</summary>
    public IReadOnlyList<decimal> MonthlyKwhPerKwp { get; }

    /// <summary>The year's total per kilowatt-peak.</summary>
    public decimal AnnualKwhPerKwp => MonthlyKwhPerKwp.Sum();

    /// <summary>Creates a yield from twelve non-negative monthly figures that add to a sane year.</summary>
    public static Result<SolarYield> Create(IReadOnlyList<decimal> monthlyKwhPerKwp)
    {
        ArgumentNullException.ThrowIfNull(monthlyKwhPerKwp);
        if (monthlyKwhPerKwp.Count != 12 || monthlyKwhPerKwp.Any(static m => m < 0 || m > 250))
        {
            return Result.Failure<SolarYield>(DomainErrors.YieldUnavailable());
        }

        decimal annual = monthlyKwhPerKwp.Sum();
        return annual is < 300 or > 1500
            ? Result.Failure<SolarYield>(DomainErrors.YieldUnavailable())
            : Result.Success(new SolarYield(monthlyKwhPerKwp));
    }
}

/// <summary>A household's electricity use over a year.</summary>
public readonly record struct AnnualDemand
{
    /// <summary>Lowest annual use the model accepts.</summary>
    public const int Lowest = 500;

    /// <summary>Highest annual use the model accepts.</summary>
    public const int Highest = 30_000;

    /// <summary>What a home car charger adds in a year.</summary>
    public const int ElectricCarKwh = 2_000;

    /// <summary>What a heat pump adds in a year for an ordinary house.</summary>
    public const int HeatPumpKwh = 3_500;

    private AnnualDemand(decimal kwh) => KilowattHours = kwh;

    /// <summary>Kilowatt-hours a year.</summary>
    public decimal KilowattHours { get; }

    /// <summary>Creates a demand within the range.</summary>
    public static Result<AnnualDemand> Create(decimal kwh) =>
        kwh is < Lowest or > Highest
            ? Result.Failure<AnnualDemand>(DomainErrors.SolarInputOutOfRange("electricity use", Lowest, Highest))
            : Result.Success(new AnnualDemand(kwh));

    /// <summary>
    /// A typical figure from the household's size (Ofgem's typical domestic consumption values, 2026)
    /// plus a car charger and a heat pump where present.
    /// </summary>
    public static AnnualDemand Estimate(HouseholdSize size, bool electricCar, bool heatPump)
    {
        decimal baseline = size.Count switch
        {
            1 => 1_800m,
            2 => 2_700m,
            3 => 3_300m,
            4 => 4_100m,
            _ => 4_600m,
        };
        return new AnnualDemand(baseline + (electricCar ? ElectricCarKwh : 0) + (heatPump ? HeatPumpKwh : 0));
    }

    /// <summary>The month's share of the year.</summary>
    public decimal ForMonth(int monthIndex) => KilowattHours * Months.DemandShare[monthIndex];
}

/// <summary>Where a year's electricity came from and went, in kilowatt-hours.</summary>
/// <param name="Generated">What the panels made.</param>
/// <param name="UsedDirectly">Solar used as it was made.</param>
/// <param name="UsedFromBattery">Solar stored by day and used that evening.</param>
/// <param name="Exported">Solar sold to the grid.</param>
/// <param name="ImportedByDay">Grid electricity at the day rate.</param>
/// <param name="ImportedOvernight">Grid electricity charged into the battery on a cheap window.</param>
public sealed record EnergyFlows(decimal Generated, decimal UsedDirectly, decimal UsedFromBattery, decimal Exported, decimal ImportedByDay, decimal ImportedOvernight)
{
    /// <summary>Solar used at home, directly or through the battery.</summary>
    public decimal SelfConsumed => UsedDirectly + UsedFromBattery;

    /// <summary>Everything bought from the grid.</summary>
    public decimal Imported => ImportedByDay + ImportedOvernight;

    /// <summary>Adds two years' flows; used to total the months.</summary>
    public static EnergyFlows operator +(EnergyFlows left, EnergyFlows right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new EnergyFlows(
            left.Generated + right.Generated,
            left.UsedDirectly + right.UsedDirectly,
            left.UsedFromBattery + right.UsedFromBattery,
            left.Exported + right.Exported,
            left.ImportedByDay + right.ImportedByDay,
            left.ImportedOvernight + right.ImportedOvernight);
    }

    /// <summary>Nothing flowed.</summary>
    public static EnergyFlows Zero { get; } = new(0, 0, 0, 0, 0, 0);
}
