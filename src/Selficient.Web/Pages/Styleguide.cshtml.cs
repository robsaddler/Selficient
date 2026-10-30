using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Selficient.Web.Pages;

/// <summary>The living styleguide: every token and component, so design drift is visible.</summary>
public sealed class StyleguideModel : PageModel
{
    /// <summary>The colour tokens and what each is for, mirrored from <c>tokens.css</c>.</summary>
    public IReadOnlyList<ColourToken> ColourTokens { get; } =
    [
        new("--c-slate", "Ink, headings, the plan text."),
        new("--c-sky", "Page ground."),
        new("--c-pine", "Primary actions, links, ticked states."),
        new("--c-lantern", "Progress, the ticked compass point, the focus ring. One per screen."),
        new("--c-lantern-ink", "The Days Ready figure and every counted number: lantern as text, dark enough to read in daylight."),
        new("--c-contour", "Rules, borders, contour lines, disabled."),
        new("--c-ember", "Genuine warnings only."),
        new("--c-paper", "Cards and the plan sheet."),
    ];

    /// <summary>Renders the page.</summary>
    public IActionResult OnGet() => Page();

    /// <summary>A colour token and its purpose.</summary>
    public sealed record ColourToken(string Name, string Purpose);
}
