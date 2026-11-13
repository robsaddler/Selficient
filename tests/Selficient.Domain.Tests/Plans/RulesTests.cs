using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Plans.Rules;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Plans;

public sealed class RulesTests
{
    private static readonly RiskAssessment NoRisks = new RiskAssessmentPipeline([]).Run(Profiles.Flat());
    private static readonly RiskAssessment GardenFireAndFlood = new RiskAssessmentPipeline([new GardenFireAssessor(), new FloodingAssessor()]).Run(Profiles.FloodZoneBungalow());

    private static List<PlanAction> Run(IPlanRule rule, HouseholdProfile profile, RiskAssessment? risks = null) =>
        [.. rule.Contribute(profile, risks ?? NoRisks)];

    [Fact]
    public void The_baseline_sizes_water_light_and_banks_to_the_household()
    {
        List<PlanAction> actions = Run(new BaselineChecklistRule(), Profiles.RobsHouse());

        actions.Count.ShouldBe(12);
        PlanAction liners = actions.Single(a => a.Id.Value == "no-flush-toilet-kit");
        liners.Title.ShouldBe("Toilet liners and gelling powder for 4 people for three days");
        liners.Why.ShouldContain("the official advice is not to flush at all, even with stored water");
        liners.Cost.ShouldBe(MoneyRange.Pounds(10m, 34m));
        liners.Mitigates.ShouldBe([RiskKind.RegionalDisruption, RiskKind.WaterInterruption], ignoreOrder: true);
        PlanAction backup = actions.Single(a => a.Id.Value == "landline-battery-backup");
        backup.Title.ShouldBe("A battery backup for the router, so the broadband and the house phone work in a power cut");
        backup.Why.ShouldContain("31 January 2027");
        backup.Why.ShouldContain("it has to give a backup that lasts at least an hour, free, to anyone who relies on the landline");
        backup.Cost.ShouldBe(MoneyRange.Pounds(0m, 62m));
        PlanAction radio = actions.Single(a => a.Id.Value == "radio-dab-fm-with-crank");
        radio.Title.ShouldBe("A radio with FM that runs on AA cells, with a wind-up handle");
        radio.Why.ShouldContain("BBC Radio 2 and Radio 4");
        radio.Mitigates.ShouldContain(RiskKind.RegionalDisruption);
        PlanAction water = actions.Single(a => a.Id.Value == "water-bottled-three-days");
        water.Title.ShouldBe("36 litres of sealed bottled water");
        water.Why.ShouldStartWith("Three days of drinking water for 4 people. That is 18 of the big two-litre bottles");
        water.Provides.WaterLitres.ShouldBe(36m);
        actions.Single(a => a.Id.Value == "power-failure-lights").Title.ShouldBe("4 plug-in power-failure torches");
        actions.Single(a => a.Id.Value == "headtorches").Title.ShouldBe("4 headtorches");
        actions.Single(a => a.Id.Value == "power-banks").Title.ShouldStartWith("2 power banks");
        actions.ShouldAllBe(a => a.Tier == Tier.ThisWeek);
        actions.ShouldAllBe(a => a.Why.EndsWith('.'));
    }

    [Fact]
    public void The_baseline_speaks_of_one_person_in_the_singular()
    {
        List<PlanAction> actions = Run(new BaselineChecklistRule(), Profiles.Flat());

        actions.Single(a => a.Id.Value == "water-bottled-three-days").Why.ShouldStartWith("Three days of drinking water for one person. That is 5 of the big two-litre bottles");
        actions.Single(a => a.Id.Value == "power-banks").Title.ShouldStartWith("2 power banks");
    }

    [Fact]
    public void Priority_services_only_for_those_who_qualify()
    {
        Run(new PriorityServicesRule(), Profiles.RobsHouse()).ShouldHaveSingleItem().Id.Value.ShouldBe("priority-services-register");
        Run(new PriorityServicesRule(), Profiles.Flat()).ShouldBeEmpty();
    }

    [Fact]
    public void A_tall_tree_gets_a_survey_this_week_and_a_decision_this_month()
    {
        List<PlanAction> actions = Run(new TallTreeRule(), Profiles.RobsHouse());

        actions.Select(a => (a.Id.Value, a.Tier)).ShouldBe([("arborist-survey", Tier.ThisWeek), ("tree-work-if-advised", Tier.ThisMonth)]);
        Run(new TallTreeRule(), Profiles.Flat()).ShouldBeEmpty();
    }

    [Fact]
    public void Garden_fire_actions_follow_the_risk_and_the_decking()
    {
        RiskAssessment robsRisks = new RiskAssessmentPipeline([new GardenFireAssessor()]).Run(Profiles.RobsHouse());

        Run(new GardenFireRule(), Profiles.RobsHouse(), robsRisks).Select(a => a.Id.Value).ShouldBe(["clear-leaf-litter-under-decking", "garden-firebreak-and-hose"]);
        Run(new GardenFireRule(), Profiles.FloodZoneBungalow(), GardenFireAndFlood).Select(a => a.Id.Value).ShouldBe(["garden-firebreak-and-hose"]);
        Run(new GardenFireRule(), Profiles.Flat()).ShouldBeEmpty();
    }

    private static HouseholdProfile With(HouseholdProfile profile, bool electricCar = false, bool woodStove = false, bool waterButt = false, int infants = 0) =>
        profile with { Particulars = new Particulars(electricCar, woodStove, waterButt, infants) };

    [Fact]
    public void A_car_gets_a_half_tank_rule()
    {
        Run(new CarRule(), Profiles.RobsHouse()).ShouldHaveSingleItem().Mitigates.ShouldContain(RiskKind.WinterAccess);
        Run(new CarRule(), Profiles.Flat()).ShouldBeEmpty();
    }

    [Fact]
    public void An_electric_car_is_asked_to_keep_its_charge_and_never_a_tank()
    {
        PlanAction charge = Run(new CarRule(), With(Profiles.RobsHouse(), electricCar: true)).ShouldHaveSingleItem();

        charge.Id.Value.ShouldBe("car-above-half-charge");
        charge.Title.ShouldBe("Keep the car above half charge from November to March");
        charge.Why.ShouldBe("Charging stops with the power, at home and at most public chargers. Half a battery is several days of local driving and a way off the hill before the road closes, and the car's own sockets charge phones and power banks.");
        charge.Cost.ShouldBe(MoneyRange.Free);
        charge.Tier.ShouldBe(Tier.ThisWeek);
        charge.Title.ShouldNotContain("tank");
        Run(new CarRule(), Profiles.RobsHouse()).Single().Id.Value.ShouldBe("car-above-half-a-tank");
        Run(new CarRule(), With(Profiles.Flat(), electricCar: true)).ShouldBeEmpty("an electric car in a house that said it has no car is no car");
    }

    [Fact]
    public void A_baby_gets_nappies_and_formula_that_needs_no_boiled_water()
    {
        PlanAction one = Run(new InfantsRule(), With(Profiles.RobsHouse(), infants: 1)).ShouldHaveSingleItem();
        PlanAction two = Run(new InfantsRule(), With(Profiles.RobsHouse(), infants: 2)).Single();

        one.Id.Value.ShouldBe("formula-and-nappies");
        one.Tier.ShouldBe(Tier.ThisWeek);
        one.Title.ShouldBe("Two weeks of nappies, wipes and, if you use it, formula");
        one.Why.ShouldBe("The shops and the deliveries are the first things to stop. Formula made from powder needs boiled water, so it depends on a way to cook: keep some ready-to-feed cartons, which need neither, for the first days.");
        one.Cost.ShouldBe(MoneyRange.Pounds(25m, 70m));
        two.Cost.ShouldBe(MoneyRange.Pounds(50m, 140m));
        Run(new InfantsRule(), Profiles.RobsHouse()).ShouldBeEmpty("children who are not infants need nothing a shop of nappies would give");
        Selficient.Domain.Resilience.Providers.Find("formula-and-nappies").ShouldNotBeNull("so having it moves the board's row for infants and pets");
    }

    [Fact]
    public void Infants_cannot_outnumber_the_children()
    {
        Household household = Profiles.RobsHouse().Household;

        Particulars.Create(household, true, true, true, 2).Value.ShouldBe(new Particulars(true, true, true, 2));
        Particulars.Create(household, false, false, false, 3).Error.Code.ShouldBe(Selficient.Domain.Primitives.DomainErrors.InfantsOutOfRange().Code);
        Particulars.Create(household, false, false, false, -1).IsFailure.ShouldBeTrue();
        Should.Throw<ArgumentNullException>(() => Particulars.Create(null!, false, false, false, 0));
        Particulars.None.ShouldBe(new Particulars(false, false, false, 0));
        Profiles.RobsHouse().Nation.ShouldBe(Nation.Unknown);
    }

    [Fact]
    public void A_stove_is_swept_and_stocked_whatever_the_main_heating_is()
    {
        PlanAction ready = Run(new WoodStoveRule(), With(Profiles.RobsHouse(), woodStove: true)).ShouldHaveSingleItem();

        ready.Id.Value.ShouldBe("wood-stove-ready");
        ready.Tier.ShouldBe(Tier.ThisMonth);
        ready.Title.ShouldBe("Have the flue swept and the log store full before winter");
        ready.Why.ShouldBe("Your stove is heat, a kettle and a hot meal that need nothing from anyone, for as long as the logs last. A swept flue and dry logs are what make it safe to lean on, and the carbon monoxide alarm belongs in the same room.");
        ready.Cost.ShouldBe(MoneyRange.Pounds(75m, 250m));
        Run(new WoodStoveRule(), Profiles.FloodZoneBungalow()).ShouldHaveSingleItem("wood as the main heating is a stove");
        Run(new WoodStoveRule(), Profiles.RobsHouse()).ShouldBeEmpty();
    }

    [Fact]
    public void Flood_actions_only_when_flooding_is_a_risk()
    {
        Run(new FloodRule(), Profiles.FloodZoneBungalow(), GardenFireAndFlood).Select(a => a.Id.Value).ShouldBe(["flood-warnings-and-plan", "flood-protection-kit", "flood-water-alarm"]);
        Run(new FloodRule(), Profiles.RobsHouse()).ShouldBeEmpty("very low on the map is nothing to do, and nothing is said");
    }

    [Fact]
    public void A_house_whose_flood_risk_nobody_knows_is_sent_to_its_own_nations_map_first()
    {
        PlanAction check = Run(new FloodRule(), Profiles.Flat() with { Nation = Nation.Scotland }).ShouldHaveSingleItem();

        check.Id.Value.ShouldBe("check-flood-risk");
        check.Tier.ShouldBe(Tier.ThisWeek);
        check.Title.ShouldBe("Check what the flood map says about your address");
        check.Why.ShouldBe("This plan does not know your flood risk yet, and it changes what comes first. The check takes two minutes with your postcode and covers rivers, the sea and surface water. If it says low, medium or high, change your answer and the plan adds what to do about it.");
        check.Cost.ShouldBe(MoneyRange.Free);
        check.Link.ShouldBe(new ActionLink("Check your address on SEPA's flood maps", new Uri("https://beta.sepa.scot/flooding/flood-maps/")));
        Run(new FloodRule(), Profiles.Flat() with { Nation = Nation.England }).Single().Link!.Address.ShouldBe(new Uri("https://www.gov.uk/check-long-term-flood-risk"));
        Run(new FloodRule(), Profiles.Flat() with { Nation = Nation.Wales }).Single().Link!.Text.ShouldBe("Check your address with Natural Resources Wales");
        Run(new FloodRule(), Profiles.Flat() with { Nation = Nation.NorthernIreland }).Single().Link!.Address.ShouldBe(new Uri("https://www.nidirect.gov.uk/articles/check-risk-flooding-your-area"));
        Run(new FloodRule(), Profiles.Flat()).Single().Link.ShouldBeNull("with no nation there is no service to name, and the action still stands");
    }

    [Fact]
    public void Nobody_is_told_to_sign_up_for_a_flood_warning_they_cannot_get()
    {
        static PlanAction Plan(Nation nation, FloodWater from) =>
            Run(new FloodRule(), Profiles.FloodZoneBungalow() with { Nation = nation, FloodFrom = from }, GardenFireAndFlood)[0];

        // Rivers and the sea, where a nation runs a warning service: sign up, at that nation's own address.
        PlanAction england = Plan(Nation.England, FloodWater.RiversOrSea);
        england.Title.ShouldBe("Sign up for free flood warnings and write a one-page flood plan");
        england.Why.ShouldBe("Know where the stopcock and the fuse board are, where you would move the car, and what is lifted off the floor first. There is no upstairs to go to, so decide now where you would go and when. Warnings come by phone call, text and email. Check that your buildings and contents insurance covers flood.");
        england.Link.ShouldBe(new ActionLink("Sign up for flood warnings on GOV.UK", new Uri("https://www.gov.uk/get-flood-warnings")));
        Plan(Nation.Wales, FloodWater.NotSaid).Link!.Address.ShouldBe(new Uri("https://naturalresources.wales/flooding/sign-up-to-receive-flood-warnings/"));
        Plan(Nation.Scotland, FloodWater.RiversOrSea).Link.ShouldBe(new ActionLink("Sign up to SEPA's Floodline", new Uri("https://www.sepa.org.uk/flooding/floodline-register/")));
        Plan(Nation.Unknown, FloodWater.NotSaid).Link.ShouldBeNull();
        Plan(Nation.Unknown, FloodWater.NotSaid).Title.ShouldStartWith("Sign up for free flood warnings");

        // Northern Ireland has no warning service, and says so.
        PlanAction northernIreland = Plan(Nation.NorthernIreland, FloodWater.RiversOrSea);
        northernIreland.Title.ShouldBe("Write a one-page flood plan, and watch the Met Office warnings for heavy rain");
        northernIreland.Why.ShouldContain("There is no flood warning service to sign up to in Northern Ireland, so the Met Office's warnings for rain are the notice you get.");
        northernIreland.Link.ShouldBe(new ActionLink("Preparing for a flood, on nidirect", new Uri("https://www.nidirect.gov.uk/articles/preparing-flood")));

        // Surface water comes with the rain, anywhere, and nobody calls ahead of it.
        PlanAction rain = Plan(Nation.England, FloodWater.SurfaceWater);
        rain.Title.ShouldBe("Write a one-page flood plan, and watch the Met Office warnings for heavy rain");
        rain.Why.ShouldBe("Know where the stopcock and the fuse board are, where you would move the car, and what is lifted off the floor first. There is no upstairs to go to, so decide now where you would go and when. The risk here is surface water, which comes with heavy rain, too fast for a flood warning service to call ahead, so a yellow or amber warning for rain is the notice you get. Check that your buildings and contents insurance covers flood.");
        rain.Link.ShouldBe(new ActionLink("Weather warnings from the Met Office", new Uri("https://weather.metoffice.gov.uk/warnings-and-advice/uk-warnings")));
        rain.Title.ShouldNotContain("Sign up");
        Plan(Nation.NorthernIreland, FloodWater.SurfaceWater).Link.ShouldBe(FloodServices.RainWarnings);

        // The plan is for this house: stairs where there are stairs, and no car to move where there is none.
        HouseholdProfile terrace = Profiles.Build(2, 0, false, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, car: false, false, false, false, null, FloodRisk.High) with { Nation = Nation.England };
        Run(new FloodRule(), terrace, GardenFireAndFlood)[0].Why.ShouldStartWith("Know where the stopcock and the fuse board are, and what goes upstairs first. Warnings come");
        england.Why.ShouldNotContain("goes upstairs", customMessage: "a bungalow has no upstairs to carry things to");
    }

    [Fact]
    public void Every_official_address_is_https_on_a_government_or_agency_host()
    {
        string[] allowed = ["www.gov.uk", "naturalresources.wales", "beta.sepa.scot", "www.sepa.org.uk", "www.nidirect.gov.uk", "weather.metoffice.gov.uk"];
        IEnumerable<ActionLink?> links = Enum.GetValues<Nation>().SelectMany(nation => new[] { FloodServices.Check(nation), FloodServices.Warnings(nation) })
            .Append(FloodServices.NorthernIrelandAdvice).Append(FloodServices.RainWarnings);

        foreach (ActionLink link in links.OfType<ActionLink>())
        {
            link.Address.Scheme.ShouldBe("https");
            allowed.ShouldContain(link.Address.Host);
            link.Text.ShouldNotBeNullOrWhiteSpace();
        }

        FloodServices.Check(Nation.Unknown).ShouldBeNull();
        FloodServices.Warnings(Nation.NorthernIreland).ShouldBeNull("there is no service to sign up to");
        Enum.GetValues<Nation>().Count(nation => FloodServices.Check(nation) is not null).ShouldBe(4);
    }

    [Fact]
    public void Water_stores_scale_down_for_a_flat_and_add_a_butt_for_a_garden()
    {
        List<PlanAction> house = Run(new WaterStoreRule(), Profiles.RobsHouse());
        List<PlanAction> flat = Run(new WaterStoreRule(), Profiles.Flat());

        house.Select(a => a.Id.Value).ShouldBe(["water-cans", "bath-liner", "water-butt"]);
        house[0].Title.ShouldBe("4 food-grade 25-litre water cans with taps");
        house[0].Why.ShouldStartWith("100 litres is about 8 days of drinking water for 4 people.");
        flat.Select(a => a.Id.Value).ShouldBe(["water-cans", "bath-liner"]);
        flat[0].Title.ShouldBe("1 food-grade 25-litre water cans with taps");
        Run(new WaterStoreRule(), With(Profiles.RobsHouse(), waterButt: true)).Select(a => a.Id.Value).ShouldBe(["water-cans", "bath-liner"], "a garden that has a butt is not sold another");
        Run(new WaterStoreRule(), With(Profiles.Flat(), waterButt: true)).Select(a => a.Id.Value).ShouldBe(["water-cans", "bath-liner"]);
    }

    [Fact]
    public void Pets_get_a_row_of_their_own_beside_the_month_of_food()
    {
        List<PlanAction> withPets = Run(new FoodStoreRule(), Profiles.FloodZoneBungalow());

        withPets.Select(a => a.Id.Value).ShouldBe(["food-one-month", "pet-food-two-weeks"]);
        withPets[0].Why.ShouldBe("Take from the front and restock at the back, and nothing ever goes off. Add a manual tin opener.");
        withPets[1].Title.ShouldBe("Two weeks of pet food, and any medication, kept ahead of the bag in use");
        withPets[1].Why.ShouldBe("Buy the next bag or box before the last one is opened, so there is always a fortnight in the house and nothing goes off. Store a few litres of water a day for them as well as your own.");
        withPets[1].Cost.ShouldBe(MoneyRange.Pounds(15m, 60m));
        withPets[1].Tier.ShouldBe(Tier.ThisMonth);
        Run(new FoodStoreRule(), Profiles.RobsHouse()).Single().Id.Value.ShouldBe("food-one-month");
        Selficient.Domain.Resilience.Providers.Find("pet-food-two-weeks").ShouldNotBeNull("so having it moves the board's row for infants and pets");
    }

    [Fact]
    public void Cooking_is_a_lighter_for_a_gas_hob_and_a_stove_otherwise()
    {
        Run(new CookingRule(), Profiles.FloodZoneBungalow()).Single().Id.Value.ShouldBe("long-reach-lighter-for-gas-hob");
        Run(new CookingRule(), Profiles.RobsHouse()).Single().Id.Value.ShouldBe("butane-cartridge-stove");
    }

    [Fact]
    public void A_flat_with_nowhere_outside_is_not_sold_a_stove_it_cannot_use()
    {
        PlanAction flasks = Run(new CookingRule(), Profiles.Flat()).ShouldHaveSingleItem();

        flasks.Id.Value.ShouldBe("flasks-for-hot-food");
        flasks.Title.ShouldBe("Two large vacuum flasks, filled from the kettle when a warning comes");
        flasks.Why.ShouldBe("A camping stove is not safe indoors and a flat has nowhere outside to use one, so this plan does not ask you to buy one. Two flasks filled before the power goes are hot drinks and instant meals for most of a day; after that it is the food that needs no cooking.");
        flasks.Cost.ShouldBe(MoneyRange.Pounds(50m, 95m));
        Selficient.Domain.Resilience.ResilienceProfile.FromProfile(Profiles.Flat()).CanHave(Selficient.Domain.Resilience.Placement.OutdoorsOnly).ShouldBeFalse("the board would not count a stove there, which is why the plan does not sell one");

        HouseholdProfile gardenFlat = Profiles.Build(1, 0, false, false, false, DwellingType.Flat, 1, HeatingType.Electric, HobType.Electric, false, garden: true, false, false, 12m, FloodRisk.Unknown);
        Run(new CookingRule(), gardenFlat).Single().Id.Value.ShouldBe("butane-cartridge-stove", "a flat with a garden has somewhere to cook");
        HouseholdProfile gasFlat = Profiles.Build(1, 0, false, false, false, DwellingType.Flat, 1, HeatingType.Electric, HobType.Gas, false, false, false, false, 12m, FloodRisk.Unknown);
        Run(new CookingRule(), gasFlat).Single().Id.Value.ShouldBe("long-reach-lighter-for-gas-hob");
    }

    [Fact]
    public void Lanterns_documents_filter_and_drill_apply_to_everyone()
    {
        Run(new LanternRule(), Profiles.RobsHouse()).Single().Title.ShouldBe("3 rechargeable LED lanterns");
        Run(new LanternRule(), Profiles.Flat()).Single().Title.ShouldBe("2 rechargeable LED lanterns");
        Run(new DocumentsRule(), Profiles.Flat()).Single().Tier.ShouldBe(Tier.ThisMonth);
        Run(new WaterFilterRule(), Profiles.Flat()).Single().Provides.WaterLitres.ShouldBe(980m);
        Run(new DrillRule(), Profiles.Flat()).Single().Id.Value.ShouldBe("lights-out-drill");
    }

    [Fact]
    public void Solar_needs_a_roof_of_your_own()
    {
        Run(new SolarQuotesRule(), Profiles.RobsHouse()).Select(a => (a.Id.Value, a.Tier)).ShouldBe([("solar-quotes", Tier.ThisMonth), ("rooftop-solar-and-battery", Tier.TwelveMonths)]);
        Run(new SolarQuotesRule(), Profiles.Flat()).ShouldBeEmpty();
    }

    [Fact]
    public void The_power_station_pairs_with_a_boiler_plug_a_heating_fallback_or_nothing()
    {
        Run(new PowerStationRule(), Profiles.RobsHouse()).Select(a => a.Id.Value).ShouldBe(["portable-power-station", "boiler-plug-and-socket"]);
        Run(new PowerStationRule(), Profiles.Flat()).Select(a => a.Id.Value).ShouldBe(["portable-power-station", "heating-fallback"]);
        Run(new PowerStationRule(), Profiles.FloodZoneBungalow()).Select(a => a.Id.Value).ShouldBe(["portable-power-station"]);
        Run(new PowerStationRule(), With(Profiles.Flat(), woodStove: true)).Select(a => a.Id.Value).ShouldBe(["portable-power-station"], "electric heating with a stove beside it has its fallback already");
        Run(new PowerStationRule(), With(Profiles.RobsHouse(), woodStove: true)).Select(a => a.Id.Value).ShouldBe(["portable-power-station", "boiler-plug-and-socket"], "a boiler is still worth a plug");
    }

    [Fact]
    public void Satellite_internet_is_for_the_home_business()
    {
        Run(new BusinessCommsRule(), Profiles.RobsHouse()).ShouldHaveSingleItem();
        Run(new BusinessCommsRule(), Profiles.Flat()).ShouldBeEmpty();
    }

    [Fact]
    public void Every_rule_rejects_a_missing_profile_or_assessment()
    {
        IPlanRule[] needProfile =
        [
            new BaselineChecklistRule(), new PriorityServicesRule(), new TallTreeRule(), new CarRule(),
            new WaterStoreRule(), new FoodStoreRule(), new CookingRule(), new LanternRule(),
            new SolarQuotesRule(), new PowerStationRule(), new WaterFilterRule(), new BusinessCommsRule(),
            new InfantsRule(), new WoodStoveRule(),
        ];
        foreach (IPlanRule rule in needProfile)
        {
            Should.Throw<ArgumentNullException>(() => rule.Contribute(null!, NoRisks).ToList());
        }

        Should.Throw<ArgumentNullException>(() => new GardenFireRule().Contribute(null!, NoRisks).ToList());
        Should.Throw<ArgumentNullException>(() => new GardenFireRule().Contribute(Profiles.Flat(), null!).ToList());
        Should.Throw<ArgumentNullException>(() => new FloodRule().Contribute(Profiles.Flat(), null!).ToList());
        Should.Throw<ArgumentNullException>(() => new FloodRule().Contribute(null!, NoRisks).ToList());
    }
}
