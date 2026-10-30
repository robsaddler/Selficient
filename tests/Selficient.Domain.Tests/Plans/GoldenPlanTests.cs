using FsCheck;
using FsCheck.Xunit;
using Selficient.Application.Composition;
using Selficient.Domain.Households;
using Selficient.Domain.Plans;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Plans;

/// <summary>
/// The whole engine against real households. Rob's house must reproduce the committed resilience
/// plan tier by tier; every random household must get the government baseline.
/// </summary>
public sealed class GoldenPlanTests
{
    private static readonly RiskAssessmentPipeline Risks = new(ApplicationServiceCollectionExtensions.Assessors());
    private static readonly PlanBuilder Builder = new(ApplicationServiceCollectionExtensions.Rules());

    [Fact]
    public void Robs_house_reproduces_the_committed_plan_tier_by_tier()
    {
        HouseholdProfile profile = Profiles.RobsHouse();
        RiskAssessment risks = Risks.Run(profile);

        Plan plan = Builder.Build(profile, risks).Value;

        risks.Ranked.Select(r => r.Kind).Take(3).ShouldBe([RiskKind.StormPowerCut, RiskKind.CommsLoss, RiskKind.GardenFire]);
        risks.Includes(RiskKind.FallingTree).ShouldBeTrue();
        risks.Includes(RiskKind.Flooding).ShouldBeFalse();

        plan.Tiers[0].Actions.Select(a => a.Id.Value).ShouldBe([
            "water-bottled-three-days", "food-no-cook-three-days", "power-failure-lights", "headtorches",
            "power-banks", "radio-dab-fm-with-crank", "safety-kit", "cash-in-small-notes",
            "candles-and-matches", "save-emergency-numbers", "priority-services-register",
            "arborist-survey", "clear-leaf-litter-under-decking", "car-above-half-a-tank",
        ]);
        plan.Tiers[1].Actions.Select(a => a.Id.Value).ShouldBe([
            "tree-work-if-advised", "garden-firebreak-and-hose", "water-cans", "bath-liner", "water-butt",
            "food-one-month", "butane-cartridge-stove", "rechargeable-lanterns",
            "fire-box-for-cash-and-documents", "solar-quotes",
        ]);
        plan.Tiers[2].Actions.Select(a => a.Id.Value).ShouldBe([
            "portable-power-station", "boiler-plug-and-socket", "gravity-water-filter",
            "satellite-internet-on-standby", "lights-out-drill",
        ]);
        plan.Tiers[3].Actions.Select(a => a.Id.Value).ShouldBe(["rooftop-solar-and-battery"]);

        plan.Tiers[0].Cost.From.Pounds.ShouldBeInRange(500m, 650m);
        plan.Tiers[3].Cost.To.Pounds.ShouldBe(17000m);
    }

    [Fact]
    public void The_same_profile_always_produces_the_same_plan()
    {
        HouseholdProfile profile = Profiles.FloodZoneBungalow();

        Plan first = Builder.Build(profile, Risks.Run(profile)).Value;
        Plan second = new PlanBuilder(ApplicationServiceCollectionExtensions.Rules().Reverse()).Build(profile, Risks.Run(profile)).Value;

        first.Actions.Select(a => a.Id.Value).ShouldBe(second.Actions.Select(a => a.Id.Value));
        first.Cost.ShouldBe(second.Cost);
        first.Tiers[0].Actions.Select(a => a.Id.Value).ShouldContain("flood-warnings-and-plan");
    }

    [Property]
    public bool Every_profile_gets_the_government_baseline(
        PositiveInt people, NonNegativeInt children, bool vulnerable, bool pets, bool business,
        int type, PositiveInt storeys, int heating, int hob, bool car, bool garden, bool decking, bool tree,
        NonNegativeInt elevation, int flood)
    {
        HouseholdProfile profile = Profiles.Build(
            1 + (people.Get % 20), Math.Min(children.Get, 1 + (people.Get % 20)), vulnerable, pets, business,
            (DwellingType)(Math.Abs(type) % 5), 1 + (storeys.Get % 6), (HeatingType)(Math.Abs(heating) % 6), (HobType)(Math.Abs(hob) % 3),
            car, garden, decking, tree, elevation.Get % 1400, (FloodRisk)(Math.Abs(flood) % 5));

        Plan plan = Builder.Build(profile, Risks.Run(profile)).Value;
        HashSet<string> ids = [.. plan.Actions.Select(a => a.Id.Value)];

        return ids.IsSupersetOf([
            "water-bottled-three-days", "food-no-cook-three-days", "power-failure-lights", "headtorches",
            "power-banks", "radio-dab-fm-with-crank", "safety-kit", "cash-in-small-notes",
            "candles-and-matches", "save-emergency-numbers",
        ]) && plan.Tiers.Count == 4 && plan.Actions.All(a => a.Why.EndsWith('.') && a.Title.Length > 0);
    }
}
