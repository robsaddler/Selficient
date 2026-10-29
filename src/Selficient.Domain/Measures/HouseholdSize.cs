using Selficient.Domain.Primitives;

namespace Selficient.Domain.Measures;

/// <summary>The number of people a plan must cover. At least one.</summary>
public readonly record struct HouseholdSize
{
    private HouseholdSize(int count) => Count = count;

    /// <summary>How many people live in the house.</summary>
    public int Count { get; }

    /// <summary>Creates a household size, refusing zero or fewer people.</summary>
    public static Result<HouseholdSize> Create(int count) =>
        count < 1
            ? Result.Failure<HouseholdSize>(DomainErrors.EmptyHousehold())
            : Result.Success(new HouseholdSize(count));
}
