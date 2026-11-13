using Selficient.Domain.Parameters;
using Selficient.Domain.Resilience;

namespace Selficient.Domain.Tests.Parameters;

/// <summary>
/// A household's own figures are kept in its browser for years. Whatever arrives, the plan is
/// built: each figure that can be used is used, and each that cannot is declined with a sentence.
/// </summary>
public sealed class OwnFigureTests
{
    private static Dictionary<string, decimal> Own(params (string Id, decimal Value)[] figures) => figures.ToDictionary(static f => f.Id, static f => f.Value);

    [Fact]
    public void With_no_figures_of_its_own_a_household_has_the_set_in_force_itself()
    {
        ParameterSet published = ParameterSet.Create(3, Own(("water-butt.stock", 250m))).Value;

        published.WithOwn(Own()).ShouldBeSameAs(published);
        ParameterSet.Default.WithOwn(Own()).ShouldBeSameAs(ParameterSet.Default);
        published.Plans.ShouldBeSameAs(published);
        published.Asked.ShouldBeEmpty();
        published.Declined.ShouldBeEmpty();
        published.IsOwn("water-butt.stock").ShouldBeFalse("an expert's correction is not the household's figure");
        Should.Throw<ArgumentNullException>(() => published.WithOwn(null!));
    }

    [Fact]
    public void Each_figure_that_can_be_used_is_used_and_each_that_cannot_is_declined_with_a_sentence_and_the_plans_stands()
    {
        ParameterSet published = ParameterSet.Create(3, Own(("water-butt.stock", 250m))).Value;

        ParameterSet own = published.WithOwn(Own(
            ("stairlift-batteries.stock", 4m),
            ("wood-stove.stock", 10.5m),
            ("water-butt.stock", 100_000m),
            ("demand.drinking-litres", 2m),
            (Assumptions.CashFigure, 20m),
            ("a-figure-a-release-removed", 1m)));

        own.Version.ShouldBe(3, "a household's figures are over the set in force, which is still the set its plan names");
        own["stairlift-batteries.stock"].ShouldBe(4m);
        own["wood-stove.stock"].ShouldBe(10.5m);
        own["water-butt.stock"].ShouldBe(250m, "the figure in force stands where the household's could not be used");
        own["demand.drinking-litres"].ShouldBe(3m);
        own[Assumptions.CashFigure].ShouldBe(10m, "the cash a person needs is given beside the cash, not here");
        own.Plans.ShouldBeSameAs(published);
        own.Plans["stairlift-batteries.stock"].ShouldBe(2m);
        own.Asked.Count.ShouldBe(6, "what was asked is kept as it was asked, so a browser can see the plan was built from what it sent");
        own.Asked["water-butt.stock"].ShouldBe(100_000m);
        own.Declined.Keys.ShouldBe(["water-butt.stock", "demand.drinking-litres", Assumptions.CashFigure, "a-figure-a-release-removed"], ignoreOrder: true);
        own.Declined["water-butt.stock"].ShouldBe("The water butt: how much there is must be between 0 and 4200 litres.");
        own.Declined["demand.drinking-litres"].ShouldBe("\"demand.drinking-litres\" is not a figure a household can give for itself.");
        own.IsOwn("stairlift-batteries.stock").ShouldBeTrue();
        own.IsOwn("water-butt.stock").ShouldBeFalse();
        own.IsOwn("closed-freezer.stock").ShouldBeFalse("a figure it never gave");
        own.Corrections.Keys.ShouldBe(["water-butt.stock", "stairlift-batteries.stock", "wood-stove.stock"], ignoreOrder: true);
        ProviderFigures.Find(own, "stairlift-batteries")!.Sentence.ShouldStartWith("About four days of trips to bed", customMessage: "the words follow the household's figure as they follow an expert's");
    }

    [Fact]
    public void A_figure_that_contradicts_the_set_it_is_laid_over_is_declined_and_the_others_still_stand()
    {
        // An expert has said a power station keeps half a kilowatt-hour back; a household then says its own holds less than that.
        ParameterSet published = ParameterSet.Create(1, Own(("portable-power-station.reserve-wh", 500m))).Value;

        ParameterSet own = published.WithOwn(Own(("portable-power-station.store-wh", 300m), ("closed-freezer.stock", 1m)));

        own["portable-power-station.store-wh"].ShouldBe(1024m);
        own["closed-freezer.stock"].ShouldBe(1m);
        own.Declined.ShouldHaveSingleItem().Value.ShouldBe("A 1 kWh portable power station cannot keep back more energy than it can give.");
    }
}
