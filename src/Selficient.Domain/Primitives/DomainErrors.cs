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

    /// <summary>A household larger than the plan can sensibly size for.</summary>
    public static Fault HouseholdTooLarge() =>
        new("household.too-large", "A plan covers up to twenty people. For more than that, make a plan for each household.");

    /// <summary>More children than people, or a negative count.</summary>
    public static Fault ChildrenOutOfRange() =>
        new("household.children", "The number of children must be between zero and the number of people.");

    /// <summary>A storey count outside anything a house has.</summary>
    public static Fault StoreysOutOfRange() =>
        new("dwelling.storeys", "A home has between one and six storeys.");

    /// <summary>A choice that is not one of the options offered.</summary>
    public static Fault UnknownChoice() =>
        new("dwelling.unknown-choice", "One of the answers about the home was not one of the options offered.");

    /// <summary>An elevation that is not in the British Isles.</summary>
    public static Fault ElevationOutOfRange() =>
        new("dwelling.elevation", "The height above sea level must be between minus ten and one thousand four hundred metres.");

    /// <summary>Not a UK postcode.</summary>
    public static Fault InvalidPostcode() =>
        new("postcode.invalid", "That does not look like a UK postcode. Check it and try again.");

    /// <summary>The postcode is well formed but no such place exists.</summary>
    public static Fault PostcodeNotFound() =>
        new("postcode.not-found", "We couldn't find that postcode. Check it and try again.");

    /// <summary>The lookup service did not answer in time.</summary>
    public static Fault LookupUnavailable() =>
        new("lookup.unavailable", "We couldn't reach the postcode service just now. Wait a moment and try again.");

    /// <summary>Two rules produced the same action.</summary>
    public static Fault DuplicateAction(string actionId) =>
        new("plan.duplicate-action", string.Create(CultureInfo.InvariantCulture, $"The plan already contains the action '{actionId}'."));
}
