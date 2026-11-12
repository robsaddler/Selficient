namespace Selficient.Web.Pages;

/// <summary>
/// What is said about keeping a signed-in household's devices in step. Sync is quiet by design:
/// it says something only when a plan has moved, when the person has to choose between two plans,
/// or when another device's household details replaced the ones here.
/// </summary>
public static class SyncCopy
{
    /// <summary>
    /// The sentences the notice can say, by the name the script asks for. Every one is on the page
    /// from the start and the script only chooses which is shown.
    /// </summary>
    public static IReadOnlyList<(string Key, string Sentence)> Says { get; } =
    [
        ("kept", "Your plan is now kept in your account."),
        ("arrived", "Your plan has arrived from your account."),
        ("replaced", "This device now has the plan from your account."),
        ("kept-here", "Your account now keeps the plan from this device."),
        ("details", "Your other device changed your household details. This device now shows those."),
    ];

    /// <summary>The panel's line when this device and the account agree.</summary>
    public static string Synced => "Your plan is kept in your account, and every device you sign in on stays in step.";

    /// <summary>The panel's line when a change made here has not reached the account yet.</summary>
    public static string Waiting => "Changes made on this device are waiting for a connection. They will go to your account when there is one.";

    /// <summary>The panel's line when the person has yet to choose between two plans.</summary>
    public static string Choosing => "This device and your account each have a plan.";

    /// <summary>The panel's button that asks again.</summary>
    public static string Choose => "Choose which to keep";

    /// <summary>The heading of the choice.</summary>
    public static string ChoiceHeading => "This device and your account each have a plan";

    /// <summary>What the choice is, and the promise that the two are not mixed.</summary>
    public static string ChoiceSentence => "Choose one. The two are never mixed, and the one you choose replaces the other on every device you sign in on.";

    /// <summary>The button that takes the account's plan.</summary>
    public static string TakeAccount => "Use the plan in my account";

    /// <summary>The button that keeps this device's plan.</summary>
    public static string TakeDevice => "Keep the plan on this device";
}
