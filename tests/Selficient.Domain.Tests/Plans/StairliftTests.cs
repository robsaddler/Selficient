using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Plans.Rules;
using Selficient.Domain.Primitives;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.Domain.Upkeep;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Plans;

/// <summary>
/// A stairlift is the way to the bed and the bathroom, and a plan that does not ask about it leaves
/// its rider on whichever floor the power cut found them. Beside it, the heating fallback: sleeping
/// bags are warmth for healthy adults and are never scored as more.
/// </summary>
public sealed class StairliftTests
{
    private static readonly RiskAssessment NoRisks = new RiskAssessmentPipeline([]).Run(Profiles.Flat());

    private static List<PlanAction> Run(IPlanRule rule, HouseholdProfile profile) => [.. rule.Contribute(profile, NoRisks)];

    private static HouseholdProfile Lift(Stairlift kind, HouseholdProfile? profile = null) =>
        (profile ?? Profiles.RobsHouse()) with { Circumstances = Circumstances.None with { Stairlift = kind } };

    private static Board Simulate(HouseholdProfile profile, Scenario scenario, params string[] held) =>
        OutageBoard.Simulate(ResilienceProfile.FromProfile(profile), held.ToHashSet(StringComparer.Ordinal), scenario, Season.Winter);

    private static BoardRow Stairs(Board board) => board.Row(nameof(NeedId.Stairs))!;

    [Fact]
    public void A_stairlift_is_one_of_four_answers_and_qualifies_the_household_for_the_registers()
    {
        Circumstances.Create(Tenure.Owner, WaterSupply.Mains, false, false, false, false, false, 0, Stairlift.Battery).Value.Stairlift.ShouldBe(Stairlift.Battery);
        Circumstances.Create(Tenure.Owner, WaterSupply.Mains, false, false, false, false, false, 0).Value.Stairlift.ShouldBe(Stairlift.None);
        Circumstances.Create(Tenure.Owner, WaterSupply.Mains, false, false, false, false, false, 0, (Stairlift)9).Error.Code.ShouldBe(DomainErrors.UnknownChoice().Code);

        Profiles.Flat().HasStairlift.ShouldBeFalse();
        Lift(Stairlift.NotKnown).HasStairlift.ShouldBeTrue();
        Lift(Stairlift.Battery).StairliftRunsInACut.ShouldBeTrue();
        Lift(Stairlift.MainsOnly).StairliftRunsInACut.ShouldBeFalse();
        Lift(Stairlift.NotKnown).StairliftRunsInACut.ShouldBeFalse("not knowing is planned for as the worse case");
        Lift(Stairlift.Battery, Profiles.Flat()).QualifiesForPriorityServices.ShouldBeTrue();
        Run(new PriorityServicesRule(), Lift(Stairlift.Battery, Profiles.Flat())).ShouldHaveSingleItem();
    }

    [Fact]
    public void A_battery_stairlift_is_told_its_trips_are_numbered_and_given_a_plan_for_a_cut_that_outlasts_them()
    {
        List<PlanAction> actions = Run(new StairliftRule(), Lift(Stairlift.Battery));

        actions.Select(a => (a.Id.Value, a.Tier)).ShouldBe([("stairlift-trips-in-a-power-cut", Tier.ThisWeek), ("downstairs-in-a-power-cut", Tier.ThisMonth)]);
        actions[0].Title.ShouldBe("Find out how many trips the stairlift makes in a power cut, and keep it parked on its charge point");
        actions[0].Why.ShouldBe("Your stairlift runs on its own batteries and only charges from the mains, so it carries on in a cut, but not for ever: one maker says more than ten return journeys on healthy batteries, and fewer on a long or curved stair. The manual or the installer has the figure for yours. It charges only when parked at the top or the bottom, and old batteries hold less. In a long cut, keep the trips for bed and the bathroom.");
        actions[0].Cost.ShouldBe(MoneyRange.Free);
        actions[1].Title.ShouldBe("A bed downstairs and a commode, for a cut that outlasts the stairlift's batteries");
        actions[1].Why.ShouldBe("The batteries are about two days of trips to bed and the bathroom. After that, whoever relies on the lift lives on one floor until the power is back. Decide which room downstairs, where the bedding is kept and who carries it, and keep a commode for those days. Bring them down while the lift still has a trip left in it.");
        actions[1].Cost.ShouldBe(MoneyRange.Pounds(30m, 120m));
        Run(new StairliftRule(), Profiles.RobsHouse()).ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => new StairliftRule().Contribute(null!, NoRisks).ToList());
    }

    [Fact]
    public void A_stairlift_that_stops_with_the_power_gets_a_bed_and_a_commode_downstairs_this_week()
    {
        PlanAction downstairs = Run(new StairliftRule(), Lift(Stairlift.MainsOnly)).ShouldHaveSingleItem();

        downstairs.Id.Value.ShouldBe("downstairs-in-a-power-cut");
        downstairs.Tier.ShouldBe(Tier.ThisWeek);
        downstairs.Title.ShouldBe("A bed made up downstairs and a commode, decided now and not on the night");
        downstairs.Why.ShouldBe("When the stairlift stops, whoever relies on it lives on the floor they are on. Decide which room downstairs, where the bedding is kept and who carries it, and keep a commode for as long as the power is off. If they are upstairs when it goes, they stay there until someone can help them down, so keep a phone, a torch and water on both floors.");
        downstairs.Cost.ShouldBe(MoneyRange.Pounds(30m, 120m));
    }

    [Fact]
    public void A_household_that_does_not_know_which_kind_it_has_is_planned_for_as_if_it_stops_and_asked_to_find_out()
    {
        List<PlanAction> actions = Run(new StairliftRule(), Lift(Stairlift.NotKnown));

        actions.Select(a => a.Id.Value).ShouldBe(["stairlift-find-out", "downstairs-in-a-power-cut"]);
        actions[0].Tier.ShouldBe(Tier.ThisWeek);
        actions[0].Title.ShouldBe("Find out whether the stairlift works in a power cut");
        actions[0].Why.ShouldBe("Most modern stairlifts run on their own batteries and keep going for a number of trips; older ones run from the mains and stop. The manual says which, and so will whoever services it. Until you know, this plan takes it that yours stops. Change your answer when you know and the plan follows.");
        actions[1].Tier.ShouldBe(Tier.ThisWeek);
    }

    [Fact]
    public void The_way_out_is_agreed_wherever_there_is_a_stairlift_and_the_plan_names_it()
    {
        PlanAction plan = Run(new HelpToLeaveRule(), Lift(Stairlift.Battery)).ShouldHaveSingleItem();

        plan.Id.Value.ShouldBe("help-to-leave-plan");
        plan.Why.ShouldContain("kept at night. The stairlift is the way down for someone here, so the plan has to work on a night when it does not. Your fire and rescue service");
        Run(new HelpToLeaveRule(), Profiles.RobsHouse() with { Circumstances = Circumstances.None with { NeedsHelpToLeave = true } }).Single().Why.ShouldNotContain("stairlift");
    }

    [Fact]
    public void Upkeep_has_the_stairlift_serviced_before_winter()
    {
        Reminder service = UpkeepSchedule.For(new UpkeepFacts(Stairlift: true)).Single(r => r.Id == "stairlift-service");

        service.Title.ShouldBe("Have the stairlift serviced and its batteries checked");
        service.Why.ShouldBe("Before winter. The batteries last a few years, and an old set makes fewer trips when the power is off.");
        service.Dates.ShouldBe([(9, 1)]);
        UpkeepSchedule.For(new UpkeepFacts()).Select(r => r.Id).ShouldNotContain("stairlift-service");
        UpkeepFacts.From(Lift(Stairlift.MainsOnly, Profiles.Flat())).ShouldBe(new UpkeepFacts(Stairlift: true));
    }

    [Fact]
    public void Getting_upstairs_is_a_row_only_for_a_house_with_a_stairlift()
    {
        Stairs(Simulate(Profiles.RobsHouse(), Scenarios.Storm)).Needed.ShouldBeFalse();
        Stairs(Simulate(Lift(Stairlift.Battery), Scenarios.Storm)).Needed.ShouldBeTrue();
        Needs.Of(NeedId.Stairs).ShouldBe(new Need(NeedId.Stairs, Rung.Survive, "Getting upstairs", "a way to the bed and the bathroom"));
        ResilienceProfile.FromProfile(Lift(Stairlift.Battery)).Fixtures.ShouldContain("stairlift-batteries");
        ResilienceProfile.FromProfile(Lift(Stairlift.Battery)).Fixtures.ShouldContain("stairlift-charger");
        ResilienceProfile.FromProfile(Lift(Stairlift.MainsOnly)).Fixtures.ShouldContain("stairlift-on-the-mains");
        ResilienceProfile.FromProfile(Lift(Stairlift.NotKnown)).Fixtures.ShouldContain("stairlift-on-the-mains");
        ResilienceProfile.FromProfile(Profiles.RobsHouse()).Fixtures.ShouldNotContain("stairlift-on-the-mains");
    }

    [Fact]
    public void A_battery_stairlift_makes_two_days_of_trips_in_a_blackout_and_a_power_station_keeps_it_going()
    {
        BoardRow alone = Stairs(Simulate(Lift(Stairlift.Battery), Scenarios.Blackout));
        BoardRow withStation = Stairs(Simulate(Lift(Stairlift.Battery), Scenarios.Blackout, "portable-power-station"));
        BoardRow withPlan = Stairs(Simulate(Lift(Stairlift.Battery), Scenarios.Blackout, "downstairs-in-a-power-cut"));

        alone.Days[0].ShouldBe(new DayState(CellState.Held, "stairlift-batteries", "stairlift-charger"));
        alone.Days[1].State.ShouldBe(CellState.Held);
        alone.Days[2].ShouldBe(new DayState(CellState.Lost, null, "stairlift-charger"), "the batteries are two days and the charger has nothing to charge from");
        alone.FirstRationedDay.ShouldBe(3);
        withStation.Days[0].ShouldBe(new DayState(CellState.Held, "stairlift-charger", null), "a small charger is something a power station can run");
        withStation.Days[2].ShouldBe(new DayState(CellState.Held, "stairlift-batteries", "stairlift-charger"), "and the lift's own batteries are still full when the station is empty");
        withStation.FirstRationedDay.ShouldBe(4, "the station is shared with the rest of the house, and it still buys the lift a day");
        withPlan.Days[2].ShouldBe(new DayState(CellState.Reduced, "downstairs-in-a-power-cut", "stairlift-charger"), "a bed and a commode downstairs is getting by, and it is not lost");
    }

    [Fact]
    public void A_stairlift_on_the_mains_is_lost_on_the_first_day_unless_the_whole_house_is_carried()
    {
        Board nothing = Simulate(Lift(Stairlift.MainsOnly), Scenarios.Blackout);

        Stairs(nothing).Days[0].ShouldBe(new DayState(CellState.Lost, null, "stairlift-on-the-mains"));
        nothing.DaysReady.ShouldBe(0.5m, "someone who cannot reach a bed or a bathroom is not standing on their own");
        Stairs(Simulate(Lift(Stairlift.MainsOnly), Scenarios.Blackout, "portable-power-station", "boiler-plug-and-socket")).Days[0].State.ShouldBe(CellState.Lost, "a wired-in lift does not run from a portable battery");
        Stairs(Simulate(Lift(Stairlift.MainsOnly), Scenarios.Blackout, "rooftop-solar-and-battery")).Days[0].ShouldBe(new DayState(CellState.Held, "stairlift-on-the-mains", null));
        Stairs(Simulate(Lift(Stairlift.MainsOnly), Scenarios.Blackout, "downstairs-in-a-power-cut")).Days[0].ShouldBe(new DayState(CellState.Reduced, "downstairs-in-a-power-cut", "stairlift-on-the-mains"));
        Stairs(Simulate(Lift(Stairlift.NotKnown), Scenarios.Blackout)).Days[0].State.ShouldBe(CellState.Lost, "not knowing is scored as the worse case");
    }

    [Fact]
    public void Sleeping_bags_are_warmth_for_healthy_adults_and_are_never_scored_as_a_heater()
    {
        HouseholdProfile adults = Profiles.Flat();
        HouseholdProfile frail = Profiles.Build(2, 0, vulnerable: true, false, false, DwellingType.Flat, 1, HeatingType.Electric, HobType.Electric, false, false, false, false, 12m, FloodRisk.VeryLow);

        Provider fallback = Providers.Find("heating-fallback")!;
        fallback.Name.ShouldBe("Sleeping bags and one warm room");
        fallback.Meets.ShouldHaveSingleItem().Partial.ShouldBeTrue();
        BoardRow warmth = Simulate(adults, Scenarios.Blackout, "heating-fallback").Row(nameof(NeedId.Warmth))!;
        warmth.Days[1].State.ShouldBe(CellState.Reduced, "one warm room is getting by, not a warm house");
        Simulate(frail, Scenarios.Blackout, "heating-fallback").Row(nameof(NeedId.Warmth))!.Days[1].State.ShouldBe(CellState.Lost, "and it is not warm enough for someone frail");

        PlanAction forAdults = Run(new PowerStationRule(), adults).Single(a => a.Id.Value == "heating-fallback");
        forAdults.Title.ShouldBe("A heating fallback for a winter cut: one warm room, sleeping bags, and a plan");
        PlanAction forFrail = Run(new PowerStationRule(), frail).Single(a => a.Id.Value == "heating-fallback");
        forFrail.Title.ShouldBe("Agree where you would go in a winter power cut, with sleeping bags for the wait");
        forFrail.Why.ShouldBe("Electric heating and heat pumps draw far more than a portable battery can supply, and one room under sleeping bags is not warm enough for a baby or for someone frail. Agree now who you would go to: a relative or a friend with a stove or a gas fire, or the rest centre your council opens. The sleeping bags are for the hours before you leave.");
        (adults with { Particulars = Particulars.None }).NeedsFullWarmth.ShouldBeFalse();
        frail.NeedsFullWarmth.ShouldBeTrue();
        (Profiles.RobsHouse() with { Particulars = new Particulars(false, false, false, 1) }).NeedsFullWarmth.ShouldBeTrue("a baby cannot be kept warm under blankets either");
    }
}
