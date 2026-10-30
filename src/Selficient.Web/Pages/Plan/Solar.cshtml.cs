using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Geography;
using Selficient.Application.Solar;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// The solar and battery estimator. GET shows the questions; POST runs the estimate and shows it
/// above the questions, so they can be changed; the roof handler asks the aerial provider, only
/// when it exists and only with the consent field set. Nothing is stored.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class SolarModel : PageModel
{
    private readonly EstimateSolarHandler _handler;
    private readonly IRoofInsightsProvider _roofs;
    private readonly IGeocoder _geocoder;

    /// <summary>Creates the page.</summary>
    public SolarModel(EstimateSolarHandler handler, IRoofInsightsProvider roofs, IGeocoder geocoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(roofs);
        ArgumentNullException.ThrowIfNull(geocoder);
        _handler = handler;
        _roofs = roofs;
        _geocoder = geocoder;
    }

    /// <summary>The questions.</summary>
    [BindProperty]
    public SolarForm Form { get; set; } = new();

    /// <summary>The estimate, once made.</summary>
    public SolarReport? Report { get; private set; }

    /// <summary>What the aerial survey said, once asked.</summary>
    public RoofInsights? Roof { get; private set; }

    /// <summary>A sentence when the survey could not help.</summary>
    public string? RoofNotice { get; private set; }

    /// <summary>Whether the page may offer the aerial survey at all.</summary>
    public bool RoofLookupOffered => _roofs.IsAvailable;

    /// <summary>The questions, with defaults.</summary>
    public IActionResult OnGet() => Page();

    /// <summary>Runs the estimate.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        Result<SolarReport> result = await _handler.HandleAsync(Form.ToRequest(), cancellationToken);
        if (result.IsFailure)
        {
            ModelState.AddModelError(FieldFor(result.Error), result.Error.Message);
            return Page();
        }

        Report = result.Value;
        return Page();
    }

    /// <summary>
    /// Asks the aerial provider about the roof at the postcode. Requires the consent field: a
    /// request without it is treated as a plain estimate.
    /// </summary>
    public async Task<IActionResult> OnPostRoofAsync(bool consent, CancellationToken cancellationToken)
    {
        if (!_roofs.IsAvailable || !consent)
        {
            return await OnPostAsync(cancellationToken);
        }

        Result<Postcode> postcode = Postcode.Create(Form.Postcode);
        if (postcode.IsFailure)
        {
            ModelState.AddModelError($"{nameof(Form)}.{nameof(SolarForm.Postcode)}", postcode.Error.Message);
            return Page();
        }

        Result<Locality> located = await _geocoder.LocateAsync(postcode.Value, cancellationToken);
        if (located.IsFailure)
        {
            ModelState.AddModelError($"{nameof(Form)}.{nameof(SolarForm.Postcode)}", located.Error.Message);
            return Page();
        }

        Result<RoofInsights> roof = await _roofs.DescribeAsync(located.Value.Coordinates, cancellationToken);
        if (roof.IsFailure)
        {
            RoofNotice = "The aerial survey has nothing for this roof. Use the compass and the panel count instead; an installer's survey is what decides it anyway.";
            return Page();
        }

        Roof = roof.Value;
        if (roof.Value.Best is { } best)
        {
            Form.Aspect = best.Aspect;
            Form.PitchDegrees = Math.Clamp((int)Math.Round(best.PitchDegrees), Domain.Solar.RoofPitch.Lowest, Domain.Solar.RoofPitch.Steepest);
            if (roof.Value.MaxPanels > 0)
            {
                Form.Panels = Math.Clamp(roof.Value.MaxPanels, Domain.Solar.PanelArray.Fewest, Domain.Solar.PanelArray.Most);
            }
        }

        // The form is re-rendered from the model, not from the posted values.
        ModelState.Clear();
        return Page();
    }

    /// <summary>Which field a fault belongs beside; the summary otherwise.</summary>
    internal static string FieldFor(Fault fault) => fault.Code switch
    {
        "postcode.invalid" or "postcode.not-found" or "lookup.unavailable" => $"{nameof(Form)}.{nameof(SolarForm.Postcode)}",
        _ => string.Empty,
    };
}
