using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages;

/// <summary>The landing page: the Days Ready figure and the invitation to start a plan.</summary>
public sealed class IndexModel : PageModel
{
    /// <summary>
    /// The figure shown before a plan exists. Half a day is what a typical house holds in the tap,
    /// the fridge and the phone battery; the real figure comes from the plan in M1.
    /// </summary>
    public decimal DaysReady { get; private set; } = 0.5m;

    /// <summary>Renders the page.</summary>
    public IActionResult OnGet()
    {
        DaysReady = 0.5m;
        return Page();
    }
}
