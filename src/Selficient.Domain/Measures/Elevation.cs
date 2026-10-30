using Selficient.Domain.Primitives;

namespace Selficient.Domain.Measures;

/// <summary>Height above sea level in metres, bounded to the British Isles.</summary>
public readonly record struct Elevation
{
    /// <summary>Below this a house is in the Fens or on a quay; lower is a data error.</summary>
    public static decimal Lowest => -10m;

    /// <summary>Ben Nevis is 1,345 m; nothing habitable is higher.</summary>
    public static decimal Highest => 1400m;

    /// <summary>Above this, snow and ice close the road before the valley loses it.</summary>
    public static decimal ExposedHillside => 150m;

    private Elevation(decimal metres) => Metres = metres;

    /// <summary>Metres above sea level.</summary>
    public decimal Metres { get; }

    /// <summary>True when the house sits high enough for winter access to be a risk in its own right.</summary>
    public bool IsExposed => Metres >= ExposedHillside;

    /// <summary>Creates an elevation, refusing values off the British Isles.</summary>
    public static Result<Elevation> Create(decimal metres) =>
        metres < Lowest || metres > Highest
            ? Result.Failure<Elevation>(DomainErrors.ElevationOutOfRange())
            : Result.Success(new Elevation(metres));
}
