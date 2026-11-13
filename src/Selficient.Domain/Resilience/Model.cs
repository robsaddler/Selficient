using Selficient.Domain.Risks;

namespace Selficient.Domain.Resilience;

/// <summary>The things that come into a house from outside and can stop. A closed list: every provider says which it needs.</summary>
public enum Utility
{
    /// <summary>The grid.</summary>
    Electricity,

    /// <summary>Water at the tap.</summary>
    MainsWater,

    /// <summary>Gas at the meter.</summary>
    MainsGas,

    /// <summary>Signal on the phone.</summary>
    MobileNetwork,

    /// <summary>The exchange and the line into the house.</summary>
    FixedInternet,

    /// <summary>Getting out of the street.</summary>
    Roads,

    /// <summary>Shops open and stocked.</summary>
    Shops,

    /// <summary>Petrol and diesel at the forecourt.</summary>
    Fuel,

    /// <summary>Post, parcels and fuel deliveries.</summary>
    Deliveries,

    /// <summary>Prescriptions dispensed.</summary>
    Pharmacy,

    /// <summary>Cash machines and card terminals.</summary>
    Cash,
}

/// <summary>Winter and summer differ in heating, daylight and sunshine; the board is drawn for one at a time.</summary>
public enum Season
{
    /// <summary>October to March.</summary>
    Winter,

    /// <summary>April to September.</summary>
    Summer,
}

/// <summary>How long the disruption lasts. The value is the number of days.</summary>
public enum Horizon
{
    /// <summary>A storm night, a local fault.</summary>
    OneDay = 1,

    /// <summary>The government's ask.</summary>
    ThreeDays = 3,

    /// <summary>A named storm's long tail.</summary>
    OneWeek = 7,

    /// <summary>A severe regional event.</summary>
    TwoWeeks = 14,

    /// <summary>The national planning case.</summary>
    OneMonth = 30,

    /// <summary>A long supply or fuel crisis.</summary>
    ThreeMonths = 90,

    /// <summary>A season of shortages.</summary>
    SixMonths = 180,
}

/// <summary>The ladder of needs. A household climbs it; a disruption knocks it down from the top.</summary>
public enum Rung
{
    /// <summary>Water, food, warmth, health, the people and animals who depend on you.</summary>
    Survive,

    /// <summary>Light, information, contact, cooking, sanitation, cash.</summary>
    Function,

    /// <summary>Cold storage, the heating system, hot water, internet, the car.</summary>
    House,

    /// <summary>Work, laundry, entertainment.</summary>
    Normal,
}

/// <summary>What a household needs each day.</summary>
public enum NeedId
{
    /// <summary>Three litres a person a day.</summary>
    DrinkingWater,

    /// <summary>Meals for everyone.</summary>
    Food,

    /// <summary>One room kept warm, everyone sleeping warm.</summary>
    Warmth,

    /// <summary>Medicines kept up.</summary>
    Health,

    /// <summary>Infants and pets: formula, nappies, pet food.</summary>
    Dependants,

    /// <summary>Rooms lit after dark.</summary>
    Light,

    /// <summary>Knowing what is happening.</summary>
    Information,

    /// <summary>A way to call for help.</summary>
    Contact,

    /// <summary>Hot food and a kettle.</summary>
    Cooking,

    /// <summary>Flushing and washing hands.</summary>
    Sanitation,

    /// <summary>Paying for things when cards fail.</summary>
    Cash,

    /// <summary>The fridge and freezer keeping their contents.</summary>
    ColdStorage,

    /// <summary>The boiler or heat pump running as normal.</summary>
    HeatingSystem,

    /// <summary>Showers and washing up.</summary>
    HotWater,

    /// <summary>The router and the line.</summary>
    Internet,

    /// <summary>The car usable and fuelled.</summary>
    Mobility,

    /// <summary>The laptop, the router and a warm room.</summary>
    WorkingFromHome,

    /// <summary>The washing machine.</summary>
    Laundry,

    /// <summary>Television, games, music.</summary>
    Entertainment,

    /// <summary>Getting upstairs, for a household that does it by a stairlift.</summary>
    Stairs,
}

/// <summary>A need with its rung and its unit.</summary>
public sealed record Need(NeedId Id, Rung Rung, string Name, string Unit);

/// <summary>The catalogue of needs and the order the engine evaluates them in.</summary>
public static class Needs
{
    /// <summary>Every need, rung by rung from Survive down. This is also the order energy is handed out, so the boiler is fed before the hob and the fridge before the television; anything a need depends on (cash for the shops, boiled water for formula) is worked out on demand.</summary>
    public static IReadOnlyList<Need> All { get; } =
    [
        new(NeedId.DrinkingWater, Rung.Survive, "Water to drink", "litres"),
        new(NeedId.Food, Rung.Survive, "Food", "person-days"),
        new(NeedId.Warmth, Rung.Survive, "Warmth", "a warm room"),
        new(NeedId.Health, Rung.Survive, "Medicines", "days"),
        new(NeedId.Dependants, Rung.Survive, "Infants and pets", "days"),
        new(NeedId.Stairs, Rung.Survive, "Getting upstairs", "a way to the bed and the bathroom"),
        new(NeedId.Light, Rung.Function, "Light", "lit hours"),
        new(NeedId.Information, Rung.Function, "Information", "a working radio or phone"),
        new(NeedId.Contact, Rung.Function, "Contact", "a way to call for help"),
        new(NeedId.Cooking, Rung.Function, "Cooking", "hot meals"),
        new(NeedId.Sanitation, Rung.Function, "Sanitation", "litres"),
        new(NeedId.Cash, Rung.Function, "Cash", "days of essentials"),
        new(NeedId.ColdStorage, Rung.House, "Fridge and freezer", "kept cold"),
        new(NeedId.HeatingSystem, Rung.House, "Heating system", "running"),
        new(NeedId.HotWater, Rung.House, "Hot water", "running"),
        new(NeedId.Internet, Rung.House, "Internet", "connected"),
        new(NeedId.Mobility, Rung.House, "The car", "usable"),
        new(NeedId.WorkingFromHome, Rung.Normal, "Working from home", "possible"),
        new(NeedId.Laundry, Rung.Normal, "Laundry", "a wash a week"),
        new(NeedId.Entertainment, Rung.Normal, "Entertainment", "something on"),
    ];

    /// <summary>Looks a need up.</summary>
    public static Need Of(NeedId id) => All.First(need => need.Id == id);
}

/// <summary>A stretch of days in which a utility is up, degraded or off. Availability is 1 for up, 0 for off, between for degraded.</summary>
public readonly record struct UtilityPhase(Utility Utility, int FromDay, int ToDay, decimal Availability)
{
    /// <summary>A phase that lasts to the end of whatever horizon is simulated.</summary>
    public const int ToEnd = int.MaxValue;
}

/// <summary>A thing that can happen to a household, as a timeline of what stops and what comes back.</summary>
public sealed record Scenario(
    string Id,
    string Name,
    string Sentence,
    Season DefaultSeason,
    IReadOnlyList<Horizon> Horizons,
    IReadOnlyList<UtilityPhase> Phases,
    RiskKind? Risk,
    bool BoilWaterNotice,
    bool Heatwave,
    bool ConfinedToHome,
    bool SewersStopWithThePower = false)
{
    /// <summary>How much of a utility the household has on a given day, from 0 to 1. The worst phase covering the day wins.</summary>
    public decimal Availability(Utility utility, int day)
    {
        decimal availability = 1m;
        foreach (UtilityPhase phase in Phases)
        {
            if (phase.Utility == utility && day >= phase.FromDay && day <= phase.ToDay && phase.Availability < availability)
            {
                availability = phase.Availability;
            }
        }

        return availability;
    }

    /// <summary>The longest horizon this scenario is drawn for.</summary>
    public Horizon LongestHorizon => Horizons[^1];
}

/// <summary>How a provider meets a need: so much a day (zero means all of it, scaled by what the utility gives), perhaps only partly, perhaps only when another need holds or another provider is present.</summary>
public sealed record Provision(NeedId Need, decimal PerDay, bool Partial = false, NeedId? RequiresHeld = null, IReadOnlyList<string>? RequiresAnyProvider = null, bool WorksWithoutSewers = false);

/// <summary>What an electrical thing draws: energy a day by season and the burst it needs at once. Hardwired things cannot be plugged into a portable source; the boiler's plug and socket frees the boiler and nothing else, so a thing that is not the boiler's says so.</summary>
public sealed record Load(decimal WinterWhPerDay, decimal SummerWhPerDay, decimal BurstW, bool Hardwired = false, bool BoilerPlugFrees = true)
{
    /// <summary>Energy a day in the season.</summary>
    public decimal WhPerDay(Season season) => season == Season.Winter ? WinterWhPerDay : SummerWhPerDay;
}

/// <summary>A store of the need's unit that is drawn down. Per person when the plan sizes it to the household. Shared when it is counted in days and one day serves every need it meets.</summary>
public sealed record Stock(decimal Quantity, bool PerPerson = false, bool Shared = false);

/// <summary>Something that produces the need's unit each day by season, after a lead time.</summary>
public sealed record Flow(decimal WinterPerDay, decimal SummerPerDay, int LeadTimeDays)
{
    /// <summary>Output a day in the season.</summary>
    public decimal PerDay(Season season) => season == Season.Winter ? WinterPerDay : SummerPerDay;
}

/// <summary>Where an energy store gets refilled from while the grid is off.</summary>
public enum RefillSource
{
    /// <summary>Nothing: what it holds is what there is.</summary>
    None,

    /// <summary>The roof, through a gateway.</summary>
    Roof,

    /// <summary>Folding panels in the garden.</summary>
    Panels,

    /// <summary>Petrol, while the forecourts pump.</summary>
    Fuel,
}

/// <summary>A source of electricity when the grid is off.</summary>
public sealed record EnergyStore(
    decimal StoreWh,
    decimal UsableFraction,
    decimal CeilingW,
    decimal RefillWinterWh,
    decimal RefillSummerWh,
    RefillSource Refill,
    decimal ReserveWh,
    bool WholeHouse)
{
    /// <summary>What can actually be drawn.</summary>
    public decimal UsableWh => StoreWh * UsableFraction;

    /// <summary>Refill a day in the season.</summary>
    public decimal RefillWh(Season season) => season == Season.Winter ? RefillWinterWh : RefillSummerWh;
}

/// <summary>Where a provider can be used, which decides whether a household can have it at all.</summary>
public enum Placement
{
    /// <summary>Anywhere.</summary>
    Anywhere,

    /// <summary>Needs a garden.</summary>
    NeedsGarden,

    /// <summary>Must run outdoors: a garden, or at least not a flat.</summary>
    OutdoorsOnly,

    /// <summary>Needs a roof of your own.</summary>
    NeedsOwnRoof,

    /// <summary>Needs a car.</summary>
    NeedsCar,

    /// <summary>Needs an electric car that can power things.</summary>
    NeedsElectricCarWithOutput,
}

/// <summary>
/// Anything that meets a need: a fixture of the house, a stock, a flow, an energy source. Its id is
/// the plan action's id where the plan sells it, so a tick is a provider held.
/// </summary>
public sealed record Provider(
    string Id,
    string Name,
    string Sentence,
    IReadOnlyList<Provision> Meets,
    IReadOnlyList<Utility> NeedsUtilities,
    Load? Load,
    Stock? Stock,
    Flow? Flow,
    EnergyStore? Store,
    Placement Where,
    string Source)
{
    /// <summary>The provision for a need, if this provider meets it.</summary>
    public Provision? For(NeedId need) => Meets.FirstOrDefault(provision => provision.Need == need);
}

/// <summary>How a need stands on a day or over a horizon, from best to worst.</summary>
public enum CellState
{
    /// <summary>Fully met.</summary>
    Held,

    /// <summary>Met, but by a lesser means: blankets instead of heating, candles instead of lamps.</summary>
    Reduced,

    /// <summary>Some, but not enough: half rations, a boiler running part of the day.</summary>
    Rationed,

    /// <summary>Nothing.</summary>
    Lost,
}
