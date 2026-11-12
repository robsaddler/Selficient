using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Geography;

/// <summary>A point on the earth.</summary>
public readonly record struct Coordinates(double Latitude, double Longitude);

/// <summary>Where a postcode is. Strings here come from an upstream service and are data, not copy.</summary>
public sealed record Locality(Coordinates Coordinates, Nation Nation, string Outcode, string Region);

/// <summary>Finds where a postcode is.</summary>
public interface IGeocoder
{
    /// <summary>Locates the postcode, or explains why not.</summary>
    Task<Result<Locality>> LocateAsync(Postcode postcode, CancellationToken cancellationToken);
}

/// <summary>Finds the postcode nearest a point, for a household that would rather share its location than type.</summary>
public interface IReverseGeocoder
{
    /// <summary>The nearest postcode, or why there is none.</summary>
    Task<Result<Postcode>> NearestAsync(Coordinates point, CancellationToken cancellationToken);
}

/// <summary>
/// What a nation's flood map says about a place: the highest band it gives there, which water that
/// band is about, and whose map it is. The point is the middle of a postcode, not a front door, so
/// the answer is for the postcode and is always said to be.
/// </summary>
public sealed record FloodFinding(FloodRisk Band, FloodWater From, string Source);

/// <summary>Reads the national flood map for a point.</summary>
public interface IFloodRiskProvider
{
    /// <summary>
    /// The highest band the map gives near the point, or why it could not be read: the nation has
    /// no map we can read, or the service did not answer. A failure is never a low risk.
    /// </summary>
    Task<Result<FloodFinding>> AtAsync(Coordinates point, Nation nation, CancellationToken cancellationToken);
}

/// <summary>Finds how high a point is.</summary>
public interface IElevationProvider
{
    /// <summary>The elevation at the point, or why it could not be found.</summary>
    Task<Result<Elevation>> ElevationAtAsync(Coordinates coordinates, CancellationToken cancellationToken);
}
