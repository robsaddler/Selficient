using Selficient.Domain.Households;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Risks;

public sealed class AssessorsTests
{
    [Fact]
    public void A_storm_cut_is_high_impact_when_the_heating_needs_electricity_and_names_the_hillside()
    {
        Risk risk = new StormPowerCutAssessor().Assess(Profiles.RobsHouse())!;

        risk.Likelihood.ShouldBe(Likelihood.High);
        risk.Impact.ShouldBe(Impact.High);
        risk.Rationale.ShouldStartWith("Your house sits high and exposed");
        risk.Rationale.ShouldContain("Your heating stops the moment the electricity does");
    }

    [Fact]
    public void A_storm_cut_is_medium_impact_for_a_wood_stove_in_the_valley()
    {
        Risk risk = new StormPowerCutAssessor().Assess(Profiles.FloodZoneBungalow())!;

        risk.Impact.ShouldBe(Impact.Medium);
        risk.Rationale.ShouldStartWith("A winter storm cut of six to forty-eight hours is the most likely disruption any UK house will see.");
        risk.Rationale.ShouldEndWith("all go with it.");
    }

    [Fact]
    public void A_stove_beside_the_boiler_is_named_as_the_room_that_stays_warm()
    {
        HouseholdProfile stove = Profiles.RobsHouse() with { Particulars = new Particulars(false, WoodStove: true, false, 0) };

        Risk risk = new StormPowerCutAssessor().Assess(stove)!;

        risk.Impact.ShouldBe(Impact.High);
        risk.Rationale.ShouldEndWith(" Your central heating stops the moment the electricity does, along with the fridge, the lights, the router and the phone masts. The stove keeps one room warm.");
    }

    [Fact]
    public void Garden_fire_names_what_this_garden_has_and_nothing_it_does_not()
    {
        GardenFireAssessor assessor = new();
        static HouseholdProfile Garden(bool decking, bool tree) => Profiles.Build(2, 0, false, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, false, garden: true, decking, tree, null, FloodRisk.VeryLow);

        assessor.Assess(Garden(decking: true, tree: true))!.Rationale.ShouldBe("Dry summers bring grass and hillside fires, and leaf litter under timber decking and the litter a tall tree drops against the house are exactly what an ember looks for.");
        assessor.Assess(Garden(decking: true, tree: false))!.Rationale.ShouldBe("Dry summers bring grass and hillside fires, and leaf litter under timber decking is exactly what an ember looks for.");
        Risk treeOnly = assessor.Assess(Garden(decking: false, tree: true))!;
        treeOnly.Rationale.ShouldBe("Dry summers bring grass and hillside fires, and the litter a tall tree drops against the house and the fence is exactly what an ember looks for.");
        treeOnly.Rationale.ShouldNotContain("decking", customMessage: "a garden with no decking is not told about decking");
        treeOnly.Likelihood.ShouldBe(Likelihood.Medium);
        assessor.Assess(Garden(decking: false, tree: false))!.Rationale.ShouldBe("Dry summers bring garden and grass fires, usually started by a barbecue or a bonfire.");
    }

    [Fact]
    public void A_tall_tree_is_a_severe_risk_and_no_tree_is_no_risk()
    {
        FallingTreeAssessor assessor = new();

        assessor.Assess(Profiles.RobsHouse())!.Impact.ShouldBe(Impact.Severe);
        assessor.Assess(Profiles.Flat()).ShouldBeNull();
    }

    [Fact]
    public void Water_interruption_mentions_the_hill_only_when_exposed()
    {
        WaterInterruptionAssessor assessor = new();

        assessor.Assess(Profiles.RobsHouse())!.Rationale.ShouldEndWith("to reach a hill.");
        assessor.Assess(Profiles.Flat())!.Rationale.ShouldEndWith("time to arrive.");
    }

    [Fact]
    public void Winter_access_is_medium_on_a_hillside_and_low_elsewhere()
    {
        WinterAccessAssessor assessor = new();

        assessor.Assess(Profiles.RobsHouse())!.Likelihood.ShouldBe(Likelihood.Medium);
        assessor.Assess(Profiles.Flat())!.Likelihood.ShouldBe(Likelihood.Low);
    }

    [Fact]
    public void Comms_loss_is_a_business_risk_for_a_home_business()
    {
        CommsLossAssessor assessor = new();

        assessor.Assess(Profiles.RobsHouse())!.Impact.ShouldBe(Impact.High);
        assessor.Assess(Profiles.Flat())!.Impact.ShouldBe(Impact.Medium);
    }

    [Fact]
    public void Garden_fire_needs_a_garden_and_is_worse_with_decking_or_a_tree()
    {
        GardenFireAssessor assessor = new();

        assessor.Assess(Profiles.Flat()).ShouldBeNull();
        assessor.Assess(Profiles.RobsHouse())!.Impact.ShouldBe(Impact.High);
        assessor.Assess(Profiles.FloodZoneBungalow())!.Impact.ShouldBe(Impact.Medium);
    }

    [Theory]
    [InlineData(FloodRisk.High, Likelihood.High, Impact.Severe)]
    [InlineData(FloodRisk.Medium, Likelihood.Medium, Impact.Severe)]
    [InlineData(FloodRisk.Low, Likelihood.Low, Impact.High)]
    public void Flooding_ranks_by_the_national_map(FloodRisk flood, Likelihood likelihood, Impact impact)
    {
        HouseholdProfile profile = Profiles.Build(2, 0, false, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, false, false, false, false, null, flood);

        Risk? risk = new FloodingAssessor().Assess(profile);

        risk.ShouldNotBeNull();
        risk.Likelihood.ShouldBe(likelihood);
        risk.Impact.ShouldBe(impact);
    }

    [Fact]
    public void A_flood_band_is_described_at_its_own_nations_odds_and_names_the_water()
    {
        static string Says(FloodRisk band, Nation nation, FloodWater from) =>
            new FloodingAssessor().Assess(Profiles.Build(2, 0, false, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, false, false, false, false, null, band) with { Nation = nation, FloodFrom = from })!.Rationale;

        Says(FloodRisk.High, Nation.England, FloodWater.NotSaid).ShouldBe("The national flood map puts your area at high risk, which means more than a one in thirty chance in any year. Flooding goes to the top of your plan.");
        Says(FloodRisk.Medium, Nation.Wales, FloodWater.RiversOrSea).ShouldBe("The national flood map puts your area at medium risk from rivers or the sea, between a one in a hundred and a one in thirty chance in any year.");
        Says(FloodRisk.Low, Nation.England, FloodWater.SurfaceWater).ShouldBe("The national flood map puts your area at low risk from surface water, which is still up to a one in a hundred chance in any year.");

        // Scotland draws its bands at one in ten, one in two hundred and one in a thousand.
        Says(FloodRisk.High, Nation.Scotland, FloodWater.RiversOrSea).ShouldBe("The national flood map puts your area at high risk from rivers or the sea, which on Scotland's map means a one in ten chance or more in any year. Flooding goes to the top of your plan.");
        Says(FloodRisk.Medium, Nation.Scotland, FloodWater.NotSaid).ShouldBe("The national flood map puts your area at medium risk, which on Scotland's map is between a one in two hundred and a one in ten chance in any year.");
        Says(FloodRisk.Low, Nation.Scotland, FloodWater.NotSaid).ShouldBe("The national flood map puts your area at low risk, which on Scotland's map is between a one in a thousand and a one in two hundred chance in any year.");
    }

    [Theory]
    [InlineData(FloodRisk.VeryLow)]
    [InlineData(FloodRisk.Unknown)]
    public void Unknown_flood_risk_keeps_the_baseline_and_adds_nothing_alarming(FloodRisk flood)
    {
        HouseholdProfile profile = Profiles.Build(2, 0, false, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, false, false, false, false, null, flood);

        new FloodingAssessor().Assess(profile).ShouldBeNull();
    }

    [Fact]
    public void Regional_disruption_and_indoor_fire_apply_to_everyone()
    {
        new RegionalDisruptionAssessor().Assess(Profiles.Flat())!.Kind.ShouldBe(RiskKind.RegionalDisruption);
        new IndoorFireAssessor().Assess(Profiles.Flat())!.Impact.ShouldBe(Impact.Severe);
    }

    [Fact]
    public void Every_assessor_rejects_a_missing_profile()
    {
        IRiskAssessor[] assessors =
        [
            new StormPowerCutAssessor(), new FallingTreeAssessor(), new WaterInterruptionAssessor(),
            new WinterAccessAssessor(), new CommsLossAssessor(), new GardenFireAssessor(),
            new FloodingAssessor(), new RegionalDisruptionAssessor(), new IndoorFireAssessor(),
        ];

        foreach (IRiskAssessor assessor in assessors)
        {
            Should.Throw<ArgumentNullException>(() => assessor.Assess(null!));
        }
    }
}
