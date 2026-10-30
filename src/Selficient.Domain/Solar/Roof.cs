using Selficient.Domain.Primitives;

namespace Selficient.Domain.Solar;

/// <summary>Which way the roof slope faces. Eight points are enough: PVGIS differences within 22° are noise.</summary>
public enum RoofAspect
{
    /// <summary>Facing north.</summary>
    North,

    /// <summary>Facing north-east.</summary>
    NorthEast,

    /// <summary>Facing east.</summary>
    East,

    /// <summary>Facing south-east.</summary>
    SouthEast,

    /// <summary>Facing south.</summary>
    South,

    /// <summary>Facing south-west.</summary>
    SouthWest,

    /// <summary>Facing west.</summary>
    West,

    /// <summary>Facing north-west.</summary>
    NorthWest,
}

/// <summary>What a roof aspect means in numbers and words.</summary>
public static class RoofAspects
{
    /// <summary>All eight, clockwise from north.</summary>
    public static IReadOnlyList<RoofAspect> All { get; } = Enum.GetValues<RoofAspect>();

    /// <summary>Azimuth in PVGIS's convention: 0 is south, east is negative, west positive.</summary>
    public static int PvgisAzimuth(this RoofAspect aspect) => aspect switch
    {
        RoofAspect.North => 180,
        RoofAspect.NorthEast => -135,
        RoofAspect.East => -90,
        RoofAspect.SouthEast => -45,
        RoofAspect.South => 0,
        RoofAspect.SouthWest => 45,
        RoofAspect.West => 90,
        RoofAspect.NorthWest => 135,
        _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Not a roof aspect."),
    };

    /// <summary>Compass bearing in degrees clockwise from north.</summary>
    public static int Bearing(this RoofAspect aspect) => ((int)aspect) * 45;

    /// <summary>The nearest of the eight to a bearing in degrees clockwise from north.</summary>
    public static RoofAspect FromBearing(double bearing)
    {
        double normalised = ((bearing % 360) + 360) % 360;
        int index = (int)Math.Round(normalised / 45, MidpointRounding.AwayFromZero) % 8;
        return (RoofAspect)index;
    }

    /// <summary>The words a person uses.</summary>
    public static string Words(this RoofAspect aspect) => aspect switch
    {
        RoofAspect.North => "north",
        RoofAspect.NorthEast => "north-east",
        RoofAspect.East => "east",
        RoofAspect.SouthEast => "south-east",
        RoofAspect.South => "south",
        RoofAspect.SouthWest => "south-west",
        RoofAspect.West => "west",
        RoofAspect.NorthWest => "north-west",
        _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Not a roof aspect."),
    };
}

/// <summary>Roof pitch in degrees from horizontal. Flat roofs get frames at the lower bound.</summary>
public readonly record struct RoofPitch
{
    /// <summary>Lowest sensible pitch (a frame on a flat roof).</summary>
    public const int Lowest = 10;

    /// <summary>Steepest pitch a panel array is fitted to.</summary>
    public const int Steepest = 60;

    /// <summary>The usual UK pitch, and PVGIS's default.</summary>
    public const int Typical = 35;

    private RoofPitch(int degrees) => Degrees = degrees;

    /// <summary>Degrees from horizontal.</summary>
    public int Degrees { get; }

    /// <summary>Creates a pitch within the range.</summary>
    public static Result<RoofPitch> Create(int degrees) =>
        degrees is < Lowest or > Steepest
            ? Result.Failure<RoofPitch>(DomainErrors.SolarInputOutOfRange("pitch", Lowest, Steepest))
            : Result.Success(new RoofPitch(degrees));
}

/// <summary>An array described by its panel count, at today's typical panel.</summary>
public readonly record struct PanelArray
{
    /// <summary>Watts per panel in 2026 residential installs.</summary>
    public const int WattsPerPanel = 430;

    /// <summary>Fewest panels anyone fits.</summary>
    public const int Fewest = 2;

    /// <summary>Most panels a domestic roof takes.</summary>
    public const int Most = 40;

    private PanelArray(int panels) => Panels = panels;

    /// <summary>How many panels.</summary>
    public int Panels { get; }

    /// <summary>Peak power in kilowatts.</summary>
    public decimal KilowattsPeak => Panels * WattsPerPanel / 1000m;

    /// <summary>Creates an array within the range.</summary>
    public static Result<PanelArray> Create(int panels) =>
        panels is < Fewest or > Most
            ? Result.Failure<PanelArray>(DomainErrors.SolarInputOutOfRange("panels", Fewest, Most))
            : Result.Success(new PanelArray(panels));
}

/// <summary>How much of the house keeps running when the grid drops.</summary>
public enum BackupLevel
{
    /// <summary>No backup: the inverter shuts down in a cut by law, and the battery with it.</summary>
    None,

    /// <summary>A few protected circuits: lights, fridge, router, boiler.</summary>
    ProtectedCircuits,

    /// <summary>A gateway that islands the whole house.</summary>
    WholeHome,
}

/// <summary>A home battery by usable capacity, or none.</summary>
public readonly record struct Battery
{
    /// <summary>Largest domestic battery the model handles.</summary>
    public const int MostKilowattHours = 30;

    /// <summary>Share of the nominal capacity cycled on an ordinary day.</summary>
    public const decimal DailyCycleShare = 0.9m;

    private Battery(decimal kilowattHours, BackupLevel backup)
    {
        KilowattHours = kilowattHours;
        Backup = backup;
    }

    /// <summary>Usable capacity in kilowatt-hours; zero for no battery.</summary>
    public decimal KilowattHours { get; }

    /// <summary>What the battery can keep running in a cut.</summary>
    public BackupLevel Backup { get; }

    /// <summary>True when there is a battery at all.</summary>
    public bool IsPresent => KilowattHours > 0;

    /// <summary>No battery, no backup.</summary>
    public static Battery None { get; } = new(0, BackupLevel.None);

    /// <summary>Creates a battery within the range; a zero size is no battery and clears the backup level.</summary>
    public static Result<Battery> Create(decimal kilowattHours, BackupLevel backup)
    {
        if (kilowattHours is < 0 or > MostKilowattHours)
        {
            return Result.Failure<Battery>(DomainErrors.SolarInputOutOfRange("battery", 0, MostKilowattHours));
        }

        if (!Enum.IsDefined(backup))
        {
            return Result.Failure<Battery>(DomainErrors.UnknownChoice());
        }

        return Result.Success(kilowattHours == 0 ? None : new Battery(kilowattHours, backup));
    }
}
