using System.Globalization;
using Selficient.Application.Geography;
using Selficient.Domain.Measures;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// What the map panel needs: where the postcode is, whether the basemap covers it, and one
/// sentence about the ground. The pin itself lives in the browser (ADR-001).
/// </summary>
public sealed class HouseMapView
{
    /// <summary>Builds the view from the located household.</summary>
    public HouseMapView(Locality locality, Elevation? elevation)
    {
        ArgumentNullException.ThrowIfNull(locality);
        Latitude = locality.Coordinates.Latitude.ToString("0.00000", CultureInfo.InvariantCulture);
        Longitude = locality.Coordinates.Longitude.ToString("0.00000", CultureInfo.InvariantCulture);
        // OS Open Zoomstack covers Great Britain; Northern Ireland is mapped by a different agency.
        Covered = locality.Nation != Nation.NorthernIreland;
        Sentence = Describe(locality, elevation);
    }

    /// <summary>Latitude of the postcode centre, five decimals (about a metre).</summary>
    public string Latitude { get; }

    /// <summary>Longitude of the postcode centre.</summary>
    public string Longitude { get; }

    /// <summary>Whether the self-hosted basemap can draw this place at all.</summary>
    public bool Covered { get; }

    /// <summary>The sentence under the heading.</summary>
    public string Sentence { get; }

    private static string Describe(Locality locality, Elevation? elevation)
    {
        string place = string.IsNullOrEmpty(locality.Region) ? locality.Outcode : $"{locality.Outcode}, {locality.Region}";
        if (elevation is null)
        {
            return $"Your postcode is in {place}. We could not find how high it sits, so the plan assumes level ground.";
        }

        string metres = elevation.Value.Metres.ToString("0", CultureInfo.InvariantCulture);
        return elevation.Value.IsExposed
            ? $"Your postcode is in {place}, about {metres} m above sea level. That is high and exposed ground, which is why wind and cold sit near the top of your risks."
            : $"Your postcode is in {place}, about {metres} m above sea level.";
    }
}
