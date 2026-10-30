using Selficient.Domain.Households;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Risks;

public sealed class RiskTests
{
    [Fact]
    public void Score_is_likelihood_weight_times_impact_weight()
    {
        new Risk(RiskKind.Flooding, Likelihood.Low, Impact.Low, "x").Score.ShouldBe(1);
        new Risk(RiskKind.Flooding, Likelihood.High, Impact.Severe, "x").Score.ShouldBe(12);
        new Risk(RiskKind.Flooding, Likelihood.Medium, Impact.High, "x").Score.ShouldBe(6);
    }

    [Fact]
    public void Risks_with_equal_scores_rank_in_a_fixed_order()
    {
        RiskAssessmentPipeline pipeline = new([
            new Fixed(new Risk(RiskKind.GardenFire, Likelihood.Medium, Impact.Medium, "b")),
            new Fixed(new Risk(RiskKind.FallingTree, Likelihood.Medium, Impact.Medium, "a")),
            new Fixed(null),
            new Fixed(new Risk(RiskKind.Flooding, Likelihood.High, Impact.Severe, "top")),
        ]);

        RiskAssessment assessment = pipeline.Run(Profiles.Flat());

        assessment.Ranked.Select(r => r.Kind).ShouldBe([RiskKind.Flooding, RiskKind.FallingTree, RiskKind.GardenFire]);
        assessment.Includes(RiskKind.Flooding).ShouldBeTrue();
        assessment.Includes(RiskKind.CommsLoss).ShouldBeFalse();
    }

    [Fact]
    public void The_pipeline_rejects_missing_arguments()
    {
        Should.Throw<ArgumentNullException>(() => new RiskAssessmentPipeline(null!));
        Should.Throw<ArgumentNullException>(() => new RiskAssessmentPipeline([]).Run(null!));
    }

    private sealed class Fixed(Risk? risk) : IRiskAssessor
    {
        public Risk? Assess(HouseholdProfile profile) => risk;
    }
}
