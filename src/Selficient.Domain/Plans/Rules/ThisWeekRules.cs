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
            MoneyRange.Pounds(14m * people, 28m * people),
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
            MoneyRange.Pounds(30m, 150m),
            Mitigates(RiskKind.CommsLoss, RiskKind.RegionalDisruption));

        // A tank of the home's own does not stop with the sewers, so the bags are for houses on the sewer.
        if (!profile.Circumstances.OwnDrains)
        {
            yield return Action(
                "no-flush-toilet-kit",
                Tier.ThisWeek,
                $"Toilet liners and gelling powder for {People(people)} for three days",
                "In a wide power cut the sewers stop as well, and the official advice is not to flush at all, even with stored water. These are bags that line the bowl of the toilet you already have, with a powder that sets the contents solid. Tie each one off and put it in the outside bin. Without them the advice is a bin bag, which works and is as unpleasant as it sounds.",
                MoneyRange.Pounds(10m, 34m),
                Mitigates(RiskKind.RegionalDisruption, RiskKind.WaterInterruption));
        }

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
            profile.HasOwnWater
                ? "Save 105, whoever looks after your water supply and the weather warnings on every phone"
                : "Save 105, your water company and the weather warnings on every phone",
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
        if (profile.QualifiesForPriorityServices)
        {
            // A house on its own supply has no water company to register with, and nobody brings water to its door.
            (string title, string why) = profile.HasOwnWater
                ? ("Register with your electricity network's Priority Services Register", "It costs nothing and brings earlier reconnection, a call when the power is off and advance notice of planned work. Households with children or anyone with a medical or mobility need qualify.")
                : ("Register with your electricity and water companies' Priority Services Registers", "It costs nothing and brings doorstep water, earlier reconnection and advance notice of planned work. Households with children or anyone with a medical or mobility need qualify.");
            yield return Action(
                "priority-services-register",
                Tier.ThisWeek,
                title,
                why,
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

        // The tree at a rented home is the landlord's to survey and to cut; what a tenant can do is put it on record.
        if (profile.IsTenant)
        {
            yield return Action(
                "tree-tell-landlord",
                Tier.ThisWeek,
                "Tell your landlord in writing about the tall tree",
                "The tree is your landlord's to have surveyed and made safe, and a dated letter or email puts it on record that they were told. Ask for an Arboricultural Association approved contractor to look at it, and keep a copy with your tenancy papers.",
                MoneyRange.Free,
                Mitigates(RiskKind.FallingTree));
            yield break;
        }

        yield return Action(
            "arborist-survey",
            Tier.ThisWeek,
            "Book an arborist survey of the tall tree",
            "An Arboricultural Association approved contractor will assess species, health, lean and root plate. A sound tree often needs little work, and heavy topping can make one more dangerous. Check whether it has a Tree Preservation Order before any work.",
            MoneyRange.Pounds(150m, 300m),
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
        if (profile.HasPetrolCar)
        {
            yield return Action(
                "car-above-half-a-tank",
                Tier.ThisWeek,
                "Keep the car above half a tank from November to March, with a 12 V inverter in the glovebox",
                "Fuel pumps need electricity too. A charged car charges laptops and power banks, and gets you off the hill before the road closes.",
                MoneyRange.Pounds(25m, 37m),
                Mitigates(RiskKind.WinterAccess, RiskKind.StormPowerCut));
        }

        // An electric car has no tank to keep half full, and what stops it filling is the cut itself.
        if (profile.HasElectricCar)
        {
            yield return Action(
                "car-above-half-charge",
                Tier.ThisWeek,
                "Keep the car above half charge from November to March",
                "Charging stops with the power, at home and at most public chargers. Half a battery is several days of local driving and a way off the hill before the road closes, and the car's own sockets charge phones and power banks.",
                MoneyRange.Free,
                Mitigates(RiskKind.WinterAccess, RiskKind.StormPowerCut));
        }
    }
}

/// <summary>A baby cannot wait for the shops to reopen: nappies, and formula that needs no boiled water.</summary>
public sealed class InfantsRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 112;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int infants = profile.Particulars.Infants;
        if (infants > 0)
        {
            yield return Action(
                "formula-and-nappies",
                Tier.ThisWeek,
                "Two weeks of nappies, wipes and, if you use it, formula",
                "The shops and the deliveries are the first things to stop. Formula made from powder needs boiled water, so it depends on a way to cook: keep some ready-to-feed cartons, which need neither, for the first days.",
                MoneyRange.Pounds(25m * infants, 70m * infants),
                Mitigates(RiskKind.StormPowerCut, RiskKind.WinterAccess, RiskKind.RegionalDisruption));
        }
    }
}

/// <summary>Someone who cannot get out alone needs the way out agreed before the night it is needed.</summary>
public sealed class HelpToLeaveRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 111;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Circumstances.NeedsHelpToLeave)
        {
            // A lift is the way out of a flat until the power goes, which is exactly when it is wanted.
            string lift = profile.IsAboveGround ? " A lift stops with the power, so the plan has to work on the stairs." : string.Empty;
            yield return Action(
                "help-to-leave-plan",
                Tier.ThisWeek,
                "Agree who helps whom to get out, and walk it through once",
                $"Someone here would need help to get downstairs or out of the home. Decide who helps, by which way, and where the chair, the frame and the medicines are kept at night.{lift} Your fire and rescue service will visit free of charge to plan it with you: ask for a home fire safety visit.",
                MoneyRange.Free,
                Mitigates(RiskKind.IndoorFireOrCarbonMonoxide, RiskKind.StormPowerCut, RiskKind.Flooding));
        }
    }
}

/// <summary>A prepayment meter is a second way for the power to be off: no credit, and nowhere open to buy any.</summary>
public sealed class PrepaymentRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 114;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Circumstances.PrepaymentMeter)
        {
            yield return Action(
                "prepayment-credit-ahead",
                Tier.ThisWeek,
                "Keep a week of credit on the prepayment meter through the winter",
                "A prepayment meter goes off when the credit runs out, and in a wide cut the shops and the app that top it up stop too, so the power can come back to a meter with nothing on it. Top up when a storm is forecast. Your supplier has to offer emergency credit, and more help if anyone here is ill, elderly or very young: find out how to switch it on before you need it.",
                MoneyRange.Free,
                Mitigates(RiskKind.StormPowerCut, RiskKind.RegionalDisruption));
        }
    }
}

/// <summary>
/// Flood warnings and a flood plan where the map says so; where nobody knows what the map says, the
/// first thing to do is find out. A house the map calls very low gets nothing, which is the point.
/// </summary>
public sealed class FloodRule : IPlanRule
{
    /// <inheritdoc />
    public int Order => 150;

    /// <inheritdoc />
    public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(risks);

        // Not known is not the same as safe: the plan says so, and sends the house to its own nation's map.
        if (profile.Dwelling.FloodRisk == FloodRisk.Unknown)
        {
            yield return Action(
                "check-flood-risk",
                Tier.ThisWeek,
                "Check what the flood map says about your address",
                "This plan does not know your flood risk yet, and it changes what comes first. The check takes two minutes with your postcode and covers rivers, the sea and surface water. If it says low, medium or high, change your answer and the plan adds what to do about it.",
                MoneyRange.Free,
                Mitigates(RiskKind.Flooding),
                link: FloodServices.Check(profile.Nation));
            yield break;
        }

        if (!risks.Includes(RiskKind.Flooding))
        {
            yield break;
        }

        (string title, string notice, ActionLink? link) = Warning(profile);
        string insurance = profile.IsTenant
            ? "Check that your contents insurance covers flood; the building is your landlord's to insure."
            : "Check that your buildings and contents insurance covers flood.";
        yield return Action(
            "flood-warnings-and-plan",
            Tier.ThisWeek,
            title,
            $"{Know(profile)} {notice} {insurance}",
            MoneyRange.Free,
            Mitigates(RiskKind.Flooding),
            link: link);

        // Water does not reach a flat above the ground, so nothing that keeps it out of a ground floor is sold to one.
        if (profile.IsAboveGround)
        {
            yield break;
        }

        string fixing = profile.IsTenant ? " Ask your landlord before anything is fixed to a door frame; the absorbent barriers need no fixing." : string.Empty;
        yield return Action(
            "flood-protection-kit",
            Tier.ThisMonth,
            "Air-brick covers, door guards and sandbags or absorbent barriers",
            $"Simple products that keep shallow water out of the ground floor for long enough to matter. Fit the air-brick covers before the warning, not during it.{fixing}",
            MoneyRange.Pounds(100m, 400m),
            Mitigates(RiskKind.Flooding));

        // Where the water arrives before anyone knows: a cellar nobody is in, or a bed on the floor it reaches first.
        bool cellar = profile.Circumstances.Cellar;
        if (cellar || profile.SleepsAtGroundLevel)
        {
            yield return Action(
                "flood-water-alarm",
                Tier.ThisMonth,
                cellar ? "A battery water alarm on the cellar floor" : "A battery water alarm on the floor by the outside door",
                cellar
                    ? "It sounds the moment water touches it. A cellar fills first and nobody is down there to see it, so the alarm is the hour you would not otherwise have. Test it when you test the smoke alarms."
                    : "It sounds the moment water touches it. Someone here sleeps on the floor the water reaches first, and surface water comes at night with no warning call, so the alarm is what wakes you. Test it when you test the smoke alarms.",
                MoneyRange.Pounds(8m, 25m),
                Mitigates(RiskKind.Flooding));
        }
    }

    // What a flood plan is for this house: no car to move where there is none, nothing goes upstairs in a home with
    // one floor, and a flat above the ground plans for being cut off, not for water in the rooms.
    private static string Know(HouseholdProfile profile)
    {
        string car = profile.Dwelling.HasCar ? ", where you would move the car" : string.Empty;
        string help = profile.Circumstances.NeedsHelpToLeave ? " Someone here needs help to leave, so go when the warning comes and not when the water does." : string.Empty;
        if (profile.IsAboveGround)
        {
            return $"The water will not reach your floor, but it can take the power, the lift and the way out for days. Decide now when you would leave{car}.{help}";
        }

        bool upstairs = profile.Dwelling.Storeys > 1;
        string lift = upstairs ? "what goes upstairs first" : "what is lifted off the floor first";
        string cellar = profile.Circumstances.Cellar ? " A cellar fills first, often up through its own floor: keep nothing down there you would miss, and never go down into flood water." : string.Empty;
        string drains = profile.Circumstances.OwnDrains ? " A tank in flooded ground cannot drain, so the toilets may not flush until the water has gone." : string.Empty;
        string beds = !upstairs
            ? " There is no upstairs to go to, so decide now where you would go and when."
            : profile.Circumstances.SleepsDownstairs ? " Someone sleeps downstairs, so agree where they sleep on a night with a warning out." : string.Empty;
        return $"Know where the stopcock and the fuse board are{car}, and {lift}.{cellar}{drains}{beds}{help}";
    }

    // What notice this house can get, and where. The warning services call ahead of rivers and the
    // sea, in three nations. Surface water comes with the rain and nobody calls ahead of it, and
    // Northern Ireland has no service to sign up to, so neither is told to sign up for one.
    private static (string Title, string Notice, ActionLink? Link) Warning(HouseholdProfile profile)
    {
        const string RainTitle = "Write a one-page flood plan, and watch the Met Office warnings for heavy rain";
        if (profile.FloodFrom == FloodWater.SurfaceWater)
        {
            return (RainTitle, "The risk here is surface water, which comes with heavy rain, too fast for a flood warning service to call ahead, so a yellow or amber warning for rain is the notice you get.", FloodServices.RainWarnings);
        }

        if (profile.Nation == Nation.NorthernIreland)
        {
            return (RainTitle, "There is no flood warning service to sign up to in Northern Ireland, so the Met Office's warnings for rain are the notice you get.", FloodServices.NorthernIrelandAdvice);
        }

        return ("Sign up for free flood warnings and write a one-page flood plan", "Warnings come by phone call, text and email.", FloodServices.Warnings(profile.Nation));
    }
}
