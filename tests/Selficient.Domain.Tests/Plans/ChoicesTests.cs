using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Risks;

namespace Selficient.Domain.Tests.Plans;

public sealed class ChoicesTests
{
    private static PlanAction Action(string id, decimal from, decimal to) =>
        new(ActionId.From(id), Tier.ThisWeek, id, "Why.", MoneyRange.Pounds(from, to), new HashSet<RiskKind>(), Provisions.None);

    private static HashSet<string> Ids(params string[] ids) => new(ids, StringComparer.Ordinal);

    [Fact]
    public void An_action_is_in_one_state_and_having_beats_planning_beats_ruling_out()
    {
        PlanChoices choices = new(Ids("water", "radio"), Ids("water", "torches", "stove"), Ids("radio", "torches", "generator"));

        choices.Have.ShouldBe(["water", "radio"], ignoreOrder: true);
        choices.Planned.ShouldBe(["torches", "stove"], ignoreOrder: true, customMessage: "water is already in the house");
        choices.Skipped.ShouldBe(["generator"], ignoreOrder: true, customMessage: "the radio is held and the torches are planned, so neither is ruled out");
        PlanChoices.None.Have.ShouldBeEmpty();
        PlanChoices.None.Planned.ShouldBeEmpty();
        PlanChoices.None.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public void The_tally_counts_each_state_and_sums_only_what_is_still_to_spend()
    {
        PlanAction[] actions = [Action("water", 12m, 18m), Action("torches", 40m, 60m), Action("stove", 25m, 35m), Action("generator", 400m, 900m), Action("numbers", 0m, 0m), Action("radio", 36m, 150m)];
        PlanChoices choices = new(Ids("water", "not-on-this-plan"), Ids("torches", "stove"), Ids("generator"));

        ChoiceTally tally = ChoiceTally.Of(actions, choices);

        tally.Actions.ShouldBe(6);
        tally.Have.ShouldBe(1, "an id that is not on the plan is not counted");
        tally.Planned.ShouldBe(2);
        tally.PlannedCost.ShouldBe(MoneyRange.Pounds(65m, 95m));
        tally.Skipped.ShouldBe(1);
        tally.Undecided.ShouldBe(2);
        tally.UndecidedCost.ShouldBe(MoneyRange.Pounds(36m, 150m), "the water is bought and the generator is ruled out, so neither is in a sum");
        (tally.Have + tally.Planned + tally.Skipped + tally.Undecided).ShouldBe(tally.Actions);
    }

    [Fact]
    public void With_nothing_said_everything_is_undecided_and_costs_the_whole_plan()
    {
        PlanAction[] actions = [Action("water", 12m, 18m), Action("torches", 40m, 60m)];

        ChoiceTally tally = ChoiceTally.Of(actions, PlanChoices.None);

        tally.ShouldBe(new ChoiceTally(2, 0, 0, MoneyRange.Free, 2, MoneyRange.Pounds(52m, 78m), 0));
        ChoiceTally.Of([], PlanChoices.None).Actions.ShouldBe(0);
    }

    [Fact]
    public void Nulls_are_refused()
    {
        Should.Throw<ArgumentNullException>(() => new PlanChoices(null!, Ids(), Ids()));
        Should.Throw<ArgumentNullException>(() => new PlanChoices(Ids(), null!, Ids()));
        Should.Throw<ArgumentNullException>(() => new PlanChoices(Ids(), Ids(), null!));
        Should.Throw<ArgumentNullException>(() => ChoiceTally.Of(null!, PlanChoices.None));
        Should.Throw<ArgumentNullException>(() => ChoiceTally.Of([], null!));
        Should.Throw<ArgumentNullException>(() => ChoiceTally.Of([], PlanChoices.None, null!));
    }
}
