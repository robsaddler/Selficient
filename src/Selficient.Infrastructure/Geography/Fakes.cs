using System.Security.Cryptography;
using System.Text;
using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Geography;

/// <summary>
/// Deterministic stand-ins for development without network and for tests. The same postcode always
/// yields the same place, nation follows the postcode area, and Rob's own postcode is exact.
/// </summary>
public sealed class FakeGeocoder : IGeocoder
{
    private static readonly HashSet<string> WelshAreas = ["CF", "LD", "LL", "NP", "SA", "SY", "CH"];
    private static readonly HashSet<string> ScottishAreas = ["AB", "DD", "DG", "EH", "FK", "G", "HS", "IV", "KA", "KW", "KY", "ML", "PA", "PH", "TD", "ZE"];

    /// <inheritdoc />
    public Task<Result<Locality>> LocateAsync(Postcode postcode, CancellationToken cancellationToken)
    {
        if (postcode.Value == "NP12 1GW")
        {
            return Task.FromResult(Result.Success(new Locality(new Coordinates(51.6655, -3.2031), Nation.Wales, "NP12", "Wales")));
        }

        if (postcode.Value == "ZE1 0ZZ")
        {
            return Task.FromResult(Result.Failure<Locality>(DomainErrors.PostcodeNotFound()));
        }

        // A place with no region name and, through the paired elevation fake, no elevation either.
        if (postcode.Value == "ZE2 9ZZ")
        {
            return Task.FromResult(Result.Success(new Locality(new Coordinates(60.0, -1.0), Nation.Scotland, "ZE2", string.Empty)));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(postcode.Value));
        double lat = 50.0 + (hash[0] / 255.0 * 8.5);
        double lon = -5.5 + (hash[1] / 255.0 * 6.5);
        Nation nation = postcode.Area == "BT"
            ? Nation.NorthernIreland
            : WelshAreas.Contains(postcode.Area)
                ? Nation.Wales
                : ScottishAreas.Contains(postcode.Area)
                    ? Nation.Scotland
                    : Nation.England;

        return Task.FromResult(Result.Success(new Locality(new Coordinates(Math.Round(lat, 4), Math.Round(lon, 4)), nation, postcode.Outcode, "Fake region <for testing>")));
    }
}

/// <summary>Elevation that follows the coordinates deterministically, with Rob's house exact.</summary>
public sealed class FakeElevationProvider : IElevationProvider
{
    /// <inheritdoc />
    public Task<Result<Elevation>> ElevationAtAsync(Coordinates coordinates, CancellationToken cancellationToken)
    {
        if (Math.Abs(coordinates.Latitude - 51.6655) < 0.0005 && Math.Abs(coordinates.Longitude + 3.2031) < 0.0005)
        {
            return Task.FromResult(Elevation.Create(232m));
        }

        if (coordinates == new Coordinates(60.0, -1.0))
        {
            return Task.FromResult(Result.Failure<Elevation>(DomainErrors.LookupUnavailable()));
        }

        decimal metres = Math.Round((decimal)(Math.Abs(coordinates.Latitude * 100 + coordinates.Longitude * 37) % 400), 1);
        return Task.FromResult(Elevation.Create(metres));
    }
}
