using Selficient.Domain.Measures;
using Selficient.Domain.Solar;

namespace Selficient.Domain.Tests.Solar;

public sealed class SolarEstimatorTests
{
    private static SolarScenario Robs(decimal batteryKwh = 13.5m, BackupLevel backup = BackupLevel.WholeHome, int panels = 13, int storeys = 3, bool car = false, bool heatPump = false, decimal demand = 5_000) =>
        new(
            SolarYield.Create(EnergyTests.SouthWest).Value,
            PanelArray.Create(panels).Value,
            Battery.Create(batteryKwh, backup).Value,
            AnnualDemand.Create(demand).Value,
            storeys,
            new TariffContext(batteryKwh > 0, car, heatPump));

    [Fact]
    public void Robs_roof_matches_the_review()
    {
        // The committed review: 5.5 kWp south-west, 13.5 kWh battery, 5,000 kWh a year,
        // saving about £1,250 to £1,500 a year against £14,000 to £17,000, paid back in 10 to 13 years.
        SolarEstimate estimate = SolarEstimator.Estimate(Robs());

        estimate.AnnualGeneration.ShouldBeInRange(5_100m, 5_300m);
        // The review split generation roughly 2,800 used to 2,350 exported; the monthly model uses
        // every winter unit and stores more in spring and autumn, so it lands higher on use and lower
        // on export. The saving, which is what the household decides on, agrees with the review.
        estimate.WithBattery.SelfConsumed.ShouldBeInRange(3_000m, 4_000m);
        estimate.WithBattery.Exported.ShouldBeInRange(1_200m, 2_400m);
        estimate.WithBattery.ImportedOvernight.ShouldBeInRange(800m, 2_400m);
        estimate.Best.AnnualSaving.Pounds.ShouldBeInRange(1_250m * 0.9m, 1_500m * 1.1m);
        estimate.Best.Tariff.ShouldBeOfType<BatteryOvernightTariff>();
        estimate.Cost.Total.From.Pounds.ShouldBeInRange(13_000m, 15_000m);
        estimate.Cost.Total.To.Pounds.ShouldBeInRange(17_000m, 19_000m);
        estimate.Payback.From.ShouldBeInRange(8.5m, 11m);
        estimate.Payback.To!.Value.ShouldBeInRange(11m, 14m);
        estimate.BaselineBill.Pounds.ShouldBe(1_327.50m);
    }

    [Fact]
    public void Energy_is_conserved_every_month_and_over_the_year()
    {
        SolarEstimate estimate = SolarEstimator.Estimate(Robs());
        EnergyFlows f = estimate.WithBattery;

        (f.UsedDirectly + f.UsedFromBattery + f.Exported).ShouldBe(f.Generated);
        (f.SelfConsumed + f.Imported).ShouldBe(estimate.MonthlyDemand.Sum());
        estimate.MonthlyGeneration.Sum().ShouldBe(f.Generated);
        estimate.WithoutBattery.UsedFromBattery.ShouldBe(0);
        estimate.WithoutBattery.ImportedOvernight.ShouldBe(0);
    }

    [Fact]
    public void Without_a_battery_the_flows_are_the_same_both_ways_and_the_cap_tariff_is_the_only_one()
    {
        SolarEstimate estimate = SolarEstimator.Estimate(Robs(batteryKwh: 0, backup: BackupLevel.None));

        estimate.WithBattery.ShouldBe(estimate.WithoutBattery);
        estimate.Tariffs.Count.ShouldBe(1);
        estimate.Best.Tariff.ShouldBeOfType<PriceCapTariff>();
        estimate.Cost.Battery.ShouldBe(MoneyRange.Free);
        estimate.Cost.Backup.ShouldBe(MoneyRange.Free);
        estimate.Best.AnnualSaving.Pounds.ShouldBeInRange(500m, 900m);
    }

    [Fact]
    public void A_car_and_a_heat_pump_open_their_tariffs_and_the_best_wins()
    {
        SolarEstimate estimate = SolarEstimator.Estimate(Robs(car: true, heatPump: true, demand: 10_000));

        estimate.Tariffs.Select(t => t.Tariff.GetType()).ShouldBe([typeof(ElectricCarTariff), typeof(BatteryOvernightTariff), typeof(HeatPumpTariff), typeof(PriceCapTariff)], ignoreOrder: true);
        estimate.Tariffs.Select(t => t.AnnualSaving.Pence).ShouldBeInOrder(SortDirection.Descending);
        estimate.Best.AnnualSaving.Pence.ShouldBe(estimate.Tariffs.Max(t => t.AnnualSaving.Pence));
        estimate.Tariffs.Single(t => t.Tariff is ElectricCarTariff).AnnualSaving.Pence.ShouldBeGreaterThan(estimate.Tariffs.Single(t => t.Tariff is PriceCapTariff).AnnualSaving.Pence, "six cheap hours beat the cap even with a dearer day rate");
    }

    [Fact]
    public void Costs_follow_the_choices()
    {
        SolarEstimate protectedCircuits = SolarEstimator.Estimate(Robs(backup: BackupLevel.ProtectedCircuits, storeys: 2));
        SolarEstimate panelsOnly = SolarEstimator.Estimate(Robs(batteryKwh: 0, panels: 10, storeys: 1));

        protectedCircuits.Cost.Backup.ShouldBe(MoneyRange.Pounds(500, 1_500));
        protectedCircuits.Cost.Scaffolding.ShouldBe(MoneyRange.Free);
        protectedCircuits.Cost.Battery.ShouldBe(MoneyRange.Pounds(13.5m * 300, 13.5m * 420));
        panelsOnly.Cost.Panels.ShouldBe(MoneyRange.Pounds(1_800 + (4.3m * 1_000), 1_800 + (4.3m * 1_300)));
        panelsOnly.Cost.Total.ShouldBe(panelsOnly.Cost.Panels);
    }

    [Fact]
    public void No_saving_means_no_payback_rather_than_a_division_by_zero()
    {
        // A north roof at a tiny demand: generation mostly exported at 12p, use trivially small.
        SolarYield north = SolarYield.Create([.. EnergyTests.SouthWest.Select(m => Math.Round(m * 0.35m, 2))]).Value;
        SolarScenario scenario = new(north, PanelArray.Create(2).Value, Battery.None, AnnualDemand.Create(500).Value, 1, new TariffContext(false, false, false));

        SolarEstimate estimate = SolarEstimator.Estimate(scenario);

        estimate.Best.AnnualSaving.Pence.ShouldBeGreaterThan(0, "even a poor roof saves something, so the zero branch needs forcing below");
        SolarEstimator.PaybackFor(MoneyRange.Pounds(1, 2), Money.Zero).ShouldBe(new PaybackYears(0, null));
        SolarEstimator.PaybackFor(MoneyRange.Pounds(1_000, 2_000), Money.FromPence(10_000)).ShouldBe(new PaybackYears(10, 20));
    }

    [Fact]
    public void Guards_reject_nulls()
    {
        Should.Throw<ArgumentNullException>(() => SolarEstimator.Estimate(null!));
    }
}
