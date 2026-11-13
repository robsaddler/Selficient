using Selficient.Application.Composition;
using Selficient.Application.Planning;
using Selficient.Domain.Households;
using Selficient.Domain.Plans;
using Selficient.Domain.Risks;

namespace Selficient.Application.Tests.Planning;

public sealed class OwnFigurePlanTests
{
    [Fact]
    public void A_plan_is_built_from_the_households_own_figures_over_the_ones_in_force()
    {
        GeneratePlanHandler handler = new(new RiskAssessmentPipeline(ApplicationServiceCollectionExtensions.Assessors()), new PlanBuilder(ApplicationServiceCollectionExtensions.Rules()));
        GeneratePlanCommand house = new(4, 2, false, false, false, DwellingType.SemiDetached, 2, HeatingType.GasBoiler, HobType.Electric, true, true, false, false, 60m, FloodRisk.VeryLow);

        Plan plans = handler.Handle(house).Value;
        Plan own = handler.Handle(house with { OwnFigures = new Dictionary<string, decimal> { ["water-butt.stock"] = 300m, ["demand.drinking-litres"] = 1m } }).Value;

        house.OwnFigures.ShouldBeEmpty();
        plans.Actions.Single(action => action.Id.Value == "water-butt").Title.ShouldBe("A 210-litre water butt on a downpipe");
        own.Actions.Single(action => action.Id.Value == "water-butt").Title.ShouldBe("A 300-litre water butt on a downpipe");
        own.Profile.Figures.IsOwn("water-butt.stock").ShouldBeTrue();
        own.Profile.Figures.Declined.ShouldContainKey("demand.drinking-litres", "what a person drinks is not one household's to say, and the plan is built all the same");
        own.Actions.Single(action => action.Id.Value == "water-bottled-three-days").Title.ShouldBe(plans.Actions.Single(action => action.Id.Value == "water-bottled-three-days").Title);
    }
}
