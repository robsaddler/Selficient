using Selficient.Domain.Plans;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;

namespace Selficient.Application.Resilience;

/// <summary>A scenario as a card: its name, and how long this house lasts in it with what it has.</summary>
public sealed record ScenarioCard(string Id, string Name, decimal DaysReady, bool Selected);

/// <summary>A source the household could switch the board to. The first is always what is in the house.</summary>
public sealed record SourceOption(string Id, string Name, string Sentence);

/// <summary>One board: a source and a season.</summary>
public sealed record SourceBoard(string SourceId, Season Season, Board Board);

/// <summary>What one untaken plan action would turn on the current board: the rows and horizons it improves.</summary>
public sealed record ActionTurn(ActionId Action, IReadOnlyList<string> Turns);

/// <summary>Everything the Board page draws for one household and one chosen scenario.</summary>
public sealed record BoardSet(
    Scenario Scenario,
    Season Season,
    IReadOnlyList<ScenarioCard> Cards,
    IReadOnlyList<SourceOption> Sources,
    IReadOnlyList<SourceBoard> Boards,
    Board Current,
    IReadOnlyList<ActionTurn> Turns)
{
    /// <summary>The id of the "nothing extra" source.</summary>
    public const string HouseSource = "house";

    /// <summary>The headline figure: the chosen scenario, what the house has, the chosen season.</summary>
    public decimal DaysReady => Current.DaysReady;
}

/// <summary>What the composer needs: the household as the engine sees it, what it holds, the plan, and the choice of scenario and season.</summary>
public sealed record BoardRequest(ResilienceProfile Profile, IReadOnlySet<string> Held, Plan Plan, string? ScenarioId, Season? Season);

/// <summary>
/// Composes the boards for a page. Pure: it only runs the engine, several times, and arranges the
/// results. Every source for both seasons is simulated so the switches on the page are instant;
/// the scenario is simulated once per card for the figure on it.
/// </summary>
public static class BoardComposer
{
    private static readonly IReadOnlyList<(string Id, Placement Where, string Name, string Sentence)> Candidates =
    [
        ("power-banks", Placement.Anywhere, "Power banks", "Phones, headtorches and the radio for days; nothing with a plug."),
        ("portable-power-station", Placement.Anywhere, "A 1 kWh power station", "The router, lights, phones and a medical device for a day or two; the fridge for a day."),
        ("large-power-station", Placement.Anywhere, "A 3 kWh power station with panels", "Two days of the essentials, and half a kilowatt-hour of winter sun a day after that."),
        ("rooftop-solar-and-battery", Placement.NeedsOwnRoof, "A home battery and gateway", "The house as normal except heavy heating; the roof refills it."),
        ("ev-output", Placement.NeedsElectricCarWithOutput, "The car, powering the house", "A fortnight of the essentials from the battery you already own, with enough kept back to drive."),
        ("petrol-generator", Placement.OutdoorsOnly, "A petrol generator", "Three kilowatts outdoors for as long as the fuel lasts, and the forecourts refill it only while they pump."),
    ];

    /// <summary>Composes the set.</summary>
    public static BoardSet Compose(BoardRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Scenario scenario = Scenarios.Find(request.ScenarioId ?? string.Empty) ?? Scenarios.Storm;
        Season season = request.Season ?? scenario.DefaultSeason;

        List<ScenarioCard> cards = [];
        foreach (Scenario candidate in Ranked(request.Plan.Risks))
        {
            Board board = OutageBoard.Simulate(request.Profile, request.Held, candidate, candidate.DefaultSeason);
            cards.Add(new ScenarioCard(candidate.Id, candidate.Name, board.DaysReady, candidate.Id == scenario.Id));
        }

        List<SourceOption> sources = [new SourceOption(BoardSet.HouseSource, "What is in the house", "Nothing bought yet beyond what the plan says you have ticked.")];
        foreach ((string id, Placement where, string name, string sentence) in Candidates)
        {
            if (request.Profile.CanHave(where) && !request.Held.Contains(id))
            {
                sources.Add(new SourceOption(id, name, sentence));
            }
        }

        List<SourceBoard> boards = [];
        foreach (SourceOption source in sources)
        {
            HashSet<string> held = new(request.Held, StringComparer.Ordinal);
            if (source.Id != BoardSet.HouseSource)
            {
                held.Add(source.Id);
            }

            foreach (Season each in Enum.GetValues<Season>())
            {
                boards.Add(new SourceBoard(source.Id, each, OutageBoard.Simulate(request.Profile, held, scenario, each)));
            }
        }

        // The house in the chosen season is simulated once more rather than looked up, so the set has no search to get wrong.
        Board current = OutageBoard.Simulate(request.Profile, request.Held, scenario, season);
        List<ActionTurn> turns = [];
        foreach (PlanAction action in request.Plan.Actions)
        {
            if (request.Held.Contains(action.Id.Value) || Providers.Find(action.Id.Value) is null)
            {
                continue;
            }

            HashSet<string> with = new(request.Held, StringComparer.Ordinal) { action.Id.Value };
            Board improved = OutageBoard.Simulate(request.Profile, with, scenario, season);
            IReadOnlyList<string> turned = Turned(current, improved);
            if (turned.Count > 0)
            {
                turns.Add(new ActionTurn(action.Id, turned));
            }
        }

        return new BoardSet(scenario, season, cards, sources, boards, current, turns);
    }

    /// <summary>Scenarios in the order the risk assessors rank them for this house, then the rest in catalogue order.</summary>
    internal static IReadOnlyList<Scenario> Ranked(RiskAssessment risks)
    {
        List<Scenario> ordered = [];
        foreach (Risk risk in risks.Ranked)
        {
            foreach (Scenario scenario in Scenarios.All)
            {
                if (scenario.Risk == risk.Kind && !ordered.Contains(scenario))
                {
                    ordered.Add(scenario);
                }
            }
        }

        foreach (Scenario scenario in Scenarios.All)
        {
            if (!ordered.Contains(scenario))
            {
                ordered.Add(scenario);
            }
        }

        return ordered;
    }

    /// <summary>The cells that improved, named by row and horizon.</summary>
    internal static IReadOnlyList<string> Turned(Board before, Board after)
    {
        List<string> turned = [];
        for (int r = 0; r < before.Rows.Count; r++)
        {
            BoardRow was = before.Rows[r];
            BoardRow now = after.Rows[r];
            for (int c = 0; c < was.Cells.Count; c++)
            {
                if (now.Cells[c].State < was.Cells[c].State)
                {
                    turned.Add($"{was.Name} at {HorizonWords(was.Cells[c].Horizon)}");
                }
            }
        }

        return turned;
    }

    private static readonly Dictionary<Horizon, string> Words = new()
    {
        [Horizon.OneDay] = "a day",
        [Horizon.ThreeDays] = "three days",
        [Horizon.OneWeek] = "a week",
        [Horizon.TwoWeeks] = "a fortnight",
        [Horizon.OneMonth] = "a month",
        [Horizon.ThreeMonths] = "three months",
        [Horizon.SixMonths] = "six months",
    };

    /// <summary>A horizon in words.</summary>
    public static string HorizonWords(Horizon horizon) => Words[horizon];
}
