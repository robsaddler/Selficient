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

        string cost = dwelling.HeatingNeedsElectricity
            ? " Your heating stops the moment the electricity does, along with the fridge, the lights, the router and the phone masts."
            : " The fridge, the lights, the router and the phone masts all go with it.";

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

/// <summary>Bursts and treatment faults stop the water for a day or three.</summary>
public sealed class WaterInterruptionAssessor : IRiskAssessor
{
    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
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

        return dwelling.HasDecking || dwelling.HasTallTreeNearHouse
            ? new Risk(RiskKind.GardenFire, Likelihood.Medium, Impact.High, "Dry summers bring grass and hillside fires, and leaf litter under timber decking or a tree belt behind the fence is exactly what an ember looks for.")
            : new Risk(RiskKind.GardenFire, Likelihood.Low, Impact.Medium, "Dry summers bring garden and grass fires, usually started by a barbecue or a bonfire.");
    }
}

/// <summary>Flooding only ranks when the agencies say the area is at risk.</summary>
public sealed class FloodingAssessor : IRiskAssessor
{
    /// <inheritdoc />
    public Risk? Assess(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Dwelling.FloodRisk switch
        {
            FloodRisk.High => new Risk(RiskKind.Flooding, Likelihood.High, Impact.Severe, "The national flood map puts your area at high risk, which means more than a one in thirty chance in any year. Flooding goes to the top of your plan."),
            FloodRisk.Medium => new Risk(RiskKind.Flooding, Likelihood.Medium, Impact.Severe, "The national flood map puts your area at medium risk, between a one in a hundred and a one in thirty chance in any year."),
            FloodRisk.Low => new Risk(RiskKind.Flooding, Likelihood.Low, Impact.High, "The national flood map puts your area at low risk, which is still up to a one in a hundred chance in any year."),
            _ => null,
        };
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
