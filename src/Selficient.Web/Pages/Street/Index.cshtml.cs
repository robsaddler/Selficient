using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Selficient.Application.Accounts;

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
    private readonly AccountSignIn _accounts;

    /// <summary>Creates the page model.</summary>
    public IndexModel(AccountSignIn accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        _accounts = accounts;
    }

    /// <summary>Renders the page. A group needs an account, so a server that offers none says so on the page that says it for accounts.</summary>
    public IActionResult OnGet()
    {
        if (!_accounts.IsAvailable)
        {
            return RedirectToPage("/Account/Unavailable");
        }

        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Page();
    }
}
