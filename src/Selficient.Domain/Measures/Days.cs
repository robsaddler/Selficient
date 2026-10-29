using Selficient.Domain.Primitives;

namespace Selficient.Domain.Measures;

/// <summary>A duration in days, never negative. Fractions are allowed: half a day is honest.</summary>
public readonly record struct Days
{
    private Days(decimal value) => Value = value;

    /// <summary>The number of days.</summary>
    public decimal Value { get; }

    /// <summary>No time at all.</summary>
    public static Days Zero => new(0m);

    /// <summary>Creates a duration, refusing negative values.</summary>
    public static Result<Days> Create(decimal value) =>
        value < 0m
            ? Result.Failure<Days>(DomainErrors.NegativeQuantity("days"))
            : Result.Success(new Days(value));

    /// <summary>The shorter of two durations. Readiness is always the weakest link.</summary>
    public static Days Min(Days left, Days right) => left.Value <= right.Value ? left : right;
}
