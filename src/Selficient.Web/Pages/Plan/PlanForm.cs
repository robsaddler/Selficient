using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Selficient.Application.Planning;
using Selficient.Domain.Households;
using Selficient.Domain.Primitives;
using Selficient.Domain.Resilience;

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

    /// <summary>Someone who could not get out alone.</summary>
    [Display(Name = "Would anyone need help to get downstairs or out of the home in an emergency?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool NeedsHelpToLeave { get; set; }

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

    /// <summary>
    /// Whether there is a car, as plans made before the one question about the car said it. The form
    /// no longer asks this on its own: <see cref="Car"/> says whether and what kind in one answer, and
    /// <see cref="OwnsCar"/> reads either.
    /// </summary>
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
    [Display(Name = "How likely is flooding where you live?")]
    public FloodRisk FloodRisk { get; set; } = FloodRisk.Unknown;

    /// <summary>A chest freezer as well as the fridge-freezer.</summary>
    [Display(Name = "Is there a chest freezer as well?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasChestFreezer { get; set; }

    /// <summary>A wood or multi-fuel stove in a house heated some other way.</summary>
    [Display(Name = "Is there a wood or multi-fuel stove, even if it is not the main heating?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasWoodStove { get; set; }

    /// <summary>A water butt.</summary>
    [Display(Name = "Is there a water butt?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasWaterButt { get; set; }

    /// <summary>Whether the household grows food.</summary>
    [EnumDataType(typeof(Growing), ErrorMessage = "Choose from the list.")]
    [Display(Name = "Do you grow any food?")]
    public Growing? Growing { get; set; }

    /// <summary>What kind of car, when there is one.</summary>
    [EnumDataType(typeof(CarKind), ErrorMessage = "Choose the kind of car from the list.")]
    [Display(Name = "Is there a car?")]
    public CarKind? Car { get; set; }

    /// <summary>Children under two.</summary>
    [Range(0, 20, ErrorMessage = "The number of infants must be between zero and the number of children.")]
    [Display(Name = "How many of the children are under two?")]
    public int? Infants { get; set; }

    /// <summary>Days of regular medicines in the house.</summary>
    [Range(0, 365, ErrorMessage = "Days of medicines must be between 0 and 365.")]
    [Display(Name = "How many days of regular medicines are in the house?")]
    public int? MedicineDaysInStock { get; set; } = 7;

    /// <summary>The floor, for a flat.</summary>
    [Range(0, 40, ErrorMessage = "The floor must be between 0 for the ground floor and 40.")]
    [Display(Name = "If it is a flat, which floor is it on? Ground is 0.")]
    public int? Floor { get; set; }

    /// <summary>Owned or rented. Empty on a plan made before the question, which reads as owned.</summary>
    [EnumDataType(typeof(Tenure), ErrorMessage = "Choose whether the home is owned or rented from the list.")]
    [Display(Name = "Is the home yours or rented?")]
    public Tenure? Tenure { get; set; }

    /// <summary>A cellar or basement.</summary>
    [Display(Name = "Is there a cellar or basement?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool HasCellar { get; set; }

    /// <summary>A bed on the ground floor or below it.</summary>
    [Display(Name = "Does anyone sleep on the ground floor or below it?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool SleepsDownstairs { get; set; }

    /// <summary>A stairlift, and what a power cut does to it. Empty on a plan made before the question, which reads as none.</summary>
    [EnumDataType(typeof(Stairlift), ErrorMessage = "Choose the answer about a stairlift from the list.")]
    [Display(Name = "Is there a stairlift?")]
    public Stairlift? Stairlift { get; set; }

    /// <summary>Where the water comes from. Empty on a plan made before the question, which reads as the mains.</summary>
    [EnumDataType(typeof(WaterSupply), ErrorMessage = "Choose where the water comes from from the list.")]
    [Display(Name = "Where does your water come from?")]
    public WaterSupply? Water { get; set; }

    /// <summary>A septic tank, cesspit or treatment plant.</summary>
    [Display(Name = "Does the waste go to a septic tank, cesspit or treatment plant, and not a sewer?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool OwnDrains { get; set; }

    /// <summary>A prepayment meter for gas or electricity.</summary>
    [Display(Name = "Is the gas or electricity on a prepayment meter?")]
    [Required(ErrorMessage = "This answer is needed to build the plan.")]
    public bool PrepaymentMeter { get; set; }

    /// <summary>The things only this household relies on.</summary>
    public List<DependencyForm> Dependencies { get; init; } = [];

    /// <summary>Which scenario the board shows; empty for the most likely one.</summary>
    [StringLength(20, ErrorMessage = "That is not one of the scenarios.")]
    public string? Scenario { get; set; }

    /// <summary>Which source the board sheet is drawn for; empty for what is in the house.</summary>
    [StringLength(40, ErrorMessage = "That is not one of the sources.")]
    public string? Source { get; set; }

    /// <summary>Which season the board shows; empty for the scenario's own.</summary>
    [EnumDataType(typeof(Season), ErrorMessage = "Choose winter or summer.")]
    public Season? Season { get; set; }

    /// <summary>True when the household has a car, said either way: by the kind, or by an older plan's tick.</summary>
    public bool OwnsCar => HasCar || Car > CarKind.None;

    /// <summary>The car there is: none, or its kind, with an older plan's bare tick read as petrol or diesel.</summary>
    public CarKind CarOwned => Car > CarKind.None ? Car.Value : HasCar ? CarKind.Petrol : CarKind.None;

    /// <summary>The floor the home is on: what a flat said, and the ground for anything else.</summary>
    public int FloorOfFlat => DwellingType == DwellingType.Flat ? Floor ?? 0 : 0;

    /// <summary>The request for the Application layer.</summary>
    public PlanHouseholdRequest ToRequest() => new(
        Postcode,
        new GeneratePlanCommand(People, Children, HasVulnerablePerson, HasPets, RunsBusinessFromHome, DwellingType, Storeys, Heating, Hob, OwnsCar, HasGarden, HasDecking, HasTallTreeNearHouse, null, FloodRisk,
            CarIsElectric: Car >= CarKind.Electric,
            HasWoodStove: HasWoodStove,
            HasWaterButt: HasWaterButt,
            Infants: Infants ?? 0,
            Tenure: Tenure ?? Selficient.Domain.Households.Tenure.Owner,
            Water: Water ?? WaterSupply.Mains,
            OwnDrains: OwnDrains,
            PrepaymentMeter: PrepaymentMeter,
            NeedsHelpToLeave: NeedsHelpToLeave,
            HasCellar: HasCellar,
            SleepsDownstairs: SleepsDownstairs,
            Floor: FloorOfFlat,
            Stairlift: Stairlift ?? Selficient.Domain.Households.Stairlift.None));

    /// <summary>
    /// The household as the resilience engine sees it. The newer questions are optional so a profile
    /// saved before they existed still posts: a car with no kind given is petrol, no growing, no
    /// infants, a week of medicines, the ground floor.
    /// </summary>
    public Result<ResilienceProfile> ToResilienceProfile(HouseholdProfile profile)
    {
        List<Dependency> dependencies = [];
        foreach (DependencyForm row in Dependencies)
        {
            if (row.IsBlank)
            {
                continue;
            }

            Result<Dependency> dependency = row.ToDependency();
            if (dependency.IsFailure)
            {
                return Result.Failure<ResilienceProfile>(dependency.Error);
            }

            dependencies.Add(dependency.Value);
        }

        return ResilienceProfile.Create(profile, HasChestFreezer, HasWoodStove, HasWaterButt, Growing ?? Selficient.Domain.Resilience.Growing.None, CarOwned, Infants ?? 0, MedicineDaysInStock ?? 7, FloorOfFlat, dependencies);
    }

    /// <summary>The kinds of car.</summary>
    public static IReadOnlyList<SelectListItem> CarKinds { get; } =
    [
        new("Yes, petrol or diesel", nameof(CarKind.Petrol)),
        new("Yes, electric", nameof(CarKind.Electric)),
        new("Yes, electric, and it can power things (vehicle-to-load or vehicle-to-home)", nameof(CarKind.ElectricWithOutput)),
    ];

    /// <summary>Whose home it is.</summary>
    public static IReadOnlyList<SelectListItem> Tenures { get; } =
    [
        new("It is ours, with or without a mortgage", nameof(Selficient.Domain.Households.Tenure.Owner)),
        new("It is rented", nameof(Selficient.Domain.Households.Tenure.Tenant)),
    ];

    /// <summary>Whether there is a stairlift, and which kind.</summary>
    public static IReadOnlyList<SelectListItem> Stairlifts { get; } =
    [
        new("No", nameof(Selficient.Domain.Households.Stairlift.None)),
        new("Yes, and it has its own battery", nameof(Selficient.Domain.Households.Stairlift.Battery)),
        new("Yes, and it stops with the power", nameof(Selficient.Domain.Households.Stairlift.MainsOnly)),
        new("Yes, and I do not know which", nameof(Selficient.Domain.Households.Stairlift.NotKnown)),
    ];

    /// <summary>Where the water comes from.</summary>
    public static IReadOnlyList<SelectListItem> WaterSupplies { get; } =
    [
        new("The mains", nameof(WaterSupply.Mains)),
        new("Our own borehole or well, with a pump", nameof(WaterSupply.OwnPumped)),
        new("Our own spring or tank, with no pump", nameof(WaterSupply.OwnGravity)),
    ];

    /// <summary>Whether and how the household grows food.</summary>
    public static IReadOnlyList<SelectListItem> GrowingKinds { get; } =
    [
        new("No", nameof(Selficient.Domain.Resilience.Growing.None)),
        new("Raised beds or a vegetable patch", nameof(Selficient.Domain.Resilience.Growing.RaisedBeds)),
        new("An allotment", nameof(Selficient.Domain.Resilience.Growing.Allotment)),
    ];

    /// <summary>The kinds of thing a household relies on.</summary>
    public static IReadOnlyList<SelectListItem> DependencyKinds { get; } =
    [
        new("A device that needs power", nameof(DependencyKind.PoweredDevice)),
        new("A medicine", nameof(DependencyKind.Medicine)),
        new("A supply, such as formula or stoma bags", nameof(DependencyKind.Supply)),
    ];

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
        new("Look it up for me", nameof(FloodRisk.Unknown)),
        new("Very low", nameof(FloodRisk.VeryLow)),
        new("Low", nameof(FloodRisk.Low)),
        new("Medium", nameof(FloodRisk.Medium)),
        new("High", nameof(FloodRisk.High)),
    ];
}
