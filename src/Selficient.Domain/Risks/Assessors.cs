using Selficient.Domain.Households;

namespace Selficient.Domain.Risks;

/// <summary>Every house loses power in a storm sooner or later; what it costs depends on the heating.</summary>
public sealed class StormPowerCutAssessor : IRiskAssessor
{
    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Dwelling dwelling = profile.Dwelling;

        string where = dwelling.IsExposed
            ? "Your house sits high and exposed, on overhead lines, so a winter storm cut of six to forty-eight hours is the most likely disruption you will see."
            : "A winter storm cut of six to forty-eight hours is the most likely disruption any UK house will see.";

        string cost = " The fridge, the lights, the router and the phone masts all go with it.";
        if (dwelling.HeatingNeedsElectricity)
        {
            // A stove beside the boiler is a warm room the cut cannot take, and the sentence should not say otherwise.
            cost = profile.HasWoodStove
                ? " Your central heating stops the moment the electricity does, along with the fridge, the lights, the router and the phone masts. The stove keeps one room warm."
                : " Your heating stops the moment the electricity does, along with the fridge, the lights, the router and the phone masts.";
        }

        return new Risk(
            RiskKind.StormPowerCut,
            Likelihood.High,
            dwelling.HeatingNeedsElectricity ? Impact.High : Impact.Medium,
            where + cost);
    }
}

/// <summary>A tall tree within reach of the house is a single-point risk to the building and to people.</summary>
public sealed class FallingTreeAssessor : IRiskAssessor
{
    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Dwelling.HasTallTreeNearHouse
            ? new Risk(
                RiskKind.FallingTree,
                Likelihood.Low,
                Impact.Severe,
                "A tree tall enough to reach the house is far more likely to cost you than any national-level scenario, so it goes near the top of the list.")
            : null;
    }
}

/// <summary>Bursts and treatment faults stop the water for a day or three; a home's own supply stops for its own reasons.</summary>
public sealed class WaterInterruptionAssessor : IRiskAssessor
{
    private const string NobodyBrings = " No water company brings bottled water to a private supply, so what you have stored is what you have.";

    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Circumstances.Water == WaterSupply.OwnPumped)
        {
            return new Risk(RiskKind.WaterInterruption, Likelihood.High, Impact.High, "Your water is lifted by an electric pump, so every power cut is a water cut as well." + NobodyBrings);
        }

        if (profile.Circumstances.Water == WaterSupply.OwnGravity)
        {
            return new Risk(RiskKind.WaterInterruption, Likelihood.Medium, Impact.Medium, "Your own supply runs without a pump, so a power cut does not stop it, but a hard frost, a dry summer or a fouled source can." + NobodyBrings);
        }

        string note = profile.Dwelling.IsExposed
            ? " The water company will bring bottled water, but it takes time to reach a hill."
            : " The water company will bring bottled water, but it takes time to arrive.";
        return new Risk(RiskKind.WaterInterruption, Likelihood.Medium, Impact.Medium, "A burst main or a treatment problem can stop the supply for one to three days." + note);
    }
}

/// <summary>Snow and ice close the road before they close the town.</summary>
public sealed class WinterAccessAssessor : IRiskAssessor
{
    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Dwelling.IsExposed
            ? new Risk(RiskKind.WinterAccess, Likelihood.Medium, Impact.Medium, "At your height above sea level you will lose the road to snow or ice before the valley does, so plan for a few days of not driving.")
            : new Risk(RiskKind.WinterAccess, Likelihood.Low, Impact.Medium, "Snow or ice can close the road for a day or two in a bad winter.");
    }
}

/// <summary>Masts have about an hour of battery and the router goes with the mains.</summary>
public sealed class CommsLossAssessor : IRiskAssessor
{
    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Household.RunsBusinessFromHome
            ? new Risk(RiskKind.CommsLoss, Likelihood.High, Impact.High, "Only about a fifth of mobile mast sites have an hour of battery, and the router goes with the mains. Because your income depends on being online at home, this is a business risk as well as a family one.")
            : new Risk(RiskKind.CommsLoss, Likelihood.High, Impact.Medium, "Only about a fifth of mobile mast sites have an hour of battery, and the router goes with the mains, so a power cut is a phone cut too.");
    }
}

/// <summary>Leaf litter under decking and a tree belt behind the fence are what embers look for.</summary>
public sealed class GardenFireAssessor : IRiskAssessor
{
    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Dwelling dwelling = profile.Dwelling;
        if (!dwelling.HasGarden)
        {
            return null;
        }

        // Named for what this garden has: a house with a tree and no decking is not told about decking.
        string? fuel = (dwelling.HasDecking, dwelling.HasTallTreeNearHouse) switch
        {
            (true, true) => "leaf litter under timber decking and the litter a tall tree drops against the house are",
            (true, false) => "leaf litter under timber decking is",
            (false, true) => "the litter a tall tree drops against the house and the fence is",
            _ => null,
        };

        return fuel is null
            ? new Risk(RiskKind.GardenFire, Likelihood.Low, Impact.Medium, "Dry summers bring garden and grass fires, usually started by a barbecue or a bonfire.")
            : new Risk(RiskKind.GardenFire, Likelihood.Medium, Impact.High, $"Dry summers bring grass and hillside fires, and {fuel} exactly what an ember looks for.");
    }
}

/// <summary>Flooding only ranks when the agencies say the area is at risk.</summary>
public sealed class FloodingAssessor : IRiskAssessor
{
    // What each band means in a year. Scotland's map draws its bands at different odds from the other
    // three nations', so a "medium" in Perth is not the "medium" of York and is not described as it.
    private static readonly Dictionary<(bool Scottish, FloodRisk Band), string> Odds = new()
    {
        [(false, FloodRisk.High)] = "at high risk{0}, which means more than a one in thirty chance in any year. Flooding goes to the top of your plan.",
        [(false, FloodRisk.Medium)] = "at medium risk{0}, between a one in a hundred and a one in thirty chance in any year.",
        [(false, FloodRisk.Low)] = "at low risk{0}, which is still up to a one in a hundred chance in any year.",
        [(true, FloodRisk.High)] = "at high risk{0}, which on Scotland's map means a one in ten chance or more in any year. Flooding goes to the top of your plan.",
        [(true, FloodRisk.Medium)] = "at medium risk{0}, which on Scotland's map is between a one in two hundred and a one in ten chance in any year.",
        [(true, FloodRisk.Low)] = "at low risk{0}, which on Scotland's map is between a one in a thousand and a one in two hundred chance in any year.",
    };

    private static readonly Dictionary<FloodWater, string> Waters = new()
    {
        [FloodWater.NotSaid] = string.Empty,
        [FloodWater.RiversOrSea] = " from rivers or the sea",
        [FloodWater.SurfaceWater] = " from surface water",
    };

    private static readonly Dictionary<FloodRisk, (Likelihood Likelihood, Impact Impact)> Ratings = new()
    {
        [FloodRisk.High] = (Likelihood.High, Impact.Severe),
        [FloodRisk.Medium] = (Likelihood.Medium, Impact.Severe),
        [FloodRisk.Low] = (Likelihood.Low, Impact.High),
    };

    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        FloodRisk band = profile.Dwelling.FloodRisk;
        if (!Ratings.TryGetValue(band, out (Likelihood Likelihood, Impact Impact) rating))
        {
            return null;
        }

        string says = string.Format(System.Globalization.CultureInfo.InvariantCulture, Odds[(profile.Nation == Nation.Scotland, band)], Waters[profile.FloodFrom]);
        return new Risk(RiskKind.Flooding, rating.Likelihood, rating.Impact, "The national flood map puts your area " + says);
    }
}

/// <summary>Unlikely, but a month of food and a water filter turn it into a nuisance.</summary>
public sealed class RegionalDisruptionAssessor : IRiskAssessor
{
    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new Risk(RiskKind.RegionalDisruption, Likelihood.Low, Impact.Medium, "A two-week regional disruption, such as a cyber attack on a utility or a fuel shortage, is unlikely but would empty the shops for a while.");
    }
}

/// <summary>The one risk a plan can create: cooking or heating indoors with the wrong kit.</summary>
public sealed class IndoorFireAssessor : IRiskAssessor
{
    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new Risk(RiskKind.IndoorFireOrCarbonMonoxide, Likelihood.Low, Impact.Severe, "Backup cooking and heating are where people get hurt in a power cut. A camping stove, barbecue or generator indoors produces carbon monoxide, and candles start fires.");
    }
}
