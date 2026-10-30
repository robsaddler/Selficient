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

    /// <summary>A solar input outside the range the model is built for.</summary>
    public static Fault SolarInputOutOfRange(string what, int lowest, int highest) =>
        new("solar.out-of-range", $"The {what} needs to be between {lowest} and {highest}.");

    /// <summary>An upload that is not an image we can read.</summary>
    public static Fault ImageInvalid() =>
        new("image.invalid", "That file is not a photo we can read. Use a JPEG, PNG or WebP straight from the camera.");

    /// <summary>An upload over the byte or pixel cap.</summary>
    public static Fault ImageTooLarge() =>
        new("image.too-large", "That photo is too large. Anything up to 10 MB and 24 megapixels is fine.");

    /// <summary>No analyst is configured, or the one that is could not answer.</summary>
    public static Fault AiUnavailable() =>
        new("ai.unavailable", "Photo help is not available just now. Everything else on the page still works.");

    /// <summary>The household has used its free analyses for the month.</summary>
    public static Fault AiAllowanceUsed(int perMonth) =>
        new("ai.allowance", $"You have used this month's {perMonth} free photo analyses. They come back on the first of the month, or use your own key under the advanced settings.");

    /// <summary>The whole site has used its daily cap.</summary>
    public static Fault AiBusy() =>
        new("ai.busy", "Photo help is busy today. Try again tomorrow, or use your own key under the advanced settings.");

    /// <summary>The yield service answered, but not with a usable year.</summary>
    public static Fault YieldUnavailable() =>
        new("solar.yield-unavailable", "We couldn't get the sunshine figures for your roof just now. Wait a moment and try again.");

    /// <summary>A catalogue entry with missing or overlong text.</summary>
    public static Fault KitPickTextOutOfRange() =>
        new("kit.text", "A kit entry needs a name and a retailer, each under 160 characters, and a note no longer than that.");

    /// <summary>A catalogue link that is not absolute https.</summary>
    public static Fault KitPickLinkNotHttps() =>
        new("kit.link", "A kit entry's link must be an absolute https address.");

    /// <summary>A catalogue entry naming an action id that is not well formed.</summary>
    public static Fault KitPickActionInvalid() =>
        new("kit.action", "A kit entry must name a plan action by its id.");

    /// <summary>Two rules produced the same action.</summary>
    public static Fault DuplicateAction(string actionId) =>
        new("plan.duplicate-action", string.Create(CultureInfo.InvariantCulture, $"The plan already contains the action '{actionId}'."));

    /// <summary>A dependency name is missing or too long.</summary>
    public static Fault DependencyNameOutOfRange() =>
        new("dependency.name", "Give the thing you rely on a short name, up to forty characters.");

    /// <summary>A device's power or hours are outside what a house can run.</summary>
    public static Fault DependencyPowerOutOfRange() =>
        new("dependency.power", "A device draws between 1 and 3,000 watts and runs between 1 and 24 hours a day.");

    /// <summary>Days of stock are outside a year.</summary>
    public static Fault DependencyStockOutOfRange() =>
        new("dependency.stock", "Days of stock at home must be between 0 and 365.");

    /// <summary>More infants than children.</summary>
    public static Fault InfantsOutOfRange() =>
        new("household.infants", "The number of infants must be between zero and the number of children.");

    /// <summary>A floor outside what a UK block has.</summary>
    public static Fault FloorOutOfRange() =>
        new("dwelling.floor", "The floor must be between 0 for the ground floor and 40.");

    /// <summary>Too many household-added dependencies.</summary>
    public static Fault TooManyDependencies() =>
        new("dependency.too-many", "Up to twelve things you rely on can be added. Combine the small ones.");

    /// <summary>A car kind given for a household that said it has no car.</summary>
    public static Fault CarContradiction() =>
        new("dwelling.car", "The plan said there is no car, so the car cannot have a kind. Change the answer about the car first.");
}
