using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Selficient.Application.Planning;
using Selficient.Domain.Households;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// What the wizard collects. Every field is a closed choice or a bounded number except the
/// postcode, which the Domain validates again. Attributes give whole-sentence messages because the
/// framework's defaults are fragments.
/// </summary>
public sealed class PlanForm
{
    /// <summary>The postcode, validated properly by the Domain; this is the first line of defence.</summary>
    [Required(ErrorMessage = "Enter your postcode so we can find your area.")]
    [StringLength(10, ErrorMessage = "That does not look like a UK postcode. Check it and try again.")]
    [Display(Name = "Postcode")]
    public string Postcode { get; set; } = string.Empty;

    /// <summary>Everyone who lives there.</summary>
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    [Range(1, 20, ErrorMessage = "A plan covers between one and twenty people.")]
    [Display(Name = "How many people live in your home?")]
    public int People { get; set; } = 2;

    /// <summary>How many are children.</summary>
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    [Range(0, 20, ErrorMessage = "The number of children must be between zero and the number of people.")]
    [Display(Name = "How many of them are children?")]
    public int Children { get; set; }

    /// <summary>Medical, mobility or age-related need.</summary>
    [Display(Name = "Does anyone depend on power, water or warmth for a medical or mobility reason?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasVulnerablePerson { get; set; }

    /// <summary>Pets.</summary>
    [Display(Name = "Do you have pets?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasPets { get; set; }

    /// <summary>Income depends on being online at home.</summary>
    [Display(Name = "Does anyone's income depend on being online at home?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool RunsBusinessFromHome { get; set; }

    /// <summary>The kind of home.</summary>
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    [EnumDataType(typeof(DwellingType), ErrorMessage = "Choose the kind of home from the list.")]
    [Display(Name = "What kind of home is it?")]
    public DwellingType DwellingType { get; set; } = DwellingType.SemiDetached;

    /// <summary>Storeys.</summary>
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    [Range(1, 6, ErrorMessage = "A home has between one and six storeys.")]
    [Display(Name = "How many storeys, counting rooms in the roof?")]
    public int Storeys { get; set; } = 2;

    /// <summary>Main heating.</summary>
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    [EnumDataType(typeof(HeatingType), ErrorMessage = "Choose the heating from the list.")]
    [Display(Name = "How is it heated?")]
    public HeatingType Heating { get; set; } = HeatingType.GasBoiler;

    /// <summary>The hob.</summary>
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    [EnumDataType(typeof(HobType), ErrorMessage = "Choose the hob from the list.")]
    [Display(Name = "What does the hob run on?")]
    public HobType Hob { get; set; } = HobType.Electric;

    /// <summary>Car.</summary>
    [Display(Name = "Is there a car?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasCar { get; set; }

    /// <summary>Garden.</summary>
    [Display(Name = "Is there a garden?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasGarden { get; set; }

    /// <summary>Decking.</summary>
    [Display(Name = "Is there timber decking?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasDecking { get; set; }

    /// <summary>Tall tree.</summary>
    [Display(Name = "Is there a tree tall enough to reach the house if it fell?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasTallTreeNearHouse { get; set; }

    /// <summary>Flood risk as reported by the national map.</summary>
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    [EnumDataType(typeof(FloodRisk), ErrorMessage = "Choose the flood risk from the list.")]
    [Display(Name = "What does the national flood map say about your area?")]
    public FloodRisk FloodRisk { get; set; } = FloodRisk.Unknown;

    /// <summary>The request for the Application layer.</summary>
    public PlanHouseholdRequest ToRequest() => new(
        Postcode,
        new GeneratePlanCommand(People, Children, HasVulnerablePerson, HasPets, RunsBusinessFromHome, DwellingType, Storeys, Heating, Hob, HasCar, HasGarden, HasDecking, HasTallTreeNearHouse, null, FloodRisk));

    /// <summary>Options for the selects, in the interface's voice.</summary>
    public static IReadOnlyList<SelectListItem> DwellingTypes { get; } =
    [
        new("A flat or maisonette", nameof(DwellingType.Flat)),
        new("A terraced house", nameof(DwellingType.Terrace)),
        new("A semi-detached house", nameof(DwellingType.SemiDetached)),
        new("A detached house", nameof(DwellingType.Detached)),
        new("A bungalow", nameof(DwellingType.Bungalow)),
    ];

    /// <summary>Heating options.</summary>
    public static IReadOnlyList<SelectListItem> HeatingTypes { get; } =
    [
        new("Mains gas boiler", nameof(HeatingType.GasBoiler)),
        new("Electric heating", nameof(HeatingType.Electric)),
        new("Heat pump", nameof(HeatingType.HeatPump)),
        new("Oil boiler", nameof(HeatingType.Oil)),
        new("LPG boiler", nameof(HeatingType.Lpg)),
        new("Wood or multi-fuel stove", nameof(HeatingType.Wood)),
    ];

    /// <summary>Hob options.</summary>
    public static IReadOnlyList<SelectListItem> HobTypes { get; } =
    [
        new("Gas", nameof(HobType.Gas)),
        new("Electric or induction", nameof(HobType.Electric)),
        new("No hob, or not sure", nameof(HobType.None)),
    ];

    /// <summary>Flood risk options.</summary>
    public static IReadOnlyList<SelectListItem> FloodRisks { get; } =
    [
        new("I have not checked yet", nameof(FloodRisk.Unknown)),
        new("Very low", nameof(FloodRisk.VeryLow)),
        new("Low", nameof(FloodRisk.Low)),
        new("Medium", nameof(FloodRisk.Medium)),
        new("High", nameof(FloodRisk.High)),
    ];
}
