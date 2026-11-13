using System.Collections.Concurrent;
using System.Collections.Frozen;
using Selficient.Domain.Primitives;
using Selficient.Domain.Resilience;

namespace Selficient.Domain.Parameters;

/// <summary>
/// One figure the engine works from: what a thing holds, what it draws, how much a person needs, how
/// much of a utility a scenario leaves. It has a name a person can read, a unit, the value the
/// product ships with and where that came from, the range a correction must stay inside, and where a
/// household meets it, so that somebody who knows better can say so and see what it changes.
/// </summary>
/// <param name="Id">A stable id: the provider or scenario it belongs to, then the figure.</param>
/// <param name="Group">The heading it is listed under: a need, the sources of electricity, a scenario.</param>
/// <param name="Name">What the figure is, in words.</param>
/// <param name="Unit">What it is counted in.</param>
/// <param name="Default">The value the product ships with.</param>
/// <param name="Min">The least a correction may be.</param>
/// <param name="Max">The most a correction may be.</param>
/// <param name="Source">Where the shipped value came from.</param>
/// <param name="Shown">Where a household meets it.</param>
public sealed record ParameterDefinition(string Id, string Group, string Name, string Unit, decimal Default, decimal Min, decimal Max, string Source, string Shown)
{
    /// <summary>The question a household is asked when it may put its own figure here; null when it may not.</summary>
    public string? HouseholdQuestion { get; init; }

    /// <summary>Whether a household may put its own figure here as the product ships. A published set may say otherwise: ask the set.</summary>
    public bool HouseholdMayOverride => HouseholdQuestion is not null;

    /// <summary>
    /// Whether an expert may switch the household's own figure on or off here. True for a figure that
    /// belongs to a thing a household has and for a price; false for what people need, for what a
    /// scenario does and for the cash a person needs, which has a box of its own.
    /// </summary>
    public bool HouseholdMayBeAsked { get; init; }

    /// <summary>Whether the figure is counted in whole things: days, not parts of a day.</summary>
    public bool WholeNumber { get; init; }

    /// <summary>Whether a value is inside the range, and whole where the figure is counted in whole things.</summary>
    public bool Accepts(decimal value) => value >= Min && value <= Max && (!WholeNumber || value == decimal.Truncate(value));
}

/// <summary>Every figure the engine works from, gathered from the tables that hold them so the list and the engine cannot disagree.</summary>
public static class ParameterRegistry
{
    /// <summary>Every parameter, in the order a person would read them: what people need, what meets it, what happens.</summary>
    public static IReadOnlyList<ParameterDefinition> All { get; } = [.. DemandFigures.Definitions, Assumptions.Definition, .. ProviderFigures.Definitions, .. ScenarioFigures.Definitions, .. Selficient.Domain.Plans.Rules.RuleFigures.Definitions];

    private static readonly FrozenDictionary<string, ParameterDefinition> ById = All.ToFrozenDictionary(static parameter => parameter.Id, StringComparer.Ordinal);

    /// <summary>The parameter with this id, or null.</summary>
    public static ParameterDefinition? Find(string id) => ById.GetValueOrDefault(id);
}

/// <summary>
/// A whole set of figures: the shipped values with a published set of corrections over them, and
/// perhaps one household's own over those. The engine is given a set and reads every figure from
/// it, so a board is always worked out from one consistent set and says which.
/// </summary>
public sealed class ParameterSet
{
    private readonly FrozenDictionary<string, decimal> _values;
    private readonly ConcurrentDictionary<Type, object> _derived = new();
    private ParameterSet? _plans;

    private static readonly IReadOnlyDictionary<string, string?> NothingAsked = new Dictionary<string, string?>();

    private ParameterSet(int version, FrozenDictionary<string, decimal> values, IReadOnlyDictionary<string, decimal> corrections, IReadOnlyDictionary<string, string?> asks)
    {
        Version = version;
        _values = values;
        Corrections = corrections;
        Asks = asks;
    }

    /// <summary>The shortest question a household may be asked.</summary>
    public const int MinQuestionLength = 10;

    /// <summary>The longest question a household may be asked.</summary>
    public const int MaxQuestionLength = 200;

    /// <summary>The shipped figures and nothing over them. This is what the code, the tests and a server with no database work from.</summary>
    public static ParameterSet Default { get; } = new(0, ParameterRegistry.All.ToFrozenDictionary(static parameter => parameter.Id, static parameter => parameter.Default, StringComparer.Ordinal), new Dictionary<string, decimal>(), NothingAsked);

    /// <summary>
    /// Where this set differs from the product as shipped in which figures a household may give for
    /// itself: the question to ask where one has been switched on or reworded, and null where one
    /// has been switched off.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Asks { get; }

    /// <summary>The question a household is asked for its own figure here, or null when it may not give one.</summary>
    public string? QuestionFor(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Asks.TryGetValue(id, out string? asked) ? asked : ParameterRegistry.Find(id)?.HouseholdQuestion;
    }

    /// <summary>Whether a household may give its own figure here under this set.</summary>
    public bool MayGive(string id) => QuestionFor(id) is not null;

    /// <summary>Which published set this is; nought for the shipped figures.</summary>
    public int Version { get; }

    /// <summary>The figures that differ from the shipped ones, by id.</summary>
    public IReadOnlyDictionary<string, decimal> Corrections { get; }

    /// <summary>The value of a figure. An id that is not a parameter is a programming error and throws.</summary>
    public decimal this[string id] => _values[id];

    /// <summary>
    /// The shipped figures with a published set of corrections over them. A correction to something
    /// that is not a parameter, or outside its range, or that leaves two figures contradicting each
    /// other, is refused with a sentence that names it.
    /// </summary>
    public static Result<ParameterSet> Create(int version, IReadOnlyDictionary<string, decimal> corrections) => Create(version, corrections, NothingAsked);

    /// <summary>
    /// The shipped figures with a published set of corrections over them, and with the figures a
    /// household may give for itself switched on, reworded or switched off as the set says. A switch
    /// on a figure that cannot be switched, or a question that is not one, is refused with a sentence.
    /// </summary>
    public static Result<ParameterSet> Create(int version, IReadOnlyDictionary<string, decimal> corrections, IReadOnlyDictionary<string, string?> asks)
    {
        ArgumentNullException.ThrowIfNull(corrections);
        ArgumentNullException.ThrowIfNull(asks);
        if (version < 1)
        {
            return Result.Failure<ParameterSet>(new Fault("parameters.version", "A published set of figures is numbered from one."));
        }

        Dictionary<string, string?> differing = new(StringComparer.Ordinal);
        foreach ((string id, string? question) in asks)
        {
            if (ParameterRegistry.Find(id) is not { HouseholdMayBeAsked: true } parameter)
            {
                return Result.Failure<ParameterSet>(new Fault("parameters.ask", $"Whether a household may give \"{id}\" for itself is not something that can be changed."));
            }

            string? asked = question?.Trim();
            if (asked is not null && (asked.Length < MinQuestionLength || asked.Length > MaxQuestionLength || !asked.EndsWith('?')))
            {
                return Result.Failure<ParameterSet>(new Fault("parameters.question", $"What a household is asked about \"{parameter.Name}\" must be a question of {MinQuestionLength} to {MaxQuestionLength} characters, ending in a question mark."));
            }

            // Only a difference from the product as shipped is kept, so a set says what it changed and nothing else.
            if (asked != parameter.HouseholdQuestion)
            {
                differing[id] = asked;
            }
        }

        return Default.Over(version, corrections, static _ => true, "is not one of the figures the plan works from", differing);
    }

    /// <summary>
    /// This set with one household's own figures over it, for that household's plan only. Only a
    /// figure marked as the household's to give is accepted, and inside the same range as anybody's.
    /// </summary>
    public Result<ParameterSet> ForHousehold(IReadOnlyDictionary<string, decimal> own)
    {
        ArgumentNullException.ThrowIfNull(own);
        return own.Count == 0 ? Result.Success(this) : Over(Version, own, parameter => MayGive(parameter.Id), "is not a figure a household can give for itself", Asks);
    }

    /// <summary>The most figures of its own a household may give. There are fewer than this to give.</summary>
    public const int MaxOwn = 20;

    /// <summary>The figures one household gave for itself, as it gave them, whether or not each could be used. Empty for a published set.</summary>
    public IReadOnlyDictionary<string, decimal> Asked { get; private init; } = new Dictionary<string, decimal>();

    /// <summary>For each figure a household gave that could not be used, the sentence that says why. The plan's own figure stands in its place.</summary>
    public IReadOnlyDictionary<string, string> Declined { get; private init; } = new Dictionary<string, string>();

    /// <summary>The same set without the household's own figures: what the plan would use, and what "use the plan's" goes back to.</summary>
    public ParameterSet Plans => _plans ?? this;

    /// <summary>Whether a figure is the household's own in this set: it gave one and it was used.</summary>
    public bool IsOwn(string id) => Asked.ContainsKey(id) && !Declined.ContainsKey(id);

    /// <summary>
    /// This set with one household's own figures over it, for its plan only, leniently: a household
    /// keeps its figures in its browser for years, and a release may since have narrowed a range or
    /// removed a figure. Each figure that can still be used is used; each that cannot is declined
    /// with a sentence and the plan's own stands. The plan is always built. What a person needs in
    /// cash is the household's to give too, but it is given beside the cash it counts and not here.
    /// </summary>
    public ParameterSet WithOwn(IReadOnlyDictionary<string, decimal> own)
    {
        ArgumentNullException.ThrowIfNull(own);
        if (own.Count == 0)
        {
            return this;
        }

        ParameterSet made = this;
        Dictionary<string, string> declined = new(StringComparer.Ordinal);
        foreach ((string id, decimal value) in own.OrderBy(static figure => figure.Key, StringComparer.Ordinal))
        {
            Result<ParameterSet> with = made.Over(Version, new Dictionary<string, decimal> { [id] = value }, OwnToGive, "is not a figure a household can give for itself", Asks);
            if (with.IsSuccess)
            {
                made = with.Value;
            }
            else
            {
                declined[id] = with.Error.Message;
            }
        }

        return new ParameterSet(Version, made._values, made.Corrections, Asks) { Asked = new Dictionary<string, decimal>(own, StringComparer.Ordinal), Declined = declined, _plans = this };
    }

    private bool OwnToGive(ParameterDefinition parameter) => MayGive(parameter.Id) && parameter.Id != Assumptions.CashFigure;

    /// <summary>Something worked out from the whole set, built once and kept: the providers and the scenarios as these figures make them.</summary>
    internal T Derive<T>(Func<ParameterSet, T> build)
        where T : class =>
        (T)_derived.GetOrAdd(typeof(T), static (_, state) => state.build(state.set), (build, set: this));

    private Result<ParameterSet> Over(int version, IReadOnlyDictionary<string, decimal> changes, Func<ParameterDefinition, bool> allowed, string refusal, IReadOnlyDictionary<string, string?> asks)
    {
        Dictionary<string, decimal> values = new(_values, StringComparer.Ordinal);
        foreach ((string id, decimal value) in changes)
        {
            if (ParameterRegistry.Find(id) is not { } parameter || !allowed(parameter))
            {
                return Result.Failure<ParameterSet>(new Fault("parameters.unknown", $"\"{id}\" {refusal}."));
            }

            if (!parameter.Accepts(value))
            {
                return Result.Failure<ParameterSet>(new Fault("parameters.range", $"{parameter.Name} must be between {Plain(parameter.Min)} and {Plain(parameter.Max)} {parameter.Unit}."));
            }

            values[id] = value;
        }

        string? contradiction = ProviderFigures.Contradiction(values) ?? ScenarioFigures.Contradiction(values) ?? Selficient.Domain.Plans.Rules.RuleFigures.Contradiction(values);
        if (contradiction is not null)
        {
            return Result.Failure<ParameterSet>(new Fault("parameters.contradiction", contradiction));
        }

        Dictionary<string, decimal> corrections = [];
        foreach (ParameterDefinition parameter in ParameterRegistry.All)
        {
            if (values[parameter.Id] != parameter.Default)
            {
                corrections[parameter.Id] = values[parameter.Id];
            }
        }

        return Result.Success(new ParameterSet(version, values.ToFrozenDictionary(StringComparer.Ordinal), corrections, asks));
    }

    private static string Plain(decimal value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
