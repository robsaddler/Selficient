using Microsoft.AspNetCore.Http;

namespace Selficient.Web.Pages;

/// <summary>
/// The sentences that say where a plan is kept. A visitor arrives expecting to be asked for an account; these are
/// what the site says instead, in the header, in the panel behind it, beside the start buttons and under the first plan.
/// </summary>
public static class KeepCopy
{
    /// <summary>Whether a page carries the chip: every page a household sees, and none of the back office.</summary>
    public static bool ShowsOn(PathString path) => !path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>The header chip before a plan exists in this browser, and the words beside a start button.</summary>
    public static string ChipNone => "No account needed";

    /// <summary>The header chip once a plan is saved in this browser.</summary>
    public static string ChipDevice => "Saved on this device";

    /// <summary>The heading of the panel the chip opens.</summary>
    public static string PanelHeading => "Where your plan is kept";

    /// <summary>The heading of the panel's row for this browser.</summary>
    public static string DeviceHeading => "On this device";

    /// <summary>The row's sentence before a plan exists.</summary>
    public static string DeviceNone => "Nothing is saved yet. When you make a plan it lives in this browser and nowhere else. You do not need an account.";

    /// <summary>The row's sentence once a plan is saved.</summary>
    public static string DeviceKept => "Your plan lives in this browser and nowhere else. You do not need an account.";

    /// <summary>The one weakness of keeping a plan in the browser, said only where it applies: Safari on an iPhone or iPad, in a tab.</summary>
    public static string SafariWarning => "Safari clears a site's data after a week without a visit. Add Selficient to your home screen to keep your plan.";

    /// <summary>The tag on the row that says where the plan is kept now, so the current answer is seen without reading every row.</summary>
    public static string KeptHere => "Kept here";

    /// <summary>The heading of the panel's row for sending a copy.</summary>
    public static string AnotherHeading => "Another device";

    /// <summary>What sending a copy does, and the one thing it does not do.</summary>
    public static string AnotherSentence => "Send a copy of your plan to your phone or another computer. Changes you make afterwards will not follow it.";

    /// <summary>The button that makes the code.</summary>
    public static string SendButton => "Send a copy";

    /// <summary>Under the code.</summary>
    public static string SendScan => "Point your other device's camera at this. It works for ten minutes, once.";

    /// <summary>What a screen reader is told the code is.</summary>
    public static string SendCodeLabel => "A code for your other device's camera";

    /// <summary>The button for a device with no camera.</summary>
    public static string SendLink => "Copy the link instead";

    /// <summary>Said when the other device has collected the copy. The moment a person might think the two are now linked, so it says they are not.</summary>
    public static string Sent => "Sent. Your other device has its own copy now. Changes you make on one will not appear on the other.";

    /// <summary>The link under the first plan that opens the panel and starts sending.</summary>
    public static string SendToPhone => "Send a copy to my phone";

    /// <summary>
    /// What the row says while and after sending, by the name the script asks for. Every one is on
    /// the page from the start and the script only chooses which is shown.
    /// </summary>
    public static IReadOnlyList<(string Key, string Sentence)> SendSays { get; } =
    [
        ("making", "Making a copy."),
        ("copied", "Link copied. Open it on the other device within ten minutes."),
        ("not-copied", "The link could not be copied. Point the other device's camera at the code instead."),
        ("expired", "That code has run out. Send a copy again."),
        ("offline", "A copy cannot be sent without a connection. Try again when you are back online."),
        ("busy", "Too many copies have been sent from here in the last minute. Wait a minute and try again."),
        ("full", "Too many copies are waiting to be collected at the moment. Try again in a few minutes."),
        ("large", "This plan is too large to send as a copy. Use \"Export as a file\" on the plan instead."),
        ("failed", "The copy could not be sent. Try again in a moment."),
    ];

    /// <summary>The header chip for a signed-in household, before its address.</summary>
    public static string ChipAccount => "Signed in as";

    /// <summary>The row's sentence for a signed-in household: the copy here is what opens without a connection.</summary>
    public static string DeviceSignedIn => "A copy of your plan lives in this browser, so it opens without a connection.";

    /// <summary>The row's sentence for a signed-in household with no plan in this browser yet.</summary>
    public static string DeviceSignedInNone => "Nothing is saved on this device yet. When you make a plan it is kept here and in your account.";

    /// <summary>The Safari warning once an account is a second way to keep a plan.</summary>
    public static string SafariWarningWithAccount => "Safari clears a site's data after a week without a visit. Add Selficient to your home screen, or make an account, to keep your plan.";

    /// <summary>The Safari warning for this server: it names an account only where one can be made.</summary>
    public static string Safari(bool accounts) => accounts ? SafariWarningWithAccount : SafariWarning;

    /// <summary>The heading of the panel's row for an account, to a guest.</summary>
    public static string AccountHeading => "An account";

    /// <summary>The two things an account does, and how little it asks.</summary>
    public static string AccountSentence => "Keep every device in step and keep your plan if this one is lost. Just an email address, no password.";

    /// <summary>The button that goes to make one.</summary>
    public static string CreateAccount => "Create an account";

    /// <summary>The quiet line under the rows, before its link.</summary>
    public static string AlreadyHave => "Already have an account?";

    /// <summary>That line's link.</summary>
    public static string SignIn => "Sign in";

    /// <summary>The heading of the account row once signed in.</summary>
    public static string YourAccountHeading => "Your account";

    /// <summary>The account row's sentence once signed in, before the address.</summary>
    public static string SignedInAs => "Signed in as";

    /// <summary>The account row's link to sign out.</summary>
    public static string SignOut => "Sign out";

    /// <summary>The account row's link to delete the account.</summary>
    public static string DeleteAccount => "Delete my account";

    /// <summary>One way to keep a plan, as the landing page says it: its mark, its name and what it does in a sentence or two.</summary>
    public sealed record Way(string Mark, string Name, string Sentence);

    /// <summary>Under a start button: the headline a visitor is waiting for, and where the plan goes.</summary>
    public static string StartNote => "Your plan is saved on this device.";

    /// <summary>Under a start button, where an account can be made: what one is for, before its link.</summary>
    public static string StartAccountQuestion => "Want it on your phone too, or safe if this device is lost?";

    /// <summary>The link that follows.</summary>
    public static string StartAccountLink => "Make a free account whenever you like";

    /// <summary>And what it asks for.</summary>
    public static string StartAccountAsks => "just an email address, no password.";

    /// <summary>The heading of the landing page's section on where a plan is kept.</summary>
    public static string WaysHeading(bool accounts) => accounts ? "Three ways to keep your plan" : "Two ways to keep your plan";

    /// <summary>Under the heading: which of them is the default, and that the others are a choice.</summary>
    public static string WaysLede(bool accounts) => accounts
        ? "You start with the first and need nothing else. The other two are there if you want them, when you want them."
        : "You start with the first and need nothing else. The second is there if you want it.";

    /// <summary>
    /// The ways to keep a plan, in the order the panel behind the header chip lists them, so the
    /// landing page and the panel teach the same thing. An account is named only where one can be made.
    /// </summary>
    public static IReadOnlyList<Way> Ways(bool accounts)
    {
        Way[] ways =
        [
            new("mark-device", "On this device", "The default. Your plan lives in your own browser and nowhere else. There is nothing to sign up for."),
            new("mark-devices", "Send a copy", "Move your plan to your phone or another computer by scanning a code. It is a one-off copy: the two do not stay in step."),
            new("mark-account", "An account, if you want one", "Keeps every device you sign in on in step, and keeps your plan if a device is lost. An email address, no password, and you can delete it at any time."),
        ];
        return accounts ? ways : ways[..2];
    }

    /// <summary>What the section says after the cards: the rest of the old promise, unchanged for a guest.</summary>
    public static string WaysPromise => "Whichever you choose, your postcode is sent once, to find your area and your height above sea level, and is not kept. There is no tracking and no analytics. Install Selficient to your phone and the plan opens without a signal.";

    /// <summary>The heading of the passkeys part of the account's row.</summary>
    public static string PasskeysHeading => "Passkeys";

    /// <summary>What a passkey is, to a household with none, and the thing that makes it safe to rely on.</summary>
    public static string PasskeysNone => "Sign in with this device's fingerprint, face or screen lock in place of a code. The emailed code still works if the device is lost.";

    /// <summary>The panel's button that adds one.</summary>
    public static string AddPasskey => "Add a passkey for this device";

    /// <summary>The words on the button beside each passkey.</summary>
    public static string RemovePasskey => "Remove";

    /// <summary>Said when one has been added.</summary>
    public static string PasskeyAdded => "A passkey has been added for this device.";

    /// <summary>Said when one has been removed, with the thing a person might worry about.</summary>
    public static string PasskeyRemoved => "That passkey has been removed. The emailed code still signs you in.";

    /// <summary>The offer made once after signing in by code: its heading.</summary>
    public static string OfferHeading => "Sign in faster next time.";

    /// <summary>The offer's sentence.</summary>
    public static string OfferSentence => "Add a passkey and this device signs you in with your fingerprint, face or screen lock. The emailed code still works, so a lost phone is never a lost account.";

    /// <summary>The offer's button.</summary>
    public static string OfferAdd => "Add a passkey";

    /// <summary>The offer's way out.</summary>
    public static string OfferNotNow => "Not now";

    /// <summary>Said to a guest after a copy has gone: the one moment an account is the answer to what they just did.</summary>
    public static string SentAccount => "An account would keep both in step.";

    /// <summary>Said once under the figure, the first time a plan is saved in this browser.</summary>
    public static string FirstPlan => "Your plan is saved on this device. You do not need an account.";
}
