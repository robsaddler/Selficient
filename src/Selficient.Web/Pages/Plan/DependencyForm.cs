using System.ComponentModel.DataAnnotations;
using Selficient.Domain.Primitives;
using Selficient.Domain.Resilience;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// One thing only this household relies on, as the form carries it. A row with no name is an empty
/// row and is ignored; everything else is range-checked here and again in the domain.
/// </summary>
public sealed class DependencyForm
{
    /// <summary>What the household calls it.</summary>
    [StringLength(Dependency.MaxNameLength, ErrorMessage = "Give the thing you rely on a short name, up to forty characters.")]
    [Display(Name = "What is it?")]
    public string? Name { get; set; }

    /// <summary>Device, medicine or supply.</summary>
    [EnumDataType(typeof(DependencyKind), ErrorMessage = "Choose what kind of thing it is from the list.")]
    [Display(Name = "Which kind of thing?")]
    public DependencyKind? Kind { get; set; }

    /// <summary>Power drawn, for a device.</summary>
    [Range(0, 3000, ErrorMessage = "A device draws between 1 and 3,000 watts.")]
    [Display(Name = "Power it uses, in watts")]
    public decimal? Watts { get; set; }

    /// <summary>Hours a day, for a device.</summary>
    [Range(0, 24, ErrorMessage = "A device runs between 1 and 24 hours a day.")]
    [Display(Name = "Hours a day it runs")]
    public decimal? HoursPerDay { get; set; }

    /// <summary>Days of stock, for a medicine or supply.</summary>
    [Range(0, 365, ErrorMessage = "Days of stock at home must be between 0 and 365.")]
    [Display(Name = "Days of it in the house")]
    public int? DaysOfStock { get; set; }

    /// <summary>Whether it needs the fridge.</summary>
    [Display(Name = "It must stay cold")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool MustStayCold { get; set; }

    /// <summary>Whether the row holds anything.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Name);

    /// <summary>The domain dependency, on the Survive rung: anything a household goes to the trouble of naming is one.</summary>
    public Result<Dependency> ToDependency() => Dependency.Create(Name ?? string.Empty, Rung.Survive, Kind ?? DependencyKind.PoweredDevice, Watts ?? 0m, HoursPerDay ?? 0m, DaysOfStock ?? 0, MustStayCold);
}
