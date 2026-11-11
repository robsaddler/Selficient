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

    [Fact]
    public void A_car_gets_a_half_tank_rule()
    {
        Run(new CarRule(), Profiles.RobsHouse()).ShouldHaveSingleItem().Mitigates.ShouldContain(RiskKind.WinterAccess);
        Run(new CarRule(), Profiles.Flat()).ShouldBeEmpty();
    }

    [Fact]
    public void Flood_actions_only_when_flooding_is_a_risk()
    {
        Run(new FloodRule(), Profiles.FloodZoneBungalow(), GardenFireAndFlood).Select(a => a.Id.Value).ShouldBe(["flood-warnings-and-plan", "flood-protection-kit"]);
        Run(new FloodRule(), Profiles.RobsHouse()).ShouldBeEmpty();
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
    }

    [Fact]
    public void A_month_of_food_remembers_the_pets()
    {
        Run(new FoodStoreRule(), Profiles.FloodZoneBungalow()).Single().Why.ShouldEndWith("Include the pets' food.");
        Run(new FoodStoreRule(), Profiles.RobsHouse()).Single().Why.ShouldEndWith("Add a manual tin opener.");
    }

    [Fact]
    public void Cooking_is_a_lighter_for_a_gas_hob_and_a_stove_otherwise()
    {
        Run(new CookingRule(), Profiles.FloodZoneBungalow()).Single().Id.Value.ShouldBe("long-reach-lighter-for-gas-hob");
        Run(new CookingRule(), Profiles.RobsHouse()).Single().Id.Value.ShouldBe("butane-cartridge-stove");
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
        ];
        foreach (IPlanRule rule in needProfile)
        {
            Should.Throw<ArgumentNullException>(() => rule.Contribute(null!, NoRisks).ToList());
        }

        Should.Throw<ArgumentNullException>(() => new GardenFireRule().Contribute(null!, NoRisks).ToList());
        Should.Throw<ArgumentNullException>(() => new GardenFireRule().Contribute(Profiles.Flat(), null!).ToList());
        Should.Throw<ArgumentNullException>(() => new FloodRule().Contribute(Profiles.Flat(), null!).ToList());
    }
}
