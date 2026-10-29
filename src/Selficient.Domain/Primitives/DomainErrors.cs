using System.Globalization;

namespace Selficient.Domain.Primitives;

/// <summary>Every expected domain failure, so wording lives in one place and tests can pin it.</summary>
public static class DomainErrors
{
    /// <summary>A quantity that cannot be negative was given a negative value.</summary>
    public static Fault NegativeQuantity(string quantity) =>
        new("quantity.negative", string.Create(CultureInfo.InvariantCulture, $"The number of {quantity} cannot be less than zero."));

    /// <summary>A household must contain at least one person.</summary>
    public static Fault EmptyHousehold() =>
        new("household.empty", "A household needs at least one person in it.");
}
