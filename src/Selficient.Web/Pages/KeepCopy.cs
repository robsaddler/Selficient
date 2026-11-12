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

    /// <summary>Said once under the figure, the first time a plan is saved in this browser.</summary>
    public static string FirstPlan => "Your plan is saved on this device. You do not need an account.";
}
