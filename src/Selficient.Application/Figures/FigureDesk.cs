using Selficient.Application.Operations;
using Selficient.Application.Planning;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Parameters;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;

namespace Selficient.Application.Figures;

/// <summary>How one reference household stands in one scenario before and after the draft: the days it could stand on its own.</summary>
/// <param name="Household">Which reference household.</param>
/// <param name="Scenario">The scenario's name.</param>
/// <param name="Doing">What the household is taken to have done.</param>
/// <param name="Before">Days ready under the set in force.</param>
/// <param name="After">Days ready with the draft published.</param>
public sealed record PreviewLine(string Household, string Scenario, string Doing, decimal Before, decimal After);

/// <summary>What one reference household's whole plan costs before and after the draft.</summary>
/// <param name="Household">Which reference household.</param>
/// <param name="Before">The plan's cost under the set in force.</param>
/// <param name="After">The plan's cost with the draft published.</param>
public sealed record PreviewCost(string Household, MoneyRange Before, MoneyRange After);

/// <summary>What publishing the draft would change, for a fixed set of households: every figure that moves, and how many were compared.</summary>
/// <param name="Lines">The headline figures that would change.</param>
/// <param name="Costs">The plan costs that would change.</param>
/// <param name="Compared">How many headline figures were worked out both ways.</param>
public sealed record FigurePreview(IReadOnlyList<PreviewLine> Lines, IReadOnlyList<PreviewCost> Costs, int Compared);

/// <summary>
/// The desk where the figures are corrected. Anyone who can sign in to the back office may draft a
/// correction, with a reason and a source; only an operator who may publish can put a draft into
/// force. A draft is checked as a whole against the set in force before it is kept, so what can be
/// published is always something the engine can run. Putting an old set back is a publication like
/// any other, and every figure it moves is recorded.
/// </summary>
public sealed class FigureDesk
{
    /// <summary>The longest reason, source or note accepted.</summary>
    public const int MaxTextLength = 400;

    private readonly IFigureStore _store;
    private readonly CurrentFigures _current;
    private readonly IOperationsLog _log;
    private readonly RiskAssessmentPipeline _risks;
    private readonly PlanBuilder _builder;
    private readonly TimeProvider _clock;

    /// <summary>Creates the desk.</summary>
    public FigureDesk(IFigureStore store, CurrentFigures current, IOperationsLog log, RiskAssessmentPipeline risks, PlanBuilder builder, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(risks);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _current = current;
        _log = log;
        _risks = risks;
        _builder = builder;
        _clock = clock;
    }

    /// <summary>Drafts a correction to one figure, replacing any earlier draft of it.</summary>
    public async Task<Result<FigureDraft>> DraftAsync(string operatorName, string parameterId, decimal value, string reason, string source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorName);
        ArgumentNullException.ThrowIfNull(parameterId);
        Result<string> why = Text(reason, "Say why the figure should change, in up to 400 characters.");
        Result<string> from = Text(source, "Say where the value comes from, in up to 400 characters.");
        if (why.IsFailure || from.IsFailure)
        {
            return Result.Failure<FigureDraft>(why.IsFailure ? why.Error : from.Error);
        }

        IReadOnlyList<FigureDraft> drafts = await _store.DraftsAsync(cancellationToken);
        FigureDraft draft = new(parameterId, value, why.Value, from.Value, operatorName, _clock.GetUtcNow());
        Result<ParameterSet> together = Drafted([.. drafts.Where(other => other.ParameterId != parameterId), draft]);
        if (together.IsFailure)
        {
            return Result.Failure<FigureDraft>(together.Error);
        }

        await _store.SaveDraftAsync(draft, cancellationToken);
        return Result.Success(draft);
    }

    /// <summary>Throws a drafted correction away; false when there was none.</summary>
    public Task<bool> DiscardAsync(string parameterId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameterId);
        return _store.DiscardDraftAsync(parameterId, cancellationToken);
    }

    /// <summary>What publishing the draft in hand would change for the reference households.</summary>
    public async Task<Result<FigurePreview>> PreviewAsync(CancellationToken cancellationToken)
    {
        Result<ParameterSet> drafted = Drafted(await _store.DraftsAsync(cancellationToken));
        return drafted.Map(after => Compare(_current.Current, after));
    }

    /// <summary>Puts the draft in hand into force as the next set.</summary>
    public async Task<Result<PublishedFigures>> PublishAsync(string operatorName, string note, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorName);
        if (await Refused(operatorName, note, cancellationToken) is { } refusal)
        {
            return Result.Failure<PublishedFigures>(refusal);
        }

        IReadOnlyList<FigureDraft> drafts = await _store.DraftsAsync(cancellationToken);
        Result<ParameterSet> drafted = Drafted(drafts);
        if (drafted.IsFailure)
        {
            return Result.Failure<PublishedFigures>(drafted.Error);
        }

        ParameterSet before = _current.Current;
        List<FigureChange> changes = [.. drafts
            .Where(draft => before[draft.ParameterId] != draft.Value)
            .Select(draft => new FigureChange(drafted.Value.Version, draft.ParameterId, before[draft.ParameterId], draft.Value, draft.Reason, draft.Source, draft.DraftedBy, draft.DraftedAt))];
        if (changes.Count == 0)
        {
            return Result.Failure<PublishedFigures>(new Fault("figures.nothing", "Nothing drafted would change a figure, so there is nothing to publish."));
        }

        PublishedFigures set = new(drafted.Value.Version, _clock.GetUtcNow(), operatorName, note.Trim(), null, drafted.Value.Corrections);
        return await CommitAsync(set, changes, clearDrafts: true, "publish-figures", cancellationToken);
    }

    /// <summary>Puts an earlier set back into force, as a new set. Version nought is the shipped figures.</summary>
    public async Task<Result<PublishedFigures>> RestoreAsync(string operatorName, int version, string note, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorName);
        if (await Refused(operatorName, note, cancellationToken) is { } refusal)
        {
            return Result.Failure<PublishedFigures>(refusal);
        }

        ParameterSet before = _current.Current;
        IReadOnlyDictionary<string, decimal>? corrections = version == 0 ? new Dictionary<string, decimal>() : (await _store.FindAsync(version, cancellationToken))?.Corrections;
        if (corrections is null || version == before.Version)
        {
            return Result.Failure<PublishedFigures>(new Fault("figures.version", "That is not an earlier set of figures."));
        }

        Result<ParameterSet> restored = ParameterSet.Create(before.Version + 1, corrections);
        if (restored.IsFailure)
        {
            return Result.Failure<PublishedFigures>(restored.Error);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        string reason = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Put back to set {version}.");
        List<FigureChange> changes = [.. ParameterRegistry.All
            .Where(parameter => before[parameter.Id] != restored.Value[parameter.Id])
            .Select(parameter => new FigureChange(restored.Value.Version, parameter.Id, before[parameter.Id], restored.Value[parameter.Id], reason, note.Trim(), operatorName, now))];
        if (changes.Count == 0)
        {
            return Result.Failure<PublishedFigures>(new Fault("figures.version", "That set has the same figures as the one in force."));
        }

        PublishedFigures set = new(restored.Value.Version, now, operatorName, note.Trim(), version, restored.Value.Corrections);
        return await CommitAsync(set, changes, clearDrafts: false, "restore-figures", cancellationToken);
    }

    /// <summary>The headline figures and plan costs that differ between two sets, for the reference households.</summary>
    internal FigurePreview Compare(ParameterSet before, ParameterSet after)
    {
        List<PreviewLine> lines = [];
        List<PreviewCost> costs = [];
        int compared = 0;
        foreach ((string name, GeneratePlanCommand command) in ReferenceHouseholds.All)
        {
            Standing was = Stand(command, before);
            Standing will = Stand(command, after);
            if (was.Cost != will.Cost)
            {
                costs.Add(new PreviewCost(name, was.Cost, will.Cost));
            }

            foreach (((string Scenario, string Doing) key, decimal days) in was.Days)
            {
                compared++;
                if (will.Days[key] != days)
                {
                    lines.Add(new PreviewLine(name, key.Scenario, key.Doing, days, will.Days[key]));
                }
            }
        }

        return new FigurePreview(lines, costs, compared);
    }

    // The set the drafts would make over the one in force, as the next number, or the sentence that says why they cannot.
    private Result<ParameterSet> Drafted(IReadOnlyList<FigureDraft> drafts)
    {
        ParameterSet current = _current.Current;
        Dictionary<string, decimal> corrections = new(current.Corrections, StringComparer.Ordinal);
        foreach (FigureDraft draft in drafts)
        {
            corrections[draft.ParameterId] = draft.Value;
        }

        return ParameterSet.Create(current.Version + 1, corrections);
    }

    private async Task<Fault?> Refused(string operatorName, string note, CancellationToken cancellationToken)
    {
        if (!await _store.MayPublishAsync(operatorName, cancellationToken))
        {
            return new Fault("figures.forbidden", "Only an operator who may publish figures can put them into force.");
        }

        Result<string> said = Text(note, "Say what this publication is for, in up to 400 characters.");
        return said.IsFailure ? said.Error : null;
    }

    private async Task<Result<PublishedFigures>> CommitAsync(PublishedFigures set, List<FigureChange> changes, bool clearDrafts, string hand, CancellationToken cancellationToken)
    {
        if (!await _store.PublishAsync(set, changes, clearDrafts, cancellationToken))
        {
            return Result.Failure<PublishedFigures>(new Fault("figures.conflict", "Somebody else published while you were looking. Read their set and try again."));
        }

        await _current.ReloadAsync(cancellationToken);
        string outcome = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Set {set.Version} is in force: {changes.Count} figures changed. {set.Note}");
        await _log.RecordHandAsync(hand, null, outcome.Length > MaxTextLength ? outcome[..MaxTextLength] : outcome, set.By, cancellationToken);
        return Result.Success(set);
    }

    private Standing Stand(GeneratePlanCommand command, ParameterSet figures)
    {
        HouseholdProfile profile = GeneratePlanHandler.ToProfile(command, figures).Value;
        Plan plan = _builder.Build(profile, _risks.Run(profile)).Value;
        ResilienceProfile resilience = ResilienceProfile.FromProfile(profile);
        HashSet<string> nothing = [];
        HashSet<string> thisWeek = [.. plan.Tiers.Where(static tier => tier.Tier == Tier.ThisWeek).SelectMany(static tier => tier.Actions).Select(static action => action.Id.Value)];
        Dictionary<(string, string), decimal> days = [];
        foreach (Scenario scenario in ScenarioFigures.Under(figures))
        {
            days[(scenario.Name, ReferenceHouseholds.NothingDone)] = OutageBoard.Simulate(resilience, nothing, scenario, scenario.DefaultSeason).DaysReady;
            days[(scenario.Name, ReferenceHouseholds.ThisWeekDone)] = OutageBoard.Simulate(resilience, thisWeek, scenario, scenario.DefaultSeason).DaysReady;
        }

        return new Standing(plan.Cost, days);
    }

    private static Result<string> Text(string? text, string asking)
    {
        string trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length is 0 or > MaxTextLength
            ? Result.Failure<string>(new Fault("figures.text", asking))
            : Result.Success(trimmed);
    }

    private sealed record Standing(MoneyRange Cost, Dictionary<(string Scenario, string Doing), decimal> Days);
}

/// <summary>
/// The households a draft is tried on before it is published: few enough to read, different enough
/// that a figure which matters to anyone moves at least one of them.
/// </summary>
public static class ReferenceHouseholds
{
    /// <summary>What a household is taken to have done when it has done nothing.</summary>
    public const string NothingDone = "with nothing done";

    /// <summary>What a household is taken to have done when it has done this week's actions.</summary>
    public const string ThisWeekDone = "with this week's actions done";

    /// <summary>The households, each with a name a person would recognise.</summary>
    public static IReadOnlyList<(string Name, GeneratePlanCommand Command)> All { get; } =
    [
        ("A family of four in a semi with a gas boiler, a car and a garden",
            new GeneratePlanCommand(4, 2, false, false, false, DwellingType.SemiDetached, 2, HeatingType.GasBoiler, HobType.Electric, true, true, false, false, 60m, FloodRisk.VeryLow)),
        ("One person renting a third-floor flat with electric heating and no car",
            new GeneratePlanCommand(1, 0, false, false, false, DwellingType.Flat, 1, HeatingType.Electric, HobType.Electric, false, false, false, false, 20m, FloodRisk.VeryLow, Tenure: Tenure.Tenant, Floor: 3)),
        ("A couple in a bungalow with a wood stove, a gas hob, pets and a medium flood risk",
            new GeneratePlanCommand(2, 0, true, true, false, DwellingType.Bungalow, 1, HeatingType.Wood, HobType.Gas, true, true, false, false, 5m, FloodRisk.Medium, FloodFrom: FloodWater.RiversOrSea)),
        ("A family with a baby in a detached house with a heat pump and an electric car",
            new GeneratePlanCommand(3, 1, false, false, true, DwellingType.Detached, 2, HeatingType.HeatPump, HobType.Electric, true, true, true, true, 230m, FloodRisk.VeryLow, CarIsElectric: true, Infants: 1)),
        ("An older person in a terrace with a battery stairlift and a prepayment meter",
            new GeneratePlanCommand(1, 0, true, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, false, false, false, false, 40m, FloodRisk.Low, PrepaymentMeter: true, NeedsHelpToLeave: true, Stairlift: Stairlift.Battery)),
        ("A household on its own borehole and septic tank with an oil boiler",
            new GeneratePlanCommand(2, 0, false, true, false, DwellingType.Detached, 2, HeatingType.Oil, HobType.Electric, true, true, false, true, 180m, FloodRisk.VeryLow, Water: WaterSupply.OwnPumped, OwnDrains: true)),
    ];
}
