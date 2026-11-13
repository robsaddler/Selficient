using Selficient.Domain.Households;
using Selficient.Domain.Parameters;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Parameters;

/// <summary>
/// Every figure the engine works from is a parameter somebody can read and correct, and the engine
/// reads all of them from the set it is given. With nothing corrected it is the engine the other
/// tests pin, to the instance.
/// </summary>
public sealed class ParameterTests
{
    private static ParameterSet Corrected(params (string Id, decimal Value)[] corrections) =>
        ParameterSet.Create(1, corrections.ToDictionary(static c => c.Id, static c => c.Value)).Value;

    private static ResilienceProfile Rob(ParameterSet figures, HouseholdProfile? profile = null) =>
        ResilienceProfile.FromProfile((profile ?? Profiles.RobsHouse()) with { Figures = figures });

    private static Board Simulate(ResilienceProfile profile, Scenario scenario, Season season = Season.Winter, params string[] held) =>
        OutageBoard.Simulate(profile, held.ToHashSet(StringComparer.Ordinal), scenario, season);

    private static BoardRow Row(Board board, NeedId need) => board.Row(need.ToString())!;

    [Fact]
    public void Every_figure_has_a_name_a_unit_a_source_a_range_and_a_place_a_household_meets_it()
    {
        IReadOnlyList<ParameterDefinition> all = ParameterRegistry.All;

        all.Count.ShouldBeGreaterThan(340);
        all.Select(p => p.Id).ShouldBeUnique();
        foreach (ParameterDefinition parameter in all)
        {
            parameter.Id.ShouldMatch("^[a-z0-9]+([.-][a-z0-9]+)*$", customMessage: parameter.Id);
            parameter.Name.ShouldNotBeNullOrWhiteSpace(parameter.Id);
            parameter.Unit.ShouldNotBeNullOrWhiteSpace(parameter.Id);
            parameter.Source.ShouldNotBeNullOrWhiteSpace(parameter.Id);
            parameter.Group.ShouldNotBeNullOrWhiteSpace(parameter.Id);
            (parameter.Shown.StartsWith("On the board", StringComparison.Ordinal) || parameter.Shown.StartsWith("In the plan", StringComparison.Ordinal)).ShouldBeTrue($"{parameter.Id} does not say where a household meets it");
            parameter.Min.ShouldBeLessThan(parameter.Max, parameter.Id);
            parameter.Accepts(parameter.Default).ShouldBeTrue($"{parameter.Id} ships outside its own range");
        }

        ParameterRegistry.Find("no-such-figure").ShouldBeNull();
    }

    [Fact]
    public void A_figure_reads_as_a_person_would_say_it()
    {
        ParameterDefinition lift = ParameterRegistry.Find("stairlift-batteries.stock")!;
        ParameterDefinition charger = ParameterRegistry.Find("stairlift-charger.winter-wh")!;
        ParameterDefinition station = ParameterRegistry.Find("portable-power-station.usable")!;
        ParameterDefinition water = ParameterRegistry.Find("water-bottled-three-days.stock")!;
        ParameterDefinition storm = ParameterRegistry.Find("scenario.storm.mobilenetwork-from-1.share")!;
        ParameterDefinition waterCut = ParameterRegistry.Find("scenario.water.mainswater-from-1.last-day")!;

        lift.ShouldBe(new ParameterDefinition("stairlift-batteries.stock", "Getting upstairs", "The stairlift's own batteries: how much there is", "days", 2m, 0m, 100m, "Acorn Stairlifts: more than ten return journeys on a charge, about two days at four or five a day", "On the board, under getting upstairs")
        {
            HouseholdQuestion = "How many days of trips to bed and the bathroom does your stairlift make in a power cut?",
        });
        lift.HouseholdMayOverride.ShouldBeTrue();
        charger.Name.ShouldBe("The stairlift on its charger: energy it draws on a winter day");
        (charger.Unit, charger.Default, charger.Max, charger.HouseholdMayOverride).ShouldBe(("watt-hours", 100m, 100_000m, false));
        (station.Group, station.Unit, station.Default, station.Max).ShouldBe(("Sources of electricity", "as a share, 0 to 1", 0.85m, 1m));
        station.Shown.ShouldBe("On the board, in what carries each electrical thing when the grid is off");
        (water.Unit, water.Default, water.Max).ShouldBe(("litres a person", 9m, 180m));
        storm.Name.ShouldBe("How much of the mobile network there is, from day 1 to day 1");
        (storm.Group, storm.Default, storm.Min, storm.Max).ShouldBe(("Scenario: A storm takes the power out", 0.5m, 0m, 1m));
        storm.Shown.ShouldBe("On the board, when \"A storm takes the power out\" is chosen");
        waterCut.Name.ShouldBe("The day the stretch for mains water ends (from day 1 to day 3)");
        (waterCut.Default, waterCut.WholeNumber).ShouldBe((3m, true));
        ParameterRegistry.Find("scenario.storm.electricity-from-1.last-day").ShouldBeNull("a stretch that runs to the end has no last day to correct");
        ParameterRegistry.Find("scenario.storm.electricity-from-1.share")!.Name.ShouldBe("How much of electricity there is, from day 1");
        ParameterRegistry.Find("stored-food.stock").ShouldBeNull("a stock the household counts for itself is not a figure of ours");
        ParameterRegistry.Find("medicine-stock.stock").ShouldBeNull();
        ParameterRegistry.Find("gravity-water-filter.per-day")!.Unit.ShouldBe("litres a day");
        ParameterRegistry.Find("water-butt.lead-days")!.WholeNumber.ShouldBeTrue();
        ParameterRegistry.All.Count(p => p.HouseholdMayOverride).ShouldBe(9);
    }

    [Fact]
    public void With_nothing_corrected_the_engine_is_the_shipped_one_to_the_instance()
    {
        ParameterSet shipped = ParameterSet.Default;

        shipped.Version.ShouldBe(0);
        shipped.Corrections.ShouldBeEmpty();
        shipped["demand.drinking-litres"].ShouldBe(3m);
        ProviderFigures.Under(shipped).Count.ShouldBe(Providers.All.Count);
        for (int i = 0; i < Providers.All.Count; i++)
        {
            ProviderFigures.Under(shipped)[i].ShouldBeSameAs(Providers.All[i]);
        }

        ScenarioFigures.Under(shipped).ShouldBe(Scenarios.All);
        ScenarioFigures.Under(shipped, Scenarios.Storm).ShouldBeSameAs(Scenarios.Storm);
        ProviderFigures.Under(shipped).ShouldBeSameAs(ProviderFigures.Under(shipped), "worked out once for a set and kept");
        ResilienceProfile.FromProfile(Profiles.Flat()).Figures.ShouldBeSameAs(shipped);
        Should.Throw<KeyNotFoundException>(() => shipped["no-such-figure"]);
    }

    [Fact]
    public void A_published_set_is_refused_when_it_names_nothing_strays_from_its_range_or_contradicts_itself()
    {
        static Selficient.Domain.Primitives.Result<ParameterSet> Create(int version, params (string Id, decimal Value)[] corrections) =>
            ParameterSet.Create(version, corrections.ToDictionary(static c => c.Id, static c => c.Value));

        Create(0).Error.Message.ShouldBe("A published set of figures is numbered from one.");
        Create(1, ("no-such-figure", 1m)).Error.Message.ShouldBe("\"no-such-figure\" is not one of the figures the plan works from.");
        Create(1, ("demand.drinking-litres", 0.5m)).Error.Message.ShouldBe("Water a person drinks and cooks with in a day must be between 1 and 10 litres.");
        Create(1, ("demand.drinking-litres", 10.5m)).Error.Code.ShouldBe("parameters.range");
        Create(1, ("demand.laundry-after-days", 7.5m)).Error.Message.ShouldBe("Days a household goes before it needs to wash clothes must be between 1 and 30 whole days.");
        Create(1, ("portable-power-station.reserve-wh", 900m)).Error.Message.ShouldBe("A 1 kWh portable power station cannot keep back more energy than it can give.");
        Create(1, ("scenario.water.mainswater-from-1.first-day", 5m)).Error.Message.ShouldBe("In \"The water stops\", a stretch for mains water ends before it starts.");

        ParameterSet set = Create(7, ("demand.drinking-litres", 4m), ("stairlift-batteries.stock", 2m), ("portable-power-station.reserve-wh", 800m)).Value;
        set.Version.ShouldBe(7);
        set["demand.drinking-litres"].ShouldBe(4m);
        set["demand.sanitation-litres"].ShouldBe(12m, "what was not corrected is as shipped");
        set.Corrections.ShouldBe(new Dictionary<string, decimal> { ["demand.drinking-litres"] = 4m, ["portable-power-station.reserve-wh"] = 800m }, ignoreOrder: true, "a correction to the shipped value is no correction");
        Create(1).Value.Corrections.ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => ParameterSet.Create(1, null!));
    }

    [Fact]
    public void A_household_may_give_only_the_figures_that_are_its_own_to_know()
    {
        ParameterSet published = Corrected(("demand.drinking-litres", 4m));

        ParameterSet own = published.ForHousehold(new Dictionary<string, decimal> { ["stairlift-batteries.stock"] = 5m }).Value;

        own.Version.ShouldBe(1, "it is still that published set, with one house's figure over it");
        own["stairlift-batteries.stock"].ShouldBe(5m);
        own["demand.drinking-litres"].ShouldBe(4m);
        published["stairlift-batteries.stock"].ShouldBe(2m, "and nobody else's plan moved");
        published.ForHousehold(new Dictionary<string, decimal>()).Value.ShouldBeSameAs(published);
        published.ForHousehold(new Dictionary<string, decimal> { ["demand.drinking-litres"] = 2m }).Error.Message.ShouldBe("\"demand.drinking-litres\" is not a figure a household can give for itself.");
        published.ForHousehold(new Dictionary<string, decimal> { ["no-such-figure"] = 2m }).Error.Code.ShouldBe("parameters.unknown");
        published.ForHousehold(new Dictionary<string, decimal> { ["stairlift-batteries.stock"] = 101m }).Error.Code.ShouldBe("parameters.range");
        Should.Throw<ArgumentNullException>(() => published.ForHousehold(null!));
    }

    [Fact]
    public void Every_kind_of_figure_a_provider_carries_is_written_from_the_set()
    {
        ParameterSet set = Corrected(
            ("stairlift-batteries.stock", 4m), ("gravity-water-filter.per-day", 30m),
            ("fridge-freezer.winter-wh", 900m), ("fridge-freezer.summer-wh", 1300m), ("fridge-freezer.burst-w", 450m),
            ("water-butt.winter-flow", 10m), ("water-butt.summer-flow", 2m), ("raised-beds.lead-days", 45m),
            ("large-power-station.store-wh", 4000m), ("large-power-station.usable", 0.9m), ("large-power-station.ceiling-w", 3000m),
            ("large-power-station.refill-winter-wh", 400m), ("large-power-station.refill-summer-wh", 2500m), ("ev-output.reserve-wh", 30_000m));
        IReadOnlyList<Provider> made = ProviderFigures.Under(set);
        Provider Of(string id) => made.Single(p => p.Id == id);

        Of("stairlift-batteries").Stock.ShouldBe(new Stock(4m));
        Of("gravity-water-filter").Meets[0].PerDay.ShouldBe(30m);
        Of("gravity-water-filter").Meets[0].RequiresAnyProvider.ShouldBe(["bath-liner", "water-butt", "water-cans"], "only the figure moved");
        Of("fridge-freezer").Load.ShouldBe(new Load(900m, 1300m, 450m));
        Of("water-butt").Flow.ShouldBe(new Flow(10m, 2m, 0));
        Of("raised-beds").Flow!.LeadTimeDays.ShouldBe(45);
        Of("large-power-station").Store.ShouldBe(new EnergyStore(4000m, 0.9m, 3000m, 400m, 2500m, RefillSource.Panels, 0m, WholeHouse: false));
        Of("ev-output").Store!.ReserveWh.ShouldBe(30_000m);
        Of("mains-tap").ShouldBeSameAs(Providers.Find("mains-tap"), "a provider nobody corrected is the shipped one");
        made.Select(p => p.Id).ShouldBe(Providers.All.Select(p => p.Id), "in the shipped order, which is the order the engine tries them");
        Should.Throw<ArgumentNullException>(() => ProviderFigures.Under(null!));
    }

    [Fact]
    public void The_board_follows_a_corrected_provider()
    {
        HouseholdProfile lift = Profiles.RobsHouse() with { Circumstances = Circumstances.None with { Stairlift = Stairlift.Battery } };

        Row(Simulate(Rob(ParameterSet.Default, lift), Scenarios.Blackout), NeedId.Stairs).FirstRationedDay.ShouldBe(3);
        Row(Simulate(Rob(Corrected(("stairlift-batteries.stock", 3m)), lift), Scenarios.Blackout), NeedId.Stairs).FirstRationedDay.ShouldBe(4, "an expert says three days, and the board says three days");
        Row(Simulate(Rob(ParameterSet.Default.ForHousehold(new Dictionary<string, decimal> { ["stairlift-batteries.stock"] = 1m }).Value, lift), Scenarios.Blackout), NeedId.Stairs).FirstRationedDay.ShouldBe(2, "and a household that knows its own lift says one");
    }

    [Fact]
    public void The_board_follows_what_people_are_said_to_need()
    {
        string[] bottled = ["water-bottled-three-days"];
        HouseholdProfile pets = Profiles.FloodZoneBungalow();

        Row(Simulate(Rob(ParameterSet.Default), Scenarios.WaterCut, held: bottled), NeedId.DrinkingWater).FirstRationedDay.ShouldBeNull("three days of bottles for three days of dry taps");
        Row(Simulate(Rob(Corrected(("demand.drinking-litres", 6m))), Scenarios.WaterCut, held: bottled), NeedId.DrinkingWater).FirstRationedDay.ShouldBe(2, "at six litres a person the same bottles are a day and a half");
        Row(Simulate(Rob(Corrected(("demand.pet-water-litres", 20m)), pets), Scenarios.WaterCut, held: bottled), NeedId.DrinkingWater).FirstRationedDay.ShouldBe(1, "and thirsty pets drink it first");
        Row(Simulate(Rob(Corrected(("demand.heatwave-extra-litres", 5m))), Scenarios.Heatwave, Season.Summer), NeedId.DrinkingWater).Days[0].State
            .ShouldBe(Row(Simulate(Rob(ParameterSet.Default), Scenarios.Heatwave, Season.Summer), NeedId.DrinkingWater).Days[0].State, "the tap carries a heatwave whatever a person drinks");

        Row(Simulate(Rob(ParameterSet.Default), Scenarios.Storm), NeedId.Laundry).Days[7].State.ShouldBe(CellState.Lost);
        Row(Simulate(Rob(Corrected(("demand.laundry-after-days", 10m))), Scenarios.Storm), NeedId.Laundry).Days[7].ShouldBe(DayState.NotNeeded);

        Row(Simulate(Rob(ParameterSet.Default), Scenarios.Storm, held: ["headtorches"]), NeedId.Light).FirstRationedDay.ShouldBe(6, "forty hours at eight a winter day");
        Row(Simulate(Rob(Corrected(("demand.winter-lit-hours", 4m))), Scenarios.Storm, held: ["headtorches"]), NeedId.Light).FirstRationedDay.ShouldBe(11);
        Row(Simulate(Rob(Corrected(("demand.summer-lit-hours", 8m))), Scenarios.Storm, Season.Summer, "headtorches"), NeedId.Light).FirstRationedDay.ShouldBe(6);

        Row(Simulate(Rob(ParameterSet.Default), Scenarios.WaterCut, held: ["water-cans"]), NeedId.Sanitation).Days[0].State.ShouldBe(CellState.Held);
        Row(Simulate(Rob(Corrected(("demand.sanitation-litres", 60m))), Scenarios.WaterCut, held: ["water-cans"]), NeedId.Sanitation).Days[0].State.ShouldBe(CellState.Reduced, "a hundred litres is not a day of full flushes for four, so it is the bin bags");
    }

    [Fact]
    public void The_board_follows_a_corrected_scenario_whichever_copy_of_it_the_caller_held()
    {
        ParameterSet mild = Corrected(("scenario.storm.electricity-from-1.share", 1m));
        ParameterSet late = Corrected(("scenario.water.mainswater-from-1.first-day", 2m), ("scenario.water.mainswater-from-1.last-day", 2m));

        Row(Simulate(Rob(ParameterSet.Default), Scenarios.Storm), NeedId.Cooking).Days[0].State.ShouldBe(CellState.Lost);
        Row(Simulate(Rob(mild), Scenarios.Storm), NeedId.Cooking).Days[0].State.ShouldBe(CellState.Held, "a storm that leaves the power on leaves the hob on");
        Simulate(Rob(mild), Scenarios.Storm).Scenario.Availability(Utility.Electricity, 1).ShouldBe(1m, "the board says which storm it drew");
        ScenarioFigures.Under(mild, Scenarios.Storm).ShouldNotBeSameAs(Scenarios.Storm);
        ScenarioFigures.Under(mild, Scenarios.WaterCut).ShouldBeSameAs(Scenarios.WaterCut, "a scenario nobody corrected is the shipped one");
        ScenarioFigures.Under(mild).Select(s => s.Id).ShouldBe(Scenarios.All.Select(s => s.Id));

        Board dry = Simulate(Rob(late), Scenarios.WaterCut);
        Row(dry, NeedId.DrinkingWater).Days[0].CarriedBy.ShouldBe("mains-tap", "the main bursts a day later");
        Row(dry, NeedId.DrinkingWater).Days[1].FailedBy.ShouldBe("mains-tap");
        dry.Scenario.Phases[0].ShouldBe(new UtilityPhase(Utility.MainsWater, 2, 2, 0m));
        dry.Scenario.Phases[1].ToDay.ShouldBe(UtilityPhase.ToEnd, "a stretch that runs to the end still does");

        // A scenario that is not one of ours has no figures, and is simulated as it was given.
        Scenario own = new("own", "A test", "A sentence.", Season.Winter, [Horizon.OneDay], [new(Utility.Electricity, 1, UtilityPhase.ToEnd, 0m)], RiskKind.StormPowerCut, false, false, false);
        ScenarioFigures.Under(mild, own).ShouldBeSameAs(own);
        Simulate(Rob(mild), own).Scenario.ShouldBeSameAs(own);
        Should.Throw<ArgumentNullException>(() => ScenarioFigures.Under(null!));
        Should.Throw<ArgumentNullException>(() => ScenarioFigures.Under(mild, null!));
        Should.Throw<ArgumentNullException>(() => DemandFigures.Of(null!));
    }
}
