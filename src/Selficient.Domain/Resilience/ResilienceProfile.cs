using Selficient.Domain.Households;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Resilience;

/// <summary>What kind of car, because an electric one is a source in a cut and a petrol one is a tank.</summary>
public enum CarKind
{
    /// <summary>No car.</summary>
    None,

    /// <summary>Petrol or diesel.</summary>
    Petrol,

    /// <summary>Electric, charged from the house.</summary>
    Electric,

    /// <summary>Electric with vehicle-to-load or vehicle-to-home, so it can power the house.</summary>
    ElectricWithOutput,
}

/// <summary>Whether the household grows any food.</summary>
public enum Growing
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Raised beds or a vegetable patch.</summary>
    RaisedBeds,

    /// <summary>A full allotment plot.</summary>
    Allotment,
}

/// <summary>The kinds of thing only this household relies on.</summary>
public enum DependencyKind
{
    /// <summary>A device that needs power, such as a CPAP or an oxygen concentrator.</summary>
    PoweredDevice,

    /// <summary>A medicine with so many days in the house.</summary>
    Medicine,

    /// <summary>A supply with so many days in the house, such as formula or stoma bags.</summary>
    Supply,
}

/// <summary>Something only this household relies on, typed in by the household and kept in its browser.</summary>
public sealed record Dependency
{
    /// <summary>The most a household can add.</summary>
    public const int MaxPerHousehold = 12;

    /// <summary>The longest name accepted.</summary>
    public const int MaxNameLength = 40;

    private Dependency(string name, Rung rung, DependencyKind kind, decimal watts, decimal hoursPerDay, int daysOfStock, bool mustStayCold)
    {
        Name = name;
        Rung = rung;
        Kind = kind;
        Watts = watts;
        HoursPerDay = hoursPerDay;
        DaysOfStock = daysOfStock;
        MustStayCold = mustStayCold;
    }

    /// <summary>What the household calls it.</summary>
    public string Name { get; }

    /// <summary>Which rung it sits on.</summary>
    public Rung Rung { get; }

    /// <summary>Device, medicine or supply.</summary>
    public DependencyKind Kind { get; }

    /// <summary>Power drawn, for a device.</summary>
    public decimal Watts { get; }

    /// <summary>Hours a day it runs, for a device.</summary>
    public decimal HoursPerDay { get; }

    /// <summary>Days of stock at home, for a medicine or supply.</summary>
    public int DaysOfStock { get; }

    /// <summary>Whether it needs the fridge.</summary>
    public bool MustStayCold { get; }

    /// <summary>Energy a day, for a device.</summary>
    public decimal WhPerDay => Watts * HoursPerDay;

    /// <summary>Validates and creates. Every field is range-checked because it comes from a form.</summary>
    public static Result<Dependency> Create(string name, Rung rung, DependencyKind kind, decimal watts, decimal hoursPerDay, int daysOfStock, bool mustStayCold)
    {
        ArgumentNullException.ThrowIfNull(name);
        string trimmed = name.Trim();
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            return Result.Failure<Dependency>(DomainErrors.DependencyNameOutOfRange());
        }

        if (!Enum.IsDefined(rung) || !Enum.IsDefined(kind))
        {
            return Result.Failure<Dependency>(DomainErrors.UnknownChoice());
        }

        if (kind == DependencyKind.PoweredDevice && (watts is <= 0 or > 3000 || hoursPerDay is <= 0 or > 24))
        {
            return Result.Failure<Dependency>(DomainErrors.DependencyPowerOutOfRange());
        }

        if (kind != DependencyKind.PoweredDevice && daysOfStock is < 0 or > 365)
        {
            return Result.Failure<Dependency>(DomainErrors.DependencyStockOutOfRange());
        }

        return Result.Success(new Dependency(trimmed, rung, kind, kind == DependencyKind.PoweredDevice ? watts : 0m, kind == DependencyKind.PoweredDevice ? hoursPerDay : 0m, kind == DependencyKind.PoweredDevice ? 0 : daysOfStock, mustStayCold));
    }
}

/// <summary>
/// The household as the resilience engine sees it: the plan's profile plus the facts the first
/// wizard did not ask. Defaults are chosen so a v1 profile produces a sensible board.
/// </summary>
public sealed record ResilienceProfile
{
    /// <summary>The highest floor a flat can be on, as the wizard asks it.</summary>
    public const int MaxFloor = Circumstances.MaxFloor;

    private static readonly Dictionary<WaterSupply, string> TapFixtures = new()
    {
        [WaterSupply.Mains] = "mains-tap",
        [WaterSupply.OwnPumped] = "own-water-pumped",
        [WaterSupply.OwnGravity] = "own-water-gravity",
    };

    private static readonly Dictionary<HeatingType, string> HeatingFixtures = new()
    {
        [HeatingType.GasBoiler] = "gas-boiler",
        [HeatingType.HeatPump] = "heat-pump",
        [HeatingType.Electric] = "electric-heating",
        [HeatingType.Wood] = "wood-stove",
        [HeatingType.Oil] = "oil-or-lpg-boiler",
        [HeatingType.Lpg] = "oil-or-lpg-boiler",
    };

    private ResilienceProfile(HouseholdProfile profile, bool hasChestFreezer, bool hasWoodStove, bool hasWaterButt, Growing growing, CarKind car, int infants, int medicineDaysInStock, int floor, IReadOnlyList<Dependency> dependencies)
    {
        Profile = profile;
        HasChestFreezer = hasChestFreezer;
        HasWoodStove = hasWoodStove;
        HasWaterButt = hasWaterButt;
        Growing = growing;
        Car = car;
        Infants = infants;
        MedicineDaysInStock = medicineDaysInStock;
        Floor = floor;
        Dependencies = dependencies;
    }

    /// <summary>The figures this household's board is worked out from, which are the ones its plan was.</summary>
    public Selficient.Domain.Parameters.ParameterSet Figures => Profile.Figures;

    /// <summary>The household and the dwelling from the plan.</summary>
    public HouseholdProfile Profile { get; }

    /// <summary>A chest freezer as well as the fridge-freezer.</summary>
    public bool HasChestFreezer { get; }

    /// <summary>A wood or multi-fuel stove, which is warmth without the mains.</summary>
    public bool HasWoodStove { get; }

    /// <summary>A water butt in the garden.</summary>
    public bool HasWaterButt { get; }

    /// <summary>Whether the household grows food.</summary>
    public Growing Growing { get; }

    /// <summary>What kind of car.</summary>
    public CarKind Car { get; }

    /// <summary>Children under two.</summary>
    public int Infants { get; }

    /// <summary>Days of regular medicines in the house.</summary>
    public int MedicineDaysInStock { get; }

    /// <summary>For a flat, the floor it is on. Above the third, water is pumped and stops with the power.</summary>
    public int Floor { get; }

    /// <summary>The things only this household relies on.</summary>
    public IReadOnlyList<Dependency> Dependencies { get; }

    /// <summary>The household, for short.</summary>
    public Household Household => Profile.Household;

    /// <summary>The dwelling, for short.</summary>
    public Dwelling Dwelling => Profile.Dwelling;

    /// <summary>A flat above the third floor relies on pumps for its water.</summary>
    public bool WaterIsPumped => Dwelling.Type == DwellingType.Flat && Floor >= 4;

    /// <summary>A vulnerable person or an infant cannot be kept well under blankets alone.</summary>
    public bool NeedsFullWarmth => Household.HasVulnerablePerson || Infants > 0;

    private static readonly Dictionary<Stairlift, string[]> StairliftFixtures = new()
    {
        [Stairlift.None] = [],
        [Stairlift.Battery] = ["stairlift-charger", "stairlift-batteries"],
        [Stairlift.MainsOnly] = ["stairlift-on-the-mains"],
        [Stairlift.NotKnown] = ["stairlift-on-the-mains"],
    };

    /// <summary>Whether a provider can be had at all in this house.</summary>
    public bool CanHave(Placement where) => where switch
    {
        Placement.Anywhere => true,
        Placement.NeedsGarden => Dwelling.HasGarden,
        Placement.OutdoorsOnly => Dwelling.HasGarden || Dwelling.Type != DwellingType.Flat,
        Placement.NeedsOwnRoof => Dwelling.HasOwnRoof,
        Placement.NeedsCar => Car != CarKind.None,
        _ => Car == CarKind.ElectricWithOutput,
    };

    /// <summary>
    /// The providers this house has by its nature rather than by a tick: the tap, the shops, the
    /// fridge, whatever heats it, the hob, the router, the phones, blankets, what is in the kettle
    /// and the fridge, the torches on the phones, and the car.
    /// </summary>
    public IReadOnlySet<string> Fixtures
    {
        get
        {
            HashSet<string> fixtures =
            [
                "shops", "deliveries", "pharmacy", "cash-machines", "fridge-freezer", "closed-freezer",
                "router", "phones", "phone-charging", "mains-lighting", "blankets", "kettle-water", "fridge-food", "phone-torches",
                "medicine-stock", "cash-at-home", "books-and-games", "television", "washing-machine", "laptop-and-screen",
                "supplies-at-home", "microwave", "bin-bags",
            ];
            fixtures.Add(TapFixtures[Profile.Circumstances.Water]);
            fixtures.UnionWith(StairliftFixtures[Profile.Circumstances.Stairlift]);
            fixtures.Add(HeatingFixtures[Dwelling.Heating]);
            if (Dwelling.Heating is HeatingType.Electric or HeatingType.Wood)
            {
                fixtures.Add("immersion-heater");
            }

            if (HasWoodStove)
            {
                fixtures.Add("wood-stove");
                fixtures.Add("log-deliveries");
            }

            if (Dwelling.Hob == HobType.Gas)
            {
                fixtures.Add("gas-hob");
            }
            else if (Dwelling.Hob == HobType.Electric)
            {
                fixtures.Add("electric-hob");
            }

            if (HasChestFreezer)
            {
                fixtures.Add("chest-freezer");
            }

            if (HasWaterButt)
            {
                fixtures.Add("water-butt");
            }

            if (Growing == Growing.RaisedBeds)
            {
                fixtures.Add("raised-beds");
            }
            else if (Growing == Growing.Allotment)
            {
                fixtures.Add("allotment");
            }

            switch (Car)
            {
                case CarKind.Petrol:
                    fixtures.Add("petrol-car");
                    fixtures.Add("forecourt");
                    break;
                case CarKind.Electric:
                    fixtures.Add("electric-car");
                    fixtures.Add("ev-charging");
                    break;
                case CarKind.ElectricWithOutput:
                    fixtures.Add("electric-car");
                    fixtures.Add("ev-charging");
                    fixtures.Add("ev-output");
                    break;
                default:
                    break;
            }

            return fixtures;
        }
    }

    /// <summary>A profile from the plan's own answers, with the new facts at their defaults.</summary>
    public static ResilienceProfile FromProfile(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new ResilienceProfile(profile, hasChestFreezer: false, hasWoodStove: profile.Dwelling.Heating == HeatingType.Wood, hasWaterButt: false, Growing.None, profile.Dwelling.HasCar ? CarKind.Petrol : CarKind.None, infants: 0, medicineDaysInStock: 7, floor: 0, []);
    }

    /// <summary>Validates and creates the full profile.</summary>
    public static Result<ResilienceProfile> Create(HouseholdProfile profile, bool hasChestFreezer, bool hasWoodStove, bool hasWaterButt, Growing growing, CarKind car, int infants, int medicineDaysInStock, int floor, IReadOnlyList<Dependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(dependencies);
        if (!Enum.IsDefined(growing) || !Enum.IsDefined(car))
        {
            return Result.Failure<ResilienceProfile>(DomainErrors.UnknownChoice());
        }

        if (infants < 0 || infants > profile.Household.Children)
        {
            return Result.Failure<ResilienceProfile>(DomainErrors.InfantsOutOfRange());
        }

        if (medicineDaysInStock is < 0 or > 365)
        {
            return Result.Failure<ResilienceProfile>(DomainErrors.DependencyStockOutOfRange());
        }

        if (floor is < 0 or > MaxFloor)
        {
            return Result.Failure<ResilienceProfile>(DomainErrors.FloorOutOfRange());
        }

        if (dependencies.Count > Dependency.MaxPerHousehold)
        {
            return Result.Failure<ResilienceProfile>(DomainErrors.TooManyDependencies());
        }

        if (car != CarKind.None && !profile.Dwelling.HasCar)
        {
            return Result.Failure<ResilienceProfile>(DomainErrors.CarContradiction());
        }

        return Result.Success(new ResilienceProfile(profile, hasChestFreezer, hasWoodStove || profile.Dwelling.Heating == HeatingType.Wood, hasWaterButt, growing, car, infants, medicineDaysInStock, floor, dependencies));
    }
}
