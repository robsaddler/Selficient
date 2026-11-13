using Selficient.Application.Composition;
using Selficient.Application.Resilience;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Resilience;

public sealed class BoardComposerTests
{
    private static readonly RiskAssessmentPipeline Risks = new(ApplicationServiceCollectionExtensions.Assessors());
    private static readonly PlanBuilder Builder = new(ApplicationServiceCollectionExtensions.Rules());

    // The undecided sum with the month costed at two thirds: the same sum, less a third of the month.
    private static MoneyRange ExpectedUndecided(MoneyRange whole, MoneyRange month) =>
        new(Money.FromPence(whole.From.Pence - month.From.Pence + month.Share(20m / 30m).From.Pence), Money.FromPence(whole.To.Pence - month.To.Pence + month.Share(20m / 30m).To.Pence));

    private static Plan PlanFor(HouseholdProfile profile) => Builder.Build(profile, Risks.Run(profile)).Value;

    [Fact]
    public void The_set_is_composed_under_the_households_figures_from_the_scenario_to_the_name_on_a_source()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        Selficient.Domain.Parameters.ParameterSet figures = Selficient.Domain.Parameters.ParameterSet.Create(3, new Dictionary<string, decimal>
        {
            ["portable-power-station.store-wh"] = 2048m,
            ["scenario.water.mainswater-from-1.last-day"] = 5m,
        }).Value;
        ResilienceProfile profile = ResilienceProfile.FromProfile(rob with { Figures = figures });

        BoardSet set = BoardComposer.Compose(new BoardRequest(profile, new HashSet<string>(), PlanFor(rob), "water", null));
        BoardSet shipped = BoardComposer.Compose(new BoardRequest(ResilienceProfile.FromProfile(rob), new HashSet<string>(), PlanFor(rob), "water", null));

        set.Scenario.Sentence.ShouldStartWith("A burst main or a treatment problem stops the supply for five days.");
        set.Scenario.Id.ShouldBe("water");
        set.Sources.Single(source => source.Id == "portable-power-station").Name.ShouldBe("A 2 kWh power station");
        set.Sources.Single(source => source.Id == "large-power-station").Name.ShouldBe("A 3 kWh power station with panels");
        set.Sheet.Rows.ShouldAllBe(row => ReferenceEquals(row.Figures, figures));
        set.Cards.Count.ShouldBe(Scenarios.All.Count);
        shipped.Scenario.ShouldBeSameAs(Scenarios.WaterCut);
        shipped.Sources.Single(source => source.Id == "portable-power-station").Name.ShouldBe("A 1 kWh power station");
        shipped.Sources.Select(source => source.Sentence).ShouldAllBe(sentence => !sentence.Any(char.IsDigit), "what a source carries, and for how long, is what the board is for");
        BoardComposer.Compose(new BoardRequest(profile, new HashSet<string>(), PlanFor(rob), "no-such-scenario", null)).Scenario.Id.ShouldBe("storm");
    }

    [Fact]
    public void The_set_has_a_card_per_scenario_a_board_per_source_and_season_and_the_storm_first_for_a_hill()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        ResilienceProfile profile = ResilienceProfile.FromProfile(rob);

        BoardSet set = BoardComposer.Compose(new BoardRequest(profile, new HashSet<string>(), PlanFor(rob), null, null));

        set.Scenario.ShouldBe(Scenarios.Storm, "no choice means the most likely");
        set.Season.ShouldBe(Season.Winter);
        set.Cards.Count.ShouldBe(Scenarios.All.Count);
        set.Cards[0].Id.ShouldBe("storm");
        set.Cards[0].Selected.ShouldBeTrue();
        set.Cards.Count(card => card.Selected).ShouldBe(1);
        set.Cards.First(card => card.Id == "storm").DaysReady.ShouldBe(1m);
        set.Sources.Select(source => source.Id).ShouldBe([BoardSet.HouseSource, "power-banks", "portable-power-station", "large-power-station", "rooftop-solar-and-battery", "petrol-generator"], "a petrol car is not a source and a detached house has a roof and a garden");
        set.SourceId.ShouldBe(BoardSet.HouseSource, "no choice means what is in the house");
        set.Sheet.ShouldBeSameAs(set.Current, "the house sheet is the current board");
        set.DaysReady.ShouldBe(1m);
        set.Current.Scenario.ShouldBe(Scenarios.Storm);
    }

    [Fact]
    public void A_flat_offers_fewer_sources_and_an_electric_car_with_output_offers_itself()
    {
        HouseholdProfile flat = Profiles.Flat();
        HouseholdProfile rob = Profiles.RobsHouse();
        ResilienceProfile flatProfile = ResilienceProfile.FromProfile(flat);
        ResilienceProfile evProfile = ResilienceProfile.Create(rob, false, false, false, Growing.None, CarKind.ElectricWithOutput, 0, 7, 0, []).Value;

        BoardSet flatSet = BoardComposer.Compose(new BoardRequest(flatProfile, new HashSet<string>(), PlanFor(flat), "water", null));
        BoardSet evSet = BoardComposer.Compose(new BoardRequest(evProfile, new HashSet<string> { "power-banks" }, PlanFor(rob), "storm", Season.Summer, "rooftop-solar-and-battery"));
        BoardSet unknownSource = BoardComposer.Compose(new BoardRequest(flatProfile, new HashSet<string>(), PlanFor(flat), "water", null, "rooftop-solar-and-battery"));

        flatSet.Sources.Select(source => source.Id).ShouldBe([BoardSet.HouseSource, "power-banks", "portable-power-station", "large-power-station"], "no roof, no garden, no car");
        flatSet.Scenario.ShouldBe(Scenarios.WaterCut);
        evSet.Sources.Select(source => source.Id).ShouldContain("ev-output");
        evSet.Sources.Select(source => source.Id).ShouldNotContain("power-banks", "already held, so not offered as a what-if");
        evSet.Season.ShouldBe(Season.Summer);
        evSet.Current.Season.ShouldBe(Season.Summer, "the current board is the house's summer one");
        evSet.DaysReady.ShouldBe(evSet.Current.DaysReady, "the figure is the house's whatever sheet is shown");
        evSet.SourceId.ShouldBe("rooftop-solar-and-battery");
        evSet.Sheet.ShouldNotBeSameAs(evSet.Current);
        evSet.Sheet.DaysReady.ShouldBeGreaterThanOrEqualTo(evSet.Current.DaysReady, "a battery never makes the house worse");
        unknownSource.SourceId.ShouldBe(BoardSet.HouseSource, "a source the flat cannot have falls back to the house");
    }

    [Fact]
    public void An_unknown_scenario_falls_back_to_the_storm()
    {
        HouseholdProfile rob = Profiles.RobsHouse();

        BoardSet set = BoardComposer.Compose(new BoardRequest(ResilienceProfile.FromProfile(rob), new HashSet<string>(), PlanFor(rob), "zombies", null));

        set.Scenario.ShouldBe(Scenarios.Storm);
        Should.Throw<ArgumentNullException>(() => BoardComposer.Compose(null!));
    }

    [Fact]
    public void Each_untaken_action_that_matters_says_which_cells_it_turns()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        Plan plan = PlanFor(rob);

        BoardSet set = BoardComposer.Compose(new BoardRequest(ResilienceProfile.FromProfile(rob), new HashSet<string> { "headtorches" }, plan, null, null));

        ActionTurn cash = set.Turns.Single(turn => turn.Action.Value == "cash-in-small-notes");
        NeedShift food = cash.Shifts.Single(shift => shift.Need == "Food");
        food.Better.ShouldBeTrue("with notes the shops count for something");
        food.To.ShouldBeGreaterThanOrEqualTo(food.From);
        food.From.ShouldBe(set.DaysReady, "food is what the figure runs out of first");
        set.Turns.ShouldNotContain(turn => turn.Action.Value == "headtorches", "already held");
        set.Turns.ShouldNotContain(turn => turn.Action.Value == "arborist-survey", "not a provider of anything on the board");
        set.Turns.ShouldAllBe(turn => turn.Shifts.Count > 0);
        set.Tiers.Count.ShouldBe(plan.Tiers.Count, "a destination per tier");
        set.Tiers.Select(tier => tier.Tier).ShouldBe(plan.Tiers.Select(tier => tier.Tier));
        set.Tiers[0].DaysReady.ShouldBeGreaterThan(set.DaysReady, "this week's shelf takes the house past where it stands");
        for (int i = 1; i < set.Tiers.Count; i++)
        {
            set.Tiers[i].DaysReady.ShouldBeGreaterThanOrEqualTo(set.Tiers[i - 1].DaysReady, "each tier assumes the ones before it and never makes the house worse");
        }
    }

    [Fact]
    public void What_is_planned_moves_the_figure_for_the_plan_and_never_the_figure_for_today()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        Plan plan = PlanFor(rob);
        ResilienceProfile profile = ResilienceProfile.FromProfile(rob);
        HashSet<string> shelf = ["water-bottled-three-days", "food-no-cook-three-days", "cash-in-small-notes"];

        BoardSet bare = BoardComposer.Compose(new BoardRequest(profile, new HashSet<string>(), plan, null, null));
        BoardSet planned = BoardComposer.Compose(new BoardRequest(profile, new HashSet<string>(), plan, null, null, null, shelf, new HashSet<string>()));
        BoardSet bought = BoardComposer.Compose(new BoardRequest(profile, shelf, plan, null, null, null, shelf));

        bare.PlannedDaysReady.ShouldBe(bare.DaysReady, "with nothing planned the plan takes the house nowhere new");
        bare.Tally.Have.ShouldBe(0);
        bare.Tally.Planned.ShouldBe(0);
        bare.Tally.Skipped.ShouldBe(0);
        bare.Tally.Undecided.ShouldBe(plan.Actions.Count());
        bare.Tally.UndecidedCost.ShouldBe(plan.Cost, "nothing decided is the whole plan still to weigh");
        planned.DaysReady.ShouldBe(bare.DaysReady, "meaning to buy tins feeds nobody today");
        planned.PlannedDaysReady.ShouldBe(bought.DaysReady, "the plan's figure is the one the house reaches when the planned things are in it");
        planned.PlannedDaysReady.ShouldBeGreaterThan(planned.DaysReady);
        planned.Tally.Planned.ShouldBe(3);
        planned.Tally.PlannedCost.ShouldBe(shelf.Select(id => plan.Actions.Single(a => a.Id.Value == id).Cost).Aggregate((sum, cost) => sum + cost));
        planned.Turns.ShouldContain(turn => turn.Action.Value == "food-no-cook-three-days", "a planned action is not yet in the house, so its row still says what it would do");
        bought.Tally.Have.ShouldBe(3);
        bought.Tally.Planned.ShouldBe(0, "what is in the house is no longer something to plan");
        bought.PlannedDaysReady.ShouldBe(bought.DaysReady);
        planned.Tiers.Sum(tier => tier.Tally.Actions).ShouldBe(plan.Actions.Count(), "the stretches' tallies add up to the plan's");
        planned.Tiers[0].Tally.Planned.ShouldBe(3);
        planned.Tiers[0].Title.ShouldBe("This week");
    }

    [Fact]
    public void What_is_ruled_out_is_not_suggested_again_and_is_left_out_of_where_a_stretch_leads()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        Plan plan = PlanFor(rob);
        ResilienceProfile profile = ResilienceProfile.FromProfile(rob);
        HashSet<string> none = [];
        HashSet<string> ruledOut = ["food-no-cook-three-days", "food-one-month", "arborist-survey"];

        BoardSet all = BoardComposer.Compose(new BoardRequest(profile, none, plan, null, null));
        BoardSet without = BoardComposer.Compose(new BoardRequest(profile, none, plan, null, null, null, null, ruledOut));

        all.Turns.ShouldContain(turn => turn.Action.Value == "food-no-cook-three-days");
        without.Turns.ShouldNotContain(turn => turn.Action.Value == "food-no-cook-three-days", "the household said no; the row does not argue");
        without.Turns.ShouldNotContain(turn => turn.Action.Value == "food-one-month");
        all.Tiers.ShouldAllBe(tier => !tier.Excludes);
        without.Tiers.ShouldAllBe(tier => tier.Excludes, "the first stretch leaves the tins out and every later one inherits that");
        without.Tiers[^1].DaysReady.ShouldBeLessThan(all.Tiers[^1].DaysReady, "a plan with no food store does not reach where the whole plan does");
        without.Tally.Skipped.ShouldBe(3);
        without.Tally.Undecided.ShouldBe(plan.Actions.Count() - 3);
        without.DaysReady.ShouldBe(all.DaysReady, "ruling something out takes nothing from the house");
        without.PlannedDaysReady.ShouldBe(all.DaysReady);
    }

    [Fact]
    public void A_counted_shelf_moves_the_figure_and_costs_only_what_is_missing()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        Plan plan = PlanFor(rob);
        ResilienceProfile profile = ResilienceProfile.FromProfile(rob);
        HashSet<string> cash = ["cash-in-small-notes"];
        Dictionary<string, int> tenDays = new() { [MeasuredStocks.FoodDays] = 10 };
        PlanAction month = plan.Actions.Single(action => action.Id.Value == "food-one-month");

        BoardSet ticked = BoardComposer.Compose(new BoardRequest(profile, new HashSet<string> { "cash-in-small-notes", "food-no-cook-three-days" }, plan, "snow", null));
        BoardSet counted = BoardComposer.Compose(new BoardRequest(profile, cash, plan, "snow", null, null, null, null, tenDays));
        BoardSet planned = BoardComposer.Compose(new BoardRequest(profile, cash, plan, "snow", null, "power-banks", new HashSet<string> { "food-one-month" }, null, tenDays));
        BoardSet whole = BoardComposer.Compose(new BoardRequest(profile, new HashSet<string> { "cash-in-small-notes", "food-one-month" }, plan, "snow", null));

        ticked.Progress.ShouldBeEmpty("no figure, nothing to report");
        counted.DaysReady.ShouldBeGreaterThan(ticked.DaysReady, "ten days of tins outlast the three the tick stood for");
        counted.Progress.Select(action => action.Counted.ActionId).ShouldBe(["food-no-cook-three-days", "food-one-month"]);
        counted.Progress[1].Rest.ShouldBe(month.Cost.Share(20m / 30m));
        counted.Tally.Have.ShouldBe(2, "the cash and the three-day shelf the count reaches");
        counted.Tally.UndecidedCost.ShouldBe(ExpectedUndecided(ticked.Tally.UndecidedCost, month.Cost), "the month is costed at the two thirds still to buy");
        counted.Turns.ShouldContain(turn => turn.Action.Value == "food-one-month");
        planned.Tally.PlannedCost.ShouldBe(month.Cost.Share(20m / 30m));
        planned.PlannedDaysReady.ShouldBe(whole.PlannedDaysReady, "planning the month tops the shelf up to a month; it does not stack a month on ten days");
        planned.Sheet.ShouldNotBeSameAs(planned.Current, "another source is still drawn beside a counted house");
        counted.Tiers[^1].DaysReady.ShouldBe(ticked.Tiers[^1].DaysReady, "with everything in place the shelf is the plan's month either way");
    }

    [Fact]
    public void Everything_planned_is_a_source_of_its_own_once_something_is_planned()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        Plan plan = PlanFor(rob);
        ResilienceProfile profile = ResilienceProfile.FromProfile(rob);
        HashSet<string> none = [];
        HashSet<string> shelf = ["water-bottled-three-days", "food-no-cook-three-days", "cash-in-small-notes"];

        BoardSet nothingPlanned = BoardComposer.Compose(new BoardRequest(profile, none, plan, null, null, BoardSet.PlannedSource));
        BoardSet offered = BoardComposer.Compose(new BoardRequest(profile, none, plan, null, null, null, shelf));
        BoardSet chosen = BoardComposer.Compose(new BoardRequest(profile, none, plan, null, null, BoardSet.PlannedSource, shelf));

        nothingPlanned.Sources.ShouldNotContain(source => source.Id == BoardSet.PlannedSource, "with nothing planned the sheet would be the house again");
        nothingPlanned.SourceId.ShouldBe(BoardSet.HouseSource);
        offered.Sources[1].ShouldBe(new SourceOption(BoardSet.PlannedSource, "Everything we plan to get", "What is in the house, with every action marked as planned in place."), "straight after the house, before the things that could be bought");
        offered.SourceId.ShouldBe(BoardSet.HouseSource, "offered, not chosen");
        offered.Sheet.ShouldBeSameAs(offered.Current);
        chosen.SourceId.ShouldBe(BoardSet.PlannedSource);
        chosen.Sheet.DaysReady.ShouldBe(chosen.PlannedDaysReady, "the sheet is the board behind the plan's figure");
        chosen.Sheet.DaysReady.ShouldBeGreaterThan(chosen.Current.DaysReady);
        chosen.DaysReady.ShouldBe(offered.DaysReady, "the headline stays the house as it is tonight");
        offered.Compared.ShouldBeEmpty("the house's own sheet is today, so there is nothing to compare");
        chosen.Compared.ShouldNotBeEmpty();
        chosen.Compared.Select(change => change.Key).ShouldBeUnique();
        RowShift food = chosen.Compared.Single(change => change.Key == "Food");
        food.Shift.Need.ShouldBe("Food");
        food.Shift.To.ShouldBeGreaterThan(food.Shift.From, "the tins and the notes feed the house for longer");
        chosen.Compared.ShouldAllBe(change => chosen.Sheet.Row(change.Key) != null);
        chosen.Compared.ShouldNotContain(change => change.Key == "Laundry", "nothing planned washes clothes");
    }

    [Fact]
    public void The_households_own_rate_for_cash_moves_what_the_cash_action_asks_for()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        Plan plan = PlanFor(rob);
        ResilienceProfile profile = ResilienceProfile.FromProfile(rob);
        Dictionary<string, int> twoHundred = new() { [MeasuredStocks.CashPounds] = 200 };

        BoardSet plans = BoardComposer.Compose(new BoardRequest(profile, new HashSet<string>(), plan, null, null, null, null, null, twoHundred));
        BoardSet cheap = BoardComposer.Compose(new BoardRequest(profile, new HashSet<string>(), plan, null, null, null, null, null, twoHundred, Assumptions.Create(5).Value));

        plans.Assumed.ShouldBeSameAs(Assumptions.Default);
        plans.Targets.Single(target => target.ActionId == "cash-in-small-notes").ShouldBe(new ActionTarget("cash-in-small-notes", MeasuredStocks.CashPounds, 400m));
        plans.Targets.Select(target => target.StockId).Distinct().Count().ShouldBe(5, "every stock a detached house with an electric hob can count");
        plans.Tally.Have.ShouldBe(0, "£200 is half of what the plan asks at ten pounds");
        cheap.Assumed.CashPoundsPerPersonPerDay.ShouldBe(5);
        cheap.Targets.Single(target => target.ActionId == "cash-in-small-notes").Target.ShouldBe(200m);
        cheap.Tally.Have.ShouldBe(1, "and all of it at five");
    }

    [Fact]
    public void Scenarios_rank_by_the_risks_and_the_rest_follow()
    {
        HouseholdProfile rob = Profiles.RobsHouse();
        HouseholdProfile flat = Profiles.Flat();

        IReadOnlyList<Scenario> hill = BoardComposer.Ranked(Risks.Run(rob));
        IReadOnlyList<Scenario> flatOrder = BoardComposer.Ranked(Risks.Run(flat));

        hill.Count.ShouldBe(Scenarios.All.Count);
        hill.Select(scenario => scenario.Id).ShouldBeUnique();
        hill[0].ShouldBe(Scenarios.Storm);
        hill.ShouldContain(Scenarios.Heatwave, "a scenario with no risk still appears, at the end");
        flatOrder.Count.ShouldBe(Scenarios.All.Count);
    }

    [Fact]
    public void Shifts_list_only_needs_that_improved_and_how_far()
    {
        ResilienceProfile rob = ResilienceProfile.FromProfile(Profiles.RobsHouse());
        Board before = OutageBoard.Simulate(rob, new HashSet<string>(), Scenarios.Storm, Season.Winter);
        Board after = OutageBoard.Simulate(rob, new HashSet<string> { "rooftop-solar-and-battery" }, Scenarios.Storm, Season.Winter);

        IReadOnlyList<NeedShift> shifts = BoardComposer.Shifts(before, after);

        NeedShift warmth = shifts.Single(shift => shift.Need == "Warmth");
        warmth.From.ShouldBe(BoardComposer.DaysHeld(before.Row("Warmth")!, before.DaysSimulated));
        warmth.To.ShouldBe(BoardComposer.DaysHeld(after.Row("Warmth")!, after.DaysSimulated));
        shifts.ShouldContain(shift => shift.Need == "Fridge and freezer" && shift.To > shift.From);
        shifts.ShouldNotContain(shift => shift.Need == "Water to drink", "already held");
        shifts.ShouldAllBe(shift => shift.Better);
        BoardComposer.Shifts(before, before).ShouldBeEmpty();
        BoardComposer.DaysHeld(before.Row("DrinkingWater")!, 30).ShouldBe(30m, "never rationed: the whole stretch");
        BoardComposer.DaysHeld(new BoardRow("x", "x", Rung.Survive, true, [new DayState(CellState.Rationed, null, null)], []), 30).ShouldBe(0.5m, "rationed on the first day is half a day");
        BoardComposer.DaysHeld(new BoardRow("x", "x", Rung.Survive, true, [new DayState(CellState.Held, "a", null), new DayState(CellState.Held, "a", null), new DayState(CellState.Lost, null, "b")], []), 30).ShouldBe(2m);
    }

    [Fact]
    public void A_need_lost_later_is_a_need_that_improved_though_no_cell_changes_state()
    {
        static Board Lasting(int days)
        {
            List<DayState> states = [.. Enumerable.Repeat(new DayState(CellState.Held, "tins", null), days), new DayState(CellState.Lost, null, "tins")];
            return new Board(Scenarios.Storm, Season.Winter, 30, [new BoardRow("Food", "Food", Rung.Survive, true, states, [new HorizonCell(Horizon.TwoWeeks, CellState.Lost, days)])]);
        }

        NeedShift later = BoardComposer.Shifts(Lasting(6), Lasting(12)).ShouldHaveSingleItem();

        later.From.ShouldBe(6m);
        later.To.ShouldBe(12m);
        BoardComposer.Shifts(Lasting(12), Lasting(6)).ShouldBeEmpty("lost sooner is not better");
        BoardComposer.Shifts(Lasting(6), Lasting(6)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Horizon.OneDay, "a day")]
    [InlineData(Horizon.ThreeDays, "three days")]
    [InlineData(Horizon.OneWeek, "a week")]
    [InlineData(Horizon.TwoWeeks, "a fortnight")]
    [InlineData(Horizon.OneMonth, "a month")]
    [InlineData(Horizon.ThreeMonths, "three months")]
    [InlineData(Horizon.SixMonths, "six months")]
    public void Horizons_are_said_in_words(Horizon horizon, string words) => BoardComposer.HorizonWords(horizon).ShouldBe(words);
}
