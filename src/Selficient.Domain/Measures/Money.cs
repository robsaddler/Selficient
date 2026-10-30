using Selficient.Domain.Primitives;

namespace Selficient.Domain.Measures;

/// <summary>Pounds sterling held as integer pence, so sums never drift.</summary>
public readonly record struct Money
{
    private Money(long pence) => Pence = pence;

    /// <summary>The amount in pence.</summary>
    public long Pence { get; }

    /// <summary>The amount in pounds, for display.</summary>
    public decimal Pounds => Pence / 100m;

    /// <summary>Nothing to pay.</summary>
    public static Money Zero => new(0);

    /// <summary>Creates an amount from pounds, refusing negatives.</summary>
    public static Result<Money> FromPounds(decimal pounds) =>
        pounds < 0m
            ? Result.Failure<Money>(DomainErrors.NegativeQuantity("pounds"))
            : Result.Success(new Money(checked((long)Math.Round(pounds * 100m, MidpointRounding.AwayFromZero))));

    /// <summary>The sum of two amounts.</summary>
    public Money Add(Money other) => new(checked(Pence + other.Pence));

    /// <summary>The sum of two amounts.</summary>
    public static Money operator +(Money left, Money right) => left.Add(right);
}

/// <summary>A cost estimate from the cheapest sensible option to the dearest.</summary>
public readonly record struct MoneyRange(Money From, Money To)
{
    /// <summary>Free.</summary>
    public static MoneyRange Free => new(Money.Zero, Money.Zero);

    /// <summary>A range given in pounds. Programming error if negative or inverted, so it throws.</summary>
    public static MoneyRange Pounds(decimal from, decimal to)
    {
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "The top of a cost range cannot be below the bottom.");
        }

        return new MoneyRange(Money.FromPounds(from).Value, Money.FromPounds(to).Value);
    }

    /// <summary>The sum of two ranges.</summary>
    public MoneyRange Add(MoneyRange other) => new(From + other.From, To + other.To);

    /// <summary>The sum of two ranges.</summary>
    public static MoneyRange operator +(MoneyRange left, MoneyRange right) => left.Add(right);
}
