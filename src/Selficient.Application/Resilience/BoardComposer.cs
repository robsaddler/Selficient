using Selficient.Domain.Measures;
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

/// <summary>A row of the board that stands better on the sheet being shown than it does today, by the row's key, with how far it moved.</summary>
public sealed record RowShift(string Key, NeedShift Shift);

/// <summary>What one untaken plan action would do to the current board: the needs it moves and how far.</summary>
public sealed record ActionTurn(ActionId Action, IReadOnlyList<NeedShift> Shifts);

/// <summary>
/// Where a tier takes the house: the figure with everything up to and including that tier in place,
/// in the chosen scenario and season, leaving out what the household ruled out. Excludes says
/// whether anything was left out, so the sentence can; the tally counts the tier's own actions.
/// </summary>
public sealed record TierOutcome(Tier Tier, string Title, decimal DaysReady, ChoiceTally Tally, bool Excludes);

/// <summary>A plan action the household put its own figure towards: how far the figure goes, and what the rest of the action would cost.</summary>
public sealed record CountedAction(Progress Counted, MoneyRange Rest);

/// <summary>What a plan action asks this household for, in the figure of the stock it counts towards, under the assumptions in force.</summary>
public sealed record ActionTarget(string ActionId, string StockId, decimal Target);

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
    ChoiceTally Tally,
    IReadOnlyList<CountedAction> Progress,
    IReadOnlyList<ActionTarget> Targets,
    Assumptions Assumed,
    IReadOnlyList<RowShift> Compared)
{
    /// <summary>The id of the "nothing extra" source.</summary>
    public const string HouseSource = "house";

    /// <summary>The id of the source that is the house with everything it plans to get.</summary>
    public const string PlannedSource = "planned";

    /// <summary>The headline figure: the chosen scenario, what the house has, the chosen season.</summary>
    public decimal DaysReady => Current.DaysReady;
}

/// <summary>
/// What the composer needs: the household as the engine sees it, what it holds, the plan, and the
/// choice of scenario and season. Planned and Skipped are the actions the household means to get
/// and the ones it has ruled out; neither moves the figure for today. Amounts are the household's
/// own figures for the stocks it counted, already checked, which do; Assumptions are the plan's
/// figures the household changed to its own.
/// </summary>
public sealed record BoardRequest(
    ResilienceProfile Profile,
    IReadOnlySet<string> Held,
    Plan Plan,
    string? ScenarioId,
    Season? Season,
    string? SourceId = null,
    IReadOnlySet<string>? Planned = null,
    IReadOnlySet<string>? Skipped = null,
    IReadOnlyDictionary<string, int>? Amounts = null,
    Assumptions? Assumptions = null);

/// <summary>
/// Composes the set for a page. Pure: it only runs the engine, several times, and arranges the
/// results. One sheet is simulated, for the chosen source and season; a switch on the page posts
/// for another. The scenario is simulated once per card for the figure on it, and once per untaken
/// action for what it would turn.
/// </summary>
public static class BoardComposer
{
    // A source's name states its size only by reading it from the provider, and its sentence states no figure at all:
    // what it carries and for how long is what the board is for.
    private static readonly IReadOnlyList<(string Id, Placement Where, Func<Provider, string> Name, string Sentence)> Candidates =
    [
        ("power-banks", Placement.Anywhere, static _ => "Power banks", "Phones, headtorches and the radio for days; nothing with a plug."),
        ("portable-power-station", Placement.Anywhere, static source => $"A {Selficient.Domain.Parameters.Say.KilowattHours(source.Store!.StoreWh)} power station", "The router, lights, phones and a medical device, and the fridge for a while."),
        ("large-power-station", Placement.Anywhere, static source => $"A {Selficient.Domain.Parameters.Say.KilowattHours(source.Store!.StoreWh)} power station with panels", "The essentials for longer, with folding panels that put some back each day."),
        ("rooftop-solar-and-battery", Placement.NeedsOwnRoof, static _ => "A home battery and gateway", "The house as normal except heavy heating; the roof refills it."),
        ("ev-output", Placement.NeedsElectricCarWithOutput, static _ => "The car, powering the house", "The essentials from the battery you already own, with enough kept back to drive."),
        ("petrol-generator", Placement.OutdoorsOnly, static _ => "A petrol generator", "Runs outdoors for as long as the fuel lasts, and the forecourts refill it only while they pump."),
    ];

    /// <summary>Composes the set.</summary>
    public static BoardSet Compose(BoardRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The scenarios as this household's figures make them: a published correction to a storm is the storm on every card.
        IReadOnlyList<Scenario> scenarios = ScenarioFigures.Under(request.Profile.Figures);
        Scenario scenario = scenarios.FirstOrDefault(candidate => candidate.Id == request.ScenarioId) ?? scenarios[0];
        Season season = request.Season ?? scenario.DefaultSeason;

        // What the house has: the actions held, with the household's own figures deciding the stocks it counted.
        Possessions has = Possessions.Of(request.Profile, request.Held, request.Amounts, request.Assumptions);

        List<ScenarioCard> cards = [];
        foreach (Scenario candidate in Ranked(request.Plan.Risks, scenarios))
        {
            Board board = OutageBoard.Simulate(request.Profile, has, candidate, candidate.DefaultSeason);
            cards.Add(new ScenarioCard(candidate.Id, candidate.Name, board.DaysReady, candidate.Id == scenario.Id));
        }

        PlanChoices choices = new(has.Have, request.Planned ?? PlanChoices.None.Planned, request.Skipped ?? PlanChoices.None.Skipped);

        // What the house has with everything it means to get: the second figure, and a sheet of its own to switch the board to.
        Possessions intended = has.Copy();
        foreach (string planned in choices.Planned)
        {
            intended.Add(planned);
        }

        List<SourceOption> sources = [new SourceOption(BoardSet.HouseSource, "What is in the house", "Nothing bought yet beyond what the plan says you have ticked.")];
        if (choices.Planned.Count > 0)
        {
            sources.Add(new SourceOption(BoardSet.PlannedSource, "Everything we plan to get", "What is in the house, with every action marked as planned in place."));
        }

        foreach ((string id, Placement where, Func<Provider, string> name, string sentence) in Candidates)
        {
            if (request.Profile.CanHave(where) && !has.Have.Contains(id))
            {
                sources.Add(new SourceOption(id, name(ProviderFigures.Find(request.Profile.Figures, id)!), sentence));
            }
        }

        // The house in the chosen season is the figure; the sheet is that, or the house with one more source.
        Board current = OutageBoard.Simulate(request.Profile, has, scenario, season);
        string sourceId = request.SourceId is { } chosen && sources.Any(source => source.Id == chosen) ? chosen : BoardSet.HouseSource;
        // Where the plan takes the house. One more run, and only when something is planned.
        Board withPlanned = choices.Planned.Count > 0 ? OutageBoard.Simulate(request.Profile, intended, scenario, season) : current;
        decimal plannedDays = withPlanned.DaysReady;
        Board sheet = current;
        if (sourceId == BoardSet.PlannedSource)
        {
            sheet = withPlanned;
        }
        else if (sourceId != BoardSet.HouseSource)
        {
            sheet = OutageBoard.Simulate(request.Profile, With(has, sourceId), scenario, season);
        }

        List<ActionTurn> turns = [];
        List<CountedAction> progress = [];
        List<ActionTarget> targets = [];
        Assumptions assumed = request.Assumptions ?? Assumptions.Default;
        foreach (PlanAction action in request.Plan.Actions)
        {
            if (MeasuredStocks.ForAction(action.Id.Value) is { } measured)
            {
                targets.Add(new ActionTarget(action.Id.Value, measured.Id, MeasuredStocks.Target(measured, action.Id.Value, request.Profile, assumed)));
            }

            if (has.ProgressOn(action.Id.Value) is { } counted)
            {
                progress.Add(new CountedAction(counted, action.Cost.Share(counted.CostRemaining)));
            }

            // An action the household ruled out is not suggested again under its own row.
            if (has.Have.Contains(action.Id.Value) || choices.Skipped.Contains(action.Id.Value) || Providers.Find(action.Id.Value) is null)
            {
                continue;
            }

            Board improved = OutageBoard.Simulate(request.Profile, With(has, action.Id.Value), scenario, season);
            IReadOnlyList<NeedShift> shifts = Shifts(current, improved);
            if (shifts.Count > 0)
            {
                turns.Add(new ActionTurn(action.Id, shifts));
            }
        }

        // An action part met by the household's own figure costs the share of it still to buy.
        Dictionary<string, decimal> shares = progress.ToDictionary(static action => action.Counted.ActionId, static action => action.Counted.CostRemaining, StringComparer.Ordinal);
        decimal Remaining(string actionId) => shares.GetValueOrDefault(actionId, 1m);

        // Each tier's destination is cumulative: this month assumes this week is done, as the plan does.
        List<TierOutcome> tiers = [];
        Possessions sofar = has.Copy();
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
            tiers.Add(new TierOutcome(tier.Tier, tier.Title, days, ChoiceTally.Of(tier.Actions, choices, Remaining), excludes));
        }

        return new BoardSet(scenario, season, sourceId, cards, sources, sheet, current, turns, tiers, plannedDays, ChoiceTally.Of(request.Plan.Actions, choices, Remaining), progress, targets, assumed, Changes(current, sheet));
    }

    private static Possessions With(Possessions has, string actionId)
    {
        Possessions with = has.Copy();
        with.Add(actionId);
        return with;
    }

    /// <summary>Scenarios in the order the risk assessors rank them for this house, then the rest in catalogue order.</summary>
    internal static IReadOnlyList<Scenario> Ranked(RiskAssessment risks, IReadOnlyList<Scenario>? scenarios = null)
    {
        scenarios ??= Scenarios.All;
        List<Scenario> ordered = [];
        foreach (Risk risk in risks.Ranked)
        {
            foreach (Scenario scenario in scenarios)
            {
                if (scenario.Risk == risk.Kind && !ordered.Contains(scenario))
                {
                    ordered.Add(scenario);
                }
            }
        }

        foreach (Scenario scenario in scenarios)
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
    internal static IReadOnlyList<NeedShift> Shifts(Board before, Board after) => [.. Changes(before, after).Select(static change => change.Shift)];

    /// <summary>The rows that stand better on the second board than on the first, by key: what a sheet changes against today. Nothing when they are the same board.</summary>
    internal static IReadOnlyList<RowShift> Changes(Board before, Board after)
    {
        List<RowShift> shifts = [];
        for (int r = 0; r < before.Rows.Count; r++)
        {
            BoardRow was = before.Rows[r];
            BoardRow now = after.Rows[r];
            bool better = false;
            for (int c = 0; c < was.Cells.Count; c++)
            {
                // Better is a better state, or the same state reached later: a shelf that gives out on day twelve where it gave out on day six.
                if ((now.Cells[c].State, -now.Cells[c].HeldDays).CompareTo((was.Cells[c].State, -was.Cells[c].HeldDays)) < 0)
                {
                    better = true;
                }
            }

            if (better)
            {
                shifts.Add(new RowShift(was.Key, new NeedShift(was.Name, DaysHeld(was, before.DaysSimulated), DaysHeld(now, after.DaysSimulated), true)));
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
