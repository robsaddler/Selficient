using Selficient.Domain.Households;
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

/// <summary>
/// The nearest postcode without a network: anywhere plausible is Rob's house, so the wizard that
/// follows knows the place; Shetland's sea answers with no postcode, and one point off the Hebrides
/// answers as an outage, so both sentences can be seen.
/// </summary>
public sealed class FakeReverseGeocoder : IReverseGeocoder
{
    /// <inheritdoc />
    public Task<Result<Postcode>> NearestAsync(Coordinates point, CancellationToken cancellationToken)
    {
        if (point is { Latitude: 60.0, Longitude: -1.0 })
        {
            return Task.FromResult(Result.Failure<Postcode>(DomainErrors.NoPostcodeHere()));
        }

        if (point is { Latitude: 58.0, Longitude: -8.0 })
        {
            return Task.FromResult(Result.Failure<Postcode>(DomainErrors.LookupUnavailable()));
        }

        return Task.FromResult(Postcode.Create("NP12 1GW"));
    }
}

/// <summary>
/// A flood map without a network. Rob's house is very low, as the real map says it is. Northern
/// Ireland has no map, as in life, and the Shetland point that has no height has no flood answer
/// either. Everywhere else the band follows the coordinates: most places very low, and one in eight
/// each low from surface water, medium from a river and high from a river, so every sentence the
/// plan can say about flooding can be seen from a postcode.
/// </summary>
public sealed class FakeFloodRiskProvider : IFloodRiskProvider
{
    private static readonly Dictionary<Nation, string> Sources = new()
    {
        [Nation.England] = EnvironmentAgencyFloodMap.Source,
        [Nation.Wales] = NaturalResourcesWalesFloodMap.Source,
        [Nation.Scotland] = SepaFloodMap.Source,
    };

    private static readonly (FloodRisk Band, FloodWater From)[] Bands =
    [
        (FloodRisk.VeryLow, FloodWater.NotSaid), (FloodRisk.VeryLow, FloodWater.NotSaid), (FloodRisk.VeryLow, FloodWater.NotSaid),
        (FloodRisk.VeryLow, FloodWater.NotSaid), (FloodRisk.VeryLow, FloodWater.NotSaid),
        (FloodRisk.Low, FloodWater.SurfaceWater), (FloodRisk.Medium, FloodWater.RiversOrSea), (FloodRisk.High, FloodWater.RiversOrSea),
    ];

    /// <inheritdoc />
    public Task<Result<FloodFinding>> AtAsync(Coordinates point, Nation nation, CancellationToken cancellationToken)
    {
        if (!Sources.TryGetValue(nation, out string? source) || point == new Coordinates(60.0, -1.0))
        {
            return Task.FromResult(Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable()));
        }

        if (point == new Coordinates(51.6655, -3.2031))
        {
            return Task.FromResult(Result.Success(new FloodFinding(FloodRisk.VeryLow, FloodWater.NotSaid, source)));
        }

        (FloodRisk band, FloodWater from) = Bands[(int)Math.Abs(Math.Round((point.Latitude + point.Longitude) * 10_000)) % Bands.Length];
        return Task.FromResult(Result.Success(new FloodFinding(band, from, source)));
    }
}
