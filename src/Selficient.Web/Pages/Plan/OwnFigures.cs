using System.Globalization;
using Selficient.Domain.Parameters;
using Selficient.Domain.Primitives;
using Selficient.Domain.Resilience;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// One of the plan's figures that a household may replace with its own, as the plan page asks it:
/// the question, the box, and what the plan would use otherwise.
/// </summary>
/// <param name="Definition">The figure.</param>
/// <param name="Value">What the box shows: the household's own where it gave one that could be used, the plan's otherwise.</param>
/// <param name="Plans">The plan's own figure.</param>
/// <param name="State">"own" when the household's figure is in use, "declined" when it gave one that could not be used, "plans" otherwise.</param>
/// <param name="Says">The sentence under the box.</param>
public sealed record OwnFigureQuestion(ParameterDefinition Definition, decimal Value, decimal Plans, string State, string Says)
{
    /// <summary>What the household is asked, as the set in force words it.</summary>
    public string Question { get; init; } = Definition.HouseholdQuestion ?? Definition.Name;

    /// <summary>The box's id, which its label and its sentence point at.</summary>
    public string InputId => "own-" + Definition.Id.Replace('.', '-');

    /// <summary>What the box steps by: whole numbers where the figure is counted in whole things.</summary>
    public string Step => Definition.WholeNumber ? "1" : "any";

    /// <summary>The button that puts the plan's own figure back.</summary>
    public string Reset => $"Use the plan's {OwnFigures.Plain(Plans)}";
}

/// <summary>The words around the household's own figures, decided in one place.</summary>
public static class OwnFigureCopy
{
    /// <summary>What the questions are, and what answering one does.</summary>
    public const string Lede = "Where nothing published settles a figure for your home, the plan uses its own best one. If you know yours, put it in, and your plan and the board are worked out again from it. Your figures are kept with your plan, in this browser.";

    /// <summary>The sentence shown once, on the plan built from a figure just given or taken back.</summary>
    public const string Rebuilt = "Your plan has been worked out again from your figures.";

    /// <summary>The line that opens the questions, with how many there are.</summary>
    public static string Summary(int questions) => questions == 1
        ? "One figure you can change to your own"
        : string.Create(CultureInfo.InvariantCulture, $"{questions} figures you can change to your own");
}

/// <summary>
/// The household's own figures, on their way in from a browser and on their way out onto the plan
/// page. A browser keeps them for years, so what arrives is treated as a guess at what the plan
/// still asks: a figure the plan can no longer use is declined with a sentence by the set, and only
/// something no browser of ours would send (text for a number, more figures than there are) is refused.
/// </summary>
public static class OwnFigures
{
    /// <summary>A figure as the page writes it: no trailing noughts, no grouping.</summary>
    public static string Plain(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Reads the figures a browser posted, or says why they are not figures.</summary>
    public static Result<IReadOnlyDictionary<string, decimal>> Read(IReadOnlyDictionary<string, string> posted)
    {
        ArgumentNullException.ThrowIfNull(posted);
        if (posted.Count > ParameterSet.MaxOwn)
        {
            return Result.Failure<IReadOnlyDictionary<string, decimal>>(new Fault("plan.own", "That is more figures of your own than the plan asks for."));
        }

        Dictionary<string, decimal> figures = new(StringComparer.Ordinal);
        foreach ((string id, string text) in posted)
        {
            if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out decimal value))
            {
                return Result.Failure<IReadOnlyDictionary<string, decimal>>(new Fault("plan.own", "One of your own figures is not a number."));
            }

            figures[id] = value;
        }

        return Result.Success<IReadOnlyDictionary<string, decimal>>(figures);
    }

    /// <summary>
    /// The questions this household is asked: a figure belongs to a thing, and the question is asked
    /// only of a household that has the thing by its nature or is offered it by its plan. Nobody
    /// without a stairlift is asked how long one runs.
    /// </summary>
    public static IReadOnlyList<OwnFigureQuestion> Questions(Selficient.Domain.Plans.Plan plan, ResilienceProfile household)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(household);
        ParameterSet figures = household.Figures;
        HashSet<string> has = new(household.Fixtures, StringComparer.Ordinal);
        has.UnionWith(plan.Actions.Select(static action => action.Id.Value));
        List<OwnFigureQuestion> questions = [];
        foreach (ParameterDefinition parameter in ParameterRegistry.All)
        {
            // Asked only where the set in force says a household may give it, and only of a household that has the thing or is offered it.
            if (figures.QuestionFor(parameter.Id) is not { } question || !has.Contains(ThingOf(parameter.Id)))
            {
                continue;
            }

            decimal plans = figures.Plans[parameter.Id];
            OwnFigureQuestion asked = figures.Declined.TryGetValue(parameter.Id, out string? why)
                ? new OwnFigureQuestion(parameter, plans, plans, "declined", $"The plan could not use the figure you gave before, so it is using its own. {why}")
                : figures.IsOwn(parameter.Id)
                    ? new OwnFigureQuestion(parameter, figures[parameter.Id], plans, "own", $"The plan is using your figure. Its own is {Plain(plans)} {parameter.Unit}.")
                    : new OwnFigureQuestion(parameter, plans, plans, "plans", PlansSays(parameter));
            questions.Add(asked with { Question = question });
        }

        return questions;
    }

    // The thing a figure is about: the provider it belongs to, or for a price the action that is bought.
    private static string ThingOf(string id)
    {
        string owner = id[..id.LastIndexOf('.')];
        return owner.StartsWith("price.", StringComparison.Ordinal) ? owner["price.".Length..] : owner;
    }

    // Where the plan's figure has a source outside the plan, the household is told it; "the plan" is not a source worth naming.
    private static string PlansSays(ParameterDefinition parameter) => parameter.Source.StartsWith("Plan", StringComparison.Ordinal)
        ? "This is the plan's own figure. Change it if you know yours."
        : $"This is the plan's own figure, from: {parameter.Source}. Change it if you know yours.";

    /// <summary>
    /// What a row of the board says when it was worked out from the household's own figure for
    /// something that carries it or fails it, or nothing when it was not.
    /// </summary>
    public static string OnRow(BoardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Figures.Asked.Count == 0)
        {
            return string.Empty;
        }

        HashSet<string> named = new(StringComparer.Ordinal);
        foreach (DayState day in row.Days)
        {
            named.Add(day.CarriedBy ?? string.Empty);
            named.Add(day.FailedBy ?? string.Empty);
        }

        List<string> own = [.. row.Figures.Asked.Keys
            .Where(id => row.Figures.IsOwn(id) && named.Contains(id[..id.LastIndexOf('.')]))
            .Select(id => BoardCopy.Name(id[..id.LastIndexOf('.')], row.Figures))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        return own.Count == 0 ? string.Empty : $"Worked out from your own figure for {string.Join(" and ", own)}.";
    }
}
