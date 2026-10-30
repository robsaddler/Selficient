using Selficient.Domain.Solar;

namespace Selficient.Domain.Tests.Solar;

public sealed class TariffTests
{
    [Fact]
    public void Eligibility_follows_what_the_household_has()
    {
        TariffContext nothing = new(false, false, false);
        TariffContext battery = new(true, false, false);
        TariffContext car = new(false, true, false);
        TariffContext heatPump = new(false, false, true);

        new PriceCapTariff().IsEligible(nothing).ShouldBeTrue();
        new BatteryOvernightTariff().IsEligible(nothing).ShouldBeFalse();
        new BatteryOvernightTariff().IsEligible(battery).ShouldBeTrue();
        new ElectricCarTariff().IsEligible(nothing).ShouldBeFalse();
        new ElectricCarTariff().IsEligible(car).ShouldBeTrue();
        new HeatPumpTariff().IsEligible(nothing).ShouldBeFalse();
        new HeatPumpTariff().IsEligible(heatPump).ShouldBeTrue();
        TariffBook.Baseline.ShouldBeOfType<PriceCapTariff>();
        TariffBook.All.Count.ShouldBe(4);
    }

    [Fact]
    public void A_bill_is_day_plus_overnight_less_export_in_whole_pence()
    {
        BatteryOvernightTariff tariff = new();

        tariff.AnnualBill(new EnergyFlows(0, 0, 0, 100, 1_000, 500)).Pence.ShouldBe((long)Math.Round((1_000 * 20.22m) + (500 * 10.36m) - (100 * 16.5m)));
        tariff.AnnualBill(new EnergyFlows(0, 0, 0, 10_000, 0, 0)).Pence.ShouldBe(-165_000, "more export than import is a credit");
        tariff.Name.ShouldContain("EDF Empower");
        tariff.Suits.ShouldEndWith(".");
        tariff.OvernightHours.ShouldBe(3);
        tariff.DayPence.ShouldBe(20.22m);
        tariff.OvernightPence.ShouldBe(10.36m);
        tariff.ExportPence.ShouldBe(16.5m);
        Should.Throw<ArgumentNullException>(() => tariff.AnnualBill(null!));
    }

    [Fact]
    public void Prices_are_reviewed_every_six_months()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        TariffBook.All.ShouldAllBe(t => today.DayNumber - t.Seen.DayNumber <= 180, "a tariff price over 180 days old is a reminder to review the book");
    }
}
