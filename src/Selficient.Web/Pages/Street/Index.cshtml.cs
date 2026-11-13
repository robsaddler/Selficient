using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages.Street;

/// <summary>
/// The street group pages. One page, the same for everyone: an invitation's key is in the link's
/// fragment, which never reaches this server, and what a street shares is opened in the browser.
/// The page sends no referrer anywhere, so the address of a group is not handed to another site,
/// and it is kept out of search and out of every cache.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class IndexModel : PageModel
{
    /// <summary>Renders the page.</summary>
    public IActionResult OnGet()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Page();
    }
}
