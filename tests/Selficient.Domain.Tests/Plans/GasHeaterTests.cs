using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Parameters;
using Selficient.Domain.Plans;
using Selficient.Domain.Plans.Rules;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.Domain.Upkeep;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Plans;

/// <summary>
/// A portable gas heater on a cylinder is how a great many British homes would keep one room warm
/// with the power off, and it is the one fallback warm enough for a baby or for someone frail. A
/// household that has one is scored for it; one that needs it and may keep a cylinder is offered it.
/// </summary>
public sealed class GasHeaterTests
{
    private static readonly RiskAssessment NoRisks = new RiskAssessmentPipeline([]).Run(Profiles.Flat());

    private static List<PlanAction> Run(HouseholdProfile profile) => [.. new GasHeaterRule().Contribute(profile, NoRisks)];

    private static HouseholdProfile Frail(DwellingType type = DwellingType.Terrace, HeatingType heating = HeatingType.GasBoiler) =>
        Profiles.Build(2, 0, vulnerable: true, false, false, type, 2, heating, HobType.Electric, false, false, false, false, 40m, FloodRisk.VeryLow);

    private static HouseholdProfile WithHeater(HouseholdProfile profile) => profile with { Particulars = profile.Particulars with { GasHeater = true } };

    private static HouseholdProfile OnFloor(int floor, ParameterSet? figures = null) =>
        Frail(DwellingType.Flat, HeatingType.Electric) with { Circumstances = Circumstances.None with { Floor = floor }, Figures = figures ?? ParameterSet.Default };

    private static BoardRow Warmth(HouseholdProfile profile, params string[] held) =>
        OutageBoard.Simulate(ResilienceProfile.FromProfile(profile), held.ToHashSet(StringComparer.Ordinal), Scenarios.Blackout, Season.Winter).Row(nameof(NeedId.Warmth))!;

    [Fact]
    public void A_household_that_has_one_is_scored_for_a_properly_warm_room_for_as_long_as_the_cylinder_lasts()
    {
        HouseholdProfile frail = Frail();

        BoardRow without = Warmth(frail);
        BoardRow with = Warmth(WithHeater(frail));

        Particulars.None.GasHeater.ShouldBeFalse();
        frail.HasGasHeater.ShouldBeFalse();
        WithHeater(frail).HasGasHeater.ShouldBeTrue();
        ResilienceProfile.FromProfile(WithHeater(frail)).Fixtures.ShouldContain("portable-gas-heater");
        ResilienceProfile.FromProfile(frail).Fixtures.ShouldNotContain("portable-gas-heater");
        without.Days[0].State.ShouldBe(CellState.Lost, "blankets are not warm enough for someone frail, and the boiler has no power");
        with.Days[0].ShouldBe(new DayState(CellState.Held, "portable-gas-heater", "gas-boiler"), "the heater needs nothing from the mains");
        with.Days.ShouldAllBe(day => day.State == CellState.Held, "one cylinder is twelve days, which is longer than the blackout the board draws");
        with.FirstRationedDay.ShouldBeNull();
        Warmth(frail, "portable-gas-heater").Days[0].State.ShouldBe(CellState.Held, "a household that buys the one the plan offers holds the same thing");
    }

    [Fact]
    public void The_heater_is_a_figure_on_the_desk_with_its_words_built_from_it_and_a_household_may_say_how_long_its_own_lasts()
    {
        Provider heater = Providers.Find("portable-gas-heater")!;
        ParameterDefinition stock = ParameterRegistry.Find("portable-gas-heater.stock")!;

        heater.Name.ShouldBe("A portable gas heater and its cylinder");
        heater.Sentence.ShouldBe("One room kept properly warm with no electricity for about twelve days on a full cylinder, on the low setting from morning to bedtime. A window open a crack, a carbon monoxide alarm in the same room, and off before anyone sleeps.");
        heater.Meets.ShouldHaveSingleItem().ShouldBe(new Provision(NeedId.Warmth, 0m));
        heater.NeedsUtilities.ShouldBeEmpty();
        heater.Source.ShouldBe("Calor: a 15 kg butane cylinder is about 150 hours on a cabinet heater's low setting; twelve hours a day");
        (stock.Default, stock.Unit, stock.Group).ShouldBe((12m, "days", "Warmth"));
        stock.HouseholdQuestion.ShouldBe("How many days does your gas heater run on the cylinders you keep, at twelve hours a day?");
        ProviderFigures.Find(ParameterSet.Default.WithOwn(new Dictionary<string, decimal> { ["portable-gas-heater.stock"] = 24m }), "portable-gas-heater")!.Sentence.ShouldStartWith("One room kept properly warm with no electricity for about 24 days on a full cylinder");
        BoardRow nearlyEmpty = Warmth(WithHeater(Frail()) with { Figures = ParameterSet.Default.WithOwn(new Dictionary<string, decimal> { ["portable-gas-heater.stock"] = 3m }) });
        nearlyEmpty.Days[2].State.ShouldBe(CellState.Held);
        nearlyEmpty.Days[3].State.ShouldNotBe(CellState.Held, "a cylinder a household says is three days is warmth for three days and no longer");
        nearlyEmpty.Days[3].FailedBy.ShouldBe("portable-gas-heater");
        nearlyEmpty.FirstRationedDay.ShouldBe(4);
    }

    [Fact]
    public void A_household_that_needs_full_warmth_and_has_no_stove_is_offered_one_this_month_with_how_to_use_it_safely()
    {
        PlanAction heater = Run(Frail()).ShouldHaveSingleItem();

        heater.Id.Value.ShouldBe("portable-gas-heater");
        heater.Tier.ShouldBe(Tier.ThisMonth);
        heater.Title.ShouldBe("A portable gas heater and a full cylinder, for one properly warm room");
        heater.Why.ShouldBe("A cabinet heater on a butane cylinder needs no electricity and keeps one room warm enough for a baby or for someone frail, which blankets and sleeping bags do not: about twelve days on a full cylinder, on its low setting from morning to bedtime. Use it in a living room with a window open a crack and a carbon monoxide alarm in the same room, never in a bedroom or a bathroom, and turn it off before anyone sleeps.");
        heater.Cost.ShouldBe(MoneyRange.Pounds(184m, 234m));
        Run(Frail() with { Circumstances = Circumstances.None with { Tenure = Tenure.Tenant } }).Single().Why.ShouldEndWith("turn it off before anyone sleeps. Check that your tenancy allows a gas cylinder before you buy.");
        Run(Profiles.RobsHouse() with { Particulars = new Particulars(false, false, false, 1) }).ShouldHaveSingleItem("a baby needs a warm room as someone frail does");

        Run(Profiles.RobsHouse()).ShouldBeEmpty("healthy adults are offered sleeping bags, which cost a tenth as much");
        Run(Frail() with { Particulars = new Particulars(false, WoodStove: true, false, 0) }).ShouldBeEmpty("a stove is the warm room already");
        Run(Frail(heating: HeatingType.Wood)).ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => new GasHeaterRule().Contribute(null!, NoRisks).ToList());
    }

    [Fact]
    public void A_flat_is_offered_one_only_on_as_many_floors_from_the_ground_as_the_figure_in_force_says()
    {
        ParameterDefinition floors = ParameterRegistry.Find(RuleFigures.GasHeaterFloors)!;
        ParameterSet none = ParameterSet.Create(1, new Dictionary<string, decimal> { [RuleFigures.GasHeaterFloors] = 0m }).Value;
        ParameterSet three = ParameterSet.Create(1, new Dictionary<string, decimal> { [RuleFigures.GasHeaterFloors] = 3m }).Value;

        (floors.Default, floors.Min, floors.Max, floors.WholeNumber, floors.Unit).ShouldBe((1m, 0m, 41m, true, "whole floors"));
        floors.Name.ShouldBe("Floors of a block of flats, counted up from the ground, on which the plan recommends a portable gas heater; none means no flat is offered one, one means the ground floor only");
        floors.Source.ShouldBe("Makers' instructions (Calor, Sealey, Clarke): not in high-rise flats; and many leases forbid a gas cylinder");

        Run(OnFloor(0)).ShouldHaveSingleItem("as shipped, a ground-floor flat is offered one");
        Run(OnFloor(1)).ShouldBeEmpty("and a flat above the ground is not");
        OnFloor(1).MayBeOfferedGasHeater.ShouldBeFalse();
        Run(OnFloor(0, none)).ShouldBeEmpty("an expert may say no flat at all");
        Run(OnFloor(2, three)).ShouldHaveSingleItem("or the first three floors");
        Run(OnFloor(3, three)).ShouldBeEmpty();
        (Frail() with { Circumstances = Circumstances.None with { Floor = 30 }, Figures = none }).MayBeOfferedGasHeater.ShouldBeTrue("a house is not a flat, whatever a stale answer about its floor says");
        ParameterSet.Create(1, new Dictionary<string, decimal> { [RuleFigures.GasHeaterFloors] = 1.5m }).IsFailure.ShouldBeTrue("floors are whole");
    }

    [Fact]
    public void A_household_that_has_one_is_told_to_keep_a_spare_cylinder_and_is_reminded_to_check_it_before_the_cold()
    {
        PlanAction ready = Run(WithHeater(Profiles.RobsHouse())).ShouldHaveSingleItem();
        Reminder check = UpkeepSchedule.For(new UpkeepFacts(GasHeater: true)).Single(r => r.Id == "gas-heater-check");

        ready.Id.Value.ShouldBe("gas-heater-ready");
        ready.Tier.ShouldBe(Tier.ThisMonth);
        ready.Title.ShouldBe("Keep a full spare cylinder for the gas heater, and have the heater checked before winter");
        ready.Why.ShouldBe("Your heater is warmth that needs nothing from anyone: about twelve days of one warm room on a full cylinder. A spare means a cut that outlasts the first is still a warm one. Have the hose and regulator looked at each autumn. Use it in a living room with a window open a crack and a carbon monoxide alarm in the same room, never in a bedroom or a bathroom, and turn it off before anyone sleeps.");
        ready.Cost.ShouldBe(MoneyRange.Pounds(58m, 108m));
        Run(WithHeater(OnFloor(9))).Single().Id.Value.ShouldBe("gas-heater-ready", "a flat that already has one is not told otherwise by the plan; it is told how to use it");
        Run(WithHeater(Frail())).Select(a => a.Id.Value).ShouldBe(["gas-heater-ready"], "and nobody who has one is sold another");

        check.Title.ShouldBe("Check the gas heater, its hose and the spare cylinder");
        check.Dates.ShouldBe([(9, 1)]);
        UpkeepSchedule.For(new UpkeepFacts()).Select(r => r.Id).ShouldNotContain("gas-heater-check");
        UpkeepFacts.From(WithHeater(Profiles.Flat())).ShouldBe(new UpkeepFacts(GasHeater: true));
    }
}
