using FsCheck;
using FsCheck.Xunit;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Tests.Measures;

public sealed class MeasuresTests
{
    [Fact]
    public void Days_cannot_be_negative()
    {
        Result<Days> result = Days.Create(-0.1m);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("quantity.negative");
        result.Error.Message.ShouldBe("The number of days cannot be less than zero.");
    }

    [Fact]
    public void Days_accepts_zero_and_fractions()
    {
        Days.Create(0m).Value.ShouldBe(Days.Zero);
        Days.Create(0.5m).Value.Value.ShouldBe(0.5m);
    }

    [Fact]
    public void The_shorter_duration_wins_because_readiness_is_the_weakest_link()
    {
        Days three = Days.Create(3m).Value;
        Days seven = Days.Create(7m).Value;

        Days.Min(three, seven).ShouldBe(three);
        Days.Min(seven, three).ShouldBe(three);
        Days.Min(three, three).ShouldBe(three);
    }

    [Fact]
    public void Litres_cannot_be_negative()
    {
        Result<Litres> result = Litres.Create(-1m);

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldBe("The number of litres cannot be less than zero.");
    }

    [Fact]
    public void Litres_add_up()
    {
        Litres a = Litres.Create(10m).Value;
        Litres b = Litres.Create(26m).Value;

        (a + b).Value.ShouldBe(36m);
        a.Add(b).ShouldBe(a + b);
        Litres.Zero.Value.ShouldBe(0m);
    }

    [Fact]
    public void Thirty_six_litres_is_three_days_of_drinking_water_for_four_people()
    {
        Litres water = Litres.Create(36m).Value;
        HouseholdSize four = HouseholdSize.Create(4).Value;

        water.DaysOfDrinkingWaterFor(four).Value.ShouldBe(3m);
    }

    [Fact]
    public void A_household_needs_at_least_one_person()
    {
        HouseholdSize.Create(0).Error.Code.ShouldBe("household.empty");
        HouseholdSize.Create(-2).Error.Message.ShouldBe("A household needs at least one person in it.");
        HouseholdSize.Create(1).Value.Count.ShouldBe(1);
    }

    [Property]
    public bool Adding_water_never_lowers_days_ready(PositiveInt stored, PositiveInt extra, PositiveInt people)
    {
        Litres before = Litres.Create(stored.Get).Value;
        Litres after = before + Litres.Create(extra.Get).Value;
        HouseholdSize household = HouseholdSize.Create(people.Get).Value;

        return after.DaysOfDrinkingWaterFor(household).Value >= before.DaysOfDrinkingWaterFor(household).Value;
    }

    [Property]
    public bool Days_ready_is_never_negative(NonNegativeInt stored, PositiveInt people)
    {
        Litres water = Litres.Create(stored.Get).Value;
        HouseholdSize household = HouseholdSize.Create(people.Get).Value;

        return water.DaysOfDrinkingWaterFor(household).Value >= 0m;
    }
}
