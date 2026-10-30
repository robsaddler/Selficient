using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages;

/// <summary>The privacy page.</summary>
public sealed class PrivacyModel : PageModel
{
    /// <summary>Renders the page.</summary>
    public IActionResult OnGet() => Page();
}
