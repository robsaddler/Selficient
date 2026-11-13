using System.Text.RegularExpressions;
using Selficient.Domain.Parameters;
using Selficient.Domain.Resilience;

namespace Selficient.Domain.Tests.Parameters;

/// <summary>
/// The words follow the figures. A sentence that states how much a thing holds or how long a
/// scenario lasts is built from the figure, so correcting the figure corrects the sentence; and a
/// sentence that is not built from figures states none.
/// </summary>
public sealed partial class WordsTests
{
    private static ParameterSet Corrected(params (string Id, decimal Value)[] corrections) =>
        ParameterSet.Create(1, corrections.ToDictionary(static c => c.Id, static c => c.Value)).Value;

    [Fact]
    public void Figures_are_said_as_a_sentence_says_them()
    {
        Say.Number(0m).ShouldBe("none");
        Say.Number(12m).ShouldBe("twelve");
        Say.Number(13m).ShouldBe("13");
        Say.Number(1800m).ShouldBe("1,800");
        Say.Number(1.5m).ShouldBe("1.5");
        Say.Number(0.25m).ShouldBe("0.25");
        Say.Number(-2m).ShouldBe("-2");

        Say.Days(0.5m).ShouldBe("half a day");
        Say.Days(1m).ShouldBe("a day");
        Say.Days(3m).ShouldBe("three days");
        Say.Days(7m).ShouldBe("a week");
        Say.Days(14m).ShouldBe("two weeks");
        Say.Days(28m).ShouldBe("four weeks");
        Say.Days(30m).ShouldBe("a month");
        Say.Days(35m).ShouldBe("35 days");
        Say.Days(10m).ShouldBe("ten days");
        Say.Days(2.5m).ShouldBe("2.5 days");

        Say.Hours(1m).ShouldBe("an hour");
        Say.Hours(16m).ShouldBe("16 hours");
        Say.Litres(1m).ShouldBe("a litre");
        Say.Litres(380m).ShouldBe("380 litres");
        Say.Energy(180m).ShouldBe("180 watt-hours");
        Say.Energy(1024m).ShouldBe("a kilowatt-hour");
        Say.Energy(1170m).ShouldBe("1.2 kilowatt-hours");
        Say.Energy(13500m).ShouldBe("13.5 kilowatt-hours");
        Say.Power(600m).ShouldBe("600 watts");
        Say.Power(1000m).ShouldBe("a kilowatt");
        Say.Power(1800m).ShouldBe("1.8 kilowatts");
        Say.Power(3000m).ShouldBe("three kilowatts");
        Say.KilowattHours(1024m).ShouldBe("1 kWh");
        Say.KilowattHours(3072m).ShouldBe("3 kWh");
        Say.Capital("about two days.").ShouldBe("About two days.");
        Say.Capital(string.Empty).ShouldBe(string.Empty);
        Should.Throw<ArgumentNullException>(() => Say.Capital(null!));
    }

    [Fact]
    public void A_provider_whose_words_are_not_built_from_its_figures_states_none()
    {
        foreach (Provider provider in Providers.All.Where(p => !ProviderWords.Builds(p.Id)))
        {
            Figure().IsMatch(provider.Sentence).ShouldBeFalse($"{provider.Id} states a figure in words that nothing would correct: {provider.Sentence}");
        }

        // The six names that carry a size or a length are built too; the rest name no figure a parameter holds.
        Providers.Find("portable-power-station")!.Name.ShouldBe("A 1 kWh portable power station");
        Providers.Find("large-power-station")!.Name.ShouldBe("A 3 kWh power station with panels");
        Providers.Find("food-no-cook-three-days")!.Name.ShouldBe("Three days of food that needs no fridge or cooking");
        Providers.Find("food-one-month")!.Name.ShouldBe("A month of food");
        Providers.Find("formula-and-nappies")!.Name.ShouldBe("Two weeks of formula and nappies");
        Providers.Find("pet-food-two-weeks")!.Name.ShouldBe("Two weeks of pet food and medication");
        ProviderWords.Builds("stairlift-batteries").ShouldBeTrue();
        ProviderWords.Builds("mains-tap").ShouldBeFalse();
    }

    [Fact]
    public void The_shipped_sentences_say_the_shipped_figures()
    {
        Providers.Find("kettle-water")!.Sentence.ShouldBe("About 1.5 litres a person: the kettle, a jug, a few bottles. Half a day.");
        Providers.Find("water-bottled-three-days")!.Sentence.ShouldBe("Nine litres a person, which is three days of drinking, kept sealed and dark; it has two years of shelf life.");
        Providers.Find("no-flush-toilet-kit")!.Sentence.ShouldBe("Bags that line the bowl of the toilet you already have, with a powder that sets the contents solid and holds the smell: three days for everyone, tied off and put in the outside bin.");
        Providers.Find("stairlift-batteries")!.Sentence.ShouldBe("About two days of trips to bed and the bathroom on healthy batteries, fewer on a long stair or an old set.");
        Providers.Find("fridge-freezer")!.Sentence.ShouldBe("About 1.2 kilowatt-hours a day and 600 watts to start, so a source needs the headroom as well as the energy.");
        Providers.Find("water-butt")!.Sentence.ShouldBe("About 210 litres of rain, refilled by the weather at about eight litres a day in winter and three in summer, for flushing and the garden; drinking water only through a filter.");
        Providers.Find("portable-power-station")!.Sentence.ShouldBe("About a kilowatt-hour at up to 1.8 kilowatts: the router, lights, phones and a medical device for a day or two; the fridge for a day; the kettle one cup at a time.");
        Providers.Find("ev-output")!.Sentence.ShouldBe("About 60 kilowatt-hours with 20 kilowatt-hours kept back to drive, at three kilowatts through a vehicle-to-load adapter: a fortnight of essentials, and nothing that needs more than that at once.");
        Providers.Find("router")!.Sentence.ShouldBe("About 300 watt-hours a day, and a line that only works while the exchange has power or battery.");
        Providers.Find("petrol-car")!.Sentence.ShouldBe("Half a tank is about a week of local driving, and a 12-volt socket that charges phones.");
        Providers.All.ShouldAllBe(p => p.Sentence.EndsWith('.') && char.IsUpper(p.Sentence[0]));
    }

    [Fact]
    public void A_corrected_figure_corrects_the_words_that_state_it()
    {
        ParameterSet set = Corrected(("stairlift-batteries.stock", 4m), ("portable-power-station.store-wh", 2048m), ("food-no-cook-three-days.stock", 5m), ("demand.drinking-litres", 6m), ("demand.sanitation-litres", 18m));
        Provider Of(string id) => ProviderFigures.Find(set, id)!;

        Of("stairlift-batteries").Sentence.ShouldBe("About four days of trips to bed and the bathroom on healthy batteries, fewer on a long stair or an old set.");
        Of("portable-power-station").Name.ShouldBe("A 2 kWh portable power station");
        Of("portable-power-station").Sentence.ShouldStartWith("About two kilowatt-hours at up to 1.8 kilowatts:");
        Of("food-no-cook-three-days").Name.ShouldBe("Five days of food that needs no fridge or cooking");
        Of("food-no-cook-three-days").Sentence.ShouldBe("Tins, oats, nuts and biscuits: five days of meals a person, eaten in date order and replaced.");

        // What people need changes how long a stock lasts, though the stock itself was not touched.
        Of("water-bottled-three-days").Sentence.ShouldBe("Nine litres a person, which is 1.5 days of drinking, kept sealed and dark; it has two years of shelf life.");
        Of("water-bottled-three-days").Stock.ShouldBe(new Stock(9m, PerPerson: true));
        Of("kettle-water").Sentence.ShouldEndWith("a few bottles. 0.3 days.");
        Of("no-flush-toilet-kit").Sentence.ShouldContain("holds the smell: two days for everyone");

        Of("mains-tap").ShouldBeSameAs(Providers.Find("mains-tap"), "a provider whose figures and words are unchanged is the shipped one");
        Of("fridge-freezer").ShouldBeSameAs(Providers.Find("fridge-freezer"));
        ProviderFigures.Find(set, "no-such-provider").ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => ProviderFigures.Find(null!, "mains-tap"));
        Should.Throw<ArgumentNullException>(() => ProviderFigures.Find(set, null!));
    }

    [Fact]
    public void A_scenarios_sentence_follows_its_stretches_and_one_that_is_not_built_states_no_figure()
    {
        ParameterSet set = Corrected(
            ("scenario.water.mainswater-from-1.last-day", 5m), ("scenario.regional.electricity-from-1.last-day", 2m), ("scenario.blackout.electricity-from-1.last-day", 1m),
            ("scenario.shortages.electricity-from-14.first-day", 21m), ("demand.heatwave-extra-litres", 2m));
        Scenario Of(string id) => ScenarioFigures.Under(set).Single(s => s.Id == id);

        Scenarios.WaterCut.Sentence.ShouldStartWith("A burst main or a treatment problem stops the supply for three days.");
        Of("water").Sentence.ShouldStartWith("A burst main or a treatment problem stops the supply for five days.");
        Of("regional").Sentence.ShouldContain("The power is off for two days and then comes and goes");
        Of("blackout").Sentence.ShouldContain("The power is off everywhere for a day and comes back unevenly.");
        Of("shortages").Sentence.ShouldContain("from day 21 the power is cut for a few hours a day");
        Scenarios.Shortages.Sentence.ShouldContain("from day 14 the power is cut");
        Of("heatwave").Sentence.ShouldContain("everyone needs two litres more to drink each day");
        Of("heatwave").Phases.ShouldBe(Scenarios.Heatwave.Phases, "only what people need moved, and the sentence with it");
        Scenarios.Heatwave.Sentence.ShouldContain("everyone needs a litre more to drink each day");
        Of("storm").ShouldBeSameAs(Scenarios.Storm);

        foreach (Scenario scenario in Scenarios.All)
        {
            ScenarioWords.Sentence(scenario, DemandFigures.Of(ParameterSet.Default)).ShouldBe(scenario.Sentence, $"{scenario.Id} ships a sentence its own figures would not say");
            if (!ScenarioWords.Builds(scenario.Id))
            {
                Figure().IsMatch(scenario.Sentence).ShouldBeFalse($"{scenario.Id} states a figure in words that nothing would correct: {scenario.Sentence}");
            }
        }
    }

    [GeneratedRegex(@"\d|\b(two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|twenty|thirty|forty|fifty|sixty|hundred|thousand|fortnight|half|quarter|couple|several)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Figure();
}
