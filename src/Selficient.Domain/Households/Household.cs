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

/// <summary>Everything the rules engine knows about a household and its home.</summary>
public sealed record HouseholdProfile(Household Household, Dwelling Dwelling)
{
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
