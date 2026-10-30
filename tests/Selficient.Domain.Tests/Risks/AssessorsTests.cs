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
