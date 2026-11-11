using Selficient.Domain.Plans;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;

namespace Selficient.Application.Resilience;

/// <summary>A scenario as a card: its name, and how long this house lasts in it with what it has.</summary>
public sealed record ScenarioCard(string Id, string Name, decimal DaysReady, bool Selected);

/// <summary>A source the household could switch the board to. The first is always what is in the house.</summary>
public sealed record SourceOption(string Id, string Name, string Sentence);

/// <summary>How one need moves when a provider is added: from the days it held to the days it would hold. Equal days with Better set means the need is met properly where it was only got by.</summary>
public sealed record NeedShift(string Need, decimal From, decimal To, bool Better);

/// <summary>What one untaken plan action would do to the current board: the needs it moves and how far.</summary>
public sealed record ActionTurn(ActionId Action, IReadOnlyList<NeedShift> Shifts);

/// <summary>
/// Where a tier takes the house: the figure with everything up to and including that tier in place,
/// in the chosen scenario and season, leaving out what the household ruled out. Excludes says
/// whether anything was left out, so the sentence can; the tally counts the tier's own actions.
/// </summary>
public sealed record TierOutcome(Tier Tier, string Title, decimal DaysReady, ChoiceTally Tally, bool Excludes);

/// <summary>Everything the Board page draws for one household and one chosen scenario.</summary>
public sealed record BoardSet(
    Scenario Scenario,
    Season Season,
    string SourceId,
    IReadOnlyList<ScenarioCard> Cards,
    IReadOnlyList<SourceOption> Sources,
    Board Sheet,
    Board Current,
    IReadOnlyList<ActionTurn> Turns,
    IReadOnlyList<TierOutcome> Tiers,
    decimal PlannedDaysReady,
    ChoiceTally Tally)
{
    /// <summary>The id of the "nothing extra" source.</summary>
    public const string HouseSource = "house";

    /// <summary>The headline figure: the chosen scenario, what the house has, the chosen season.</summary>
    public decimal DaysReady => Current.DaysReady;
}

/// <summary>
/// What the composer needs: the household as the engine sees it, what it holds, the plan, and the
/// choice of scenario and season. Planned and Skipped are the actions the household means to get
/// and the ones it has ruled out; neither moves the figure for today.
/// </summary>
public sealed record BoardRequest(
    ResilienceProfile Profile,
    IReadOnlySet<string> Held,
    Plan Plan,
    string? ScenarioId,
    Season? Season,
    string? SourceId = null,
    IReadOnlySet<string>? Planned = null,
    IReadOnlySet<string>? Skipped = null);

/// <summary>
/// Composes the set for a page. Pure: it only runs the engine, several times, and arranges the
/// results. One sheet is simulated, for the chosen source and season; a switch on the page posts
/// for another. The scenario is simulated once per card for the figure on it, and once per untaken
/// action for what it would turn.
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

        // The house in the chosen season is the figure; the sheet is that, or the house with one more source.
        Board current = OutageBoard.Simulate(request.Profile, request.Held, scenario, season);
        string sourceId = request.SourceId is { } chosen && sources.Any(source => source.Id == chosen) ? chosen : BoardSet.HouseSource;
        Board sheet = current;
        if (sourceId != BoardSet.HouseSource)
        {
            HashSet<string> held = new(request.Held, StringComparer.Ordinal) { sourceId };
            sheet = OutageBoard.Simulate(request.Profile, held, scenario, season);
        }
        PlanChoices choices = new(request.Held, request.Planned ?? PlanChoices.None.Planned, request.Skipped ?? PlanChoices.None.Skipped);

        // Where the plan takes the house: what it has, with what it means to get. One more run, and only when something is planned.
        decimal plannedDays = current.DaysReady;
        if (choices.Planned.Count > 0)
        {
            HashSet<string> intended = new(request.Held, StringComparer.Ordinal);
            intended.UnionWith(choices.Planned);
            plannedDays = OutageBoard.Simulate(request.Profile, intended, scenario, season).DaysReady;
        }

        List<ActionTurn> turns = [];
        foreach (PlanAction action in request.Plan.Actions)
        {
            // An action the household ruled out is not suggested again under its own row.
            if (request.Held.Contains(action.Id.Value) || choices.Skipped.Contains(action.Id.Value) || Providers.Find(action.Id.Value) is null)
            {
                continue;
            }

            HashSet<string> with = new(request.Held, StringComparer.Ordinal) { action.Id.Value };
            Board improved = OutageBoard.Simulate(request.Profile, with, scenario, season);
            IReadOnlyList<NeedShift> shifts = Shifts(current, improved);
            if (shifts.Count > 0)
            {
                turns.Add(new ActionTurn(action.Id, shifts));
            }
        }

        // Each tier's destination is cumulative: this month assumes this week is done, as the plan does.
        List<TierOutcome> tiers = [];
        HashSet<string> sofar = new(request.Held, StringComparer.Ordinal);
        bool excludes = false;
        foreach (PlanTier tier in request.Plan.Tiers)
        {
            foreach (PlanAction action in tier.Actions)
            {
                if (choices.Skipped.Contains(action.Id.Value))
                {
                    excludes = true;
                    continue;
                }

                if (Providers.Find(action.Id.Value) is not null)
                {
                    sofar.Add(action.Id.Value);
                }
            }

            decimal days = OutageBoard.Simulate(request.Profile, sofar, scenario, season).DaysReady;
            tiers.Add(new TierOutcome(tier.Tier, tier.Title, days,ChoiceTally.Of(tier.Actions, choices), excludes));
        }

        return new BoardSet(scenario, season, sourceId, cards, sources, sheet, current, turns, tiers, plannedDays, ChoiceTally.Of(request.Plan.Actions, choices));
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
    /// <summary>The needs that move between two boards: more days held, or held properly where they were only got by.</summary>
    internal static IReadOnlyList<NeedShift> Shifts(Board before, Board after)
    {
        List<NeedShift> shifts = [];
        for (int r = 0; r < before.Rows.Count; r++)
        {
            BoardRow was = before.Rows[r];
            BoardRow now = after.Rows[r];
            bool better = false;
            for (int c = 0; c < was.Cells.Count; c++)
            {
                if (now.Cells[c].State < was.Cells[c].State)
                {
                    better = true;
                }
            }

            if (better)
            {
                shifts.Add(new NeedShift(was.Name, DaysHeld(was, before.DaysSimulated), DaysHeld(now, after.DaysSimulated), true));
            }
        }

        return shifts;
    }

    /// <summary>Days a need held before it was rationed or lost, as the strip and the figure count them.</summary>
    internal static decimal DaysHeld(BoardRow row, int simulated) => row.FirstRationedDay switch
    {
        null => simulated,
        1 => 0.5m,
        int first => first - 1,
    };

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
