using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages;

/// <summary>
/// Where a copy of a plan arrives. The page is the same for everyone: the ticket and the key are
/// in the link's fragment, which never reaches this server, and everything that happens to them
/// happens in the browser. It sends no referrer anywhere and is kept out of search.
/// </summary>
public sealed class ReceiveModel : PageModel
{
    /// <summary>Renders the page.</summary>
    public IActionResult OnGet()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Page();
    }
}
