using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Households;

/// <summary>The kind of home, which decides what the roof, garden and storage can hold.</summary>
public enum DwellingType
{
    /// <summary>A flat or maisonette: no roof of its own, little storage, usually no garden.</summary>
    Flat,

    /// <summary>A terraced house.</summary>
    Terrace,

    /// <summary>A semi-detached house.</summary>
    SemiDetached,

    /// <summary>A detached house.</summary>
    Detached,

    /// <summary>A single-storey house.</summary>
    Bungalow,
}

/// <summary>How the home is heated, which decides what a power cut takes away.</summary>
public enum HeatingType
{
    /// <summary>Mains gas boiler and radiators. Needs electricity to run.</summary>
    GasBoiler,

    /// <summary>Storage heaters, panel heaters or electric boiler.</summary>
    Electric,

    /// <summary>Air or ground source heat pump.</summary>
    HeatPump,

    /// <summary>Oil boiler. Needs electricity to run.</summary>
    Oil,

    /// <summary>Liquefied petroleum gas boiler. Needs electricity to run.</summary>
    Lpg,

    /// <summary>Wood or multi-fuel stove as the main heat.</summary>
    Wood,
}

/// <summary>What the cooker runs on.</summary>
public enum HobType
{
    /// <summary>Gas hob, which still works in a power cut when lit by hand.</summary>
    Gas,

    /// <summary>Electric or induction hob.</summary>
    Electric,

    /// <summary>No hob, or unknown.</summary>
    None,
}

/// <summary>Flood risk as reported by the national agencies, or not yet known.</summary>
public enum FloodRisk
{
    /// <summary>Not looked up yet.</summary>
    Unknown,

    /// <summary>Less than a 0.1% chance a year.</summary>
    VeryLow,

    /// <summary>Between 0.1% and 1% a year.</summary>
    Low,

    /// <summary>Between 1% and 3.3% a year.</summary>
    Medium,

    /// <summary>More than a 3.3% chance a year.</summary>
    High,
}

/// <summary>The physical facts about the home that the rules need. No free text, by design.</summary>
public sealed record Dwelling
{
    /// <summary>Homes have between one and six storeys.</summary>
    public const int MaxStoreys = 6;

    private Dwelling(
        DwellingType type,
        int storeys,
        HeatingType heating,
        HobType hob,
        bool hasCar,
        bool hasGarden,
        bool hasDecking,
        bool hasTallTreeNearHouse,
        Elevation? elevation,
        FloodRisk floodRisk)
    {
        Type = type;
        Storeys = storeys;
        Heating = heating;
        Hob = hob;
        HasCar = hasCar;
        HasGarden = hasGarden;
        HasDecking = hasDecking;
        HasTallTreeNearHouse = hasTallTreeNearHouse;
        Elevation = elevation;
        FloodRisk = floodRisk;
    }

    /// <summary>The kind of home.</summary>
    public DwellingType Type { get; }

    /// <summary>Storeys, including rooms in the roof.</summary>
    public int Storeys { get; }

    /// <summary>Main heating.</summary>
    public HeatingType Heating { get; }

    /// <summary>The hob.</summary>
    public HobType Hob { get; }

    /// <summary>Whether the household has a car to keep fuelled and to charge from.</summary>
    public bool HasCar { get; }

    /// <summary>Whether there is a garden for a water butt and a firebreak.</summary>
    public bool HasGarden { get; }

    /// <summary>Timber decking that traps leaf litter.</summary>
    public bool HasDecking { get; }

    /// <summary>A tree tall enough to reach the house if it fell.</summary>
    public bool HasTallTreeNearHouse { get; }

    /// <summary>Height above sea level, once looked up.</summary>
    public Elevation? Elevation { get; }

    /// <summary>Flood risk, once looked up.</summary>
    public FloodRisk FloodRisk { get; }

    /// <summary>True for any home with a roof of its own to put panels on.</summary>
    public bool HasOwnRoof => Type != DwellingType.Flat;

    /// <summary>True when the heating stops the moment the electricity does.</summary>
    public bool HeatingNeedsElectricity => Heating is HeatingType.GasBoiler or HeatingType.Electric or HeatingType.HeatPump or HeatingType.Oil or HeatingType.Lpg;

    /// <summary>True when the house sits on an exposed hillside.</summary>
    public bool IsExposed => Elevation is { IsExposed: true };

    /// <summary>Creates a dwelling, refusing storey counts no home has.</summary>
    public static Result<Dwelling> Create(
        DwellingType type,
        int storeys,
        HeatingType heating,
        HobType hob,
        bool hasCar,
        bool hasGarden,
        bool hasDecking,
        bool hasTallTreeNearHouse,
        Elevation? elevation,
        FloodRisk floodRisk)
    {
        if (storeys is < 1 or > MaxStoreys)
        {
            return Result.Failure<Dwelling>(DomainErrors.StoreysOutOfRange());
        }

        // A model binder will happily deliver (FloodRisk)99; closed sets stay closed here.
        if (!Enum.IsDefined(type) || !Enum.IsDefined(heating) || !Enum.IsDefined(hob) || !Enum.IsDefined(floodRisk))
        {
            return Result.Failure<Dwelling>(DomainErrors.UnknownChoice());
        }

        return Result.Success(new Dwelling(type, storeys, heating, hob, hasCar, hasGarden, hasDecking, hasTallTreeNearHouse, elevation, floodRisk));
    }
}
