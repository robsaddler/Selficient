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

    /// <summary>The name the feature goes by wherever it is pointed to: the header, the landing page, the plan.</summary>
    public const string Name = "Your street";

    /// <summary>The claim, as a heading: on the landing page and on the plan.</summary>
    public const string Claim = "A street copes better than a house";

    /// <summary>The claim made concrete, under the landing page's heading.</summary>
    public const string Scene = "In a long power cut the house three doors down has a gas hob, and yours has the only radio that still works. Neither of you knows. A street group is where neighbours say, ahead of time, what each could offer.";

    /// <summary>Why a household would, in four points: what it is for, who sees what, who cannot, and the day it is needed.</summary>
    public static IReadOnlyList<(string Heading, string Sentence)> Points { get; } =
    [
        ("Offers, never needs", "A warm room, a hob that works, somewhere to charge a phone, a lift. Nobody says who in their home is frail, what they keep or when they are away, and there is no setting that could."),
        ("You choose who sees what", "Each offer goes to nobody until you say otherwise, and then to the whole group or only to the homes you know well, wherever on the street they are."),
        ("We cannot read it", "What a street shares is locked in your browser before it leaves. We keep that you are in a group and nothing we could read: not its name, not your house number, not what you offer."),
        ("On paper for the day itself", "The day you need your street is the day the screens are dark. The street prints on one sheet for the kitchen drawer, and your phone keeps the last copy it saw."),
    ];

    /// <summary>An example street for the landing page, as one of its homes would see it: the house, a first name where that home has it in its closer circle, and what it offers.</summary>
    public static IReadOnlyList<(string House, string? Name, bool Mine, IReadOnlyList<string> Offers)> Example { get; } =
    [
        ("12", "Doris", false, ["A warm room", "A hob or stove that works without power"]),
        ("27", null, true, ["A first-aid kit", "A car with fuel or charge"]),
        ("31", null, false, ["A radio that works without power"]),
        ("78", "Sam", false, ["Drinking water to spare", "A lantern or torch to lend"]),
    ];

    /// <summary>Under the example.</summary>
    public const string ExampleCaption = "An example street, as number 27 sees it. A first name means that home has put number 27 in its closer circle.";

    /// <summary>The button that starts a group, wherever it is.</summary>
    public const string Start = "Start a group for your street";

    /// <summary>Beside the button on the landing page: the other way to begin, and why it is a good one.</summary>
    public const string StartWithPlan = "Start with your plan";

    /// <summary>What an account has to do with it, said where the landing page says no account is needed.</summary>
    public const string AccountNote = "Your plan needs no account. A street group does, because the group has to live somewhere other than one phone: an email address, and nothing else.";

    /// <summary>The tile under "What it covers".</summary>
    public const string Covers = "A group for your neighbours, where each home says what it could offer when the power is off. It is locked in the browser, so we cannot read it.";

    /// <summary>How it goes, in three steps, for somebody deciding whether to begin.</summary>
    public static IReadOnlyList<(string Heading, string Sentence)> Steps { get; } =
    [
        ("Start a group, or open a neighbour's invitation", "A group is found only by its invitation: a link to send, or a slip with a code to post through a door. There is no directory and no search."),
        ("Say what you could offer", "A warm room, a hob that works, a radio. Each offer goes to nobody until you choose, then to everyone in the group or only to the homes you pick."),
        ("See what the street has between it", "Home by home, and counted across the street. Print it for the kitchen drawer, for the day the screens are dark."),
    ];

    /// <summary>On the plan, under the claim, for a household that has not begun.</summary>
    public const string OnPlan = "Neighbours in a group tell each other what each could offer when the power is off. You choose what to share and with whom, and Selficient cannot read any of it.";

    /// <summary>On the plan, above the offers this household's plan says it could make.</summary>
    public const string OnPlanCould = "From what you have ticked, you could offer your street:";

    /// <summary>On the plan, where nothing ticked is something a neighbour could use yet.</summary>
    public const string OnPlanNothingYet = "Tick what you have above, and this will say what you could offer your street.";

    /// <summary>The plan's button for a household whose device is already in a group.</summary>
    public const string Open = "Open your street group";

    /// <summary>The link to a member's groups, in "Where your plan is kept".</summary>
    public const string YourGroups = "Your street groups";

    /// <summary>On a group's page, to a household with no plan in this browser.</summary>
    public const string NextPlan = "Make your plan, and this page will say what you could offer your neighbours. It takes about a minute.";

    /// <summary>The button beside it.</summary>
    public const string NextPlanButton = "Start your plan";

    /// <summary>On a group's page, to a household whose plan holds one thing a neighbour could use and that offers nothing yet.</summary>
    public const string NextOfferOne = "Your plan says you could offer one thing. Choose who sees it under \"What you share, and with whom\". Nothing is shared until you save.";

    /// <summary>The same for several; the script puts the number in.</summary>
    public const string NextOfferMany = "Your plan says you could offer {n} things. Choose who sees each under \"What you share, and with whom\". Nothing is shared until you save.";

    /// <summary>What nobody has offered this household; the script puts the list in.</summary>
    public const string Gaps = "Nobody has offered you these yet: {list}.";

    /// <summary>Where the street is shown from the copy this device kept; the script puts the date in.</summary>
    public const string Seen = "You are offline. This is the street as this device last saw it, on {date}. Nothing can be changed until you are back online.";

    /// <summary>What goes with the invitation when the device's own sharing is used.</summary>
    public const string ShareText = "Our street has a group on Selficient, where neighbours say what each could offer in a power cut. This link lets you join:";

    /// <summary>On the slip that goes through a door; the script puts the house in.</summary>
    public const string SlipFrom = "Your neighbour at {house} is inviting you to our street's group on Selficient.";

    /// <summary>What a group is, to somebody who has never heard of this site, in the room a slip has.</summary>
    public const string SlipWhat = "It is where neighbours say, ahead of time, what each could offer when the power, the water or the phones are off: a warm room, a hob that works, somewhere to charge a phone. You choose what to share and with whom. It costs nothing.";

    /// <summary>How to use the slip.</summary>
    public const string SlipHow = "Point your phone's camera at this code to join.";

    /// <summary>Whose site it is, at the slip's foot.</summary>
    public const string SlipFoot = "selficient.app: a plan for your house, and for your street.";

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
