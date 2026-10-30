using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages;

/// <summary>
/// The page the service worker shows for a navigation it cannot serve. Precached with the shell,
/// so it is always there; it says what works without a connection and nothing more.
/// </summary>
public sealed class OfflineModel : PageModel
{
    /// <summary>Renders the page.</summary>
    public IActionResult OnGet() => Page();
}
