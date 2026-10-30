using Selficient.Domain.Risks;

namespace Selficient.Domain.Resilience;

/// <summary>
/// The things that happen to British houses, as timelines of what stops and what comes back.
/// Availability is what the household gets of each utility: 1 is normal, 0 is off, between is
/// rationed, intermittent or out of reach. Sources are the National Risk Register 2025, the
/// Ofcom resilience guidance on mast and exchange batteries, the Energy Networks Association's
/// storm reports, and the Food Standards Agency.
/// </summary>
public static class Scenarios
{
    private const int End = UtilityPhase.ToEnd;

    /// <summary>A storm takes the power out, in any season. Masts last hours, exchanges run on battery, cash machines and forecourts stop, shops take cash only.</summary>
    public static Scenario Storm { get; } = new(
        "storm",
        "A storm takes the power out",
        "Overhead lines come down in the wind and the power is off for the length of the cut. Mobile masts have an hour or so of battery, the exchange a few hours more, cash machines and card terminals stop with the power, and the forecourt pumps do too.",
        Season.Winter,
        [Horizon.OneDay, Horizon.ThreeDays, Horizon.OneWeek, Horizon.TwoWeeks, Horizon.OneMonth],
        [
            new(Utility.Electricity, 1, End, 0m),
            new(Utility.MobileNetwork, 1, 1, 0.5m),
            new(Utility.MobileNetwork, 2, End, 0m),
            new(Utility.FixedInternet, 1, End, 0.5m),
            new(Utility.Cash, 1, End, 0m),
            new(Utility.Shops, 1, End, 0.5m),
            new(Utility.Fuel, 1, End, 0m),
            new(Utility.Roads, 1, 1, 0.5m),
            new(Utility.Deliveries, 1, End, 0.5m),
        ],
        RiskKind.StormPowerCut,
        BoilWaterNotice: false,
        Heatwave: false,
        ConfinedToHome: false);

    /// <summary>The water stops: a burst main or a treatment problem. The power is on; bottled water sells out in hours; a boil notice follows the repair.</summary>
    public static Scenario WaterCut { get; } = new(
        "water",
        "The water stops",
        "A burst main or a treatment problem stops the supply for three days. The power is on, so everything electrical works, but the taps are dry, the toilets do not flush, and the shops sell out of bottled water in the first hours. When the supply returns on the fourth day it comes with a boil notice, so the tap is only drinking water once it has been through the kettle.",
        Season.Winter,
        [Horizon.OneDay, Horizon.ThreeDays, Horizon.OneWeek],
        [
            new(Utility.MainsWater, 1, 3, 0m),
            new(Utility.Shops, 1, End, 0.7m),
        ],
        RiskKind.WaterInterruption,
        BoilWaterNotice: true,
        Heatwave: false,
        ConfinedToHome: false);

    /// <summary>Snow or ice closes the road. Nothing comes in or out; the power is at risk from laden lines.</summary>
    public static Scenario Snow { get; } = new(
        "snow",
        "Snow closes the road",
        "The road out of the valley is lost to snow or ice. The shops, the pharmacy and the forecourt are there but you cannot reach them, deliveries stop, and heavy snow on the lines makes the power flicker.",
        Season.Winter,
        [Horizon.OneDay, Horizon.ThreeDays, Horizon.OneWeek, Horizon.TwoWeeks],
        [
            new(Utility.Roads, 1, End, 0m),
            new(Utility.Shops, 1, End, 0m),
            new(Utility.Pharmacy, 1, End, 0m),
            new(Utility.Fuel, 1, End, 0m),
            new(Utility.Deliveries, 1, End, 0m),
            new(Utility.Electricity, 1, 3, 0.8m),
        ],
        RiskKind.WinterAccess,
        BoilWaterNotice: false,
        Heatwave: false,
        ConfinedToHome: false);

    /// <summary>A fortnight of regional disruption: a cyber attack on a utility or a fuel shortage. Three days dark, then rolling cuts; shops half-stocked; no fuel, no cash machines.</summary>
    public static Scenario Regional { get; } = new(
        "regional",
        "A fortnight of regional disruption",
        "A cyber attack on a utility or a fuel shortage. The power is off for three days and then comes and goes, the shops are half-stocked and queueing, there is no fuel at the forecourts and the cash machines are down. Phones and the internet limp.",
        Season.Winter,
        [Horizon.OneDay, Horizon.ThreeDays, Horizon.OneWeek, Horizon.TwoWeeks],
        [
            new(Utility.Electricity, 1, 3, 0m),
            new(Utility.Electricity, 4, End, 0.8m),
            new(Utility.MobileNetwork, 1, 3, 0.5m),
            new(Utility.MobileNetwork, 4, End, 0.8m),
            new(Utility.FixedInternet, 1, 3, 0.5m),
            new(Utility.FixedInternet, 4, End, 0.8m),
            new(Utility.Shops, 1, End, 0.4m),
            new(Utility.Fuel, 1, End, 0m),
            new(Utility.Cash, 1, End, 0m),
            new(Utility.Pharmacy, 1, End, 0.6m),
        ],
        RiskKind.RegionalDisruption,
        BoilWaterNotice: false,
        Heatwave: false,
        ConfinedToHome: false);

    /// <summary>Months of shortages: a long supply-chain or fuel crisis, a war footing. Shops rationed, fuel scarce, planned cuts from the second week, pharmacies substituting.</summary>
    public static Scenario Shortages { get; } = new(
        "shortages",
        "Months of shortages",
        "A long supply-chain or fuel crisis. The shops are open but rationed and gappy, fuel is scarce, deliveries are slow, the pharmacy substitutes and limits, and from the second week the power is cut for a few hours a day to save it. Nothing stops entirely; everything is less.",
        Season.Winter,
        [Horizon.OneWeek, Horizon.TwoWeeks, Horizon.OneMonth, Horizon.ThreeMonths, Horizon.SixMonths],
        [
            new(Utility.Shops, 1, End, 0.7m),
            new(Utility.Fuel, 1, End, 0.3m),
            new(Utility.Deliveries, 1, End, 0.6m),
            new(Utility.Pharmacy, 1, End, 0.6m),
            new(Utility.Electricity, 14, End, 0.83m),
        ],
        RiskKind.RegionalDisruption,
        BoilWaterNotice: false,
        Heatwave: false,
        ConfinedToHome: false);

    /// <summary>A heatwave: evening cuts as the grid strains, water restrictions, and the vulnerable at risk from the heat.</summary>
    public static Scenario Heatwave { get; } = new(
        "heatwave",
        "A heatwave",
        "A week or two above thirty degrees. The grid strains in the evenings and cuts out for an hour or two, the water company restricts supply, everyone needs an extra litre a day, and the people who feel heat most need a cool room.",
        Season.Summer,
        [Horizon.OneDay, Horizon.ThreeDays, Horizon.OneWeek, Horizon.TwoWeeks],
        [
            new(Utility.Electricity, 1, End, 0.9m),
        ],
        null,
        BoilWaterNotice: false,
        Heatwave: true,
        ConfinedToHome: false);

    /// <summary>You must stay at home: illness or an order. Everything works, but you cannot go to the shops or the pharmacy; deliveries are slow.</summary>
    public static Scenario StayAtHome { get; } = new(
        "stay-home",
        "You must stay at home",
        "Illness in the house or an instruction to stay in. Every utility works, but nobody can go to the shops, the pharmacy or the forecourt, and deliveries are slower than usual because everyone is ordering.",
        Season.Winter,
        [Horizon.OneWeek, Horizon.TwoWeeks, Horizon.OneMonth],
        [
            new(Utility.Shops, 1, End, 0m),
            new(Utility.Pharmacy, 1, End, 0.7m),
            new(Utility.Fuel, 1, End, 0m),
            new(Utility.Deliveries, 1, End, 0.7m),
        ],
        null,
        BoilWaterNotice: false,
        Heatwave: false,
        ConfinedToHome: true);

    /// <summary>Every scenario, in the order they are offered before ranking.</summary>
    public static IReadOnlyList<Scenario> All { get; } = [Storm, WaterCut, Snow, Regional, Shortages, Heatwave, StayAtHome];

    /// <summary>Looks a scenario up by id, or null.</summary>
    public static Scenario? Find(string id) => All.FirstOrDefault(scenario => scenario.Id == id);
}
