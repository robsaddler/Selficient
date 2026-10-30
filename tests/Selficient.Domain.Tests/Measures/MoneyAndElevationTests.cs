using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Tests.Measures;

public sealed class MoneyAndElevationTests
{
    [Fact]
    public void Money_cannot_be_negative()
    {
        Result<Money> result = Money.FromPounds(-0.01m);

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldBe("The number of pounds cannot be less than zero.");
    }

    [Fact]
    public void Money_adds_in_pence_without_drift()
    {
        Money a = Money.FromPounds(0.1m).Value;
        Money b = Money.FromPounds(0.2m).Value;

        (a + b).Pence.ShouldBe(30);
        a.Add(b).Pounds.ShouldBe(0.3m);
        Money.Zero.Pence.ShouldBe(0);
        Money.FromPounds(1.005m).Value.Pence.ShouldBe(101);
    }

    [Fact]
    public void A_cost_range_cannot_be_inverted()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MoneyRange.Pounds(10m, 5m));
    }

    [Fact]
    public void Cost_ranges_add_end_to_end()
    {
        MoneyRange a = MoneyRange.Pounds(10m, 20m);
        MoneyRange b = MoneyRange.Pounds(5m, 7.5m);

        MoneyRange sum = a + b;
        sum.From.Pounds.ShouldBe(15m);
        sum.To.Pounds.ShouldBe(27.5m);
        a.Add(b).ShouldBe(sum);
        MoneyRange.Free.ShouldBe(new MoneyRange(Money.Zero, Money.Zero));
    }

    [Theory]
    [InlineData(-10.01)]
    [InlineData(1400.01)]
    public void Elevation_rejects_values_off_the_british_isles(decimal metres)
    {
        Result<Elevation> result = Elevation.Create(metres);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("dwelling.elevation");
    }

    [Theory]
    [InlineData(-10, false)]
    [InlineData(149.9, false)]
    [InlineData(150, true)]
    [InlineData(1400, true)]
    public void Elevation_knows_when_a_house_is_exposed(decimal metres, bool exposed)
    {
        Elevation elevation = Elevation.Create(metres).Value;

        elevation.Metres.ShouldBe(metres);
        elevation.IsExposed.ShouldBe(exposed);
    }
}
