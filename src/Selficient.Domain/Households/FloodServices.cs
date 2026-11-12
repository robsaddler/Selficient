using Selficient.Domain.Plans;

namespace Selficient.Domain.Households;

/// <summary>
/// Each nation's own flood services: where a household checks its address on the flood map, and
/// where it signs up for warnings. Every address was opened on 12 November 2026. Northern Ireland
/// has a map and no warning service to sign up to, so it is sent to the advice it does have.
/// </summary>
public static class FloodServices
{
    private static readonly Dictionary<Nation, ActionLink> Checks = new()
    {
        [Nation.England] = new("Check your address on GOV.UK", new Uri("https://www.gov.uk/check-long-term-flood-risk")),
        [Nation.Wales] = new("Check your address with Natural Resources Wales", new Uri("https://naturalresources.wales/flooding/check-your-flood-risk-by-postcode/")),
        [Nation.Scotland] = new("Check your address on SEPA's flood maps", new Uri("https://beta.sepa.scot/flooding/flood-maps/")),
        [Nation.NorthernIreland] = new("Check your area on nidirect", new Uri("https://www.nidirect.gov.uk/articles/check-risk-flooding-your-area")),
    };

    private static readonly Dictionary<Nation, ActionLink> WarningServices = new()
    {
        [Nation.England] = new("Sign up for flood warnings on GOV.UK", new Uri("https://www.gov.uk/get-flood-warnings")),
        [Nation.Wales] = new("Sign up with Natural Resources Wales", new Uri("https://naturalresources.wales/flooding/sign-up-to-receive-flood-warnings/")),
        [Nation.Scotland] = new("Sign up to SEPA's Floodline", new Uri("https://www.sepa.org.uk/flooding/floodline-register/")),
    };

    /// <summary>Northern Ireland's own advice on getting ready, which is what it has in place of a warning service.</summary>
    public static ActionLink NorthernIrelandAdvice { get; } = new("Preparing for a flood, on nidirect", new Uri("https://www.nidirect.gov.uk/articles/preparing-flood"));

    /// <summary>The Met Office's weather warnings, which are the only notice anyone gets of surface water.</summary>
    public static ActionLink RainWarnings { get; } = new("Weather warnings from the Met Office", new Uri("https://weather.metoffice.gov.uk/warnings-and-advice/uk-warnings"));

    /// <summary>Where a household in this nation checks its own address, or null where the nation is not known.</summary>
    public static ActionLink? Check(Nation nation) => Checks.GetValueOrDefault(nation);

    /// <summary>Where a household in this nation signs up for flood warnings, or null where there is no such service or the nation is not known.</summary>
    public static ActionLink? Warnings(Nation nation) => WarningServices.GetValueOrDefault(nation);
}
