using Selficient.Domain.Households;
using Selficient.Domain.Resilience;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Resilience;

/// <summary>
/// The engine against real houses. The figures were read from the first run of the engine and
/// checked by hand against the design documents; a change here is a change to what a household
/// is told, so it is made deliberately.
/// </summary>
public sealed class OutageBoardTests
{
    private static readonly string[] ThisWeek = ["water-bottled-three-days", "food-no-cook-three-days", "headtorches", "radio-dab-fm-with-crank", "power-banks", "cash-in-small-notes", "power-failure-lights"];

    private static ResilienceProfile Rob() => ResilienceProfile.FromProfile(Profiles.RobsHouse());

    private static Board Storm(ResilienceProfile profile, params string[] held) => OutageBoard.Simulate(profile, held.ToHashSet(StringComparer.Ordinal), Scenarios.Storm, Season.Winter);

    private static Board Blackout(ResilienceProfile profile, params string[] held) => OutageBoard.Simulate(profile, held.ToHashSet(StringComparer.Ordinal), Scenarios.Blackout, Season.Winter);

    private static BoardRow Row(Board board, NeedId need) => board.Row(need.ToString())!;

    [Fact]
    public void Robs_house_with_nothing_done_stands_a_day_in_a_storm()
    {
        Board board = Storm(Rob());

        board.DaysReady.ShouldBe(1m, "the fridge and the shops feed the first day; after that the shops want cash the house does not have");
        Row(board, NeedId.DrinkingWater).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "mains water stays on in a power cut");
        Row(board, NeedId.Warmth).Days[0].ShouldBe(new DayState(CellState.Reduced, "blankets", "gas-boiler"));
        Row(board, NeedId.Cooking).Days[0].ShouldBe(new DayState(CellState.Lost, null, "electric-hob"));
        Row(board, NeedId.ColdStorage).Days[0].State.ShouldBe(CellState.Reduced, "a shut freezer keeps its cold");
        Row(board, NeedId.ColdStorage).Days[2].State.ShouldBe(CellState.Lost, "and loses it after 48 hours");
        Row(board, NeedId.Cash).FirstRationedDay.ShouldBe(3, "two days of notes in the drawer");
        Row(board, NeedId.Light).Days[0].State.ShouldBe(CellState.Rationed, "the phone torches give two and a half of eight hours");
        Row(board, NeedId.Laundry).Days[6].ShouldBe(DayState.NotNeeded);
        Row(board, NeedId.Laundry).Days[7].State.ShouldBe(CellState.Lost);
        Row(board, NeedId.Mobility).Days[0].State.ShouldBe(CellState.Rationed, "half the roads are blocked on the first day");
        board.Row("nothing").ShouldBeNull();
    }

    [Fact]
    public void This_weeks_shelf_takes_robs_house_to_a_week_and_the_cash_is_why()
    {
        Board shelf = Storm(Rob(), ThisWeek);
        Board withoutCash = Storm(Rob(), ThisWeek.Where(id => id != "cash-in-small-notes").ToArray());

        shelf.DaysReady.ShouldBe(7m);
        withoutCash.DaysReady.ShouldBe(4m, "without notes the shops stop counting on day three and the tins run out sooner");
        Row(shelf, NeedId.Information).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "the wind-up radio");
        Row(shelf, NeedId.Light).Cells[2].State.ShouldBe(CellState.Held, "headtorches and plug-in lights for a week");
        Row(shelf, NeedId.Contact).Days[1].State.ShouldBe(CellState.Lost, "the masts are down and a power bank cannot help that");
    }

    [Fact]
    public void A_whole_house_battery_runs_the_boiler_before_the_hob_and_the_fridge_before_the_television()
    {
        Board board = Storm(Rob(), "water-bottled-three-days", "food-no-cook-three-days", "headtorches", "radio-dab-fm-with-crank", "rooftop-solar-and-battery");

        board.DaysReady.ShouldBe(4m, "the battery does not feed people; the cash does");
        Row(board, NeedId.Warmth).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "the boiler's pump is cheap and comes first");
        Row(board, NeedId.HotWater).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "the same pump heats the water on the same energy");
        Row(board, NeedId.Cooking).Cells.ShouldAllBe(cell => cell.State == CellState.Held);
        Row(board, NeedId.ColdStorage).Cells[3].State.ShouldBe(CellState.Rationed, "by the second week the roof's three kilowatt-hours a day are not enough for everything");
        Row(board, NeedId.Entertainment).Cells[3].State.ShouldBe(CellState.Reduced, "the television gives way to books before the fridge does");
        Row(board, NeedId.Internet).Cells[0].State.ShouldBe(CellState.Rationed, "the router runs but the exchange is on battery");
    }

    [Fact]
    public void A_portable_station_needs_the_boiler_on_a_plug_to_help_with_warmth()
    {
        string[] kit = ["water-bottled-three-days", "food-no-cook-three-days", "large-power-station"];
        Board hardwired = Storm(Rob(), kit);
        Board plugged = Storm(Rob(), [.. kit, "boiler-plug-and-socket"]);

        Row(hardwired, NeedId.Warmth).Days[0].State.ShouldBe(CellState.Reduced, "a hardwired boiler cannot be plugged into a power station");
        Row(hardwired, NeedId.Warmth).Days[0].FailedBy.ShouldBe("gas-boiler");
        Row(plugged, NeedId.Warmth).Days[0].ShouldBe(new DayState(CellState.Held, "gas-boiler", null));
        Row(plugged, NeedId.Warmth).Days[2].State.ShouldBe(CellState.Reduced, "three kilowatt-hours and half a kilowatt-hour of winter sun run out");
    }

    [Fact]
    public void A_fifth_floor_flat_loses_its_water_with_the_power()
    {
        HouseholdProfile flat = Profiles.Build(1, 0, false, false, false, DwellingType.Flat, 1, HeatingType.Electric, HobType.Electric, false, false, false, false, 20m, FloodRisk.Unknown);
        ResilienceProfile fifth = ResilienceProfile.Create(flat, false, false, false, Growing.None, CarKind.None, 0, 7, 5, []).Value;
        ResilienceProfile ground = ResilienceProfile.Create(flat, false, false, false, Growing.None, CarKind.None, 0, 7, 0, []).Value;

        Board high = Storm(fifth);
        Board low = Storm(ground);

        high.DaysReady.ShouldBe(0.5m);
        Row(high, NeedId.DrinkingWater).Days[0].ShouldBe(new DayState(CellState.Rationed, "kettle-water", "mains-tap"));
        Row(high, NeedId.Sanitation).Days[0].ShouldBe(new DayState(CellState.Reduced, "bin-bags", "mains-tap"), "nothing to flush with, so it is a bag and the outside bin");
        Row(Storm(fifth, "no-flush-toilet-kit"), NeedId.Sanitation).Days[2].ShouldBe(new DayState(CellState.Held, "no-flush-toilet-kit", "mains-tap"), "three days of liners");
        Row(Storm(fifth, "no-flush-toilet-kit"), NeedId.Sanitation).Days[3].ShouldBe(new DayState(CellState.Reduced, "bin-bags", "mains-tap"), "and bin bags once they are gone");
        Row(low, NeedId.DrinkingWater).Days[0].State.ShouldBe(CellState.Held, "the ground floor is gravity fed");
        Row(high, NeedId.Mobility).Needed.ShouldBeFalse("no car, no row that matters");
        Row(high, NeedId.Warmth).Days[0].ShouldBe(new DayState(CellState.Reduced, "blankets", "electric-heating"));
    }

    [Fact]
    public void Blankets_do_not_count_for_a_frail_person_or_a_baby()
    {
        HouseholdProfile heatPump = Profiles.Build(2, 0, true, false, false, DwellingType.SemiDetached, 2, HeatingType.HeatPump, HobType.Electric, true, true, false, false, 80m, FloodRisk.Low);
        HouseholdProfile terrace = Profiles.Build(3, 1, false, true, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, true, true, false, false, 50m, FloodRisk.Low);
        ResilienceProfile frail = ResilienceProfile.FromProfile(heatPump);
        ResilienceProfile baby = ResilienceProfile.Create(terrace, false, false, false, Growing.None, CarKind.Petrol, 1, 14, 0, []).Value;

        Board frailBoard = Storm(frail, "water-bottled-three-days", "food-no-cook-three-days");
        Board babyBoard = Storm(baby, "water-bottled-three-days", "food-no-cook-three-days", "formula-and-nappies", "pet-food-two-weeks");

        frailBoard.DaysReady.ShouldBe(0.5m);
        Row(frailBoard, NeedId.Warmth).Days[0].ShouldBe(new DayState(CellState.Lost, null, "heat-pump"));
        babyBoard.DaysReady.ShouldBe(0.5m);
        Row(babyBoard, NeedId.Warmth).Days[0].State.ShouldBe(CellState.Lost);
        Row(babyBoard, NeedId.Cooking).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "a gas hob lights with a match");
        Row(babyBoard, NeedId.Dependants).Cells[2].State.ShouldBe(CellState.Held, "formula with boiled water from the gas hob, and the dog's food");
    }

    [Fact]
    public void Formula_depends_on_boiled_water_and_a_boil_notice_puts_the_tap_behind_the_hob()
    {
        HouseholdProfile electricHob = Profiles.Build(3, 1, false, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Electric, false, true, false, false, 50m, FloodRisk.Low);
        ResilienceProfile baby = ResilienceProfile.Create(electricHob, false, false, false, Growing.None, CarKind.None, 1, 14, 0, []).Value;

        Board storm = Storm(baby, "formula-and-nappies");
        Board water = OutageBoard.Simulate(baby, new HashSet<string>(), Scenarios.WaterCut, Season.Winter);

        Row(storm, NeedId.Dependants).Days[0].ShouldBe(new DayState(CellState.Lost, null, "shops"), "no cooking in a cut on an electric hob, so no formula, whatever is in the cupboard");
        Row(water, NeedId.Cooking).Days[0].State.ShouldBe(CellState.Held, "the power is on in a water cut");
        Row(water, NeedId.DrinkingWater).Days[0].ShouldBe(new DayState(CellState.Rationed, "kettle-water", "mains-tap"), "the mains are off for three days");
        Row(water, NeedId.DrinkingWater).Days[3].ShouldBe(new DayState(CellState.Held, "mains-tap", null), "the mains return on the fourth day and the hob boils the notice away");

        // A storm with a boil notice: the mains are on but there is no cooking on an electric hob, so the tap is not drinking water.
        Scenario stormWithNotice = Scenarios.Storm with { BoilWaterNotice = true };
        Board notice = OutageBoard.Simulate(baby, new HashSet<string>(), stormWithNotice, Season.Winter, Horizon.OneDay);
        Row(notice, NeedId.DrinkingWater).Days[0].ShouldBe(new DayState(CellState.Rationed, "kettle-water", "mains-tap"));
        Row(notice, NeedId.Sanitation).Days[0].State.ShouldBe(CellState.Held, "a boil notice is about drinking, not flushing");
    }

    [Fact]
    public void Household_dependencies_sit_on_their_rung_and_decide_the_headline()
    {
        Dependency concentrator = Dependency.Create("Oxygen concentrator", Rung.Survive, DependencyKind.PoweredDevice, 300m, 24m, 0, false).Value;
        Dependency insulin = Dependency.Create("Insulin", Rung.Survive, DependencyKind.Medicine, 0, 0, 28, mustStayCold: true).Value;
        Dependency bags = Dependency.Create("Stoma bags", Rung.Function, DependencyKind.Supply, 0, 0, 5, false).Value;
        ResilienceProfile profile = ResilienceProfile.Create(Profiles.RobsHouse(), false, false, false, Growing.None, CarKind.Petrol, 0, 7, 0, [concentrator, insulin, bags]).Value;

        Board storm = Storm(profile, ThisWeek);
        Board heatwave = OutageBoard.Simulate(profile, new HashSet<string>(), Scenarios.Heatwave, Season.Summer);
        Board stayHome = OutageBoard.Simulate(profile, new HashSet<string>(), Scenarios.StayAtHome, Season.Winter);
        Board withStation = Storm(profile, [.. ThisWeek, "large-power-station"]);
        Board withBattery = Storm(profile, [.. ThisWeek, "rooftop-solar-and-battery"]);

        storm.Row("dependency-0")!.Days[0].ShouldBe(new DayState(CellState.Lost, null, "the sources"), "a concentrator with no source");
        storm.DaysReady.ShouldBe(0.5m, "the concentrator is on the Survive rung");
        storm.Row("dependency-1")!.Days[0].State.ShouldBe(CellState.Held, "the insulin is cold while the shut fridge is cold");
        storm.Row("dependency-1")!.Days[2].ShouldBe(new DayState(CellState.Lost, null, "fridge-freezer"), "and spoils when the fridge is lost");
        storm.Row("dependency-2")!.Rung.ShouldBe(Rung.Function);
        heatwave.Row("dependency-0")!.Days[0].State.ShouldBe(CellState.Rationed, "a ten percent cut is two hours a day without oxygen");
        heatwave.DaysReady.ShouldBe(0.5m);
        withStation.Row("dependency-0")!.Days[0].ShouldBe(new DayState(CellState.Rationed, "the mains, for part of the day", "the sources"), "seven kilowatt-hours a day is more than twice what a 3 kWh station holds");
        withBattery.Row("dependency-0")!.Days[0].ShouldBe(new DayState(CellState.Held, "the mains or a source", null), "the whole-house battery carries it for the first day");
        withBattery.Row("dependency-0")!.Days[1].State.ShouldBe(CellState.Rationed, "and the roof's three kilowatt-hours cannot keep up");
        stayHome.Row("dependency-2")!.Days[0].ShouldBe(new DayState(CellState.Held, "the stock in the house", null), "the shops are out of reach, so the five days of bags carry it");
        stayHome.Row("dependency-2")!.Days[5].State.ShouldBe(CellState.Lost);
        stayHome.Row("dependency-1")!.Days[0].State.ShouldBe(CellState.Held, "the pharmacy delivers seven tenths and the fridge stock covers the rest");
        stayHome.Row("dependency-1")!.Days[0].CarriedBy.ShouldBe("the stock in the house");
    }

    [Fact]
    public void A_medicine_dependency_comes_from_the_pharmacy_while_it_dispenses_and_a_supply_from_the_shops()
    {
        Dependency medicine = Dependency.Create("Thyroxine", Rung.Survive, DependencyKind.Medicine, 0, 0, 2, false).Value;
        Dependency supply = Dependency.Create("Catheters", Rung.Survive, DependencyKind.Supply, 0, 0, 2, false).Value;
        ResilienceProfile profile = ResilienceProfile.Create(Profiles.RobsHouse(), false, false, false, Growing.None, CarKind.Petrol, 0, 7, 0, [medicine, supply]).Value;

        Board calm = OutageBoard.Simulate(profile, new HashSet<string>(), Scenarios.StayAtHome with { Phases = [] }, Season.Winter, Horizon.OneWeek);
        Board snow = OutageBoard.Simulate(profile, new HashSet<string>(), Scenarios.Snow, Season.Winter);
        Board regional = OutageBoard.Simulate(profile, new HashSet<string>(), Scenarios.Regional, Season.Winter);

        calm.Row("dependency-0")!.Days[0].ShouldBe(new DayState(CellState.Held, "pharmacy", null));
        calm.Row("dependency-1")!.Days[0].ShouldBe(new DayState(CellState.Held, "shops", null));
        snow.Row("dependency-0")!.Days[2].ShouldBe(new DayState(CellState.Lost, null, "the stock in the house"), "two days of stock and the road is shut");
        regional.Row("dependency-0")!.Days[2].State.ShouldBe(CellState.Held, "the pharmacy dispenses six tenths and the stock covers the rest for five days");
        regional.Row("dependency-0")!.Days[5].ShouldBe(new DayState(CellState.Rationed, "the stock in the house", "pharmacy"));
        regional.Row("dependency-1")!.Days[3].ShouldBe(new DayState(CellState.Rationed, "the stock in the house", "shops"), "the shops give four tenths and two days of stock stretch to three");
    }

    [Fact]
    public void A_shared_stock_counted_in_days_serves_every_need_it_meets_on_the_same_day()
    {
        Board board = Storm(Rob());

        // The phones' own charge is one day; information and contact both draw on it on day one.
        Row(board, NeedId.Information).Days[0].ShouldBe(new DayState(CellState.Rationed, "phones", "phone-charging"), "half a signal on the first day");
        Row(board, NeedId.Contact).Days[0].ShouldBe(new DayState(CellState.Rationed, "phones", "phone-charging"));
        Row(board, NeedId.Contact).Days[1].State.ShouldBe(CellState.Lost);
    }

    [Fact]
    public void A_wood_stove_warms_the_house_for_as_long_as_the_logs_last_and_boils_a_kettle()
    {
        ResilienceProfile stove = ResilienceProfile.Create(Profiles.RobsHouse(), false, true, false, Growing.None, CarKind.Petrol, 0, 7, 0, []).Value;

        // A storm that also stops the log merchant, so the store itself is what counts.
        Scenario noMerchant = Scenarios.Storm with { Phases = [.. Scenarios.Storm.Phases, new UtilityPhase(Utility.Deliveries, 1, UtilityPhase.ToEnd, 0m)] };
        Board board = OutageBoard.Simulate(stove, new HashSet<string> { "water-bottled-three-days", "food-one-month", "cash-in-small-notes" }, noMerchant, Season.Winter);

        Row(board, NeedId.Warmth).Days[0].ShouldBe(new DayState(CellState.Held, "wood-stove", "gas-boiler"));
        Row(Storm(stove), NeedId.Warmth).Days[0].CarriedBy.ShouldBe("log-deliveries", "in an ordinary storm the merchant still delivers");
        Row(board, NeedId.Warmth).Days[20].State.ShouldBe(CellState.Held);
        Row(board, NeedId.Warmth).Days[21].State.ShouldBe(CellState.Reduced, "the log store is empty after three weeks");
        Row(board, NeedId.Cooking).Days[0].ShouldBe(new DayState(CellState.Reduced, "wood-stove", "electric-hob"), "a kettle on the stove top");
    }

    [Fact]
    public void Water_can_be_made_drinkable_from_a_butt_or_a_liner_only_with_a_filter()
    {
        ResilienceProfile butt = ResilienceProfile.Create(Profiles.RobsHouse(), false, false, true, Growing.None, CarKind.Petrol, 0, 7, 0, []).Value;

        Board without = OutageBoard.Simulate(butt, new HashSet<string>(), Scenarios.WaterCut, Season.Winter);
        Board filtered = OutageBoard.Simulate(butt, new HashSet<string> { "gravity-water-filter" }, Scenarios.WaterCut, Season.Winter);
        Board filterAlone = OutageBoard.Simulate(Rob(), new HashSet<string> { "gravity-water-filter" }, Scenarios.WaterCut, Season.Winter);

        Row(without, NeedId.Sanitation).Cells[2].State.ShouldBe(CellState.Held, "two hundred litres and the rain flush the loo until the mains return on day four");
        Row(OutageBoard.Simulate(Rob(), new HashSet<string>(), Scenarios.WaterCut, Season.Winter), NeedId.Sanitation).Days[0].ShouldBe(new DayState(CellState.Reduced, "bin-bags", "mains-tap"), "without the butt there is nothing to flush with, and a bin bag is what is left");
        Row(OutageBoard.Simulate(Rob(), new HashSet<string> { "no-flush-toilet-kit" }, Scenarios.WaterCut, Season.Winter), NeedId.Sanitation).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "the liners last the three days the mains are off");
        Row(without, NeedId.DrinkingWater).Days[1].State.ShouldBe(CellState.Lost);
        Row(filtered, NeedId.DrinkingWater).Days[1].ShouldBe(new DayState(CellState.Held, "gravity-water-filter", "mains-tap"));
        Row(filterAlone, NeedId.DrinkingWater).Days[1].State.ShouldBe(CellState.Lost, "a filter with nothing to filter");
    }

    [Fact]
    public void When_the_whole_grid_goes_down_the_water_follows_the_power_and_the_shelf_is_three_days()
    {
        Board nothing = Blackout(Rob());
        Board shelf = Blackout(Rob(), ThisWeek);
        Board withCansAndLiners = Blackout(Rob(), [.. ThisWeek, "no-flush-toilet-kit", "water-cans"]);

        nothing.DaysReady.ShouldBe(0.5m);
        nothing.DaysSimulated.ShouldBe(7);
        Row(nothing, NeedId.DrinkingWater).Days[0].ShouldBe(new DayState(CellState.Rationed, "kettle-water", "mains-tap"), "the tap runs for a few hours while the towers drain");
        Row(nothing, NeedId.DrinkingWater).Days[4].ShouldBe(new DayState(CellState.Lost, null, "mains-tap"), "the water is back on the fifth day under a boil notice, and an electric hob on rolling power cannot be relied on to boil it");
        Row(nothing, NeedId.DrinkingWater).Days[5].ShouldBe(new DayState(CellState.Held, "mains-tap", null));
        shelf.DaysReady.ShouldBe(3m, "three days of bottled water is three days");
        Row(shelf, NeedId.DrinkingWater).FirstRationedDay.ShouldBe(4);
        Row(shelf, NeedId.Information).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "the radio is the one source of news that needs nothing from anyone");
        Row(shelf, NeedId.Contact).Days[1].State.ShouldBe(CellState.Lost, "every mast runs out of battery together");
        Row(shelf, NeedId.Cash).Days[3].ShouldBe(new DayState(CellState.Held, "cash-in-small-notes", "cash-machines"));
        withCansAndLiners.DaysReady.ShouldBe(7m, "stored water carries the two days the bottles do not");
    }

    [Fact]
    public void When_the_sewers_stop_with_the_power_stored_water_cannot_flush_and_only_a_bag_will_do()
    {
        Board nothing = Blackout(Rob());
        Board storedWater = Blackout(Rob(), "water-cans", "bath-liner");
        Board liners = Blackout(Rob(), "no-flush-toilet-kit");
        Board storm = Storm(Rob(), "no-flush-toilet-kit");

        Row(nothing, NeedId.Sanitation).Days[0].ShouldBe(new DayState(CellState.Reduced, "bin-bags", "mains-tap"), "the tap still runs on the first day, and must not be used to flush");
        Row(storedWater, NeedId.DrinkingWater).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "a hundred litres in cans is drinking water for the week");
        Row(storedWater, NeedId.Sanitation).Days[1].ShouldBe(new DayState(CellState.Reduced, "bin-bags", "mains-tap"), "four hundred and eighty litres in the house and nowhere for a flush to go");
        Row(liners, NeedId.Sanitation).Days[0].ShouldBe(new DayState(CellState.Held, "no-flush-toilet-kit", "mains-tap"));
        Row(liners, NeedId.Sanitation).Days[2].State.ShouldBe(CellState.Held);
        Row(liners, NeedId.Sanitation).Days[3].ShouldBe(new DayState(CellState.Reduced, "bin-bags", "mains-tap"), "three days of liners, then bin bags");
        Row(nothing, NeedId.Sanitation).Days[4].State.ShouldBe(CellState.Reduced, "the sewers wait for steady power, not rolling power");
        Row(nothing, NeedId.Sanitation).Days[5].ShouldBe(new DayState(CellState.Held, "mains-tap", null));
        Row(storm, NeedId.Sanitation).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "a line down in the street leaves the sewers running");
        Row(storm, NeedId.Sanitation).Days[0].CarriedBy.ShouldBe("mains-tap", "and the liners stay in the cupboard while the toilet flushes");
        Scenarios.All.Where(scenario => scenario.SewersStopWithThePower).ShouldBe([Scenarios.Blackout]);
    }

    [Fact]
    public void Food_is_grown_after_a_lead_time_and_in_season()
    {
        ResilienceProfile allotment = ResilienceProfile.Create(Profiles.RobsHouse(), true, true, true, Growing.Allotment, CarKind.Petrol, 0, 28, 0, []).Value;

        // Shortages without deliveries, so the plot and the cupboard are all there is beyond the rationed shops.
        Scenario shortages = Scenarios.Shortages with { Phases = [.. Scenarios.Shortages.Phases, new UtilityPhase(Utility.Deliveries, 1, UtilityPhase.ToEnd, 0m)] };
        HashSet<string> kit = ["food-one-month", "cash-in-small-notes", "rooftop-solar-and-battery"];
        Board winter = OutageBoard.Simulate(allotment, kit, shortages, Season.Winter);
        Board summer = OutageBoard.Simulate(allotment, kit, shortages, Season.Summer);
        Board none = OutageBoard.Simulate(Rob(), kit, shortages, Season.Winter);

        winter.DaysReady.ShouldBe(70m, "twenty-eight days of medicines against a pharmacy dispensing six tenths");
        Row(summer, NeedId.Food).FirstRationedDay.ShouldBeNull("in the growing season the plot more than covers what the rationed shops leave short");
        Row(winter, NeedId.Food).FirstRationedDay.ShouldNotBeNull("a few stored roots do not");
        Row(none, NeedId.Food).FirstRationedDay!.Value.ShouldBeLessThan(Row(winter, NeedId.Food).FirstRationedDay!.Value, "no plot at all is worse than a winter plot");
        Row(winter, NeedId.Food).Days[0].CarriedBy.ShouldBe("shops");
    }

    [Fact]
    public void Energy_sources_refill_only_where_they_can()
    {
        ResilienceProfile rob = Rob();
        HouseholdProfile flatProfile = Profiles.Flat();
        ResilienceProfile flat = ResilienceProfile.FromProfile(flatProfile);

        HouseholdProfile gasFlatProfile = Profiles.Build(2, 0, false, false, false, DwellingType.Flat, 1, HeatingType.GasBoiler, HobType.Electric, false, false, false, false, 20m, FloodRisk.Unknown);
        ResilienceProfile gasFlat = ResilienceProfile.FromProfile(gasFlatProfile);
        Board panelsOutdoors = Storm(rob, "large-power-station", "boiler-plug-and-socket");
        Board panelsInAFlat = Storm(gasFlat, "large-power-station", "boiler-plug-and-socket");
        Board generatorWithFuel = OutageBoard.Simulate(rob, new HashSet<string> { "petrol-generator", "boiler-plug-and-socket", "food-one-month", "cash-in-small-notes" }, Scenarios.Shortages, Season.Winter);
        Board generatorNoFuel = Storm(rob, "petrol-generator", "boiler-plug-and-socket");
        Board generatorInAFlat = Storm(flat, "petrol-generator");

        Row(panelsOutdoors, NeedId.Warmth).Days[13].CarriedBy.ShouldBe("gas-boiler", "half a kilowatt-hour of winter sun a day keeps the boiler's pump turning for part of each day");
        Row(panelsOutdoors, NeedId.Warmth).Days[13].State.ShouldBe(CellState.Reduced);
        Row(panelsInAFlat, NeedId.Warmth).Days[13].ShouldBe(new DayState(CellState.Reduced, "blankets", "gas-boiler"), "nowhere to put the panels, so the station is empty by the second week");
        Row(generatorWithFuel, NeedId.ColdStorage).Cells[4].State.ShouldBe(CellState.Held, "fuel at a third is still fuel over six months of four-hour cuts");
        Row(generatorNoFuel, NeedId.ColdStorage).Cells[4].State.ShouldBe(CellState.Lost, "twenty litres and the forecourts off");
        Row(generatorInAFlat, NeedId.ColdStorage).Days[0].State.ShouldBe(CellState.Reduced, "a generator cannot run in a flat, so the freezer only keeps its cold");
    }

    [Fact]
    public void The_car_powers_the_house_but_keeps_its_reserve()
    {
        ResilienceProfile ev = ResilienceProfile.Create(Profiles.RobsHouse(), false, false, false, Growing.None, CarKind.ElectricWithOutput, 0, 7, 0, []).Value;
        ResilienceProfile plainEv = ResilienceProfile.Create(Profiles.RobsHouse(), false, false, false, Growing.None, CarKind.Electric, 0, 7, 0, []).Value;

        Board output = Storm(ev, "boiler-plug-and-socket", "water-bottled-three-days", "food-no-cook-three-days");
        Board plain = Storm(plainEv, "boiler-plug-and-socket", "water-bottled-three-days", "food-no-cook-three-days");

        Row(output, NeedId.Warmth).Cells[2].State.ShouldBe(CellState.Held, "forty kilowatt-hours is more than a week of essentials");
        Row(output, NeedId.Warmth).Cells[3].State.ShouldBe(CellState.Reduced);
        Row(output, NeedId.Mobility).Days[1].State.ShouldBe(CellState.Held, "the twenty kilowatt-hours kept back still drive");
        Row(plain, NeedId.Warmth).Days[0].State.ShouldBe(CellState.Reduced, "an electric car without vehicle-to-load is a load, not a source");
        Row(plain, NeedId.Mobility).Days[0].FailedBy.ShouldBe("ev-charging");
    }

    [Fact]
    public void Power_banks_carry_the_phones_and_nothing_with_a_plug()
    {
        Board banks = Storm(Rob(), "power-banks", "satellite-internet-on-standby");

        Row(banks, NeedId.Information).Days[1].ShouldBe(new DayState(CellState.Lost, null, "phones"), "the masts are gone and the dish wants seventy-five watts the banks cannot give");
        Row(banks, NeedId.ColdStorage).Days[0].FailedBy.ShouldBe("fridge-freezer");
    }

    [Fact]
    public void Short_horizons_cut_the_board_to_what_was_simulated()
    {
        Board oneDay = OutageBoard.Simulate(Rob(), new HashSet<string>(), Scenarios.Storm, Season.Winter, Horizon.OneDay);
        Board whole = OutageBoard.Simulate(Rob(), new HashSet<string>(), Scenarios.Storm, Season.Winter);

        oneDay.DaysSimulated.ShouldBe(1);
        Row(oneDay, NeedId.Food).Cells.Count.ShouldBe(1);
        Row(oneDay, NeedId.Food).Cells[0].ShouldBe(new HorizonCell(Horizon.OneDay, CellState.Held, 1));
        oneDay.DaysReady.ShouldBe(1m, "nothing on the Survive rung fails within the day simulated");
        Row(whole, NeedId.Food).Cells.Select(cell => cell.Horizon).ShouldBe(Scenarios.Storm.Horizons);
        Row(whole, NeedId.Food).Cells[1].ShouldBe(new HorizonCell(Horizon.ThreeDays, CellState.Lost, 1), "the worst day in the stretch, and how far in it got");
        whole.Season.ShouldBe(Season.Winter);
        whole.Scenario.ShouldBe(Scenarios.Storm);
    }

    [Fact]
    public void The_summer_board_differs_from_the_winter_one()
    {
        Board winter = Storm(Rob(), ThisWeek);
        Board summer = OutageBoard.Simulate(Rob(), ThisWeek.ToHashSet(StringComparer.Ordinal), Scenarios.Storm, Season.Summer);

        Row(summer, NeedId.Warmth).Needed.ShouldBeFalse();
        Row(summer, NeedId.Warmth).Days[0].ShouldBe(DayState.NotNeeded);
        Row(winter, NeedId.Warmth).Needed.ShouldBeTrue();
        Row(summer, NeedId.Light).Cells[3].State.ShouldBe(CellState.Held, "four lit hours a day in summer lasts the torches twice as long");
    }

    [Fact]
    public void The_arguments_are_checked()
    {
        ResilienceProfile rob = Rob();
        HashSet<string> none = [];

        Should.Throw<ArgumentNullException>(() => OutageBoard.Simulate(null!, none, Scenarios.Storm, Season.Winter));
        Should.Throw<ArgumentNullException>(() => OutageBoard.Simulate(rob, (IReadOnlySet<string>)null!, Scenarios.Storm, Season.Winter));
        Should.Throw<ArgumentNullException>(() => OutageBoard.Simulate(rob, none, null!, Season.Winter));
        Should.Throw<ArgumentNullException>(() => OutageBoard.Simulate(null!, none, Scenarios.Storm, Season.Winter, Horizon.OneDay));
        Should.Throw<ArgumentNullException>(() => OutageBoard.Simulate(rob, null!, Scenarios.Storm, Season.Winter, Horizon.OneDay));
        Should.Throw<ArgumentNullException>(() => OutageBoard.Simulate(rob, none, null!, Season.Winter, Horizon.OneDay));
    }
}
