using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Selficient.Application.Solar;
using Selficient.Domain.Solar;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// The solar estimator's questions. Every value-type field carries its own sentence so the
/// framework never shows a fragment; the Domain re-checks every range.
/// </summary>
public sealed class SolarForm
{
    /// <summary>Where the roof is.</summary>
    [Display(Name = "Postcode")]
    [Required(ErrorMessage = "Enter your postcode so we can look up the sunshine for your roof.")]
    [StringLength(10, ErrorMessage = "A postcode is at most eight characters with a space.")]
    public string Postcode { get; set; } = string.Empty;

    /// <summary>Which way the main slope faces.</summary>
    [Display(Name = "Which way does your main roof slope face?")]
    [Required(ErrorMessage = "Choose the direction the roof faces.")]
    public RoofAspect Aspect { get; set; } = RoofAspect.South;

    /// <summary>Degrees from horizontal.</summary>
    [Display(Name = "How steep is the roof, in degrees from flat?")]
    [Required(ErrorMessage = "Enter the pitch; 35 is the usual guess for a house.")]
    [Range(RoofPitch.Lowest, RoofPitch.Steepest, ErrorMessage = "The pitch needs to be between 10 and 60 degrees.")]
    public int PitchDegrees { get; set; } = RoofPitch.Typical;

    /// <summary>How many panels.</summary>
    [Display(Name = "How many panels would fit?")]
    [Required(ErrorMessage = "Enter a panel count; ten to fourteen is typical for a three-bed house.")]
    [Range(PanelArray.Fewest, PanelArray.Most, ErrorMessage = "The panel count needs to be between 2 and 40.")]
    public int Panels { get; set; } = 12;

    /// <summary>Battery size.</summary>
    [Display(Name = "Battery")]
    [Required(ErrorMessage = "Choose a battery size, or none.")]
    [Range(0, Battery.MostKilowattHours, ErrorMessage = "The battery needs to be between 0 and 30 kWh.")]
    public decimal BatteryKilowattHours { get; set; } = 13.5m;

    /// <summary>Backup level.</summary>
    [Display(Name = "In a power cut, what should keep running?")]
    [Required(ErrorMessage = "Choose what the battery should keep running in a cut.")]
    public BackupLevel Backup { get; set; } = BackupLevel.WholeHome;

    /// <summary>House height.</summary>
    [Display(Name = "How many storeys, counting rooms in the roof?")]
    [Required(ErrorMessage = "Enter the number of storeys; scaffolding for three or more costs extra.")]
    [Range(1, 6, ErrorMessage = "Storeys need to be between 1 and 6.")]
    public int Storeys { get; set; } = 2;

    /// <summary>Household size.</summary>
    [Display(Name = "How many people live in your home?")]
    [Required(ErrorMessage = "Enter how many people live there so we can estimate your electricity use.")]
    [Range(1, 20, ErrorMessage = "People need to be between 1 and 20.")]
    public int People { get; set; } = 2;

    /// <summary>A car charged at home.</summary>
    [Display(Name = "Do you charge an electric car at home?")]
    [Required(ErrorMessage = "Say whether you charge a car at home.")]
    public bool HasElectricCar { get; set; }

    /// <summary>A heat pump.</summary>
    [Display(Name = "Do you heat the house with a heat pump?")]
    [Required(ErrorMessage = "Say whether you have a heat pump.")]
    public bool HasHeatPump { get; set; }

    /// <summary>The household's own annual figure.</summary>
    [Display(Name = "Your electricity use in a year, in kWh, if you know it from a bill")]
    [Range(AnnualDemand.Lowest, AnnualDemand.Highest, ErrorMessage = "Electricity use needs to be between 500 and 30,000 kWh a year.")]
    public decimal? AnnualKilowattHours { get; set; }

    /// <summary>The request for the handler.</summary>
    public EstimateSolarRequest ToRequest() => new(Postcode, Aspect, PitchDegrees, Panels, BatteryKilowattHours, Backup, Storeys, People, HasElectricCar, HasHeatPump, AnnualKilowattHours);

    /// <summary>Battery choices, in the interface's voice.</summary>
    public static IReadOnlyList<SelectListItem> Batteries { get; } =
    [
        new("No battery, panels only", "0"),
        new("5 kWh, a small evening battery", "5"),
        new("10 kWh, most of an evening and night", "10"),
        new("13.5 kWh, a Powerwall-sized battery", "13.5"),
        new("18 kWh, a day of ordinary use", "18"),
    ];

    /// <summary>Backup choices.</summary>
    public static IReadOnlyList<SelectListItem> Backups { get; } =
    [
        new("Nothing: the system shuts down in a cut, as standard inverters must", nameof(BackupLevel.None)),
        new("Lights, fridge, router and boiler on protected circuits", nameof(BackupLevel.ProtectedCircuits)),
        new("The whole house, through a backup gateway", nameof(BackupLevel.WholeHome)),
    ];
}
