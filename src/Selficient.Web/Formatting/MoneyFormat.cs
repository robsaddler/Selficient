using System.Globalization;
using Selficient.Domain.Measures;
using Selficient.Web.Composition;

namespace Selficient.Web.Formatting;

/// <summary>Pounds for the page: whole pounds without pence where possible, en-GB always.</summary>
public static class MoneyFormat
{
    /// <summary>"£15", "£1,250" or "£720 to £1,250"; "Free" when both ends are zero.</summary>
    public static string Range(MoneyRange range)
    {
        if (range.To.Pence == 0)
        {
            return "Free";
        }

        return range.From == range.To
            ? Amount(range.To)
            : $"{Amount(range.From)} to {Amount(range.To)}";
    }

    /// <summary>"£15" or "£12.60".</summary>
    public static string Amount(Money money) =>
        money.Pence % 100 == 0
            ? money.Pounds.ToString("C0", SelficientCulture.EnGb)
            : money.Pounds.ToString("C2", SelficientCulture.EnGb);
}

/// <summary>Risk wording for the page, kept out of the Domain so copy can change without a rule change.</summary>
public static class RiskCopy
{
    /// <summary>A short title for a risk kind.</summary>
    public static string Title(Domain.Risks.RiskKind kind) => kind switch
    {
        Domain.Risks.RiskKind.StormPowerCut => "A storm takes the power out",
        Domain.Risks.RiskKind.FallingTree => "The tall tree comes down",
        Domain.Risks.RiskKind.WaterInterruption => "The water stops",
        Domain.Risks.RiskKind.WinterAccess => "Snow or ice closes the road",
        Domain.Risks.RiskKind.CommsLoss => "Phones and internet go with the power",
        Domain.Risks.RiskKind.GardenFire => "A garden or hillside fire",
        Domain.Risks.RiskKind.Flooding => "Flooding",
        Domain.Risks.RiskKind.RegionalDisruption => "A two-week regional disruption",
        Domain.Risks.RiskKind.IndoorFireOrCarbonMonoxide => "Fire or carbon monoxide from backup cooking",
        _ => "Something else",
    };

    /// <summary>"High likelihood, severe impact".</summary>
    public static string Rating(Domain.Risks.Risk risk)
    {
        ArgumentNullException.ThrowIfNull(risk);
        return string.Create(CultureInfo.InvariantCulture, $"{risk.Likelihood} likelihood, {Impact(risk.Impact)} impact").ToLowerInvariant() is var text
            ? char.ToUpperInvariant(text[0]) + text[1..]
            : string.Empty;
    }

    private static string Impact(Domain.Risks.Impact impact) => impact == Domain.Risks.Impact.Severe ? "severe" : impact.ToString();
}

/// <summary>Where a postcode is, as a phrase: "NP12, Wales" or just "NP12" when the region is unknown.</summary>
public static class PlaceCopy
{
    /// <summary>The outcode, then the region where there is one.</summary>
    public static string Place(Selficient.Application.Geography.Locality locality)
    {
        ArgumentNullException.ThrowIfNull(locality);
        return string.IsNullOrEmpty(locality.Region) ? locality.Outcode : $"{locality.Outcode}, {locality.Region}";
    }
}
