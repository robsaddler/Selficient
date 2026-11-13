using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Parameters;
using Selficient.Domain.Plans;
using Selficient.Domain.Plans.Rules;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Parameters;

/// <summary>
/// The plan asks for what the board counts, at prices somebody can correct. A rule reads its
/// quantities and its prices from the household's figures, and the sentence it writes says them.
/// </summary>
public sealed class RuleFigureTests
{
    private static readonly RiskAssessment NoRisks = new RiskAssessmentPipeline([]).Run(Profiles.Flat());

    private static ParameterSet Corrected(params (string Id, decimal Value)[] corrections) =>
        ParameterSet.Create(1, corrections.ToDictionary(static c => c.Id, static c => c.Value)).Value;

    private static PlanAction Act(IPlanRule rule, HouseholdProfile profile, string id, ParameterSet figures) =>
        rule.Contribute(profile with { Figures = figures }, NoRisks).Single(action => action.Id.Value == id);

    [Fact]
    public void Every_action_that_costs_money_has_a_least_and_a_most_that_can_be_corrected()
    {
        ParameterDefinition low = ParameterRegistry.Find("price.headtorches.low")!;
        ParameterDefinition high = ParameterRegistry.Find("price.rooftop-solar-and-battery.high")!;

        low.ShouldBe(new ParameterDefinition("price.headtorches.low", "What things cost", "A headtorch: the least it costs", "pounds", 14m, 0m, 280m, "Shop prices seen at the catalogue's last review", "In the plan, as the cost of \"headtorches\"") { HouseholdMayBeAsked = true });
        (high.Name, high.Default, high.Max).ShouldBe(("Rooftop solar with a battery and backup: the most it costs", 17000m, 170_000m));
        ParameterRegistry.Find("price.long-reach-lighter-for-gas-hob.high")!.Max.ShouldBe(100m, "a cheap thing still has room to be corrected");
        ParameterRegistry.All.Count(p => p.Group == "What things cost").ShouldBe(76);
        ParameterRegistry.All.Count(p => p.Group == "What the plan asks for").ShouldBe(3);

        ParameterSet.Create(1, new Dictionary<string, decimal> { ["price.headtorches.low"] = 40m }).Error.Message.ShouldBe("A headtorch cannot cost more at its least than at its most.");
        ParameterSet.Create(1, new Dictionary<string, decimal> { ["price.headtorches.low"] = 28m }).IsSuccess.ShouldBeTrue("the same at both ends is a fixed price");
    }

    [Fact]
    public void A_corrected_price_is_the_cost_on_the_plan_multiplied_as_the_rule_multiplies_it()
    {
        ParameterSet dearer = Corrected(
            ("price.headtorches.low", 20m), ("price.headtorches.high", 40m), ("price.water-bottled-three-days.high", 1m),
            ("price.water-cans.low", 10m), ("price.water-purifying-tablets.low", 5m), ("price.water-purifying-tablets.high", 5m), ("price.boiler-plug-and-socket.low", 150m));
        HouseholdProfile rob = Profiles.RobsHouse();

        Act(new BaselineChecklistRule(), rob, "headtorches", dearer).Cost.ShouldBe(MoneyRange.Pounds(80m, 160m), "four people, four headtorches");
        Act(new BaselineChecklistRule(), rob, "headtorches", ParameterSet.Default).Cost.ShouldBe(MoneyRange.Pounds(56m, 112m));
        Act(new BaselineChecklistRule(), rob, "water-bottled-three-days", dearer).Cost.ShouldBe(MoneyRange.Pounds(12.6m, 36m), "thirty-six litres at 35p to a pound");
        Act(new BaselineChecklistRule(), rob, "radio-dab-fm-with-crank", dearer).Cost.ShouldBe(MoneyRange.Pounds(30m, 150m), "what was not corrected is as shipped");
        Act(new WaterStoreRule(), rob, "water-cans", dearer).Cost.ShouldBe(MoneyRange.Pounds(45m, 77m), "four cans and one tub of tablets");
        Act(new WaterStoreRule(), rob, "water-cans", ParameterSet.Default).Cost.ShouldBe(MoneyRange.Pounds(56m, 80m));

        Act(new PowerStationRule(), rob, "boiler-plug-and-socket", dearer).Cost.ShouldBe(MoneyRange.Pounds(150m, 200m));
        HouseholdProfile tenant = rob with { Circumstances = Circumstances.None with { Tenure = Tenure.Tenant } };
        Act(new PowerStationRule(), tenant, "boiler-plug-and-socket", dearer).Cost.ShouldBe(MoneyRange.Pounds(0m, 200m), "a landlord may pay, whatever an electrician charges");
    }

    [Fact]
    public void The_plan_asks_for_what_the_board_counts_and_says_so()
    {
        ParameterSet set = Corrected(
            ("water-bottled-three-days.stock", 12m), (RuleFigures.BottleLitres, 1.5m), ("food-no-cook-three-days.stock", 5m), ("no-flush-toilet-kit.stock", 60m),
            ("formula-and-nappies.stock", 21m), ("stairlift-batteries.stock", 3m), (RuleFigures.WaterCanLitres, 20m), ("bath-liner.stock", 300m), ("water-butt.stock", 250m),
            ("food-one-month.stock", 28m), ("pet-food-two-weeks.stock", 7m), ("butane-cartridge-stove.stock", 8m), ("rooftop-solar-and-battery.store-wh", 10_000m),
            ("gas-boiler.winter-wh", 900m), ("gravity-water-filter.per-day", 30m), ("satellite-internet-on-standby.winter-wh", 700m));
        HouseholdProfile rob = Profiles.RobsHouse();
        HouseholdProfile bungalow = Profiles.FloodZoneBungalow();

        PlanAction water = Act(new BaselineChecklistRule(), rob, "water-bottled-three-days", set);
        water.Title.ShouldBe("48 litres of sealed bottled water");
        water.Why.ShouldBe("Four days of drinking water for 4 people. That is 32 of the big 1.5-litre bottles, and it keeps for two years.");
        PlanAction food = Act(new BaselineChecklistRule(), rob, "food-no-cook-three-days", set);
        food.Title.ShouldBe("Five days of food that needs no fridge or cooking");
        Act(new BaselineChecklistRule(), rob, "no-flush-toilet-kit", set).Title.ShouldBe("Toilet liners and gelling powder for 4 people for five days");

        Act(new InfantsRule(), rob with { Particulars = new Particulars(false, false, false, 1) }, "formula-and-nappies", set).Title.ShouldBe("Three weeks of nappies, wipes and, if you use it, formula");
        Act(new StairliftRule(), rob with { Circumstances = Circumstances.None with { Stairlift = Stairlift.Battery } }, "downstairs-in-a-power-cut", set).Why.ShouldStartWith("The batteries are about three days of trips to bed and the bathroom.");

        PlanAction cans = Act(new WaterStoreRule(), rob, "water-cans", set);
        cans.Title.ShouldBe("4 food-grade 20-litre water cans with taps");
        cans.Why.ShouldStartWith("80 litres is about 6 days of drinking water for 4 people.");
        Act(new WaterStoreRule(), rob, "bath-liner", set).Why.ShouldStartWith("It holds about 300 litres and fills from the tap in twenty minutes,");
        Act(new WaterStoreRule(), rob, "water-butt", set).Title.ShouldBe("A 250-litre water butt on a downpipe");
        Act(new WaterStoreRule(), rob, "water-butt", ParameterSet.Default).Title.ShouldBe("A 210-litre water butt on a downpipe");

        PlanAction month = Act(new FoodStoreRule(), bungalow, "food-one-month", set);
        month.Title.ShouldBe("Four weeks of food, built by buying one extra of what you already eat");
        PlanAction pets = Act(new FoodStoreRule(), bungalow, "pet-food-two-weeks", set);
        pets.Title.ShouldBe("A week of pet food, and any medication, kept ahead of the bag in use");
        pets.Why.ShouldStartWith("Buy the next bag or box before the last one is opened, so there is always a week in the house");

        Act(new CookingRule(), rob, "butane-cartridge-stove", set).Title.ShouldBe("A butane cartridge stove and eight cartridges");
        Act(new SolarQuotesRule(), rob, "rooftop-solar-and-battery", set).Title.ShouldBe("Rooftop solar with a 10 kWh battery and whole-home backup");
        Act(new SolarQuotesRule(), rob, "rooftop-solar-and-battery", ParameterSet.Default).Title.ShouldBe("Rooftop solar with a 13.5 kWh battery and whole-home backup");
        Act(new PowerStationRule(), rob, "boiler-plug-and-socket", set).Why.ShouldStartWith("A boiler's pump and controls use about 900 watt-hours on a winter day, which a power station can carry.");
        PlanAction filter = Act(new WaterFilterRule(), rob, "gravity-water-filter", set);
        filter.Why.ShouldStartWith("Turns the water butt, a stream or rain into drinking water at about 30 litres a day, indefinitely.");
        Act(new BusinessCommsRule(), rob, "satellite-internet-on-standby", set).Why.ShouldContain("It uses about 700 watt-hours a day, which a power station can carry for a while.");
    }

    [Fact]
    public void A_households_own_figure_counts_towards_what_the_plan_asks_for_under_its_figures()
    {
        ParameterSet set = Corrected(("food-no-cook-three-days.stock", 5m), ("food-one-month.stock", 28m), ("water-bottled-three-days.stock", 12m), (RuleFigures.WaterCanLitres, 20m), ("cash-in-small-notes.stock", 7m), ("butane-cartridge-stove.stock", 8m));
        ResilienceProfile rob = ResilienceProfile.FromProfile(Profiles.RobsHouse() with { Figures = set });
        ResilienceProfile shipped = ResilienceProfile.FromProfile(Profiles.RobsHouse());
        static decimal Target(string stock, string action, ResilienceProfile profile) => MeasuredStocks.Target(MeasuredStocks.Find(stock)!, action, profile);

        Target(MeasuredStocks.FoodDays, "food-no-cook-three-days", rob).ShouldBe(5m);
        Target(MeasuredStocks.FoodDays, "food-one-month", rob).ShouldBe(28m);
        Target(MeasuredStocks.BottledWaterLitres, "water-bottled-three-days", rob).ShouldBe(48m);
        Target(MeasuredStocks.WaterCansLitres, "water-cans", rob).ShouldBe(80m);
        Target(MeasuredStocks.CashPounds, "cash-in-small-notes", rob).ShouldBe(280m, "seven days for four people at ten pounds");
        Target(MeasuredStocks.StoveCartridges, "butane-cartridge-stove", rob).ShouldBe(8m);
        Target(MeasuredStocks.FoodDays, "food-one-month", shipped).ShouldBe(30m);
        Target(MeasuredStocks.BottledWaterLitres, "water-bottled-three-days", shipped).ShouldBe(36m);
        Target(MeasuredStocks.CashPounds, "cash-in-small-notes", shipped).ShouldBe(400m);
    }
}
