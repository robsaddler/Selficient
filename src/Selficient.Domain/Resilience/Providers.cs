namespace Selficient.Domain.Resilience;

/// <summary>
/// Everything that meets a need, with its figures and where they came from. Order matters: for
/// each need the engine tries providers in this order, so the free and the mains-fed come first,
/// stocks next, and anything that burns stored energy last. Ids match plan action ids where the
/// plan sells the thing, so a tick is a provider held; the rest are fixtures of the house or new
/// things the plan will sell once the board exists.
/// </summary>
public static class Providers
{
    private const string Decc = "DECC Household Electricity Survey 2013, measured daily energy";
    private const string Cse = "Centre for Sustainable Energy appliance table, July 2026";
    private const string Fsa = "Food Standards Agency, food safety in a power cut";
    private const string Manuals = "Vaillant, Worcester Bosch and Ideal boiler manuals; Prepared Britain";
    private const string Nesta = "Nesta and ukelectricityprices.co.uk on heat pump daily use";
    private const string Vendors = "EcoFlow, Bluetti, Jackery and Anker specifications, 2026";
    private const string Ofcom = "Ofcom resilience guidance on mast and exchange batteries";
    private const string Cmo = "UK Chief Medical Officers, water and sanitation advice for a national power outage, December 2025";
    private const string Rhs = "Royal Horticultural Society yields; a 250 square metre plot feeds a family a third of its vegetables";

    private static readonly IReadOnlyList<Utility> Nothing = [];

    /// <summary>Every provider, in evaluation order.</summary>
    public static IReadOnlyList<Provider> All { get; } =
    [
        // Water. The tap first, then what is stored, then what can be made drinkable.
        Fixture("mains-tap", "The tap", "Mains water usually stays on in a power cut, because the pumping stations have their own backup. A flat above the third floor relies on the block's pumps, which do not.", [All_(NeedId.DrinkingWater), All_(NeedId.Sanitation)], [Utility.MainsWater], null, "Water UK"),
        Stocked("kettle-water", "What is in the kettle and the fridge door", "About a litre and a half a person: the kettle, a jug, a few bottles. Half a day.", [All_(NeedId.DrinkingWater)], new Stock(1.5m, PerPerson: true), Placement.Anywhere, "Be Ready baseline"),
        Stocked("water-bottled-three-days", "Sealed bottled water", "Three litres a person a day for three days, kept sealed and dark, which is two years of shelf life.", [All_(NeedId.DrinkingWater)], new Stock(9m, PerPerson: true), Placement.Anywhere, "Be Ready: 3 litres a person a day"),
        Stocked("water-cans", "Filled water cans", "Four 25-litre food-grade cans filled from the tap at the clock changes, a hundred litres for drinking and washing.", [All_(NeedId.DrinkingWater), All_(NeedId.Sanitation)], new Stock(100m), Placement.Anywhere, "Plan action"),
        Stocked("bath-liner", "A bath liner filled when the warning comes", "About 380 litres from the tap in twenty minutes, for flushing and washing, and for drinking through a filter.", [All_(NeedId.Sanitation)], new Stock(380m), Placement.Anywhere, "Plan action"),
        new("water-butt", "The water butt", "Two hundred litres of rain, refilled by the weather, for flushing and the garden; drinking water only through a filter.", [All_(NeedId.Sanitation)], Nothing, null, new Stock(210m), new Flow(8m, 3m, 0), null, Placement.NeedsGarden, "Met Office average rainfall on a 20 square metre roof"),
        new("gravity-water-filter", "A gravity water filter", "Turns bath or butt water into drinking water at about twenty litres a day, which is a family's drinking for the length of the stored water.", [new Provision(NeedId.DrinkingWater, 20m, RequiresAnyProvider: ["bath-liner", "water-butt", "water-cans"])], Nothing, null, null, null, null, Placement.Anywhere, "Manufacturer flow rates, ceramic and carbon gravity filters"),

        // Food. The shops while they are open and you can pay, then what is in the house, then what the garden gives.
        new("shops", "The shops", "While they are open, reachable and able to take payment. In a power cut most take cash only.", [new Provision(NeedId.Food, 0m, RequiresHeld: NeedId.Cash), new Provision(NeedId.Dependants, 0m, RequiresHeld: NeedId.Cash)], [Utility.Shops, Utility.Roads], null, null, null, null, Placement.Anywhere, "Observed in Storm Arwen and Storm Éowyn"),
        new("deliveries", "Deliveries", "Groceries and parcels keep arriving while the roads are open, the vans have fuel and you can get online to order, slower when everyone is ordering.", [new Provision(NeedId.Food, 0m, RequiresHeld: NeedId.Internet), new Provision(NeedId.Dependants, 0m, RequiresHeld: NeedId.Internet)], [Utility.Deliveries, Utility.Roads], null, null, null, null, Placement.Anywhere, "Plan"),
        Stocked("fridge-food", "What is in the fridge and the cupboard", "Half a day of meals for everyone, which is what most houses have on an ordinary evening.", [All_(NeedId.Food)], new Stock(0.5m, PerPerson: true), Placement.Anywhere, "Be Ready baseline"),
        Stocked("food-no-cook-three-days", "Three days of food that needs no fridge or cooking", "Tins, oats, nuts and biscuits: three days of meals a person, eaten in date order and replaced.", [All_(NeedId.Food)], new Stock(3m, PerPerson: true), Placement.Anywhere, "Be Ready"),
        Stocked("food-one-month", "A month of food", "Built by buying one extra of what you already eat; a month of meals a person.", [All_(NeedId.Food)], new Stock(30m, PerPerson: true), Placement.Anywhere, "Plan action"),
        new("raised-beds", "Raised beds", "A vegetable patch feeds from early summer, about a quarter of one person's meals a day at its best, and nothing in winter.", [All_(NeedId.Food)], Nothing, null, null, new Flow(0m, 0.25m, 60), null, Placement.NeedsGarden, Rhs),
        new("allotment", "An allotment", "A full plot, worked, feeds a family about a third of its vegetables over the year: roughly a person-day of food a day in the growing season from three months after planting, and a few stored roots in winter.", [All_(NeedId.Food)], Nothing, null, null, new Flow(0.3m, 1.3m, 90), null, Placement.Anywhere, Rhs),

        // Cooking. Gas works with a match; electric needs the mains or a big source; a stove outside.
        Fixture("gas-hob", "The gas hob", "A gas hob lights with a match when the power is off; the gas supply almost never fails with it.", [All_(NeedId.Cooking)], [Utility.MainsGas], null, "Gas Networks"),
        Fixture("electric-hob", "The electric hob", "One ring at a time from a source that can give two kilowatts, about a kilowatt-hour a meal.", [All_(NeedId.Cooking)], Nothing, new Load(1000m, 1000m, 2000m), Cse),
        Stocked("butane-cartridge-stove", "A butane cartridge stove", "About two hours of cooking a cartridge; ten cartridges is ten days of hot meals. Outdoors, under an open window or in the garage with the door open, never in a closed room.", [All_(NeedId.Cooking)], new Stock(10m), Placement.OutdoorsOnly, "Plan action"),
        Fixture("microwave", "The microwave and the kettle", "A hot meal and a hot drink from a source that can give 1.2 kilowatts for a few minutes at a time.", [All_(NeedId.Cooking)], Nothing, new Load(400m, 400m, 1200m), Cse),

        // Cash. The machines while they work, then the notes in the house.
        Fixture("cash-machines", "Cash machines and card terminals", "Cards and cash machines stop with the power and with the network; shops that stay open take cash.", [All_(NeedId.Cash)], [Utility.Cash], null, "Observed in Storm Arwen"),
        Stocked("cash-at-home", "The cash in the house", "What is in wallets and the kitchen drawer: a couple of days of essentials.", [All_(NeedId.Cash)], new Stock(2m), Placement.Anywhere, "Be Ready baseline"),
        Stocked("cash-in-small-notes", "Cash in small notes", "About ten days of essentials in notes a shop can change, in the fire box.", [All_(NeedId.Cash)], new Stock(10m), Placement.Anywhere, "Plan action"),

        // Cold storage. The fridge-freezer on the mains or a source, the chest freezer likewise, and the cold a shut freezer keeps.
        Fixture("fridge-freezer", "The fridge-freezer", "About 1.2 kilowatt-hours a day and a 600-watt start, so a source needs the headroom as well as the energy.", [All_(NeedId.ColdStorage)], Nothing, new Load(1170m, 1170m, 600m), Decc),
        Fixture("chest-freezer", "The chest freezer", "About a kilowatt-hour a day; a full one keeps its cold for two days shut.", [All_(NeedId.ColdStorage)], Nothing, new Load(990m, 990m, 500m), Decc),
        Stocked("closed-freezer", "A shut fridge and freezer", "Kept shut, a fridge is safe for four hours and a full freezer for 48; after that the high-risk food goes.", [Part(NeedId.ColdStorage)], new Stock(2m), Placement.Anywhere, Fsa),

        // Warmth and the heating system. The system first, then what stands in for it, then blankets.
        Fixture("gas-boiler", "The gas boiler", "The gas is on but the boiler's pump and controls need about 1.2 kilowatt-hours a day in winter from the mains or a source it can be plugged into.", [All_(NeedId.Warmth), All_(NeedId.HeatingSystem), All_(NeedId.HotWater)], [Utility.MainsGas], new Load(1200m, 300m, 150m, Hardwired: true), Manuals),
        Fixture("oil-or-lpg-boiler", "The oil or LPG boiler", "The tank is yours, but the burner, pump and controls need electricity like a gas boiler does.", [All_(NeedId.Warmth), All_(NeedId.HeatingSystem), All_(NeedId.HotWater)], Nothing, new Load(1200m, 300m, 150m, Hardwired: true), Manuals),
        Fixture("heat-pump", "The heat pump", "Sixteen to twenty-five kilowatt-hours a day in a cold snap: only the car or a generator carries it, and not for long.", [All_(NeedId.Warmth), All_(NeedId.HeatingSystem), All_(NeedId.HotWater)], Nothing, new Load(20000m, 3000m, 3000m, Hardwired: true), Nesta),
        Fixture("electric-heating", "Electric heating", "Panel or storage heaters: sixteen kilowatt-hours a winter day for one warm room, which no household source carries beyond a day; in summer only the controls and a towel rail.", [All_(NeedId.Warmth), All_(NeedId.HeatingSystem)], Nothing, new Load(16000m, 200m, 2000m, Hardwired: true), Cse),
        Fixture("immersion-heater", "The immersion heater", "Three kilowatt-hours to heat a tank, at three kilowatts: a whole-house battery or the car, nothing smaller.", [All_(NeedId.HotWater)], Nothing, new Load(3000m, 3000m, 3000m, Hardwired: true), Cse),
        Stocked("heating-fallback", "A portable gas heater and its cylinder", "One room kept warm for about a week on a cylinder, with the carbon monoxide alarm fitted and a window cracked.", [All_(NeedId.Warmth)], new Stock(7m), Placement.Anywhere, "Plan action; Calor"),
        Fixture("log-deliveries", "The log merchant", "While the roads are open and the merchant delivers, the log store is kept up.", [All_(NeedId.Warmth), All_(NeedId.HeatingSystem)], [Utility.Deliveries, Utility.Roads], null, "Plan"),
        new("wood-stove", "The wood stove and the log store", "Warmth without the mains for as long as the logs last, about three weeks from a full store, and a kettle on the top.", [All_(NeedId.Warmth), All_(NeedId.HeatingSystem), Part(NeedId.Cooking)], Nothing, null, new Stock(21m, Shared: true), null, null, Placement.Anywhere, "Stove Industry Alliance"),
        Fixture("blankets", "Blankets, duvets and one room", "Everyone warm enough in one closed room under duvets. Not enough for a baby or someone frail, which is why the board scores it lost for them.", [Part(NeedId.Warmth)], Nothing, null, "UKHSA cold weather guidance"),

        // Health and dependants. The pharmacy while it dispenses, then the stock in the house.
        Fixture("pharmacy", "The pharmacy", "Prescriptions while the pharmacy is open, stocked and reachable.", [All_(NeedId.Health)], [Utility.Pharmacy, Utility.Roads], null, "Plan"),
        Stocked("medicine-stock", "The medicines in the house", "Regular medicines for as many days as there are in the cupboard.", [All_(NeedId.Health)], new Stock(7m), Placement.Anywhere, "Household's own figure"),
        Stocked("supplies-at-home", "Formula, nappies and pet food in the house", "What a house has on an ordinary day: about three days.", [All_(NeedId.Dependants)], new Stock(3m), Placement.Anywhere, "Be Ready baseline"),
        Stocked("formula-and-nappies", "Two weeks of formula and nappies", "Formula needs boiled water, so this depends on cooking.", [All_(NeedId.Dependants)], new Stock(14m), Placement.Anywhere, "NHS infant feeding"),
        Stocked("pet-food-two-weeks", "Two weeks of pet food and medication", "A bag and a box kept ahead of the one in use.", [All_(NeedId.Dependants)], new Stock(14m), Placement.Anywhere, "Plan"),

        // Light. The mains or a source, then what is stored in cells.
        Fixture("mains-lighting", "The lights", "LED lighting in the rooms in use is a few hundred watt-hours a night.", [All_(NeedId.Light)], Nothing, new Load(200m, 100m, 60m), Cse),
        Stocked("phone-torches", "The torches on the phones", "A couple of hours before the phones are needed for something else.", [All_(NeedId.Light)], new Stock(2.5m), Placement.Anywhere, "Be Ready baseline"),
        Stocked("power-failure-lights", "Plug-in power-failure torches", "They switch on when the power drops and give a few hours each; about sixteen hours across the house.", [All_(NeedId.Light)], new Stock(16m), Placement.Anywhere, "Plan action"),
        Stocked("headtorches", "Headtorches", "Hands-free light, about forty hours across the household on the cells they come with.", [All_(NeedId.Light)], new Stock(40m), Placement.Anywhere, "Plan action"),
        Stocked("rechargeable-lanterns", "Rechargeable LED lanterns", "Two hundred hours on the low setting across three lanterns; sixty at a useful brightness.", [All_(NeedId.Light)], new Stock(60m), Placement.Anywhere, "Plan action"),
        Stocked("candles-and-matches", "Candles and matches", "Light, but a fire risk with children and pets, so it counts as partial.", [Part(NeedId.Light)], new Stock(40m), Placement.Anywhere, "Plan action"),

        // Information and contact. The radio always; the phones while the masts stand; the line while the exchange stands.
        Fixture("radio-dab-fm-with-crank", "The wind-up radio", "Local radio carries the emergency information and needs nothing from anyone.", [All_(NeedId.Information)], Nothing, null, "Plan action"),
        new("phones", "The phones on their own batteries", "A day of charge, and a signal only while the masts' batteries last, which is an hour or so.", [All_(NeedId.Information), All_(NeedId.Contact)], [Utility.MobileNetwork], null, new Stock(1m, Shared: true), null, null, Placement.Anywhere, Ofcom),
        Fixture("phone-charging", "Phones kept charged", "Fifty watt-hours a day keeps a household's phones alive, from the mains, a power bank or any source.", [All_(NeedId.Information), All_(NeedId.Contact)], [Utility.MobileNetwork], new Load(50m, 50m, 20m), "Apple and Samsung battery capacities"),
        Fixture("landline", "The landline", "Digital voice lines die with the power at the router, and the copper network is being switched off; a corded phone is no longer a backup.", [All_(NeedId.Contact)], [Utility.FixedInternet], new Load(30m, 30m, 15m), "Ofcom PSTN switch-off"),
        Fixture("satellite-internet-on-standby", "Satellite internet", "A dish on standby gives internet and calls when the masts and the exchange are down, for about 1.4 kilowatt-hours a day.", [All_(NeedId.Internet), All_(NeedId.Contact), All_(NeedId.Information)], Nothing, new Load(1400m, 1400m, 75m), "Starlink standard kit"),

        // Internet and work.
        Fixture("router", "The router", "Ten watts, and a line that only works while the exchange has power or battery.", [All_(NeedId.Internet)], [Utility.FixedInternet], new Load(300m, 300m, 15m), Cse),
        Fixture("laptop-and-screen", "The laptop and a screen", "About four hundred watt-hours a working day, and the internet to go with it.", [new Provision(NeedId.WorkingFromHome, 0m, RequiresHeld: NeedId.Internet)], Nothing, new Load(400m, 400m, 80m), Cse),

        // Sanitation is water while the sewers run: the tap, the cans, the liner and the butt are above. When
        // they do not, it is a bag: the bin bags every house has, which is the Chief Medical Officers' own
        // advice and counts as partial, and the kit made for the job, which counts in full. The kit is tried
        // first, so the board names it while it lasts and the bin bags only after it has run out.
        new("no-flush-toilet-kit", "Toilet liners and gelling powder", "Bags that line the bowl of the toilet you already have, with a powder that sets the contents solid and holds the smell: three days for everyone, tied off and put in the outside bin.", [new Provision(NeedId.Sanitation, 0m, WorksWithoutSewers: true)], Nothing, null, new Stock(36m, PerPerson: true), null, null, Placement.Anywhere, Cmo),
        new("bin-bags", "Bin bags and the outside bin", "The official advice when the toilet cannot be flushed: collect it in a plastic bag, seal it, and put it in the outside bin. It works, and nobody would call it comfortable.", [new Provision(NeedId.Sanitation, 0m, Partial: true, WorksWithoutSewers: true)], Nothing, null, null, null, null, Placement.Anywhere, Cmo),

        // The car.
        new("petrol-car", "The car and what is in its tank", "Half a tank is about a week of local driving, and a 12-volt socket that charges phones.", [All_(NeedId.Mobility)], [Utility.Roads], null, new Stock(7m), null, null, Placement.NeedsCar, "Plan action"),
        Stocked("car-above-half-a-tank", "A tank kept above half", "Another week of driving from a habit, not a purchase.", [All_(NeedId.Mobility)], new Stock(7m), Placement.NeedsCar, "Plan action"),
        Fixture("forecourt", "The forecourt", "While the pumps have power and the tanker has come.", [All_(NeedId.Mobility)], [Utility.Fuel, Utility.Roads], null, "Plan"),
        new("electric-car", "The electric car on its charge", "About five days of local driving on an ordinary charge, kept back from powering the house.", [All_(NeedId.Mobility)], [Utility.Roads], null, new Stock(5m), null, null, Placement.NeedsCar, "ev-database.org ranges"),
        Fixture("ev-charging", "Charging the car", "Seven kilowatts from the wall box while the mains are on.", [All_(NeedId.Mobility)], [Utility.Roads], new Load(7000m, 7000m, 7000m, Hardwired: true), "Plan"),

        // Laundry and entertainment.
        Fixture("washing-machine", "The washing machine", "A kilowatt-hour a wash at two kilowatts, and water.", [All_(NeedId.Laundry)], [Utility.MainsWater], new Load(1000m, 1000m, 2000m), Cse),
        Fixture("television", "The television", "A hundred watts for an evening.", [All_(NeedId.Entertainment)], Nothing, new Load(300m, 300m, 100m), Cse),
        Fixture("books-and-games", "Books, cards and board games", "Entertainment that needs nothing, which is why it counts as partial rather than lost.", [Part(NeedId.Entertainment)], Nothing, null, "Plan"),

        // Energy sources. They meet nothing directly; they carry the loads above when the grid is off.
        Source("power-banks", "Two power banks", "About 180 watt-hours between them: phones, headtorches and the radio for days; nothing with a plug.", new EnergyStore(180m, 1m, 20m, 0m, 0m, RefillSource.None, 0m, WholeHouse: false), Placement.Anywhere, Vendors),
        Source("portable-power-station", "A 1 kWh portable power station", "About a kilowatt-hour at up to 1,800 watts: the router, lights, phones and a medical device for a day or two; the fridge for a day; the kettle one cup at a time.", new EnergyStore(1024m, 0.85m, 1800m, 0m, 0m, RefillSource.None, 0m, WholeHouse: false), Placement.Anywhere, Vendors),
        Source("large-power-station", "A 3 kWh power station with panels", "Three kilowatt-hours at up to 3,600 watts, and folding panels that put back about half a kilowatt-hour on a winter day and two in summer.", new EnergyStore(3072m, 0.85m, 3600m, 600m, 2000m, RefillSource.Panels, 0m, WholeHouse: false), Placement.Anywhere, Vendors),
        Source("rooftop-solar-and-battery", "Rooftop solar with a battery and a backup gateway", "Thirteen and a half kilowatt-hours at five kilowatts for the whole house, refilled by the roof: about three kilowatt-hours on a December day and twenty in June on five kilowatts of panels.", new EnergyStore(13500m, 0.9m, 5000m, 3000m, 20000m, RefillSource.Roof, 0m, WholeHouse: true), Placement.NeedsOwnRoof, "PVGIS for South Wales; GivEnergy and Tesla specifications"),
        Source("ev-output", "The car, powering the house", "Sixty kilowatt-hours with twenty kept back to drive, at three kilowatts through a vehicle-to-load adapter: a fortnight of essentials, nothing that needs more than three kilowatts at once.", new EnergyStore(60000m, 1m, 3000m, 0m, 0m, RefillSource.None, 20000m, WholeHouse: false), Placement.NeedsElectricCarWithOutput, "Hyundai, Kia and MG vehicle-to-load specifications"),
        Source("petrol-generator", "A petrol generator and twenty litres", "Three kilowatts outdoors, about forty-four kilowatt-hours from twenty litres, refilled only while the forecourts pump. Carbon monoxide kills; never in a garage.", new EnergyStore(44000m, 1m, 3000m, 44000m, 44000m, RefillSource.Fuel, 0m, WholeHouse: false), Placement.OutdoorsOnly, "Honda and Champion specifications; 2.2 kWh a litre delivered"),

        // Enablers. They meet nothing and carry nothing; they let something else be carried.
        new("boiler-plug-and-socket", "The boiler on a plug and socket", "An electrician puts the boiler on a plug so it can run from a power station or the car in a cut. Without it, only a whole-house battery or the mains can run it.", [], Nothing, null, null, null, null, Placement.Anywhere, "Plan action; Gas Safe"),
    ];

    /// <summary>Looks a provider up, or null.</summary>
    public static Provider? Find(string id) => All.FirstOrDefault(provider => provider.Id == id);

    private static Provision All_(NeedId need) => new(need, 0m);

    private static Provision Part(NeedId need) => new(need, 0m, Partial: true);

    private static Provider Fixture(string id, string name, string sentence, IReadOnlyList<Provision> meets, IReadOnlyList<Utility> utilities, Load? load, string source) =>
        new(id, name, sentence, meets, utilities, load, null, null, null, Placement.Anywhere, source);

    private static Provider Stocked(string id, string name, string sentence, IReadOnlyList<Provision> meets, Stock stock, Placement where, string source) =>
        new(id, name, sentence, meets, Nothing, null, stock, null, null, where, source);

    private static Provider Source(string id, string name, string sentence, EnergyStore store, Placement where, string source) =>
        new(id, name, sentence, [], Nothing, null, null, null, store, where, source);
}
