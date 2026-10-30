using Selficient.Domain.Measures;

namespace Selficient.Domain.Solar;

/// <summary>What the household has that a tariff might require.</summary>
/// <param name="HasBattery">A home battery that can charge overnight.</param>
/// <param name="HasElectricCar">A car charged at home.</param>
/// <param name="HasHeatPump">A heat pump.</param>
public readonly record struct TariffContext(bool HasBattery, bool HasElectricCar, bool HasHeatPump);

/// <summary>
/// A way of paying for electricity. Each strategy knows who it is for, what the overnight window
/// is worth, and how to price a year's flows. Prices are pence per kilowatt-hour as seen on the
/// date given, South Wales region, and move at every price-cap change.
/// </summary>
public interface IEnergyTariff
{
    /// <summary>The name a person would search for.</summary>
    string Name { get; }

    /// <summary>One sentence on who it suits.</summary>
    string Suits { get; }

    /// <summary>Hours of cheap electricity each night, zero for a flat tariff.</summary>
    int OvernightHours { get; }

    /// <summary>When the prices were last checked.</summary>
    DateOnly Seen { get; }

    /// <summary>Whether this household can be on it.</summary>
    bool IsEligible(TariffContext context);

    /// <summary>The year's bill before standing charges; negative means a net credit.</summary>
    Money AnnualBill(EnergyFlows flows);
}

/// <summary>Shared arithmetic: day imports at one rate, overnight at another, export paid.</summary>
public abstract class RatedTariff : IEnergyTariff
{
    /// <summary>Creates the tariff.</summary>
    protected RatedTariff(string name, string suits, decimal dayPence, decimal overnightPence, int overnightHours, decimal exportPence, DateOnly seen)
    {
        Name = name;
        Suits = suits;
        DayPence = dayPence;
        OvernightPence = overnightPence;
        OvernightHours = overnightHours;
        ExportPence = exportPence;
        Seen = seen;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Suits { get; }

    /// <summary>Pence per kilowatt-hour by day.</summary>
    public decimal DayPence { get; }

    /// <summary>Pence per kilowatt-hour in the overnight window.</summary>
    public decimal OvernightPence { get; }

    /// <inheritdoc />
    public int OvernightHours { get; }

    /// <summary>Pence paid per exported kilowatt-hour.</summary>
    public decimal ExportPence { get; }

    /// <inheritdoc />
    public DateOnly Seen { get; }

    /// <inheritdoc />
    public abstract bool IsEligible(TariffContext context);

    /// <inheritdoc />
    public Money AnnualBill(EnergyFlows flows)
    {
        ArgumentNullException.ThrowIfNull(flows);
        decimal pence = (flows.ImportedByDay * DayPence) + (flows.ImportedOvernight * OvernightPence) - (flows.Exported * ExportPence);
        return Money.FromPence((long)Math.Round(pence, MidpointRounding.AwayFromZero));
    }
}

/// <summary>The default: the price cap with the Smart Export Guarantee's typical rate.</summary>
public sealed class PriceCapTariff : RatedTariff
{
    /// <summary>The cap rate used as the baseline for every saving.</summary>
    public static decimal CapPence => 26.55m;

    /// <summary>Creates the tariff.</summary>
    public PriceCapTariff()
        : base("Standard variable tariff (price cap) with a 12p export rate", "Anyone. The baseline every saving is measured against.", CapPence, CapPence, 0, 12m, new DateOnly(2026, 10, 29))
    {
    }

    /// <inheritdoc />
    public override bool IsEligible(TariffContext context) => true;
}

/// <summary>A battery tariff: a three-hour cheap window to fill the battery from, and a strong export rate.</summary>
public sealed class BatteryOvernightTariff : RatedTariff
{
    /// <summary>Creates the tariff.</summary>
    public BatteryOvernightTariff()
        : base("Battery tariff (EDF Empower Fixed, 01:00 to 04:00 at 10.36p)", "Any home with a battery. No car or heat pump needed.", 20.22m, 10.36m, 3, 16.5m, new DateOnly(2026, 10, 29))
    {
    }

    /// <inheritdoc />
    public override bool IsEligible(TariffContext context) => context.HasBattery;
}

/// <summary>An EV tariff: six cheap hours a night, which a battery can share.</summary>
public sealed class ElectricCarTariff : RatedTariff
{
    /// <summary>Creates the tariff.</summary>
    public ElectricCarTariff()
        : base("Car tariff (Intelligent Octopus Go, 23:30 to 05:30 at 8p)", "Homes with an electric car charged at home.", 30m, 8m, 6, 15m, new DateOnly(2026, 10, 29))
    {
    }

    /// <inheritdoc />
    public override bool IsEligible(TariffContext context) => context.HasElectricCar;
}

/// <summary>A heat-pump tariff: three cheap windows adding to eight hours.</summary>
public sealed class HeatPumpTariff : RatedTariff
{
    /// <summary>Creates the tariff.</summary>
    public HeatPumpTariff()
        : base("Heat pump tariff (Cosy Octopus, three windows at 13p)", "Homes with a heat pump.", 29m, 13m, 8, 15m, new DateOnly(2026, 10, 29))
    {
    }

    /// <inheritdoc />
    public override bool IsEligible(TariffContext context) => context.HasHeatPump;
}

/// <summary>The tariffs the model knows, in a fixed order.</summary>
public static class TariffBook
{
    /// <summary>All strategies.</summary>
    public static IReadOnlyList<IEnergyTariff> All { get; } =
    [
        new PriceCapTariff(),
        new BatteryOvernightTariff(),
        new ElectricCarTariff(),
        new HeatPumpTariff(),
    ];

    /// <summary>The baseline the savings are measured against.</summary>
    public static IEnergyTariff Baseline => All[0];
}
