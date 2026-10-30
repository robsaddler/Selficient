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

/// <summary>Everything the rules engine knows about a household and its home.</summary>
public sealed record HouseholdProfile(Household Household, Dwelling Dwelling);
