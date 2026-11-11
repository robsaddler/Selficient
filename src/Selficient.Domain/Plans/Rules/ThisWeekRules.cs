using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Risks;
using static Selficient.Domain.Plans.Rules.RuleSupport;

namespace Selficient.Domain.Plans.Rules;

/// <summary>The government's Be Ready checklist, sized to the household. Every profile gets this.</summary>
public sealed class BaselineChecklistRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int people = profile.Household.Size.Count;
        decimal litres = 3m * people * Litres.DrinkingPerPersonPerDay;
        decimal bottles = Math.Ceiling(litres / 2m);

        yield return Action(
            "water-bottled-three-days",
            Tier.ThisWeek,
            $"{N(litres)} litres of sealed bottled water",
            $"Three days of drinking water for {People(people)}. That is {N(bottles)} of the big two-litre bottles, and it keeps for two years.",
            MoneyRange.Pounds(litres * 0.35m, litres * 0.5m),
            Mitigates(RiskKind.WaterInterruption, RiskKind.StormPowerCut),
            new Provisions(WaterLitres: litres));

        yield return Action(
            "food-no-cook-three-days",
            Tier.ThisWeek,
            "Three days of food that needs no fridge or cooking",
            "Tinned meat, fish, beans and fruit, oats, nuts and biscuits, so a cut does not mean a cold house and a hungry one. Buy what you already eat.",
            MoneyRange.Pounds(8m * people, 12m * people),
            Mitigates(RiskKind.StormPowerCut, RiskKind.RegionalDisruption),
            new Provisions(FoodDays: 3m));

        int failureLights = profile.Dwelling.Storeys + 1;
        yield return Action(
            "power-failure-lights",
            Tier.ThisWeek,
            $"{N(failureLights)} plug-in power-failure torches",
            "They charge in the socket and switch on the instant the power drops, so nobody is on a dark stair. One for every hall and stairwell and one in the kitchen.",
            MoneyRange.Pounds(20m * failureLights, 24m * failureLights),
            Mitigates(RiskKind.StormPowerCut),
            new Provisions(LightHours: 6m * failureLights));

        yield return Action(
            "headtorches",
            Tier.ThisWeek,
            $"{N(people)} headtorches",
            "Hands-free light is worth more than any lantern. One each, with cells that are easy to find.",
            MoneyRange.Pounds(17m * people, 26m * people),
            Mitigates(RiskKind.StormPowerCut),
            new Provisions(LightHours: 20m * people));

        int banks = Math.Max(2, (people + 1) / 2);
        yield return Action(
            "power-banks",
            Tier.ThisWeek,
            $"{N(banks)} power banks of about 25,000 mAh",
            "Each charges a phone four or five times. Keep them at half to three-quarters full and top them up every three months.",
            MoneyRange.Pounds(47m * banks, 79m * banks),
            Mitigates(RiskKind.CommsLoss, RiskKind.StormPowerCut));

        yield return Action(
            "radio-dab-fm-with-crank",
            Tier.ThisWeek,
            "A radio with FM that runs on AA cells, with a wind-up handle",
            "When the whole grid is down, the emergency broadcasts go out on BBC Radio 2 and Radio 4, and FM is what still reaches almost everywhere. In Lancaster in 2015 it was the one source of news that worked. Write the two frequencies for your area on the back. The cells are the real backup; the handle is the bonus.",
            MoneyRange.Pounds(36m, 150m),
            Mitigates(RiskKind.CommsLoss, RiskKind.RegionalDisruption));

        yield return Action(
            "no-flush-toilet-kit",
            Tier.ThisWeek,
            $"Toilet liners and gelling powder for {People(people)} for three days",
            "In a wide power cut the sewers stop as well, and the official advice is not to flush at all, even with stored water. These are bags that line the bowl of the toilet you already have, with a powder that sets the contents solid. Tie each one off and put it in the outside bin. Without them the advice is a bin bag, which works and is as unpleasant as it sounds.",
            MoneyRange.Pounds(10m, 34m),
            Mitigates(RiskKind.RegionalDisruption, RiskKind.WaterInterruption));

        yield return Action(
            "landline-battery-backup",
            Tier.ThisWeek,
            "A battery backup for the router, so the broadband and the house phone work in a power cut",
            "The copper phone network is being switched off, with 31 January 2027 as the date, and a digital line dies with the router the moment the power does. Ask your phone company first: it has to give a backup that lasts at least an hour, free, to anyone who relies on the landline. Otherwise a small backup unit keeps the router and the phone going for a few hours of a local cut.",
            MoneyRange.Pounds(0m, 62m),
            Mitigates(RiskKind.CommsLoss, RiskKind.StormPowerCut));

        yield return Action(
            "safety-kit",
            Tier.ThisWeek,
            "Carbon monoxide alarm, fire blanket, first-aid kit and two weeks of any prescriptions",
            "Backup cooking and heating are where people get hurt in a power cut. Buy the alarm before the stove.",
            MoneyRange.Pounds(60m, 80m),
            Mitigates(RiskKind.IndoorFireOrCarbonMonoxide));

        yield return Action(
            "cash-in-small-notes",
            Tier.ThisWeek,
            "Cash in small notes and coins, up to your insurance limit",
            "Card terminals and cash machines die with the power. Most contents policies cover between £250 and £1,000 of cash at home, so check the schedule and hold that much.",
            MoneyRange.Free,
            Mitigates(RiskKind.StormPowerCut, RiskKind.RegionalDisruption));

        yield return Action(
            "candles-and-matches",
            Tier.ThisWeek,
            "Tea lights in jars, a few pillar candles, lighters and matches",
            "A third line of light that never goes off, and matches light a gas hob in a cut. Hard surfaces only, never on the stairs, never left burning in a bedroom.",
            MoneyRange.Pounds(10m, 15m),
            Mitigates(RiskKind.StormPowerCut),
            new Provisions(LightHours: 10m));

        yield return Action(
            "save-emergency-numbers",
            Tier.ThisWeek,
            "Save 105, your water company and the weather warnings on every phone",
            "105 reaches the electricity network from any phone. Agree where the family meets and that you text first, because texts get through when calls do not.",
            MoneyRange.Free,
            Mitigates(RiskKind.CommsLoss, RiskKind.StormPowerCut));
    }
}

/// <summary>Utilities put households with vulnerable people or children first; it costs nothing.</summary>
public sealed class PriorityServicesRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 110;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Household.QualifiesForPriorityServices)
        {
            yield return Action(
                "priority-services-register",
                Tier.ThisWeek,
                "Register with your electricity and water companies' Priority Services Registers",
                "It costs nothing and brings doorstep water, earlier reconnection and advance notice of planned work. Households with children or anyone with a medical or mobility need qualify.",
                MoneyRange.Free,
                Mitigates(RiskKind.StormPowerCut, RiskKind.WaterInterruption));
        }
    }
}

/// <summary>A tall tree gets a survey this week and a decision next month.</summary>
public sealed class TallTreeRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 120;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.Dwelling.HasTallTreeNearHouse)
        {
            yield break;
        }

        yield return Action(
            "arborist-survey",
            Tier.ThisWeek,
            "Book an arborist survey of the tall tree",
            "An Arboricultural Association approved contractor will assess species, health, lean and root plate. A sound tree often needs little work, and heavy topping can make one more dangerous. Check whether it has a Tree Preservation Order before any work.",
            MoneyRange.Pounds(100m, 250m),
            Mitigates(RiskKind.FallingTree));

        yield return Action(
            "tree-work-if-advised",
            Tier.ThisMonth,
            "Act on the arborist's report",
            "Crown reduction or thinning if advised, with council consent first if the tree is protected. If it is not your tree, send the report and a dated letter to the owner, and tell your insurer in writing.",
            MoneyRange.Pounds(0m, 1500m),
            Mitigates(RiskKind.FallingTree));
    }
}

/// <summary>Leaf litter, decking and a firebreak.</summary>
public sealed class GardenFireRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 130;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(risks);
        if (!risks.Includes(RiskKind.GardenFire))
        {
            yield break;
        }

        if (profile.Dwelling.HasDecking)
        {
            yield return Action(
                "clear-leaf-litter-under-decking",
                Tier.ThisWeek,
                "Clear the leaf litter from under the decking",
                "A leaf blower through the gaps, or lift two boards and rake. Do it after leaf-fall and again in late spring, and fit fine mesh around the skirt to stop it building up again.",
                MoneyRange.Free,
                Mitigates(RiskKind.GardenFire));
        }

        yield return Action(
            "garden-firebreak-and-hose",
            Tier.ThisMonth,
            "A metre of gravel or bare ground at the boundary, and a hose that reaches the back fence",
            "Keep anything that burns a metre from the boundary, hang a garden fire blanket by the back door, and never use a disposable barbecue on or near the decking.",
            MoneyRange.Pounds(50m, 150m),
            Mitigates(RiskKind.GardenFire));
    }
}

/// <summary>Half a tank and a 12 V inverter turn the car into a charger and a way out.</summary>
public sealed class CarRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 140;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Dwelling.HasCar)
        {
            yield return Action(
                "car-above-half-a-tank",
                Tier.ThisWeek,
                "Keep the car above half a tank from November to March, with a 12 V inverter in the glovebox",
                "Fuel pumps need electricity too. A charged car charges laptops and power banks, and gets you off the hill before the road closes.",
                MoneyRange.Pounds(25m, 35m),
                Mitigates(RiskKind.WinterAccess, RiskKind.StormPowerCut));
        }
    }
}

/// <summary>Flood warnings and a flood plan, only where the map says so.</summary>
public sealed class FloodRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 150;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(risks);
        if (!risks.Includes(RiskKind.Flooding))
        {
            yield break;
        }

        yield return Action(
            "flood-warnings-and-plan",
            Tier.ThisWeek,
            "Sign up for free flood warnings and write a one-page flood plan",
            "Know where the stopcock and the fuse board are, where you would move the car, and what goes upstairs first. Warnings come by phone call, text and email.",
            MoneyRange.Free,
            Mitigates(RiskKind.Flooding));

        yield return Action(
            "flood-protection-kit",
            Tier.ThisMonth,
            "Air-brick covers, door guards and sandbags or absorbent barriers",
            "Simple products that keep shallow water out of the ground floor for long enough to matter. Fit the air-brick covers before the warning, not during it.",
            MoneyRange.Pounds(100m, 400m),
            Mitigates(RiskKind.Flooding));
    }
}
