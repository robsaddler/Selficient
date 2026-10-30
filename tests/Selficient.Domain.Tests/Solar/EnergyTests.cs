using Selficient.Domain.Measures;
using Selficient.Domain.Solar;

namespace Selficient.Domain.Tests.Solar;

public sealed class EnergyTests
{
    public static IReadOnlyList<decimal> SouthWest { get; } = [29.35m, 45.67m, 79.6m, 108.6m, 122.41m, 120.59m, 121.84m, 104.49m, 85.78m, 54.37m, 35.88m, 27.12m];

    [Fact]
    public void A_yield_needs_twelve_sane_months()
    {
        SolarYield yield = SolarYield.Create(SouthWest).Value;

        yield.AnnualKwhPerKwp.ShouldBe(935.7m);
        SolarYield.Create([1, 2, 3]).Error.Code.ShouldBe("solar.yield-unavailable");
        SolarYield.Create([.. Enumerable.Repeat(-1m, 12)]).IsFailure.ShouldBeTrue();
        SolarYield.Create([.. Enumerable.Repeat(300m, 12)]).IsFailure.ShouldBeTrue("a month over 250 is not a UK roof");
        SolarYield.Create([.. Enumerable.Repeat(10m, 12)]).IsFailure.ShouldBeTrue("a year under 300 is a bad answer");
        SolarYield.Create([.. Enumerable.Repeat(130m, 12)]).IsFailure.ShouldBeTrue("a year over 1500 is a bad answer");
        Should.Throw<ArgumentNullException>(() => SolarYield.Create(null!));
    }

    [Fact]
    public void The_months_add_up()
    {
        Months.Days.Sum().ShouldBe(365);
        Months.DemandShare.Sum().ShouldBe(1.0m);
        Months.Abbreviated.Count.ShouldBe(12);
    }

    [Theory]
    [InlineData(1, false, false, 1_800)]
    [InlineData(2, false, false, 2_700)]
    [InlineData(3, false, false, 3_300)]
    [InlineData(4, false, false, 4_100)]
    [InlineData(5, false, false, 4_600)]
    [InlineData(9, true, true, 10_100)]
    public void Demand_is_estimated_from_the_household(int people, bool car, bool heatPump, int expected)
    {
        AnnualDemand demand = AnnualDemand.Estimate(HouseholdSize.Create(people).Value, car, heatPump);

        demand.KilowattHours.ShouldBe(expected);
        demand.ForMonth(0).ShouldBe(expected * 0.105m);
    }

    [Fact]
    public void A_given_demand_is_bounded()
    {
        AnnualDemand.Create(5_000).Value.KilowattHours.ShouldBe(5_000);
        AnnualDemand.Create(499).Error.Message.ShouldBe("The electricity use needs to be between 500 and 30000.");
        AnnualDemand.Create(30_001).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Flows_add_and_derive()
    {
        EnergyFlows a = new(100, 30, 20, 50, 40, 10);
        EnergyFlows b = new(1, 1, 1, 1, 1, 1);

        EnergyFlows sum = a + b;

        sum.ShouldBe(new EnergyFlows(101, 31, 21, 51, 41, 11));
        a.SelfConsumed.ShouldBe(50);
        a.Imported.ShouldBe(50);
        EnergyFlows.Zero.Generated.ShouldBe(0);
        Should.Throw<ArgumentNullException>(() => a + null!);
        Should.Throw<ArgumentNullException>(() => null! + a);
    }
}
