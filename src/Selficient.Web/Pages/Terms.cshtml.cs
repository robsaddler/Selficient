using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages;

/// <summary>The terms, affiliate disclosure and licences page.</summary>
public sealed class TermsModel : PageModel
{
    /// <summary>Renders the page.</summary>
    public IActionResult OnGet() => Page();
}
