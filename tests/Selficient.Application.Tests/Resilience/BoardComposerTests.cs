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
        set.Boards.Count.ShouldBe(set.Sources.Count * 2);
        set.Boards.ShouldContain(board => board.SourceId == "rooftop-solar-and-battery" && board.Season == Season.Summer);
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
        BoardSet evSet = BoardComposer.Compose(new BoardRequest(evProfile, new HashSet<string> { "power-banks" }, PlanFor(rob), "storm", Season.Summer));

        flatSet.Sources.Select(source => source.Id).ShouldBe([BoardSet.HouseSource, "power-banks", "portable-power-station", "large-power-station"], "no roof, no garden, no car");
        flatSet.Scenario.ShouldBe(Scenarios.WaterCut);
        evSet.Sources.Select(source => source.Id).ShouldContain("ev-output");
        evSet.Sources.Select(source => source.Id).ShouldNotContain("power-banks", "already held, so not offered as a what-if");
        evSet.Season.ShouldBe(Season.Summer);
        evSet.Current.Season.ShouldBe(Season.Summer, "the current board is the house's summer one");
        evSet.DaysReady.ShouldBe(evSet.Current.DaysReady);
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
        cash.Turns.ShouldContain("Food at three days", "with notes the shops count for longer");
        set.Turns.ShouldNotContain(turn => turn.Action.Value == "headtorches", "already held");
        set.Turns.ShouldNotContain(turn => turn.Action.Value == "arborist-survey", "not a provider of anything on the board");
        set.Turns.ShouldAllBe(turn => turn.Turns.Count > 0);
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
    public void Turned_lists_only_cells_that_improved()
    {
        ResilienceProfile rob = ResilienceProfile.FromProfile(Profiles.RobsHouse());
        Board before = OutageBoard.Simulate(rob, new HashSet<string>(), Scenarios.Storm, Season.Winter);
        Board after = OutageBoard.Simulate(rob, new HashSet<string> { "rooftop-solar-and-battery" }, Scenarios.Storm, Season.Winter);

        IReadOnlyList<string> turned = BoardComposer.Turned(before, after);

        turned.ShouldContain("Warmth at a day");
        turned.ShouldContain("Fridge and freezer at a week");
        turned.ShouldNotContain("Water to drink at a day", "already held");
        BoardComposer.Turned(before, before).ShouldBeEmpty();
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
