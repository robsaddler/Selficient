using Selficient.Application.Composition;
using Selficient.Application.Resilience;
using Selficient.Domain.Households;
using Selficient.Domain.Plans;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Resilience;

public sealed class BoardComposerTests
{
    private static readonly RiskAssessmentPipeline Risks = new(ApplicationServiceCollectionExtensions.Assessors());
    private static readonly PlanBuilder Builder = new(ApplicationServiceCollectionExtensions.Rules());

    private static Plan PlanFor(HouseholdProfile profile) => Builder.Build(profile, Risks.Run(profile)).Value;

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
