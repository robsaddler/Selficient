using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Households;

/// <summary>The people a plan must cover. No names, no free text, by design.</summary>
public sealed record Household
{
    /// <summary>A plan sizes for up to twenty people; beyond that it is two households.</summary>
    public const int MaxPeople = 20;

    private Household(HouseholdSize size, int children, bool hasVulnerablePerson, bool hasPets, bool runsBusinessFromHome)
    {
        Size = size;
        Children = children;
        HasVulnerablePerson = hasVulnerablePerson;
        HasPets = hasPets;
        RunsBusinessFromHome = runsBusinessFromHome;
    }

    /// <summary>Everyone who lives there.</summary>
    public HouseholdSize Size { get; }

    /// <summary>How many of them are children.</summary>
    public int Children { get; }

    /// <summary>Anyone with a medical, mobility or age-related need for power, water or warmth.</summary>
    public bool HasVulnerablePerson { get; }

    /// <summary>Pets need food and water in the plan too.</summary>
    public bool HasPets { get; }

    /// <summary>Someone's income depends on being online at home.</summary>
    public bool RunsBusinessFromHome { get; }

    /// <summary>True when the utilities' Priority Services Registers would accept the household.</summary>
    public bool QualifiesForPriorityServices => HasVulnerablePerson || Children > 0;

    /// <summary>Creates a household, refusing impossible counts.</summary>
    public static Result<Household> Create(int people, int children, bool hasVulnerablePerson, bool hasPets, bool runsBusinessFromHome)
    {
        if (people > MaxPeople)
        {
            return Result.Failure<Household>(DomainErrors.HouseholdTooLarge());
        }

        return HouseholdSize.Create(people).Bind(size =>
            children < 0 || children > size.Count
                ? Result.Failure<Household>(DomainErrors.ChildrenOutOfRange())
                : Result.Success(new Household(size, children, hasVulnerablePerson, hasPets, runsBusinessFromHome)));
    }
}

/// <summary>
/// What the later questions said: the things a household already has, and who is in it, that decide
/// which actions are worth offering. A plan that tells the owner of an electric car to keep half a
/// tank, or sells a water butt to a garden that has one, has not listened to its own form.
/// </summary>
/// <param name="ElectricCar">The car, where there is one, is electric.</param>
/// <param name="WoodStove">There is a wood or multi-fuel stove, whatever the main heating is.</param>
/// <param name="WaterButt">There is already a water butt.</param>
/// <param name="Infants">Children under two.</param>
public sealed record Particulars(bool ElectricCar, bool WoodStove, bool WaterButt, int Infants)
{
    /// <summary>Nothing said: a petrol car where there is one, no stove, no butt, no infants.</summary>
    public static Particulars None { get; } = new(false, false, false, 0);

    /// <summary>Creates the particulars, refusing more infants than there are children.</summary>
    public static Result<Particulars> Create(Household household, bool electricCar, bool woodStove, bool waterButt, int infants)
    {
        ArgumentNullException.ThrowIfNull(household);
        return infants < 0 || infants > household.Children
            ? Result.Failure<Particulars>(DomainErrors.InfantsOutOfRange())
            : Result.Success(new Particulars(electricCar, woodStove, waterButt, infants));
    }
}

/// <summary>Whose home it is, which decides what a household may change about it.</summary>
public enum Tenure
{
    /// <summary>The household owns it, and the roof, the boiler and the trees are its own to change.</summary>
    Owner,

    /// <summary>The household rents it, and those things are the landlord's.</summary>
    Tenant,
}

/// <summary>Where the water comes from.</summary>
public enum WaterSupply
{
    /// <summary>A water company's main.</summary>
    Mains,

    /// <summary>The home's own borehole or well, lifted by an electric pump.</summary>
    OwnPumped,

    /// <summary>The home's own spring or tank uphill, which runs down to the tap without a pump.</summary>
    OwnGravity,
}

/// <summary>
/// How the household holds its home and what it is connected to: the answers that decide whether an
/// action is the household's to take at all. A tenant cannot put panels on the landlord's roof, a
/// house on a borehole has no water company to ring, and a flood kit for a ground floor is no use
/// on the third.
/// </summary>
/// <param name="Tenure">Owned or rented.</param>
/// <param name="Water">Where the water comes from.</param>
/// <param name="OwnDrains">The waste goes to a septic tank, a cesspit or a treatment plant, not a sewer.</param>
/// <param name="PrepaymentMeter">Gas or electricity is paid for through a prepayment meter.</param>
/// <param name="NeedsHelpToLeave">Someone would need help to get downstairs or out of the home.</param>
/// <param name="Cellar">There is a cellar or basement.</param>
/// <param name="SleepsDownstairs">Someone sleeps on the ground floor or below it.</param>
/// <param name="Floor">For a flat, the floor it is on; the ground is nought.</param>
public sealed record Circumstances(Tenure Tenure, WaterSupply Water, bool OwnDrains, bool PrepaymentMeter, bool NeedsHelpToLeave, bool Cellar, bool SleepsDownstairs, int Floor)
{
    /// <summary>The highest floor a flat can be on, as the wizard asks it.</summary>
    public const int MaxFloor = 40;

    /// <summary>Nothing said: an owner on the mains and the sewer, with a credit meter, on the ground.</summary>
    public static Circumstances None { get; } = new(Tenure.Owner, WaterSupply.Mains, false, false, false, false, false, 0);

    /// <summary>Creates the circumstances, refusing a choice that is not on the list and a floor no block has.</summary>
    public static Result<Circumstances> Create(Tenure tenure, WaterSupply water, bool ownDrains, bool prepaymentMeter, bool needsHelpToLeave, bool cellar, bool sleepsDownstairs, int floor)
    {
        if (!Enum.IsDefined(tenure) || !Enum.IsDefined(water))
        {
            return Result.Failure<Circumstances>(DomainErrors.UnknownChoice());
        }

        return floor is < 0 or > MaxFloor
            ? Result.Failure<Circumstances>(DomainErrors.FloorOutOfRange())
            : Result.Success(new Circumstances(tenure, water, ownDrains, prepaymentMeter, needsHelpToLeave, cellar, sleepsDownstairs, floor));
    }
}

/// <summary>Everything the rules engine knows about a household and its home.</summary>
public sealed record HouseholdProfile(Household Household, Dwelling Dwelling)
{
    /// <summary>How the household holds its home and what it is connected to.</summary>
    public Circumstances Circumstances { get; init; } = Circumstances.None;

    /// <summary>The home is rented, so the roof, the boiler and the trees are the landlord's.</summary>
    public bool IsTenant => Circumstances.Tenure == Tenure.Tenant;

    /// <summary>The water is the home's own and no water company's.</summary>
    public bool HasOwnWater => Circumstances.Water != WaterSupply.Mains;

    /// <summary>A flat above the ground floor, which flood water does not reach.</summary>
    public bool IsAboveGround => Dwelling.Type == DwellingType.Flat && Circumstances.Floor > 0;

    /// <summary>Someone sleeps where flood water would reach first: a home on one floor, or a bed downstairs.</summary>
    public bool SleepsAtGroundLevel => !IsAboveGround && (Dwelling.Storeys == 1 || Circumstances.SleepsDownstairs);

    /// <summary>The utilities' Priority Services Registers would accept the household.</summary>
    public bool QualifiesForPriorityServices => Household.QualifiesForPriorityServices || Circumstances.NeedsHelpToLeave;

    /// <summary>The nation the home is in, once the postcode has been found.</summary>
    public Nation Nation { get; init; }

    /// <summary>Which water the flood band is about, where a flood map said.</summary>
    public FloodWater FloodFrom { get; init; }

    /// <summary>What the later questions said.</summary>
    public Particulars Particulars { get; init; } = Particulars.None;

    /// <summary>A car that runs on a battery.</summary>
    public bool HasElectricCar => Dwelling.HasCar && Particulars.ElectricCar;

    /// <summary>A car that runs on a tank.</summary>
    public bool HasPetrolCar => Dwelling.HasCar && !Particulars.ElectricCar;

    /// <summary>A stove that burns logs, as the main heating or beside it.</summary>
    public bool HasWoodStove => Particulars.WoodStove || Dwelling.Heating == HeatingType.Wood;
}
