using Selficient.Application.Planning;
using Selficient.Domain.Households;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// What the site says about flooding outside the plan's own actions: the hint under the question on
/// the form, and the line on the plan that says which band it was built on and where that came from.
/// A band read from a map is for the postcode, never the building, and every sentence says so.
/// </summary>
public static class FloodCopy
{
    private static readonly Dictionary<FloodRisk, string> Bands = new()
    {
        [FloodRisk.VeryLow] = "very low",
        [FloodRisk.Low] = "low",
        [FloodRisk.Medium] = "medium",
        [FloodRisk.High] = "high",
    };

    private static readonly Dictionary<FloodWater, string> Waters = new()
    {
        [FloodWater.NotSaid] = "of flooding",
        [FloodWater.RiversOrSea] = "from rivers or the sea",
        [FloodWater.SurfaceWater] = "from surface water",
    };

    private static readonly Dictionary<Nation, string> Names = new()
    {
        [Nation.England] = "England",
        [Nation.Wales] = "Wales",
        [Nation.Scotland] = "Scotland",
        [Nation.NorthernIreland] = "Northern Ireland",
    };

    /// <summary>Under the question: what leaving it alone does.</summary>
    public const string FormHint = "Leave this as it is and the plan looks your postcode up on your nation's flood map when it is built.";

    /// <summary>Under that: when to answer it yourself, before the four links.</summary>
    public const string FormOwnAddress = "If you know the band for your own address, choose it: an address is more exact than a postcode. You can check yours for";

    /// <summary>The four nations in the order the hint lists them, each with what follows its link.</summary>
    public static IReadOnlyList<(Nation Nation, string After)> Nations { get; } =
    [
        (Nation.England, ","), (Nation.Wales, ","), (Nation.Scotland, " or"), (Nation.NorthernIreland, "."),
    ];

    /// <summary>A nation's name, as a link says it.</summary>
    public static string NationName(Nation nation) => Names[nation];

    /// <summary>Whose data the flood bands are, as the licence asks it to be said, and what they are not.</summary>
    public const string Attribution = "Flood risk is read from the Environment Agency's Risk of Flooding from Rivers and Sea and Risk of Flooding from Surface Water, Natural Resources Wales's Flood Risk Assessment Wales and SEPA's Flood Maps, each under the Open Government Licence v3.0. Contains Environment Agency information © Environment Agency and/or database right. Contains Natural Resources Wales information © Natural Resources Wales and database right. Contains SEPA data © Scottish Environment Protection Agency and database right. Each is an indication for an area, not an assessment of a property.";

    /// <summary>The heading of the line on the plan.</summary>
    public const string Heading = "Flooding";

    /// <summary>What the plan was built on, and where that came from.</summary>
    public static string Says(FloodAnswer answer, Nation nation)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.Origin == FloodOrigin.NotKnown)
        {
            // Northern Ireland publishes a map to look at and no service a plan can ask, which is not the same as a service that failed.
            return nation == Nation.NorthernIreland
                ? "Northern Ireland's flood map is one to look at, not one we can ask for you, so your plan starts by asking you to check it."
                : "We could not read the flood map for your postcode just now, so your plan starts by asking you to check it.";
        }

        string band = Bands[answer.Band];
        if (answer.Origin == FloodOrigin.Household)
        {
            return $"You told us the flood map shows a {band} risk at your address, and the plan is built on that.";
        }

        return answer.Band == FloodRisk.VeryLow
            ? $"We looked the middle of your postcode up on {answer.Source} and it shows a very low risk from rivers, the sea and surface water, so there is nothing about flooding in your plan."
            : $"We looked the middle of your postcode up on {answer.Source} and it shows a {band} risk {Waters[answer.From]}, so flooding is in your plan.";
    }

    /// <summary>The official place to check an address in this nation: one link, or none where the nation is not known.</summary>
    public static IReadOnlyList<Selficient.Domain.Plans.ActionLink> CheckLinks(Nation nation) =>
        FloodServices.Check(nation) is { } link ? [link] : [];

    /// <summary>After it, where the band came from a map or from nowhere: the map is for a postcode, and the household can do better.</summary>
    public static string OwnAddress(FloodAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer.Origin == FloodOrigin.Household
            ? "If that changes, change your answer and the plan follows."
            : "A postcode is not a front door. If your own address says something different, change your answer and the plan follows.";
    }
}

/// <summary>What the wizard says around its form, and the link on the plan that leads back to it.</summary>
public static class WizardCopy
{
    /// <summary>Above the form: the one thing that leaves the browser, and everything it is used for.</summary>
    public const string WhatLeaves = "Nothing here leaves your browser except the postcode, which we use once to find your area, your height above sea level and what the flood map says there. We do not store it.";

    /// <summary>Above the form, when it has been filled in from the plan this browser keeps.</summary>
    public const string KeptAnswers = "These are the answers your plan was built from. Change what is different and build it again; what you have ticked stays as it is.";

    /// <summary>The link on the plan that leads back to the form.</summary>
    public const string ChangeAnswers = "Change your answers";
}
