using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Geography;

/// <summary>A point on the earth.</summary>
public readonly record struct Coordinates(double Latitude, double Longitude);

/// <summary>The nation a postcode falls in, which decides grants, flood agencies and tariffs.</summary>
public enum Nation
{
    /// <summary>Not determined.</summary>
    Unknown,

    /// <summary>England.</summary>
    England,

    /// <summary>Wales.</summary>
    Wales,

    /// <summary>Scotland.</summary>
    Scotland,

    /// <summary>Northern Ireland.</summary>
    NorthernIreland,
}

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

/// <summary>Finds how high a point is.</summary>
public interface IElevationProvider
{
    /// <summary>The elevation at the point, or why it could not be found.</summary>
    Task<Result<Elevation>> ElevationAtAsync(Coordinates coordinates, CancellationToken cancellationToken);
}
