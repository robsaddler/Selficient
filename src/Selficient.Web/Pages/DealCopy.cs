using System.Globalization;

namespace Selficient.Web.Pages;

/// <summary>The deals page's phrases, decided here so the view carries no branch.</summary>
public static class DealCopy
{
    /// <summary>How many more picks the plan shows for the action.</summary>
    public static string Others(int others) => others switch
    {
        0 => "The only pick we show for this.",
        1 => "One more pick on the plan, which lasts longer or does more.",
        _ => string.Create(CultureInfo.InvariantCulture, $"{others} more picks on the plan, which last longer or do more."),
    };
}
