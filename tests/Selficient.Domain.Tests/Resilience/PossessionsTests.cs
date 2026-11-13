using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;
using Selficient.Domain.Resilience;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Resilience;

public sealed class PossessionsTests
{
    private static readonly ResilienceProfile Rob = ResilienceProfile.FromProfile(Profiles.RobsHouse());
    private static readonly ResilienceProfile Flat = ResilienceProfile.FromProfile(Profiles.Flat());

    private static HashSet<string> Ids(params string[] ids) => new(ids, StringComparer.Ordinal);

    private static Dictionary<string, int> Figures(params (string Id, int Amount)[] figures) => figures.ToDictionary(f => f.Id, f => f.Amount, StringComparer.Ordinal);

    [Fact]
    public void Ten_days_of_tins_hold_the_three_day_shelf_and_a_third_of_the_month_whatever_was_ticked()
    {
        Possessions has = Possessions.Of(Rob, Ids("food-one-month", "headtorches"), Figures((MeasuredStocks.FoodDays, 10)));

        has.Have.ShouldBe(["food-no-cook-three-days", "headtorches"], ignoreOrder: true, customMessage: "ten days reaches the three-day shelf and not the month, and the tick on the month gives way to the count");
        has.Held.ShouldBe(["stored-food", "headtorches"], ignoreOrder: true, customMessage: "the engine sees one shelf of food, the counted one");
        has.Stock["stored-food"].ShouldBe(40m, "ten days for four people");
        has.ProgressOn("food-no-cook-three-days").ShouldBe(new Progress("food-no-cook-three-days", MeasuredStocks.FoodDays, 10m, 3m, 0m));
        Progress month = has.ProgressOn("food-one-month")!;
        month.Amount.ShouldBe(10m);
        month.Target.ShouldBe(30m);
        month.CostRemaining.ShouldBe(20m / 30m, "two thirds of the month is still to buy");
        has.ProgressOn("headtorches").ShouldBeNull("a torch is had or not");
        has.ProgressOn("water-cans").ShouldBeNull("no figure was put on the cans");
    }

    [Fact]
    public void A_counted_shelf_feeds_the_house_for_as_long_as_it_was_counted()
    {
        HashSet<string> cash = Ids("cash-in-small-notes");

        Board three = OutageBoard.Simulate(Rob, Ids("cash-in-small-notes", "food-no-cook-three-days"), Scenarios.Snow, Season.Winter);
        Board ten = OutageBoard.Simulate(Rob, Possessions.Of(Rob, cash, Figures((MeasuredStocks.FoodDays, 10))), Scenarios.Snow, Season.Winter);
        Board same = OutageBoard.Simulate(Rob, Possessions.Of(Rob, cash, Figures((MeasuredStocks.FoodDays, 3))), Scenarios.Snow, Season.Winter);
        Board none = OutageBoard.Simulate(Rob, Possessions.Of(Rob, Ids("cash-in-small-notes", "food-no-cook-three-days"), Figures((MeasuredStocks.FoodDays, 0))), Scenarios.Snow, Season.Winter);
        Board bare = OutageBoard.Simulate(Rob, cash, Scenarios.Snow, Season.Winter);

        BoardRow food = ten.Row("Food")!;
        (food.FirstRationedDay ?? int.MaxValue).ShouldBeGreaterThan(three.Row("Food")!.FirstRationedDay ?? int.MaxValue, "ten days of tins outlast three");
        food.Days[4].CarriedBy.ShouldBe("stored-food", "the board names the shelf the household counted");
        same.Row("Food")!.FirstRationedDay.ShouldBe(three.Row("Food")!.FirstRationedDay, "a count of three days is the three-day shelf");
        none.Row("Food")!.FirstRationedDay.ShouldBe(bare.Row("Food")!.FirstRationedDay, "a count of nothing is nothing, though the box was ticked");
        ten.DaysReady.ShouldBeGreaterThanOrEqualTo(three.DaysReady);
    }

    [Fact]
    public void Adding_an_action_tops_a_counted_shelf_up_to_what_it_asks_and_never_stacks_another_on_it()
    {
        Possessions has = Possessions.Of(Rob, Ids(), Figures((MeasuredStocks.FoodDays, 10)));
        Possessions month = has.Copy();
        month.Add("food-one-month");
        Possessions shelf = has.Copy();
        shelf.Add("food-no-cook-three-days");
        Possessions uncounted = Possessions.Of(Rob, Ids());
        uncounted.Add("food-one-month");
        uncounted.Add("headtorches");

        month.Stock["stored-food"].ShouldBe(120m, "a month for four, not ten days and a month");
        month.Have.ShouldContain("food-one-month");
        shelf.Stock["stored-food"].ShouldBe(40m, "already past three days, so nothing changes");
        has.Stock["stored-food"].ShouldBe(40m, "the copy was added to, not the original");
        has.Have.ShouldNotContain("food-one-month");
        uncounted.Held.ShouldBe(["food-one-month", "headtorches"], ignoreOrder: true, customMessage: "with no figure given the action is simply held, as it always was");
        uncounted.Stock.ShouldBeEmpty();
    }

    [Fact]
    public void Each_stock_is_counted_in_its_own_unit_and_turned_into_the_engines()
    {
        Possessions rob = Possessions.Of(Rob, Ids("water-bottled-three-days"), Figures((MeasuredStocks.BottledWaterLitres, 12), (MeasuredStocks.WaterCansLitres, 100), (MeasuredStocks.CashPounds, 200), (MeasuredStocks.StoveCartridges, 4)));
        Possessions flat = Possessions.Of(Flat, Ids(), Figures((MeasuredStocks.WaterCansLitres, 25), (MeasuredStocks.CashPounds, 100), (MeasuredStocks.BottledWaterLitres, 9)));

        rob.Stock["water-bottled-three-days"].ShouldBe(12m, "litres are litres");
        rob.Have.ShouldNotContain("water-bottled-three-days", "twelve litres is a third of the 36 the plan asks four people for");
        rob.ProgressOn("water-bottled-three-days")!.Target.ShouldBe(36m);
        rob.Have.ShouldContain("water-cans", "a hundred litres is the four cans the plan asks a house of four for");
        rob.Stock["cash-in-small-notes"].ShouldBe(5m, "£200 is five days for four people at ten pounds each a day");
        rob.ProgressOn("cash-in-small-notes")!.Target.ShouldBe(400m);
        rob.ProgressOn("cash-in-small-notes")!.CostRemaining.ShouldBe(1m, "holding cash is not a purchase to share out");
        rob.Stock["butane-cartridge-stove"].ShouldBe(4m);
        rob.ProgressOn("butane-cartridge-stove")!.CostRemaining.ShouldBe(1m, "a stove with four cartridges is not forty per cent of a stove");
        rob.Held.ShouldContain("butane-cartridge-stove");
        flat.Have.ShouldBe(["water-cans", "cash-in-small-notes", "water-bottled-three-days"], ignoreOrder: true, customMessage: "one person in a flat is asked for one can, £100 and nine litres");
        flat.ProgressOn("cash-in-small-notes")!.CostRemaining.ShouldBe(0m);
        Assumptions.Default.CashPoundsPerPersonPerDay.ShouldBe(10);
    }

    [Fact]
    public void A_household_that_says_a_day_costs_twenty_pounds_needs_twice_the_cash_for_the_same_days()
    {
        Assumptions twenty = Assumptions.Create(20).Value;
        Assumptions five = Assumptions.Create(5).Value;
        Dictionary<string, int> twoHundred = Figures((MeasuredStocks.CashPounds, 200));

        Possessions plans = Possessions.Of(Rob, Ids(), twoHundred);
        Possessions dear = Possessions.Of(Rob, Ids(), twoHundred, twenty);
        Possessions cheap = Possessions.Of(Rob, Ids(), twoHundred, five);
        Possessions copied = cheap.Copy();
        copied.Add("cash-in-small-notes");

        plans.Stock["cash-in-small-notes"].ShouldBe(5m, "£200 is five days for four at the plan's ten pounds");
        dear.Stock["cash-in-small-notes"].ShouldBe(2.5m, "and two and a half at twenty");
        dear.ProgressOn("cash-in-small-notes")!.Target.ShouldBe(800m);
        cheap.Stock["cash-in-small-notes"].ShouldBe(10m);
        cheap.Have.ShouldContain("cash-in-small-notes", "at five pounds a day £200 is the ten days the plan asks for");
        copied.Stock["cash-in-small-notes"].ShouldBe(10m, "a copy keeps the household's rate");
        MeasuredStocks.Target(MeasuredStocks.Find(MeasuredStocks.CashPounds)!, "cash-in-small-notes", Rob, twenty).ShouldBe(800m);
        MeasuredStocks.Target(MeasuredStocks.Find(MeasuredStocks.CashPounds)!, "cash-in-small-notes", Rob).ShouldBe(400m);
        Assumptions.Create(null).Value.ShouldBeSameAs(Assumptions.Default, "no figure given is the plan's");
        (Assumptions.Default.PlansCashPoundsPerPersonPerDay, Assumptions.Default.CashDays).ShouldBe((10, 10m));

        // An expert's figure is the plan's own from then on, and a household's still stands over it.
        Selficient.Domain.Parameters.ParameterSet set = Selficient.Domain.Parameters.ParameterSet.Create(1, new Dictionary<string, decimal> { [Assumptions.CashFigure] = 15m, ["cash-in-small-notes.stock"] = 7m }).Value;
        Assumptions experts = Assumptions.Under(set);
        (experts.CashPoundsPerPersonPerDay, experts.PlansCashPoundsPerPersonPerDay, experts.CashDays).ShouldBe((15, 15, 7m));
        Assumptions.Under(set).ShouldBeSameAs(experts, "worked out once for a set");
        Assumptions.Create(null, set).Value.ShouldBeSameAs(experts);
        Assumptions own = Assumptions.Create(20, set).Value;
        (own.CashPoundsPerPersonPerDay, own.PlansCashPoundsPerPersonPerDay, own.CashDays).ShouldBe((20, 15, 7m));
        Assumptions.Create(0, set).IsFailure.ShouldBeTrue();
        Assumptions.Definition.HouseholdMayOverride.ShouldBeTrue();
        Selficient.Domain.Parameters.ParameterSet.Create(1, new Dictionary<string, decimal> { [Assumptions.CashFigure] = 12.5m }).Error.Message.ShouldBe("Cash a person needs for a day when only cash will do must be between 1 and 100 whole pounds.");
        ResilienceProfile dearer = ResilienceProfile.FromProfile(Profiles.RobsHouse() with { Figures = set });
        MeasuredStocks.Target(MeasuredStocks.Find(MeasuredStocks.CashPounds)!, "cash-in-small-notes", dearer).ShouldBe(420m, "seven days for four people at fifteen pounds, with no figure of the household's");
        Possessions.Of(dearer, new HashSet<string>(), new Dictionary<string, int> { [MeasuredStocks.CashPounds] = 420 }).Have.ShouldContain("cash-in-small-notes");
        Should.Throw<ArgumentNullException>(() => Assumptions.Under(null!));
        Assumptions.Create(1).IsSuccess.ShouldBeTrue();
        Assumptions.Create(100).IsSuccess.ShouldBeTrue();
        Assumptions.Create(0).Error.Message.ShouldBe("The cash a person needs for a day must be between 1 and 100 pounds.");
        Assumptions.Create(101).Error.Code.ShouldBe("plan.assumptions");
    }

    [Fact]
    public void Figures_from_a_browser_are_checked_before_anything_uses_them()
    {
        MeasuredStocks.Check(Figures((MeasuredStocks.FoodDays, 365), (MeasuredStocks.CashPounds, 0))).IsSuccess.ShouldBeTrue();
        Result<IReadOnlyDictionary<string, int>> unknown = MeasuredStocks.Check(Figures(("headtorches", 4)));
        Result<IReadOnlyDictionary<string, int>> negative = MeasuredStocks.Check(Figures((MeasuredStocks.FoodDays, -1)));
        Result<IReadOnlyDictionary<string, int>> huge = MeasuredStocks.Check(Figures((MeasuredStocks.FoodDays, 366)));

        unknown.Error.Message.ShouldBe("One of the amounts is for something the plan does not measure.");
        negative.Error.Message.ShouldBe("One of the amounts is below zero or more than the plan can count.");
        huge.Error.Code.ShouldBe("plan.amounts");
        MeasuredStocks.All.Select(stock => stock.Id).ShouldBeUnique();
        MeasuredStocks.All.ShouldAllBe(stock => Providers.Find(stock.ProviderId) != null && Providers.Find(stock.ProviderId)!.Stock != null, "a figure sets a stock, so its provider must have one");
        MeasuredStocks.All.SelectMany(stock => stock.Actions).Select(action => action.ActionId).ShouldBeUnique("an action counts towards one stock");
        MeasuredStocks.Find("nothing").ShouldBeNull();
        MeasuredStocks.ForAction("headtorches").ShouldBeNull();
        Providers.Find("stored-food")!.Stock!.Quantity.ShouldBe(0m, "ticking the counted shelf without a count gives nothing");
    }

    [Fact]
    public void A_share_of_a_cost_is_rounded_to_the_penny_and_bounded()
    {
        MoneyRange month = MoneyRange.Pounds(140m, 240m);

        month.Share(20m / 30m).ShouldBe(MoneyRange.Pounds(93.33m, 160m));
        month.Share(0m).ShouldBe(MoneyRange.Free);
        month.Share(1m).ShouldBe(month);
        Should.Throw<ArgumentOutOfRangeException>(() => month.Share(1.01m));
        Should.Throw<ArgumentOutOfRangeException>(() => month.Share(-0.1m));
    }

    [Fact]
    public void Nulls_and_figures_that_skipped_the_check_are_refused()
    {
        Possessions has = Possessions.Of(Rob, Ids());

        Should.Throw<ArgumentNullException>(() => Possessions.Of(null!, Ids()));
        Should.Throw<ArgumentNullException>(() => Possessions.Of(Rob, null!));
        Should.Throw<ArgumentException>(() => Possessions.Of(Rob, Ids(), Figures(("headtorches", 1))));
        Should.Throw<ArgumentNullException>(() => has.Add(null!));
        Should.Throw<ArgumentNullException>(() => has.ProgressOn(null!));
        Should.Throw<ArgumentNullException>(() => MeasuredStocks.Check(null!));
        Should.Throw<ArgumentNullException>(() => MeasuredStocks.Target(null!, "x", Rob));
        Should.Throw<ArgumentNullException>(() => MeasuredStocks.Target(MeasuredStocks.All[0], "food-one-month", null!));
        Should.Throw<ArgumentNullException>(() => OutageBoard.Simulate(null!, has, Scenarios.Storm, Season.Winter));
        Should.Throw<ArgumentNullException>(() => OutageBoard.Simulate(Rob, (Possessions)null!, Scenarios.Storm, Season.Winter));
        Should.Throw<ArgumentNullException>(() => OutageBoard.Simulate(Rob, has, null!, Season.Winter));
    }
}
