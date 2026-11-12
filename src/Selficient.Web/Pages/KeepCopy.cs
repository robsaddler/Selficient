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

    /// <summary>Said once under the figure, the first time a plan is saved in this browser.</summary>
    public static string FirstPlan => "Your plan is saved on this device. You do not need an account.";
}
