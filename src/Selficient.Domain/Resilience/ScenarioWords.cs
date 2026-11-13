using static Selficient.Domain.Parameters.Say;

namespace Selficient.Domain.Resilience;

/// <summary>
/// The scenario sentences that state a figure, built from the scenario's own stretches and from what
/// people need. A scenario with no entry states no figure, and a test keeps that true; the shipped
/// sentence of one that has an entry is exactly what its entry says, and a test keeps that true too.
/// </summary>
internal static class ScenarioWords
{
    private static readonly Dictionary<string, Func<Scenario, DemandFigures, string>> Sentences = new(StringComparer.Ordinal)
    {
        ["water"] = static (s, _) => $"A burst main or a treatment problem stops the supply for {Days(Length(s.Phases[0]))}. The power is on, so everything electrical works, but the taps are dry, the toilets do not flush, and the shops sell out of bottled water in the first hours. When the supply returns it comes with a boil notice, so the tap is only drinking water once it has been through the kettle.",
        ["regional"] = static (s, _) => $"A cyber attack on a utility or a fuel shortage. The power is off for {Days(Length(s.Phases[0]))} and then comes and goes, the shops are half-stocked and queueing, there is no fuel at the forecourts and the cash machines are down. Phones and the internet limp.",
        ["blackout"] = static (s, _) => $"A failure of the national grid rather than a line down in your street. The power is off everywhere for {Days(Length(s.Phases[0]))} and comes back unevenly. The tap runs for a few hours and then stops, because the pumps are electric, and the toilet must not be flushed, because the sewers are not working. Phones and the internet go within a day, the cash machines and the forecourts are off, and the shops that open take cash for what is on the shelf. The emergency broadcasts are on BBC Radio 2 and Radio 4.",
        ["shortages"] = static (s, _) => $"A long supply-chain or fuel crisis. The shops are open but rationed and gappy, fuel is scarce, deliveries are slow, the pharmacy substitutes and limits, and from day {Number(s.Phases[^1].FromDay)} the power is cut for a few hours a day to save it. Nothing stops entirely; everything is less.",
        ["heatwave"] = static (_, d) => $"A week or two above thirty degrees. The grid strains in the evenings and cuts out for an hour or two, the water company restricts supply, everyone needs {Litres(d.HeatwaveExtraLitres)} more to drink each day, and the people who feel heat most need a cool room.",
    };

    /// <summary>Whether the scenario's sentence is built from its figures.</summary>
    internal static bool Builds(string scenarioId) => Sentences.ContainsKey(scenarioId);

    /// <summary>The scenario's sentence as its stretches and these needs make it; its own sentence when it states no figure.</summary>
    internal static string Sentence(Scenario scenario, DemandFigures demand) =>
        Sentences.TryGetValue(scenario.Id, out Func<Scenario, DemandFigures, string>? sentence) ? sentence(scenario, demand) : scenario.Sentence;

    private static decimal Length(UtilityPhase phase) => phase.ToDay - phase.FromDay + 1;
}
