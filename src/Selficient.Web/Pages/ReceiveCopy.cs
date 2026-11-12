namespace Selficient.Web.Pages;

/// <summary>
/// What the page a copy arrives on says, by the state the script puts it in. Every sentence is on
/// the page from the start; the script chooses which is shown and writes only the summary of what
/// arrived, from the copy itself, as text.
/// </summary>
public static class ReceiveCopy
{
    /// <summary>One state of the page: its name for the script, its heading and its sentence.</summary>
    public sealed record State(string Key, string Heading, string Sentence);

    /// <summary>Shown without a link, and without script: what this page is for.</summary>
    public static State Nothing { get; } = new("nothing", "Receive a plan from another device", "This page receives a copy of a plan sent from another device. On the device that has your plan, open \"Where your plan is kept\" at the top of any page and choose \"Send a copy\".");

    /// <summary>While the copy is collected and opened.</summary>
    public static State Reading { get; } = new("reading", "Collecting your plan", "Collecting the copy you sent from your other device.");

    /// <summary>A copy has arrived and this device holds no plan.</summary>
    public static State Arrived { get; } = new("arrived", "A plan has arrived", "Nothing has been saved on this device yet.");

    /// <summary>A copy has arrived and this device already holds a plan. Never a merge.</summary>
    public static State Choose { get; } = new("choose", "This device already has a plan", "Replace it with the one you sent, or keep the one that is here. The two are never mixed.");

    /// <summary>The copy is on this device. Said at the moment a person might think the two devices are linked.</summary>
    public static State Done { get; } = new("done", "Your plan is on this device", "This device has its own copy now. Changes you make here will not appear on the other device, and changes there will not appear here.");

    /// <summary>The person chose not to take the copy.</summary>
    public static State Kept { get; } = new("kept", "Nothing was changed", "Nothing was changed on this device. The copy you sent has been thrown away.");

    /// <summary>The ticket was used, ran out, or never was.</summary>
    public static State Used { get; } = new("used", "That link has been used", "A link works once, for ten minutes, and this one has been used or has run out. Send a copy again from the other device.");

    /// <summary>The link is missing its ticket or its key.</summary>
    public static State Incomplete { get; } = new("incomplete", "That link is not complete", "Part of the link is missing, so the copy cannot be opened. Scan the code again, or copy the whole link.");

    /// <summary>What arrived did not open, or was not a plan.</summary>
    public static State Unreadable { get; } = new("unreadable", "That copy could not be read", "What arrived could not be opened as a Selficient plan, so nothing was changed on this device. Send a copy again from the other device.");

    /// <summary>The copy could not be collected, and has not been spent.</summary>
    public static State Later { get; } = new("later", "The copy could not be collected", "The copy could not be collected just now, and it has not been used up. Check the connection and try again within ten minutes of making it.");

    /// <summary>The states that are a heading, a sentence and a way on, in the order the page holds them.</summary>
    public static IReadOnlyList<State> Endings { get; } = [Done, Kept, Used, Incomplete, Unreadable];

    /// <summary>The button that writes the copy to a device with no plan.</summary>
    public static string Put => "Put this plan on this device";

    /// <summary>The button that declines it.</summary>
    public static string Decline => "Do not put it here";

    /// <summary>The button that replaces the plan already here.</summary>
    public static string Replace => "Replace it with the one I sent";

    /// <summary>The button that keeps the plan already here.</summary>
    public static string KeepHere => "Keep the one that is here";
}
