using System.Globalization;

namespace Selficient.Web.Composition;

/// <summary>
/// The only culture Selficient renders in. SiteWise once showed dollars on a live IIS box that
/// defaulted to en-US; pinning the culture here and per request makes that impossible.
/// </summary>
public static class SelficientCulture
{
    /// <summary>British English.</summary>
    public static CultureInfo EnGb { get; } = CultureInfo.GetCultureInfo("en-GB");
}
