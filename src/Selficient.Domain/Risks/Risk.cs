using Selficient.Domain.Households;

namespace Selficient.Domain.Risks;

/// <summary>The kinds of disruption the plan prepares for. Order is the tie-break when scores match.</summary>
public enum RiskKind
{
    /// <summary>A storm takes the power out for six to forty-eight hours.</summary>
    StormPowerCut,

    /// <summary>A tree close enough to reach the house comes down.</summary>
    FallingTree,

    /// <summary>The water supply stops for a day or three.</summary>
    WaterInterruption,

    /// <summary>Snow or ice closes the road.</summary>
    WinterAccess,

    /// <summary>Mobile and internet go with the power.</summary>
    CommsLoss,

    /// <summary>A garden or hillside fire in a dry summer.</summary>
    GardenFire,

    /// <summary>River, sea or surface water flooding.</summary>
    Flooding,

    /// <summary>A two-week regional disruption such as a utility cyber attack.</summary>
    RegionalDisruption,

    /// <summary>Fire or carbon monoxide from backup cooking or heating.</summary>
    IndoorFireOrCarbonMonoxide,
}

/// <summary>How likely a risk is in a typical year.</summary>
public enum Likelihood
{
    /// <summary>Unlikely in any given year.</summary>
    Low,

    /// <summary>Happens some years.</summary>
    Medium,

    /// <summary>Happens most years.</summary>
    High,
}

/// <summary>How bad it would be.</summary>
public enum Impact
{
    /// <summary>An inconvenience.</summary>
    Low,

    /// <summary>A hard few days.</summary>
    Medium,

    /// <summary>Danger to health or the home.</summary>
    High,

    /// <summary>Danger to life or loss of the home.</summary>
    Severe,
}

/// <summary>A ranked risk with the sentence that justifies it.</summary>
public sealed record Risk(RiskKind Kind, Likelihood Likelihood, Impact Impact, string Rationale)
{
    /// <summary>Likelihood weight times impact weight; higher ranks first.</summary>
    public int Score => ((int)Likelihood + 1) * ((int)Impact + 1);
}

/// <summary>Decides whether one kind of risk applies to a household, and how much.</summary>
public interface IRiskAssessor
{
    /// <summary>The risk for this household, or null when it does not apply.</summary>
    Risk? Assess(HouseholdProfile profile);
}
