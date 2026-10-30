namespace Selficient.Web.Pages;

/// <summary>
/// The facts the legal pages and the structured data share. Pinned by tests so they cannot drift
/// between pages, and kept in one place so a change of address is one edit.
/// </summary>
public static class LegalCopy
{
    /// <summary>Where to write.</summary>
    public const string ContactEmail = "hello@selficient.app";

    /// <summary>Who makes it, as the pages say it.</summary>
    public const string MadeBy = "Selficient is made in Blackwood, South Wales.";

    /// <summary>When the legal pages were last reviewed.</summary>
    public static DateOnly Reviewed { get; } = new(2026, 10, 30);

    /// <summary>The upstream services the privacy page names, and what each receives.</summary>
    public static IReadOnlyList<(string Service, string Receives, string Purpose)> Upstreams { get; } =
    [
        ("postcodes.io", "your postcode", "to find your area, your nation and the coordinates of your postcode's centre"),
        ("Open-Meteo", "the coordinates of your postcode's centre", "to find your height above sea level"),
        ("PVGIS, from the European Commission", "the coordinates of your postcode's centre, your roof's direction and pitch", "to find what a kilowatt of panels would make on your roof, only when you use the solar estimator"),
        ("Google Solar API", "the coordinates of your postcode's centre", "to describe the slopes of the nearest roof, only when you tick the consent box on the solar estimator and only if this site has a key for it"),
        ("Anthropic", "a photograph you choose, re-encoded with its metadata removed, or the text of your plan", "to count what is in a cupboard, read a boiler plate or fuse board, or explain your plan, only when you ask; with your own key the photograph goes from your browser to Anthropic and never reaches us"),
    ];
}
