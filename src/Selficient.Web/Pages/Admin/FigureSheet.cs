using System.Globalization;
using Selficient.Application.Figures;
using Selficient.Domain.Parameters;
using Selficient.Domain.Resilience;

namespace Selficient.Web.Pages.Admin;

/// <summary>A mark beside a figure: that it has been corrected, that a correction is drafted, that a household may give its own.</summary>
/// <param name="Kind">Which mark, for the stylesheet.</param>
/// <param name="Text">What it says.</param>
public sealed record FigureChip(string Kind, string Text);

/// <summary>One figure as the desk draws it, with the box that corrects it already filled in.</summary>
/// <param name="Definition">The figure.</param>
/// <param name="Label">Its name inside its card.</param>
/// <param name="Value">The value in force, as it is written.</param>
/// <param name="State">Shipped, corrected or drafted.</param>
/// <param name="Chips">The marks beside it.</param>
/// <param name="Allowed">The range a correction must stay inside, as a sentence.</param>
/// <param name="EnteredValue">What the value box holds.</param>
/// <param name="EnteredReason">What the reason box holds.</param>
/// <param name="EnteredSource">What the source box holds.</param>
/// <param name="Refusal">Why the correction just posted was refused, or null.</param>
public sealed record FigureRow(ParameterDefinition Definition, string Label, string Value, string State, IReadOnlyList<FigureChip> Chips, string Allowed, string EnteredValue, string EnteredReason, string EnteredSource, string? Refusal)
{
    /// <summary>Where on the page the figure is, so a correction comes back to it.</summary>
    public string Anchor => FigureSheet.AnchorOf(Definition.Id);

    /// <summary>Whether its box is open when the page is drawn: only when a correction to it was just refused.</summary>
    public bool Open => Refusal is not null;
}

/// <summary>The figures of one thing: a provider with the name and sentence the board gives it, a scenario with its sentence, or a price.</summary>
/// <param name="Title">The thing's name, or null where the group's heading already says it.</param>
/// <param name="Sentence">The sentence a household reads, built from the figures in force, or null where there is none.</param>
/// <param name="Drafted">The same words as the draft would make them, when they would differ.</param>
/// <param name="Rows">Its figures.</param>
public sealed record FigureCard(string? Title, string? Sentence, string? Drafted, IReadOnlyList<FigureRow> Rows);

/// <summary>The cards under one heading.</summary>
/// <param name="Title">The heading: a need, the sources of electricity, a scenario.</param>
/// <param name="Cards">Its cards.</param>
public sealed record FigureGroup(string Title, IReadOnlyList<FigureCard> Cards);

/// <summary>A correction that was posted and refused, kept so the box can be drawn again as it was left.</summary>
/// <param name="Id">The figure.</param>
/// <param name="Value">What was typed for the value.</param>
/// <param name="Reason">What was typed for the reason.</param>
/// <param name="Source">What was typed for the source.</param>
/// <param name="Message">Why it was refused.</param>
public sealed record RefusedCorrection(string Id, string Value, string Reason, string Source, string Message);

/// <summary>
/// Lays the figures out as a household meets them: under each need, a card for each thing that meets
/// it, headed with the name and sentence the board shows; then the scenarios, each with its sentence;
/// then the prices, a card to each thing bought. The words are built from the figures in force, and
/// again from the draft, so somebody correcting a figure sees what a household would be told.
/// </summary>
public static class FigureSheet
{
    private const string PricePrefix = "price.";
    private const string ScenarioPrefix = "scenario.";

    /// <summary>Whether a figure is a price, which the desk keeps on a tab of its own.</summary>
    public static bool IsPrice(ParameterDefinition parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return parameter.Id.StartsWith(PricePrefix, StringComparison.Ordinal);
    }

    /// <summary>The fragment a figure is found at.</summary>
    public static string AnchorOf(string id) => "f-" + id;

    /// <summary>A value as the desk writes it: no trailing noughts, no grouping.</summary>
    public static string Plain(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The groups, cards and rows for these figures, in the order they were given.</summary>
    public static IReadOnlyList<FigureGroup> Build(IEnumerable<ParameterDefinition> parameters, DeskView desk, RefusedCorrection? refused)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(desk);
        Dictionary<string, FigureDraft> drafts = desk.Drafts.ToDictionary(static draft => draft.ParameterId, StringComparer.Ordinal);
        List<(string Group, List<(Subject Subject, List<FigureRow> Rows)> Cards)> groups = [];
        foreach (ParameterDefinition parameter in parameters)
        {
            Subject subject = SubjectOf(parameter, desk);
            if (groups.Count == 0 || groups[^1].Group != parameter.Group)
            {
                groups.Add((parameter.Group, []));
            }

            List<(Subject Subject, List<FigureRow> Rows)> cards = groups[^1].Cards;
            if (cards.Count == 0 || cards[^1].Subject.Key != subject.Key)
            {
                cards.Add((subject, []));
            }

            cards[^1].Rows.Add(Row(parameter, subject, desk, drafts.GetValueOrDefault(parameter.Id), refused));
        }

        return [.. groups.Select(static group => new FigureGroup(group.Group, [.. group.Cards.Select(static card => new FigureCard(card.Subject.Title, card.Subject.Sentence, card.Subject.Drafted, card.Rows))]))];
    }

    private static FigureRow Row(ParameterDefinition parameter, Subject subject, DeskView desk, FigureDraft? draft, RefusedCorrection? refused)
    {
        decimal inForce = desk.InForce[parameter.Id];
        List<FigureChip> chips = [];
        string state = "shipped";
        if (inForce != parameter.Default)
        {
            chips.Add(new FigureChip("corrected", $"Shipped as {Plain(parameter.Default)}"));
            state = "corrected";
        }

        string value = Plain(inForce);
        string reason = string.Empty;
        string source = string.Empty;
        if (draft is not null)
        {
            chips.Add(new FigureChip("drafted", $"Drafted as {Plain(draft.Value)} by {draft.DraftedBy}"));
            state = "drafted";
            (value, reason, source) = (Plain(draft.Value), draft.Reason, draft.Source);
        }

        if (parameter.HouseholdMayOverride)
        {
            chips.Add(new FigureChip("household", "A household may give its own"));
        }

        string? refusal = null;
        if (refused is not null && refused.Id == parameter.Id)
        {
            (value, reason, source, refusal) = (refused.Value, refused.Reason, refused.Source, refused.Message);
        }

        string label = subject.Title is null ? parameter.Name : Say.Capital(parameter.Name[(parameter.Name.LastIndexOf(": ", StringComparison.Ordinal) + 2)..]);
        return new FigureRow(parameter, label, Plain(inForce), state, chips, $"A correction must be between {Plain(parameter.Min)} and {Plain(parameter.Max)}.", value, reason, source, refusal);
    }

    // What a figure belongs to. A provider is found by the first part of the figure's id, which is the provider's own.
    private static Subject SubjectOf(ParameterDefinition parameter, DeskView desk)
    {
        string owner = parameter.Id[..parameter.Id.LastIndexOf('.')];
        ParameterSet drafted = desk.Drafted ?? desk.InForce;
        if (ProviderFigures.Find(desk.InForce, owner) is { } provider)
        {
            Provider would = ProviderFigures.Find(drafted, owner)!;
            return new Subject(owner, provider.Name, provider.Sentence, Differing($"{provider.Name}. {provider.Sentence}", $"{would.Name}. {would.Sentence}"));
        }

        if (parameter.Id.StartsWith(ScenarioPrefix, StringComparison.Ordinal))
        {
            string id = parameter.Id.Split('.')[1];
            Scenario scenario = ScenarioFigures.Under(desk.InForce).First(candidate => candidate.Id == id);
            Scenario would = ScenarioFigures.Under(drafted).First(candidate => candidate.Id == id);
            return new Subject(ScenarioPrefix + id, null, scenario.Sentence, Differing(scenario.Sentence, would.Sentence));
        }

        return IsPrice(parameter)
            ? new Subject(owner, parameter.Name[..parameter.Name.LastIndexOf(": ", StringComparison.Ordinal)], null, null)
            : new Subject(parameter.Group, null, null, null);
    }

    private static string? Differing(string inForce, string drafted) => drafted == inForce ? null : drafted;

    private sealed record Subject(string Key, string? Title, string? Sentence, string? Drafted);
}
