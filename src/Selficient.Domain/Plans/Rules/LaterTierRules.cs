using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Risks;
using static Selficient.Domain.Plans.Rules.RuleSupport;

namespace Selficient.Domain.Plans.Rules;

/// <summary>A week of water: cans, the bath liner, and a butt for flushing.</summary>
public sealed class WaterStoreRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 200;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int people = profile.Household.Size.Count;
        int cans = profile.Dwelling.Type == DwellingType.Flat ? Math.Max(1, (people + 1) / 2) : people;
        decimal litres = 25m * cans;

        yield return Action(
            "water-cans",
            Tier.ThisMonth,
            $"{N(cans)} food-grade 25-litre water cans with taps",
            $"{N(litres)} litres is about {N(Math.Floor(litres / (people * Litres.DrinkingPerPersonPerDay)))} days of drinking water for {People(people)}. Fill from the tap, add a water-purifying tablet, keep them dark, and refill at the clock changes.",
            MoneyRange.Pounds(12m * cans + 8m, 18m * cans + 8m),
            Mitigates(RiskKind.WaterInterruption),
            new Provisions(WaterLitres: litres));

        yield return Action(
            "bath-liner",
            Tier.ThisMonth,
            "A food-grade bath liner to fill when a warning comes",
            "It holds about 380 litres and fills from the tap in twenty minutes, which is the bath-full-of-water idea without the stale water, the lost bathroom or the drowning risk. Keep it with the bath, not filled.",
            MoneyRange.Pounds(35m, 39m),
            Mitigates(RiskKind.WaterInterruption));

        if (profile.Dwelling.HasGarden)
        {
            yield return Action(
                "water-butt",
                Tier.ThisMonth,
                "A 200-litre water butt on a downpipe",
                "For flushing and washing when the mains is off. Never drink it untreated.",
                MoneyRange.Pounds(50m, 80m),
                Mitigates(RiskKind.WaterInterruption));
        }
    }
}

/// <summary>A month of food, rotated through normal shopping.</summary>
public sealed class FoodStoreRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 210;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int people = profile.Household.Size.Count;
        string pets = profile.Household.HasPets ? " Include the pets' food." : string.Empty;

        yield return Action(
            "food-one-month",
            Tier.ThisMonth,
            "A month of food, built by buying one extra of what you already eat",
            "Take from the front and restock at the back, and nothing ever goes off. Add a manual tin opener." + pets,
            MoneyRange.Pounds(35m * people, 60m * people),
            Mitigates(RiskKind.RegionalDisruption, RiskKind.WinterAccess),
            new Provisions(FoodDays: 27m));
    }
}

/// <summary>Hot food without mains: a cartridge stove, or matches for a gas hob.</summary>
public sealed class CookingRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 220;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Dwelling.Hob == HobType.Gas)
        {
            yield return Action(
                "long-reach-lighter-for-gas-hob",
                Tier.ThisWeek,
                "A long-reach lighter by the cooker",
                "Your gas hob still works in a power cut when it is lit by hand, which makes it the simplest backup cooker there is.",
                MoneyRange.Pounds(3m, 6m),
                Mitigates(RiskKind.StormPowerCut));
            yield break;
        }

        yield return Action(
            "butane-cartridge-stove",
            Tier.ThisMonth,
            "A butane cartridge stove and ten cartridges",
            "About two hours of cooking per cartridge. Use it outside, under an open window, or in the garage with the door open, with the carbon monoxide alarm within reach. Never in a closed room.",
            MoneyRange.Pounds(60m, 100m),
            Mitigates(RiskKind.StormPowerCut, RiskKind.IndoorFireOrCarbonMonoxide));
    }
}

/// <summary>Lanterns for the rooms people sit in.</summary>
public sealed class LanternRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 230;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int lanterns = 1 + Math.Max(1, profile.Household.Size.Count / 2);
        yield return Action(
            "rechargeable-lanterns",
            Tier.ThisMonth,
            $"{N(lanterns)} rechargeable LED lanterns",
            "One for the living room that doubles as a power bank, and one per occupied bedroom. Two hundred hours on the low setting.",
            MoneyRange.Pounds(36m * lanterns, 90m * lanterns),
            Mitigates(RiskKind.StormPowerCut),
            new Provisions(LightHours: 60m * lanterns));
    }
}

/// <summary>Documents and cash in a fire-rated box.</summary>
public sealed class DocumentsRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 240;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        yield return Action(
            "fire-box-for-cash-and-documents",
            Tier.ThisMonth,
            "A thirty-minute fire-rated box for cash, passports, insurance and prescriptions",
            "Scan the documents to an encrypted memory stick and keep it in the same box. Hidden, not in the bedroom drawer.",
            MoneyRange.Pounds(45m, 55m),
            Mitigates(RiskKind.RegionalDisruption, RiskKind.Flooding));
    }
}

/// <summary>Quotes take weeks and the VAT holiday has a date, so start them in month one.</summary>
public sealed class SolarQuotesRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 250;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.Dwelling.HasOwnRoof)
        {
            yield break;
        }

        yield return Action(
            "solar-quotes",
            Tier.ThisMonth,
            "Get three MCS-certified quotes for rooftop solar with a battery and backup gateway",
            "Standard inverters shut down in a power cut by law, so insist on a backup gateway or protected circuits. Register with any group-buying scheme and 0% loan fund in your nation while you wait.",
            MoneyRange.Free,
            Mitigates(RiskKind.StormPowerCut));

        yield return Action(
            "rooftop-solar-and-battery",
            Tier.TwelveMonths,
            "Rooftop solar with a 13.5 kWh battery and whole-home backup",
            "The one item that pays for itself. It also keeps the fridge, boiler, lights and router running through a cut while the panels recharge the battery. VAT is 0% until 31 March 2027.",
            MoneyRange.Pounds(9000m, 17000m),
            Mitigates(RiskKind.StormPowerCut, RiskKind.CommsLoss));
    }
}

/// <summary>A portable power station carries the fridge, router and boiler through a cut.</summary>
public sealed class PowerStationRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 300;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        yield return Action(
            "portable-power-station",
            Tier.ThreeMonths,
            "A 1 to 2 kWh portable power station and a 200 W folding solar panel",
            "Runs the fridge-freezer for fourteen hours and the router and lights for a day, and acts as a UPS. Buy the 1 kWh size if rooftop solar with backup is coming within a year; otherwise the 2 kWh size.",
            MoneyRange.Pounds(720m, 1250m),
            Mitigates(RiskKind.StormPowerCut, RiskKind.CommsLoss),
            new Provisions(LightHours: 100m));

        if (profile.Dwelling.Heating is HeatingType.GasBoiler or HeatingType.Oil or HeatingType.Lpg)
        {
            yield return Action(
                "boiler-plug-and-socket",
                Tier.ThreeMonths,
                "Have an electrician fit a plug-and-socket on the boiler's spur",
                "A boiler draws about a hundred watts, so the power station runs it for a day. Only if the boiler manual allows it, and never by backfeeding a wall socket.",
                MoneyRange.Pounds(100m, 200m),
                Mitigates(RiskKind.StormPowerCut));
        }
        else if (profile.Dwelling.HeatingNeedsElectricity)
        {
            yield return Action(
                "heating-fallback",
                Tier.ThreeMonths,
                "A heating fallback for a winter cut: one warm room, sleeping bags, and a plan",
                "Electric heating and heat pumps draw far more than a portable battery can supply. Everyone in one room with the door shut, layered clothing and sleeping bags gets a family through a night safely.",
                MoneyRange.Pounds(0m, 150m),
                Mitigates(RiskKind.StormPowerCut));
        }
    }
}

/// <summary>A gravity filter makes the month-of-food goal credible.</summary>
public sealed class WaterFilterRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 310;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        yield return Action(
            "gravity-water-filter",
            Tier.ThreeMonths,
            "A gravity water filter",
            "Turns the water butt, a stream or rain into drinking water at seventy litres a day, indefinitely. This is what makes a month credible without storing hundreds of litres.",
            MoneyRange.Pounds(160m, 180m),
            Mitigates(RiskKind.WaterInterruption, RiskKind.RegionalDisruption),
            new Provisions(WaterLitres: 70m * 14m));
    }
}

/// <summary>Satellite internet for the household whose income depends on being online.</summary>
public sealed class BusinessCommsRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 320;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Household.RunsBusinessFromHome)
        {
            yield return Action(
                "satellite-internet-on-standby",
                Tier.ThreeMonths,
                "A satellite internet kit kept on a standby plan",
                "The only internet that survives a regional outage. It draws about thirty watts, so the power station runs it for a day. A few pounds a month on standby, more only in a month you use it.",
                MoneyRange.Pounds(179m, 200m),
                Mitigates(RiskKind.CommsLoss));
        }
    }
}

/// <summary>Prove the plan works before the weather does.</summary>
public sealed class DrillRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 330;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        yield return Action(
            "lights-out-drill",
            Tier.ThreeMonths,
            "Run a two-hour lights-out drill one evening",
            "Trip the main switch, see what fails, and fix the gaps. Then do it again after any big change.",
            MoneyRange.Free,
            Mitigates(RiskKind.StormPowerCut));
    }
}
