using static Selficient.Domain.Parameters.Say;

namespace Selficient.Domain.Resilience;

/// <summary>
/// The names and sentences that state a provider's figures, built from the figures. A provider
/// whose words say how much it holds or draws has them made here, for the shipped table and again
/// for any set that corrects it, so the words on the board are never a different number from the
/// arithmetic behind them. A provider with no entry states no figure, and a test keeps that true.
/// </summary>
internal static class ProviderWords
{
    private static readonly Dictionary<string, Func<Provider, DemandFigures, string>> Sentences = new(StringComparer.Ordinal)
    {
        ["own-water-pumped"] = static (p, d) => $"A borehole or well pump needs {Power(p.Load!.BurstW)} to start and is wired in, so in a cut only a whole-house battery lifts the water; the pressure vessel holds a few flushes and no more.",
        ["kettle-water"] = static (p, d) => $"About {Litres(p.Stock!.Quantity)} a person: the kettle, a jug, a few bottles. {Capital(Lasts(p.Stock.Quantity, d.DrinkingLitres))}.",
        ["water-bottled-three-days"] = static (p, d) => Capital($"{Litres(p.Stock!.Quantity)} a person, which is {Lasts(p.Stock.Quantity, d.DrinkingLitres)} of drinking, kept sealed and dark; it has two years of shelf life."),
        ["water-cans"] = static (p, d) => $"Food-grade cans filled from the tap at the clock changes: {Litres(p.Stock!.Quantity)} for drinking and washing.",
        ["bath-liner"] = static (p, d) => $"About {Litres(p.Stock!.Quantity)} from the tap in twenty minutes, for flushing and washing, and for drinking through a filter.",
        ["water-butt"] = static (p, d) => Capital($"about {Litres(p.Stock!.Quantity)} of rain, refilled by the weather at about {Litres(p.Flow!.WinterPerDay)} a day in winter and {Number(p.Flow.SummerPerDay)} in summer, for flushing and the garden; drinking water only through a filter."),
        ["gravity-water-filter"] = static (p, d) => $"Turns bath or butt water into drinking water at about {Litres(p.Meets[0].PerDay)} a day, which is a family's drinking for the length of the stored water.",
        ["fridge-food"] = static (p, d) => Capital($"{Days(p.Stock!.Quantity)} of meals for everyone, which is what most houses have on an ordinary evening."),
        ["food-no-cook-three-days"] = static (p, d) => $"Tins, oats, nuts and biscuits: {Days(p.Stock!.Quantity)} of meals a person, eaten in date order and replaced.",
        ["food-one-month"] = static (p, d) => $"Built by buying one extra of what you already eat; {Days(p.Stock!.Quantity)} of meals a person.",
        ["raised-beds"] = static (p, d) => $"A vegetable patch feeds from about {Days(p.Flow!.LeadTimeDays)} after planting: about {Number(p.Flow.SummerPerDay)} of one person's meals a day in summer, and {Number(p.Flow.WinterPerDay)} in winter.",
        ["allotment"] = static (p, d) => $"A full plot, worked, gives about {Number(p.Flow!.SummerPerDay)} person-days of food a day in the growing season, from {Days(p.Flow.LeadTimeDays)} after planting, and {Number(p.Flow.WinterPerDay)} a day from stored roots in winter.",
        ["electric-hob"] = static (p, d) => $"One ring at a time from a source that can give {Power(p.Load!.BurstW)}, about {Energy(p.Load.WinterWhPerDay)} a day.",
        ["butane-cartridge-stove"] = static (p, d) => $"About two hours of cooking a cartridge; {Number(p.Stock!.Quantity)} cartridges is {Number(p.Stock.Quantity)} days of hot meals. Outdoors, under an open window or in the garage with the door open, never in a closed room.",
        ["microwave"] = static (p, d) => $"A hot meal and a hot drink from a source that can give {Power(p.Load!.BurstW)} for a few minutes at a time.",
        ["cash-at-home"] = static (p, d) => $"What is in wallets and the kitchen drawer: {Days(p.Stock!.Quantity)} of essentials.",
        ["cash-in-small-notes"] = static (p, d) => $"About {Days(p.Stock!.Quantity)} of essentials in notes a shop can change, in the fire box.",
        ["fridge-freezer"] = static (p, d) => $"About {Energy(p.Load!.WinterWhPerDay)} a day and {Power(p.Load.BurstW)} to start, so a source needs the headroom as well as the energy.",
        ["chest-freezer"] = static (p, d) => $"About {Energy(p.Load!.WinterWhPerDay)} a day and {Power(p.Load.BurstW)} to start.",
        ["closed-freezer"] = static (p, d) => $"Kept shut, a fridge is safe for four hours and a full freezer keeps its cold for {Days(p.Stock!.Quantity)}; after that the high-risk food goes.",
        ["gas-boiler"] = static (p, d) => $"The gas is on but the boiler's pump and controls need about {Energy(p.Load!.WinterWhPerDay)} a day in winter, and {Energy(p.Load.SummerWhPerDay)} in summer, from the mains or a source it can be plugged into.",
        ["oil-or-lpg-boiler"] = static (p, d) => $"The tank is yours, but the burner, pump and controls need about {Energy(p.Load!.WinterWhPerDay)} a day in winter, as a gas boiler does.",
        ["heat-pump"] = static (p, d) => $"About {Energy(p.Load!.WinterWhPerDay)} a day in a cold snap: only the car or a generator carries it, and not for long.",
        ["electric-heating"] = static (p, d) => $"Panel or storage heaters: {Energy(p.Load!.WinterWhPerDay)} a winter day for one warm room, which no household source carries beyond a day; in summer only the controls and a towel rail.",
        ["immersion-heater"] = static (p, d) => Capital($"{Energy(p.Load!.WinterWhPerDay)} to heat a tank, at {Power(p.Load.BurstW)}: a whole-house battery or the car, nothing smaller."),
        ["wood-stove"] = static (p, d) => $"Warmth without the mains for as long as the logs last, about {Days(p.Stock!.Quantity)} from a full store, and a kettle on the top.",
        ["supplies-at-home"] = static (p, d) => $"What a house has on an ordinary day: about {Days(p.Stock!.Quantity)}.",
        ["formula-and-nappies"] = static (p, d) => Capital($"{Days(p.Stock!.Quantity)} of nappies and formula. Formula needs boiled water, so this depends on cooking."),
        ["pet-food-two-weeks"] = static (p, d) => Capital($"{Days(p.Stock!.Quantity)} of food, with a bag and a box kept ahead of the one in use."),
        ["stairlift-charger"] = static (p, d) => $"The lift tops its batteries up from an ordinary socket whenever it is parked, from the mains or from any source that can give {Power(p.Load!.BurstW)}; about {Energy(p.Load.WinterWhPerDay)} a day.",
        ["stairlift-batteries"] = static (p, d) => $"About {Days(p.Stock!.Quantity)} of trips to bed and the bathroom on healthy batteries, fewer on a long stair or an old set.",
        ["stairlift-on-the-mains"] = static (p, d) => $"A lift with no batteries of its own is wired in, needs {Power(p.Load!.BurstW)} and stops with the power; only a whole-house battery carries it.",
        ["mains-lighting"] = static (p, d) => $"LED lighting in the rooms in use is about {Energy(p.Load!.WinterWhPerDay)} a winter night.",
        ["phone-torches"] = static (p, d) => $"About {Hours(p.Stock!.Quantity)} before the phones are needed for something else.",
        ["power-failure-lights"] = static (p, d) => $"They switch on when the power drops; about {Hours(p.Stock!.Quantity)} across the house.",
        ["headtorches"] = static (p, d) => $"Hands-free light, about {Hours(p.Stock!.Quantity)} across the household on the cells they come with.",
        ["rechargeable-lanterns"] = static (p, d) => Capital($"about {Hours(p.Stock!.Quantity)} at a useful brightness across the lanterns, and far longer on the low setting."),
        ["candles-and-matches"] = static (p, d) => $"About {Hours(p.Stock!.Quantity)} of light, but a fire risk with children and pets, so it counts as partial.",
        ["phones"] = static (p, d) => Capital($"{Days(p.Stock!.Quantity)} of charge, and a signal only while the masts' batteries last, which is an hour or so."),
        ["phone-charging"] = static (p, d) => Capital($"about {Energy(p.Load!.WinterWhPerDay)} a day keeps a household's phones alive, from the mains, a power bank or any source."),
        ["satellite-internet-on-standby"] = static (p, d) => $"A dish on standby gives internet and calls when the masts and the exchange are down, for about {Energy(p.Load!.WinterWhPerDay)} a day.",
        ["router"] = static (p, d) => $"About {Energy(p.Load!.WinterWhPerDay)} a day, and a line that only works while the exchange has power or battery.",
        ["laptop-and-screen"] = static (p, d) => $"About {Energy(p.Load!.WinterWhPerDay)} a working day, and the internet to go with it.",
        ["no-flush-toilet-kit"] = static (p, d) => $"Bags that line the bowl of the toilet you already have, with a powder that sets the contents solid and holds the smell: {Lasts(p.Stock!.Quantity, d.SanitationLitres)} for everyone, tied off and put in the outside bin.",
        ["petrol-car"] = static (p, d) => $"Half a tank is about {Days(p.Stock!.Quantity)} of local driving, and a 12-volt socket that charges phones.",
        ["car-above-half-a-tank"] = static (p, d) => $"Another {Days(p.Stock!.Quantity)} of driving from a habit, not a purchase.",
        ["electric-car"] = static (p, d) => $"About {Days(p.Stock!.Quantity)} of local driving on an ordinary charge, kept back from powering the house.",
        ["car-above-half-charge"] = static (p, d) => $"Another {Days(p.Stock!.Quantity)} of local driving from a habit, not a purchase.",
        ["ev-charging"] = static (p, d) => Capital($"{Power(p.Load!.BurstW)} from the wall box while the mains are on."),
        ["washing-machine"] = static (p, d) => Capital($"{Energy(p.Load!.WinterWhPerDay)} a wash at {Power(p.Load.BurstW)}, and water."),
        ["television"] = static (p, d) => Capital($"about {Power(p.Load!.BurstW)} for an evening."),
        ["power-banks"] = static (p, d) => $"About {Energy(p.Store!.StoreWh)} between them: phones, headtorches and the radio for days; nothing with a plug.",
        ["portable-power-station"] = static (p, d) => $"About {Energy(p.Store!.StoreWh)} at up to {Power(p.Store.CeilingW)}: the router, lights, phones and a medical device for a day or two; the fridge for a day; the kettle one cup at a time.",
        ["large-power-station"] = static (p, d) => Capital($"about {Energy(p.Store!.StoreWh)} at up to {Power(p.Store.CeilingW)}, and folding panels that put back about {Energy(p.Store.RefillWinterWh)} on a winter day and {Energy(p.Store.RefillSummerWh)} in summer."),
        ["rooftop-solar-and-battery"] = static (p, d) => Capital($"about {Energy(p.Store!.StoreWh)} at {Power(p.Store.CeilingW)} for the whole house, refilled by the roof: about {Energy(p.Store.RefillWinterWh)} on a December day and {Energy(p.Store.RefillSummerWh)} in June."),
        ["ev-output"] = static (p, d) => Capital($"about {Energy(p.Store!.StoreWh)} with {Energy(p.Store.ReserveWh)} kept back to drive, at {Power(p.Store.CeilingW)} through a vehicle-to-load adapter: a fortnight of essentials, and nothing that needs more than that at once."),
        ["petrol-generator"] = static (p, d) => Capital($"{Power(p.Store!.CeilingW)} outdoors, about {Energy(p.Store.StoreWh)} from the can, refilled only while the forecourts pump. Carbon monoxide kills; never in a garage."),
    };

    private static readonly Dictionary<string, Func<Provider, DemandFigures, string>> Names = new(StringComparer.Ordinal)
    {
        ["food-no-cook-three-days"] = static (p, d) => Capital($"{Days(p.Stock!.Quantity)} of food that needs no fridge or cooking"),
        ["food-one-month"] = static (p, d) => Capital($"{Days(p.Stock!.Quantity)} of food"),
        ["formula-and-nappies"] = static (p, d) => Capital($"{Days(p.Stock!.Quantity)} of formula and nappies"),
        ["pet-food-two-weeks"] = static (p, d) => Capital($"{Days(p.Stock!.Quantity)} of pet food and medication"),
        ["portable-power-station"] = static (p, d) => $"A {KilowattHours(p.Store!.StoreWh)} portable power station",
        ["large-power-station"] = static (p, d) => $"A {KilowattHours(p.Store!.StoreWh)} power station with panels",
    };

    /// <summary>Whether the provider's sentence is built from its figures.</summary>
    internal static bool Builds(string providerId) => Sentences.ContainsKey(providerId);

    /// <summary>The provider with its name and sentence said from its figures; the same provider when its words state none.</summary>
    internal static Provider Worded(Provider provider, DemandFigures demand)
    {
        Provider worded = provider;
        if (Sentences.TryGetValue(provider.Id, out Func<Provider, DemandFigures, string>? sentence))
        {
            worded = worded with { Sentence = sentence(provider, demand) };
        }

        if (Names.TryGetValue(provider.Id, out Func<Provider, DemandFigures, string>? name))
        {
            worded = worded with { Name = name(provider, demand) };
        }

        return worded;
    }

    // How long a stock lasts at what a person uses in a day, to a tenth of a day.
    private static string Lasts(decimal quantity, decimal aDay) => Days(Math.Round(quantity / aDay, 1, MidpointRounding.AwayFromZero));
}
