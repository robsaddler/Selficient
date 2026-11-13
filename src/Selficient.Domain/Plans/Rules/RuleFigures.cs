using Selficient.Domain.Measures;
using Selficient.Domain.Parameters;

namespace Selficient.Domain.Plans.Rules;

/// <summary>
/// The figures the plan's rules work from that are not already a provider's: what each action costs,
/// at least and at most, and the handful of quantities the plan asks for. The price of a thing sold
/// by the unit is the price of one, and the rule multiplies it.
/// </summary>
public static class RuleFigures
{
    /// <summary>The size of a water can the plan asks for, in litres.</summary>
    public const string WaterCanLitres = "plan.water-can-litres";

    /// <summary>The size of the bottle the plan counts bottled water in, in litres.</summary>
    public const string BottleLitres = "plan.bottle-litres";

    /// <summary>The days of a water filter's output the headline figure counts.</summary>
    public const string WaterFilterDays = "plan.water-filter-days";

    private const string PriceGroup = "What things cost";
    private const string PlanGroup = "What the plan asks for";
    private const string Seen = "Shop prices seen at the catalogue's last review";

    // Each action that costs money: what it is, in words, and what one costs at least and at most.
    private static readonly (string Action, string What, decimal Low, decimal High)[] Prices =
    [
        ("water-bottled-three-days", "A litre of sealed bottled water", 0.35m, 0.5m),
        ("food-no-cook-three-days", "Food that needs no fridge or cooking, for one person", 8m, 12m),
        ("power-failure-lights", "A plug-in power-failure torch", 20m, 24m),
        ("headtorches", "A headtorch", 14m, 28m),
        ("power-banks", "A power bank", 47m, 79m),
        ("radio-dab-fm-with-crank", "A radio with FM and a wind-up handle", 30m, 150m),
        ("no-flush-toilet-kit", "Toilet liners and gelling powder", 10m, 34m),
        ("landline-battery-backup", "A battery backup for the router", 0m, 62m),
        ("safety-kit", "A carbon monoxide alarm, fire blanket and first-aid kit", 60m, 80m),
        ("candles-and-matches", "Tea lights, candles, lighters and matches", 10m, 15m),
        ("arborist-survey", "An arborist's survey of a tree", 150m, 300m),
        ("tree-work-if-advised", "The tree work an arborist advises", 0m, 1500m),
        ("garden-firebreak-and-hose", "A firebreak, a hose and a garden fire blanket", 50m, 150m),
        ("car-above-half-a-tank", "A 12 V inverter for the car", 25m, 37m),
        ("formula-and-nappies", "Nappies, wipes and formula for one infant", 25m, 70m),
        ("downstairs-in-a-power-cut", "A commode, and a bed downstairs if there is none", 30m, 120m),
        ("flood-protection-kit", "Air-brick covers, door guards and flood barriers", 100m, 400m),
        ("flood-water-alarm", "A battery water alarm", 8m, 25m),
        ("water-cans", "A food-grade water can with a tap", 12m, 18m),
        ("water-purifying-tablets", "Water-purifying tablets for the cans", 8m, 8m),
        ("bath-liner", "A bath liner", 15m, 42m),
        ("water-butt", "A water butt", 50m, 80m),
        ("food-one-month", "A month of food for one person", 35m, 60m),
        ("pet-food-two-weeks", "Pet food and medication kept ahead", 15m, 60m),
        ("long-reach-lighter-for-gas-hob", "A long-reach lighter", 3m, 6m),
        ("flasks-for-hot-food", "Two large vacuum flasks", 50m, 95m),
        ("butane-cartridge-stove", "A cartridge stove and its cartridges", 60m, 100m),
        ("wood-stove-ready", "A chimney sweep", 75m, 250m),
        ("rechargeable-lanterns", "A rechargeable lantern", 36m, 90m),
        ("fire-box-for-cash-and-documents", "A fire-rated box", 45m, 55m),
        ("rooftop-solar-and-battery", "Rooftop solar with a battery and backup", 9000m, 17000m),
        ("portable-power-station", "A portable power station with a folding panel", 720m, 1250m),
        ("boiler-plug-and-socket", "An electrician fitting a plug and socket to the boiler", 100m, 200m),
        ("heating-fallback", "Sleeping bags for one warm room", 0m, 150m),
        ("gravity-water-filter", "A gravity water filter", 160m, 180m),
        ("satellite-internet-on-standby", "A satellite internet kit", 179m, 200m),
    ];

    /// <summary>The rules' own figures as parameters: the prices, then the quantities.</summary>
    public static IReadOnlyList<ParameterDefinition> Definitions { get; } =
    [
        .. Prices.SelectMany(static price => new[]
        {
            new ParameterDefinition($"price.{price.Action}.low", PriceGroup, $"{price.What}: the least it costs", "pounds", price.Low, 0m, Most(price.High), Seen, $"In the plan, as the cost of \"{price.Action}\""),
            new ParameterDefinition($"price.{price.Action}.high", PriceGroup, $"{price.What}: the most it costs", "pounds", price.High, 0m, Most(price.High), Seen, $"In the plan, as the cost of \"{price.Action}\""),
        }),
        new(WaterCanLitres, PlanGroup, "The size of a water can the plan asks for", "litres", 25m, 5m, 50m, "The common food-grade can", "In the plan, in the water cans action and the figure a household gives for them"),
        new(BottleLitres, PlanGroup, "The size of the bottle the plan counts bottled water in", "litres", 2m, 0.5m, 10m, "The large supermarket bottle", "In the plan, in the bottled water action"),
        new(WaterFilterDays, PlanGroup, "Days of a water filter's output the headline figure counts", "whole days", 14m, 1m, 90m, "Plan", "In the plan, in what the water filter adds to the headline figure") { WholeNumber = true },
        new(LightHours("power-failure-lights"), PlanGroup, "Hours of light one plug-in power-failure torch adds to the headline figure", "hours", 6m, 0m, 200m, "Makers' run times", "In the plan, in the headline figure"),
        new(LightHours("headtorches"), PlanGroup, "Hours of light one headtorch adds to the headline figure", "hours", 20m, 0m, 200m, "Makers' run times", "In the plan, in the headline figure"),
        new(LightHours("rechargeable-lanterns"), PlanGroup, "Hours of light one lantern adds to the headline figure", "hours", 60m, 0m, 500m, "Makers' run times at a useful brightness", "In the plan, in the headline figure"),
        new(LightHours("candles-and-matches"), PlanGroup, "Hours of light the candles add to the headline figure", "hours", 10m, 0m, 200m, "Plan", "In the plan, in the headline figure"),
        new(LightHours("portable-power-station"), PlanGroup, "Hours of light a power station adds to the headline figure", "hours", 100m, 0m, 1000m, "Plan", "In the plan, in the headline figure"),
    ];

    /// <summary>The id of the figure for the hours of light an action adds to the headline figure.</summary>
    public static string LightHours(string action) => $"plan.light-hours.{action}";

    /// <summary>What an action costs under these figures: the price of one, as many times as the household needs.</summary>
    internal static MoneyRange Price(ParameterSet figures, string action, decimal times = 1m) =>
        MoneyRange.Pounds(figures[$"price.{action}.low"] * times, figures[$"price.{action}.high"] * times);

    /// <summary>A sentence when the values contradict each other, or null: nothing costs more at its least than at its most.</summary>
    internal static string? Contradiction(IReadOnlyDictionary<string, decimal> values)
    {
        foreach ((string action, string what, _, _) in Prices)
        {
            if (values[$"price.{action}.low"] > values[$"price.{action}.high"])
            {
                return $"{what} cannot cost more at its least than at its most.";
            }
        }

        return null;
    }

    private static decimal Most(decimal shipped) => Math.Max(100m, shipped * 10m);
}
