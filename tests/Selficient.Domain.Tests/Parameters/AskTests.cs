using Selficient.Domain.Parameters;
using Selficient.Domain.Plans.Rules;
using Selficient.Domain.Resilience;

namespace Selficient.Domain.Tests.Parameters;

/// <summary>
/// Which figures a household may give for itself is something a published set says: an expert
/// switches one on with the question to ask, rewords one, or switches one off.
/// </summary>
public sealed class AskTests
{
    private static readonly Dictionary<string, decimal> NoCorrections = [];

    private static Dictionary<string, string?> Asks(params (string Id, string? Question)[] asks) => asks.ToDictionary(static a => a.Id, static a => a.Question);

    [Fact]
    public void As_shipped_a_household_is_asked_the_nine_questions_the_code_carries()
    {
        ParameterSet.Default.Asks.ShouldBeEmpty();
        ParameterSet.Default.QuestionFor("water-butt.stock").ShouldBe("How many litres does your water butt hold?");
        ParameterSet.Default.MayGive("water-butt.stock").ShouldBeTrue();
        ParameterSet.Default.MayGive("headtorches.stock").ShouldBeFalse();
        ParameterSet.Default.QuestionFor("not-a-figure").ShouldBeNull();
        ParameterRegistry.All.Count(parameter => ParameterSet.Default.MayGive(parameter.Id)).ShouldBe(9);
        Should.Throw<ArgumentNullException>(() => ParameterSet.Default.QuestionFor(null!));
    }

    [Fact]
    public void Only_a_figure_about_a_thing_a_household_has_or_a_price_may_be_switched()
    {
        ParameterRegistry.Find("headtorches.stock")!.HouseholdMayBeAsked.ShouldBeTrue();
        ParameterRegistry.Find("portable-power-station.store-wh")!.HouseholdMayBeAsked.ShouldBeTrue();
        ParameterRegistry.Find("price.headtorches.low")!.HouseholdMayBeAsked.ShouldBeTrue();
        ParameterRegistry.Find("demand.drinking-litres")!.HouseholdMayBeAsked.ShouldBeFalse("what a person drinks is not one household's to say");
        ParameterRegistry.Find(Assumptions.CashFigure)!.HouseholdMayBeAsked.ShouldBeFalse("the cash a person needs has a box of its own");
        ParameterRegistry.Find(RuleFigures.GasHeaterFloors)!.HouseholdMayBeAsked.ShouldBeFalse();
        ParameterRegistry.All.Where(parameter => parameter.Id.StartsWith("scenario.", StringComparison.Ordinal)).ShouldAllBe(parameter => !parameter.HouseholdMayBeAsked);
    }

    [Fact]
    public void A_set_switches_a_figure_on_with_its_question_rewords_one_and_switches_one_off()
    {
        ParameterSet set = ParameterSet.Create(2, NoCorrections, Asks(
            ("headtorches.stock", "  How many hours of light do your headtorches give?  "),
            ("water-butt.stock", null),
            ("wood-stove.stock", "How long does a full store of logs last you, in days?"),
            ("closed-freezer.stock", "How many days does your freezer keep its cold with the door shut?"),
            ("price.headtorches.high", null))).Value;

        set.QuestionFor("headtorches.stock").ShouldBe("How many hours of light do your headtorches give?");
        set.MayGive("water-butt.stock").ShouldBeFalse();
        set.QuestionFor("wood-stove.stock").ShouldBe("How long does a full store of logs last you, in days?");
        set.Asks.Keys.ShouldBe(["headtorches.stock", "water-butt.stock", "wood-stove.stock"], ignoreOrder: true, "what matches the product as shipped is not a difference and is not kept");
        set.MayGive("closed-freezer.stock").ShouldBeTrue();
        set.MayGive("price.headtorches.high").ShouldBeFalse();
        ParameterSet.Create(2, NoCorrections).Value.Asks.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("demand.drinking-litres", "How many litres do you each drink in a day?", "parameters.ask")]
    [InlineData("not-a-figure", "How many of them do you have at home?", "parameters.ask")]
    [InlineData("headtorches.stock", "How many?", "parameters.question")]
    [InlineData("headtorches.stock", "Tell us how many hours your torches last.", "parameters.question")]
    public void A_switch_that_cannot_stand_is_refused_with_a_sentence(string id, string question, string code)
    {
        ParameterSet.Create(2, NoCorrections, Asks((id, question))).Error.Code.ShouldBe(code);
    }

    [Fact]
    public void The_refusals_name_the_figure_and_the_limits()
    {
        ParameterSet.Create(2, NoCorrections, Asks(("headtorches.stock", new string('a', 200) + "?"))).Error.Message.ShouldBe("What a household is asked about \"Headtorches: how much there is\" must be a question of 10 to 200 characters, ending in a question mark.");
        ParameterSet.Create(2, NoCorrections, Asks((Assumptions.CashFigure, null))).Error.Message.ShouldBe("Whether a household may give \"assumed.cash-pounds-a-person-a-day\" for itself is not something that can be changed.");
        ParameterSet.Create(0, NoCorrections, Asks()).Error.Code.ShouldBe("parameters.version");
        Should.Throw<ArgumentNullException>(() => ParameterSet.Create(1, NoCorrections, null!));
        Should.Throw<ArgumentNullException>(() => ParameterSet.Create(1, null!, Asks()));
    }

    [Fact]
    public void A_households_own_figure_is_taken_only_where_the_set_in_force_says_it_may_give_one()
    {
        ParameterSet set = ParameterSet.Create(2, NoCorrections, Asks(("headtorches.stock", "How many hours of light do your headtorches give?"), ("water-butt.stock", null))).Value;

        ParameterSet own = set.WithOwn(new Dictionary<string, decimal> { ["headtorches.stock"] = 60m, ["water-butt.stock"] = 300m, ["price.headtorches.low"] = 5m });

        own["headtorches.stock"].ShouldBe(60m);
        own.IsOwn("headtorches.stock").ShouldBeTrue();
        own["water-butt.stock"].ShouldBe(210m, "switched off, so a figure kept from before is declined");
        own.Declined.Keys.ShouldBe(["water-butt.stock", "price.headtorches.low"], ignoreOrder: true);
        own.QuestionFor("headtorches.stock").ShouldBe("How many hours of light do your headtorches give?", "the household's set still knows what it is asked");
        own.Asks.ShouldBe(set.Asks);
        set.ForHousehold(new Dictionary<string, decimal> { ["headtorches.stock"] = 60m }).IsSuccess.ShouldBeTrue();
        set.ForHousehold(new Dictionary<string, decimal> { ["water-butt.stock"] = 300m }).IsFailure.ShouldBeTrue();
    }
}
