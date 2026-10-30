using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Plans;

public sealed class PlanTests
{
    private static readonly RiskAssessment NoRisks = new RiskAssessmentPipeline([]).Run(Profiles.Flat());

    [Theory]
    [InlineData("water-cans")]
    [InlineData("a")]
    [InlineData("x1-y2")]
    public void Action_ids_are_kebab_case(string value)
    {
        ActionId.From(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Water Cans")]
    [InlineData("-leading")]
    [InlineData("double--hyphen")]
    public void Action_ids_refuse_anything_else(string value)
    {
        Should.Throw<ArgumentException>(() => ActionId.From(value));
    }

    [Fact]
    public void Action_ids_refuse_null()
    {
        Should.Throw<ArgumentNullException>(() => ActionId.From(null!));
        ActionId.IsValid(null).ShouldBeFalse();
        ActionId.IsValid("water-cans").ShouldBeTrue();
        ActionId.IsValid("Water Cans").ShouldBeFalse();
    }

    [Fact]
    public void Provisions_add_up_and_refuse_null()
    {
        Provisions sum = new Provisions(WaterLitres: 10m, FoodDays: 1m).Plus(new Provisions(LightHours: 5m, FoodDays: 2m));

        sum.ShouldBe(new Provisions(10m, 3m, 5m));
        Provisions.None.ShouldBe(new Provisions());
        Should.Throw<ArgumentNullException>(() => Provisions.None.Plus(null!));
    }

    [Fact]
    public void Tier_costs_are_the_sum_of_their_actions()
    {
        PlanAction a = Stub("a", Tier.ThisWeek, 10m, 20m);
        PlanAction b = Stub("b", Tier.ThisWeek, 5m, 5m);
        PlanAction c = Stub("c", Tier.ThisMonth, 100m, 200m);
        Plan plan = new(Profiles.Flat(), NoRisks, [new PlanTier(Tier.ThisWeek, "This week", [a, b]), new PlanTier(Tier.ThisMonth, "This month", [c])]);

        plan.Tiers[0].Cost.ShouldBe(MoneyRange.Pounds(15m, 25m));
        plan.Cost.ShouldBe(MoneyRange.Pounds(115m, 225m));
        plan.Actions.Select(x => x.Id.Value).ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Builder_refuses_duplicate_action_ids()
    {
        PlanBuilder builder = new([new Emits(1, Stub("same", Tier.ThisWeek, 1m, 1m)), new Emits(2, Stub("same", Tier.ThisMonth, 1m, 1m))]);

        Result<Plan> result = builder.Build(Profiles.Flat(), NoRisks);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("plan.duplicate-action");
        result.Error.Message.ShouldBe("The plan already contains the action 'same'.");
    }

    [Fact]
    public void Builder_orders_by_tier_then_rule_order_then_emission_regardless_of_registration()
    {
        PlanAction late = Stub("zed", Tier.ThisWeek, 1m, 1m);
        PlanAction early = Stub("alpha", Tier.ThisWeek, 1m, 1m);
        PlanAction month = Stub("month", Tier.ThisMonth, 1m, 1m);
        PlanAction year = Stub("year", Tier.TwelveMonths, 1m, 1m);
        PlanAction second = Stub("beta", Tier.ThisWeek, 1m, 1m);

        Plan forward = new PlanBuilder([new Emits(20, late, year), new Emits(10, month, early, second)]).Build(Profiles.Flat(), NoRisks).Value;
        Plan reversed = new PlanBuilder([new Emits(10, early, second, month), new Emits(20, year, late)]).Build(Profiles.Flat(), NoRisks).Value;

        forward.Actions.Select(a => a.Id.Value).ShouldBe(["alpha", "beta", "zed", "month", "year"]);
        reversed.Actions.Select(a => a.Id.Value).ShouldBe(forward.Actions.Select(a => a.Id.Value));
        forward.Tiers.Select(t => t.Title).ShouldBe(["This week", "This month", "Within three months", "Within a year"]);
        forward.Tiers[2].Actions.ShouldBeEmpty();
    }

    [Fact]
    public void Builder_rejects_missing_arguments()
    {
        Should.Throw<ArgumentNullException>(() => new PlanBuilder(null!));
        PlanBuilder builder = new([]);
        Should.Throw<ArgumentNullException>(() => builder.Build(null!, NoRisks));
        Should.Throw<ArgumentNullException>(() => builder.Build(Profiles.Flat(), null!));
    }

    private static PlanAction Stub(string id, Tier tier, decimal from, decimal to) =>
        new(ActionId.From(id), tier, "Title.", "Why.", MoneyRange.Pounds(from, to), new HashSet<RiskKind>(), Provisions.None);

    private sealed class Emits(int order, params PlanAction[] actions) : IPlanRule
    {
        public int Order => order;

        public IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks) => actions;
    }
}
