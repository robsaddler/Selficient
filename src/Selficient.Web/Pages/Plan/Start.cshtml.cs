using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages.Plan;

/// <summary>The wizard. It posts to the plan page, which validates and renders.</summary>
public sealed class StartModel : PageModel
{
    /// <summary>The form with its defaults.</summary>
    public PlanForm Form { get; } = new();

    /// <summary>
    /// Renders the form. The landing page hands over a postcode typed into its own box, so the
    /// first answer is already filled in; anything longer than a postcode is ignored.
    /// </summary>
    public IActionResult OnGet(string? postcode)
    {
        if (!string.IsNullOrWhiteSpace(postcode) && postcode.Length <= 10)
        {
            Form.Postcode = postcode.Trim().ToUpperInvariant();
        }

        return Page();
    }
}
