using Selficient.Domain.Primitives;

namespace Selficient.Domain.Measures;

/// <summary>A volume of water in litres, never negative.</summary>
public readonly record struct Litres
{
    /// <summary>
    /// The government's Be Ready figure (22 Oct 2026): three litres of drinking water per person per
    /// day. Cooking and hygiene are on top of this and tracked separately.
    /// </summary>
    public static decimal DrinkingPerPersonPerDay => 3m;

    private Litres(decimal value) => Value = value;

    /// <summary>The volume in litres.</summary>
    public decimal Value { get; }

    /// <summary>An empty container.</summary>
    public static Litres Zero => new(0m);

    /// <summary>Creates a volume, refusing negative values.</summary>
    public static Result<Litres> Create(decimal value) =>
        value < 0m
            ? Result.Failure<Litres>(DomainErrors.NegativeQuantity("litres"))
            : Result.Success(new Litres(value));

    /// <summary>The combined volume of two stores.</summary>
    public Litres Add(Litres other) => new(Value + other.Value);

    /// <summary>The combined volume of two stores.</summary>
    public static Litres operator +(Litres left, Litres right) => left.Add(right);

    /// <summary>How many days this volume covers as drinking water for a household.</summary>
    public Days DaysOfDrinkingWaterFor(HouseholdSize household) =>
        Days.Create(Value / (household.Count * DrinkingPerPersonPerDay)).Value;
}
