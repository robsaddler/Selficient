namespace Selficient.Web.Pages.Street;

/// <summary>
/// Every word of the street group pages, in one place. The pages are the same for everyone: what a
/// street shares is encrypted in the browser, so the server draws the words and the script puts
/// the street into them. The script takes its sentences from here, through the page, and never
/// writes one of its own.
/// </summary>
public static class StreetCopy
{
    /// <summary>What a home can offer its neighbours: a closed list, each a few plain words.</summary>
    public static IReadOnlyList<(string Id, string Words, string Between)> Offers { get; } =
    [
        ("warm-room", "A warm room", "with a warm room"),
        ("hot-food", "A hob or stove that works without power", "with a hob or stove that works without power"),
        ("phone-charging", "Somewhere to charge a phone", "where a phone can be charged"),
        ("cold-storage", "Room in a fridge that is still running", "with room in a fridge that is still running"),
        ("drinking-water", "Drinking water to spare", "with drinking water to spare"),
        ("light", "A lantern or torch to lend", "with a lantern or torch to lend"),
        ("news", "A radio that works without power", "with a radio that works without power"),
        ("first-aid", "A first-aid kit", "with a first-aid kit"),
        ("lift", "A car with fuel or charge", "with a car that has fuel or charge"),
    ];

    /// <summary>
    /// What in a household's own plan says it could make an offer: the actions it holds, and the
    /// answers it gave about its home. The plan is in the household's browser and the group is
    /// sealed there, so it is the browser that looks, with these as its instructions; the server
    /// never learns which of them was true. A sign is a prompt and nothing more: the offer stays
    /// at nobody until the household chooses otherwise.
    /// </summary>
    public static IReadOnlyDictionary<string, PlanSign> PlanSigns { get; } = new Dictionary<string, PlanSign>(StringComparer.Ordinal)
    {
        ["warm-room"] = new("portable-gas-heater", "HasWoodStove=true HasGasHeater=true Heating=Wood"),
        ["hot-food"] = new("butane-cartridge-stove", "Hob=Gas HasWoodStove=true Heating=Wood"),
        ["phone-charging"] = new("portable-power-station power-banks rooftop-solar-and-battery", "Car=ElectricWithOutput"),
        ["cold-storage"] = new("rooftop-solar-and-battery portable-power-station", string.Empty),
        ["drinking-water"] = new("water-cans gravity-water-filter", string.Empty),
        ["light"] = new("rechargeable-lanterns headtorches", string.Empty),
        ["news"] = new("radio-dab-fm-with-crank", string.Empty),
        ["first-aid"] = new("safety-kit", string.Empty),
        ["lift"] = new(string.Empty, "Car=Petrol Car=Electric Car=ElectricWithOutput HasCar=true"),
    };

    /// <summary>Beside an offer the household's plan says it could make.</summary>
    public const string PlanSays = "Your plan says you have this.";

    /// <summary>On the plan page, the way in.</summary>
    public const string OnPlanBefore = "A street copes better than a house.";

    /// <summary>The words of the link on the plan page.</summary>
    public const string OnPlanLink = "Start a group for your street";

    /// <summary>What follows the link on the plan page.</summary>
    public const string OnPlanAfter = ", and neighbours can tell each other what each could offer when the power is off. Selficient cannot read what a group shares.";

    /// <summary>The link to a member's groups, in "Where your plan is kept".</summary>
    public const string YourGroups = "Your street groups";

    /// <summary>The three choices for an offer, as the settings word them.</summary>
    public static IReadOnlyList<(string Value, string Words)> Circles { get; } =
    [
        ("", "Nobody"),
        ("close", "My closer circle"),
        ("street", "Everyone in the group"),
    ];

    /// <summary>What a street group is, for somebody who has not met one.</summary>
    public const string What = "A street group lets neighbours tell each other, ahead of time, what each could offer when the power, the water or the phones are off: a warm room, somewhere to charge a phone, a hob that works. It is offers and never needs. Nobody says who in their home is frail, what cash they keep or when they are away, and there is no setting that could.";

    /// <summary>What is kept and who can read it.</summary>
    public const string Kept = "What you share is locked in this browser before it leaves, with a key that travels only in the invitation. Selficient keeps that you are in the group and cannot read anything in it: not the street's name, not your house number, not what you offer.";

    /// <summary>Shown to somebody who is not signed in.</summary>
    public const string Guest = "A street group needs an account, because the group has to be somewhere other than one phone. An account is an email address and nothing else.";

    /// <summary>Shown to somebody who is not signed in and came by an invitation.</summary>
    public const string GuestInvited = "You have been invited to a street group. Sign in or make an account, and this page will take you on to join it.";

    /// <summary>Shown above the list when the household is in no group.</summary>
    public const string None = "You are not in a street group yet. Start one below and invite your neighbours, or open an invitation a neighbour sent you.";

    /// <summary>Shown beside a group this device has no key for.</summary>
    public const string NoKey = "This device does not have this group's key. Open the invitation on this device, from a neighbour or from your other device, and it will.";

    /// <summary>Shown when a group cannot be opened: it is gone, the household was removed, or the link is wrong.</summary>
    public const string Gone = "That group is not here. It may have been closed, or your home may have been removed from it.";

    /// <summary>Shown when an invitation opens nothing.</summary>
    public const string BadInvitation = "That invitation does not open a group. Ask your neighbour for a new one.";

    /// <summary>Shown when the server cannot be reached.</summary>
    public const string Offline = "The street group could not be reached just now. Nothing was changed. Try again when you are online.";

    /// <summary>Shown when a house number or a name is missing.</summary>
    public const string Missing = "Give your house number, and the first name your closer circle will see.";

    /// <summary>Shown when a group has no name.</summary>
    public const string Unnamed = "Give the group a name your neighbours will know, such as the name of your street.";

    /// <summary>After the sharing is saved.</summary>
    public const string Saved = "Your sharing is saved. Your neighbours see it the next time they open the group.";

    /// <summary>After a home is removed.</summary>
    public const string Removed = "That home has been removed, and the invitation it had no longer works. There is a new invitation below.";

    /// <summary>After the link is copied.</summary>
    public const string Copied = "The invitation is copied. Send it only to a neighbour you mean to invite.";

    /// <summary>Beside the household's own home in the list.</summary>
    public const string YourHome = "Your home";

    /// <summary>Where a home offers this household nothing.</summary>
    public const string Nothing = "Nothing shared with you yet.";

    /// <summary>Where a home's record cannot be read.</summary>
    public const string Unreadable = "This home's sharing could not be read.";

    /// <summary>Above the street when nobody offers anything this household can see.</summary>
    public const string NothingBetween = "Nobody has shared an offer with you yet. When they do, what the street has between it is counted here.";

    /// <summary>The street page's count when the household is alone in the group.</summary>
    public const string HomesOne = "One home so far, which is yours. Send the invitation below to a neighbour.";

    /// <summary>The street page's count of homes; the script puts the number in.</summary>
    public const string HomesMany = "{n} homes in this group.";

    /// <summary>One home offering a thing; the script puts the thing in.</summary>
    public const string BetweenOne = "One home {what}.";

    /// <summary>Several homes offering a thing; the script puts the number and the thing in.</summary>
    public const string BetweenMany = "{n} homes {what}.";

    /// <summary>A home in the closer-circle list and on the remove button; the script puts the house number in.</summary>
    public const string HouseLabel = "Number {house}";

    /// <summary>What the remove button says to a screen reader; the script puts the house number in.</summary>
    public const string RemoveLabel = "Remove number {house} from the group";
}

/// <summary>What in a plan says a household could make an offer.</summary>
/// <param name="Held">The plan actions, any one of which held says so, separated by spaces.</param>
/// <param name="Answers">The answers about the home, as field=value, any one of which says so, separated by spaces.</param>
public sealed record PlanSign(string Held, string Answers);
