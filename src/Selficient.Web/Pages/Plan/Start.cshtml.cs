using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages.Plan;

/// <summary>The wizard. It posts to the plan page, which validates and renders.</summary>
public sealed class StartModel : PageModel
{
    /// <summary>The form with its defaults.</summary>
    public PlanForm Form { get; } = new();

    /// <summary>Renders the form.</summary>
    public IActionResult OnGet() => Page();
}
