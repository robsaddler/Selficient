using Selficient.Domain.Measures;

namespace Selficient.Domain.Solar;

/// <summary>Everything the model needs, already validated.</summary>
/// <param name="Yield">What a kilowatt-peak makes on this roof each month.</param>
/// <param name="Array">The panels.</param>
/// <param name="Battery">The battery, or none.</param>
/// <param name="Demand">The year's electricity use.</param>
/// <param name="Storeys">How many storeys the roof is above the ground; three or more means scaffolding money.</param>
/// <param name="Context">What the household has that a tariff might require.</param>
public sealed record SolarScenario(SolarYield Yield, PanelArray Array, Battery Battery, AnnualDemand Demand, int Storeys, TariffContext Context);

/// <summary>One tariff priced against this household's flows.</summary>
/// <param name="Tariff">The tariff.</param>
/// <param name="AnnualBill">The year's bill; negative is a credit.</param>
/// <param name="AnnualSaving">What it saves against the baseline bill without solar.</param>
public sealed record TariffOutcome(IEnergyTariff Tariff, Money AnnualBill, Money AnnualSaving);

/// <summary>The installed cost, line by line, at zero-rate VAT.</summary>
/// <param name="Panels">Panels, inverter, fitting.</param>
/// <param name="Battery">The battery itself.</param>
/// <param name="Backup">Gateway or protected circuits.</param>
/// <param name="Scaffolding">The premium for a tall roof.</param>
public sealed record CostBreakdown(MoneyRange Panels, MoneyRange Battery, MoneyRange Backup, MoneyRange Scaffolding)
{
    /// <summary>Everything together.</summary>
    public MoneyRange Total => Panels + Battery + Backup + Scaffolding;
}

/// <summary>Years to earn the cost back, cheapest-and-best to dearest-and-worst.</summary>
/// <param name="From">The hopeful end.</param>
/// <param name="To">The cautious end; null when the saving is nothing.</param>
public readonly record struct PaybackYears(decimal From, decimal? To);

/// <summary>The estimate, as the page shows it.</summary>
/// <param name="Scenario">What went in.</param>
/// <param name="MonthlyGeneration">Kilowatt-hours the array makes each month.</param>
/// <param name="MonthlyDemand">Kilowatt-hours the house uses each month.</param>
/// <param name="WithBattery">The year's flows with the battery as specified (equal to <paramref name="WithoutBattery"/> when there is none).</param>
/// <param name="WithoutBattery">The year's flows with panels alone.</param>
/// <param name="BaselineBill">The bill today, on the price cap, with no panels.</param>
/// <param name="Tariffs">Every eligible tariff priced, best saving first.</param>
/// <param name="Cost">What it costs.</param>
/// <param name="Payback">How long it takes on the best tariff.</param>
public sealed record SolarEstimate(
    SolarScenario Scenario,
    IReadOnlyList<decimal> MonthlyGeneration,
    IReadOnlyList<decimal> MonthlyDemand,
    EnergyFlows WithBattery,
    EnergyFlows WithoutBattery,
    Money BaselineBill,
    IReadOnlyList<TariffOutcome> Tariffs,
    CostBreakdown Cost,
    PaybackYears Payback)
{
    /// <summary>The tariff that saves most.</summary>
    public TariffOutcome Best => Tariffs[0];

    /// <summary>The year's generation.</summary>
    public decimal AnnualGeneration => WithBattery.Generated;
}

/// <summary>
/// A deterministic monthly model of a rooftop array with an optional battery. Documented
/// assumptions, integer pence, no randomness, no network; the same inputs give the same page.
/// <para>
/// Each month: the array makes <c>yield × kWp</c>. Thirty percent of that is used as it is made,
/// which is the usual figure for a home whose yearly generation is close to its use. A battery
/// stores what is left, up to its daily cycle, and gives it back that evening. What the battery
/// does not use of its daily cycle it fills overnight on a cheap window, where the tariff has one.
/// The rest is exported or imported by day.
/// </para>
/// </summary>
public static class SolarEstimator
{
    /// <summary>Share of generation used as it is made, before any battery.</summary>
    public static decimal DirectUseShare => 0.30m;

    /// <summary>Fixed part of a panel install: inverter, scaffolding for a two-storey house, paperwork.</summary>
    public const int PanelsFixedPounds = 1_800;

    /// <summary>Per-kilowatt-peak range for panels and fitting.</summary>
    public static (int From, int To) PanelsPerKwpPounds => (1_000, 1_300);

    /// <summary>Per-kilowatt-hour range for a home battery.</summary>
    public static (int From, int To) BatteryPerKwhPounds => (300, 420);

    /// <summary>Protected circuits: a small board and a changeover.</summary>
    public static (int From, int To) ProtectedCircuitsPounds => (500, 1_500);

    /// <summary>A whole-home gateway.</summary>
    public static (int From, int To) WholeHomePounds => (1_500, 2_500);

    /// <summary>Extra scaffolding for three storeys and above.</summary>
    public static (int From, int To) TallRoofPounds => (800, 1_500);

    /// <summary>Runs the model.</summary>
    public static SolarEstimate Estimate(SolarScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        decimal[] generation = new decimal[12];
        decimal[] demand = new decimal[12];
        for (int month = 0; month < 12; month++)
        {
            generation[month] = Round(scenario.Yield.MonthlyKwhPerKwp[month] * scenario.Array.KilowattsPeak);
            demand[month] = Round(scenario.Demand.ForMonth(month));
        }

        EnergyFlows withBattery = EnergyFlows.Zero;
        EnergyFlows withoutBattery = EnergyFlows.Zero;
        for (int month = 0; month < 12; month++)
        {
            withBattery += Month(generation[month], demand[month], scenario.Battery, Months.Days[month]);
            withoutBattery += Month(generation[month], demand[month], Battery.None, Months.Days[month]);
        }

        Money baseline = TariffBook.Baseline.AnnualBill(new EnergyFlows(0, 0, 0, 0, scenario.Demand.KilowattHours, 0));
        TariffContext context = scenario.Context with { HasBattery = scenario.Battery.IsPresent };
        List<TariffOutcome> outcomes = [];
        foreach (IEnergyTariff tariff in TariffBook.All)
        {
            if (!tariff.IsEligible(context))
            {
                continue;
            }

            // A tariff without an overnight window cannot shift anything: its overnight import is charged by day.
            EnergyFlows priced = tariff.OvernightHours == 0
                ? withBattery with { ImportedByDay = withBattery.Imported, ImportedOvernight = 0 }
                : withBattery;
            Money bill = tariff.AnnualBill(priced);
            outcomes.Add(new TariffOutcome(tariff, bill, baseline.Minus(bill)));
        }

        outcomes.Sort(static (a, b) => b.AnnualSaving.Pence.CompareTo(a.AnnualSaving.Pence));

        CostBreakdown cost = Cost(scenario);
        return new SolarEstimate(scenario, generation, demand, withBattery, withoutBattery, baseline, outcomes, cost, PaybackFor(cost.Total, outcomes[0].AnnualSaving));
    }

    private static EnergyFlows Month(decimal generated, decimal demanded, Battery battery, int days)
    {
        decimal direct = Round(Math.Min(generated * DirectUseShare, demanded));
        decimal cycle = battery.KilowattHours * Battery.DailyCycleShare * days;
        decimal stored = Round(Math.Min(Math.Min(generated - direct, cycle), demanded - direct));
        decimal exported = generated - direct - stored;
        decimal imported = demanded - direct - stored;
        decimal overnight = Round(Math.Min(imported, Math.Max(0, cycle - stored)));
        return new EnergyFlows(generated, direct, stored, exported, imported - overnight, overnight);
    }

    private static CostBreakdown Cost(SolarScenario scenario)
    {
        decimal kwp = scenario.Array.KilowattsPeak;
        MoneyRange panels = MoneyRange.Pounds(PanelsFixedPounds + (kwp * PanelsPerKwpPounds.From), PanelsFixedPounds + (kwp * PanelsPerKwpPounds.To));
        MoneyRange battery = scenario.Battery.IsPresent
            ? MoneyRange.Pounds(scenario.Battery.KilowattHours * BatteryPerKwhPounds.From, scenario.Battery.KilowattHours * BatteryPerKwhPounds.To)
            : MoneyRange.Free;
        MoneyRange backup = scenario.Battery.Backup switch
        {
            BackupLevel.ProtectedCircuits => MoneyRange.Pounds(ProtectedCircuitsPounds.From, ProtectedCircuitsPounds.To),
            BackupLevel.WholeHome => MoneyRange.Pounds(WholeHomePounds.From, WholeHomePounds.To),
            _ => MoneyRange.Free,
        };
        MoneyRange scaffolding = scenario.Storeys >= 3 ? MoneyRange.Pounds(TallRoofPounds.From, TallRoofPounds.To) : MoneyRange.Free;
        return new CostBreakdown(panels, battery, backup, scaffolding);
    }

    /// <summary>Years to earn the cost back; no payback when there is no saving.</summary>
    public static PaybackYears PaybackFor(MoneyRange cost, Money saving)
    {
        if (saving.Pence <= 0)
        {
            return new PaybackYears(0, null);
        }

        decimal from = Math.Round(cost.From.Pence / (decimal)saving.Pence, 1);
        decimal to = Math.Round(cost.To.Pence / (decimal)saving.Pence, 1);
        return new PaybackYears(from, to);
    }

    private static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
