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

    /// <summary>Whether a household may give its own figure here, and the box that changes it.</summary>
    public AskBox Ask { get; init; } = AskBox.None;

    /// <summary>Whether the box that corrects its value is open when the page is drawn: only when a correction to it was just refused.</summary>
    public bool Open => Refusal is not null;
}

/// <summary>Whether a household may give its own figure, as the desk shows and changes it.</summary>
/// <param name="View">The view that draws the box, or nothing where this cannot be changed.</param>
/// <param name="Stands">How it stands under the set in force.</param>
/// <param name="Allow">What the box's choice shows.</param>
/// <param name="Question">What the question box holds.</param>
/// <param name="Reason">What the reason box holds.</param>
/// <param name="Refusal">Why the change just posted was refused, or null.</param>
public sealed record AskBox(string View, string Stands, bool Allow, string Question, string Reason, string? Refusal)
{
    /// <summary>Whether a household may give its own figure under the set in force.</summary>
    public bool InForce { get; init; }

    /// <summary>The line on the figure that says how it stands and opens the box: it is what an expert looks for, so it is on the row and not inside another box.</summary>
    public string Summary => InForce ? "A household may give its own figure: change that" : "A household may not give its own figure: change that";

    /// <summary>How it stands, for the stylesheet.</summary>
    public string Marked => InForce ? "true" : "false";

    /// <summary>Whether the box is open when the page is drawn: only when a change posted to it was just refused.</summary>
    public bool Open => Refusal is not null;

    /// <summary>A figure with nothing to switch, before anything has been said about why.</summary>
    public static AskBox None { get; } = Fixed(string.Empty);

    /// <summary>A figure with nothing to switch, and the line that says why, so nobody looks for a switch that is not there.</summary>
    public static AskBox Fixed(string why) => new("_FiguresNoAsk", why, false, string.Empty, string.Empty, null);

    /// <summary>The other choice.</summary>
    public bool Deny => !Allow;
}

/// <summary>A change to whether a household may give its own figure that was posted and refused, kept so the box can be drawn as it was left.</summary>
/// <param name="Id">The figure.</param>
/// <param name="Allow">What was chosen.</param>
/// <param name="Question">What was typed for the question.</param>
/// <param name="Reason">What was typed for the reason.</param>
/// <param name="Message">Why it was refused.</param>
public sealed record RefusedAsk(string Id, bool Allow, string Question, string Reason, string Message);

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
    public static IReadOnlyList<FigureGroup> Build(IEnumerable<ParameterDefinition> parameters, DeskView desk, RefusedCorrection? refused, RefusedAsk? refusedAsk = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(desk);
        Dictionary<(string Id, FigureDraftKind Kind), FigureDraft> drafts = desk.Drafts.ToDictionary(static draft => (draft.ParameterId, draft.Kind));
        // A heading is drawn once, however the figures under it are scattered through the registry: the things
        // that meet a need are listed in the order the engine tries them, which is not need by need.
        List<(string Group, List<(Subject Subject, List<FigureRow> Rows)> Cards)> groups = [];
        Dictionary<string, int> groupAt = new(StringComparer.Ordinal);
        Dictionary<(string Group, string Subject), int> cardAt = [];
        foreach (ParameterDefinition parameter in parameters)
        {
            Subject subject = SubjectOf(parameter, desk);
            if (!groupAt.TryGetValue(parameter.Group, out int group))
            {
                group = groupAt[parameter.Group] = groups.Count;
                groups.Add((parameter.Group, []));
            }

            List<(Subject Subject, List<FigureRow> Rows)> cards = groups[group].Cards;
            if (!cardAt.TryGetValue((parameter.Group, subject.Key), out int card))
            {
                card = cardAt[(parameter.Group, subject.Key)] = cards.Count;
                cards.Add((subject, []));
            }

            FigureRow row = Row(parameter, subject, desk, drafts.GetValueOrDefault((parameter.Id, FigureDraftKind.Value)), refused);
            AskBox ask = Asking(parameter, desk, drafts.GetValueOrDefault((parameter.Id, FigureDraftKind.Ask)), refusedAsk, out FigureChip? drafted);
            List<FigureChip> chips = [.. row.Chips];
            if (drafted is not null)
            {
                chips.Add(drafted);
                row = row with { State = "drafted" };
            }

            cards[card].Rows.Add(row with { Chips = chips, Ask = ask });
        }

        // The needs come in the board's own order, rung by rung, wherever each first turned up; every other heading keeps its place.
        return [.. groups
            .Select(static (group, at) => (Group: group, At: at))
            .OrderBy(static placed => NeedOrder.GetValueOrDefault(placed.Group.Group, placed.At == 0 ? -1 : int.MaxValue))
            .ThenBy(static placed => placed.At)
            .Select(static placed => new FigureGroup(placed.Group.Group, [.. placed.Group.Cards.Select(static card => new FigureCard(card.Subject.Title, card.Subject.Sentence, card.Subject.Drafted, card.Rows))]))];
    }

    private static readonly Dictionary<string, int> NeedOrder = Needs.All.Select(static (need, at) => (need.Name, at)).ToDictionary(static need => need.Name, static need => need.at, StringComparer.Ordinal);

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

        string? refusal = null;
        if (refused is not null && refused.Id == parameter.Id)
        {
            (value, reason, source, refusal) = (refused.Value, refused.Reason, refused.Source, refused.Message);
        }

        string label = subject.Title is null ? parameter.Name : Say.Capital(parameter.Name[(parameter.Name.LastIndexOf(": ", StringComparison.Ordinal) + 2)..]);
        return new FigureRow(parameter, label, Plain(inForce), state, chips, $"A correction must be between {Plain(parameter.Min)} and {Plain(parameter.Max)}.", value, reason, source, refusal);
    }

    // Whether a household may give its own figure here: how it stands, what is drafted about it, and the box as it should be drawn.
    private static AskBox Asking(ParameterDefinition parameter, DeskView desk, FigureDraft? draft, RefusedAsk? refused, out FigureChip? drafted)
    {
        drafted = null;
        if (!parameter.HouseholdMayBeAsked)
        {
            // Said on the figure, because an expert looking for the switch should be told why there is none.
            return AskBox.Fixed(desk.InForce.MayGive(parameter.Id)
                ? "A household always gives its own figure here, on its plan. That cannot be switched off."
                : "This is the same for every household, so a household cannot give its own.");
        }

        string? asked = desk.InForce.QuestionFor(parameter.Id);
        string stands = asked is null ? "As it stands, a household may not give its own figure here." : $"As it stands, a household may give its own figure here, and is asked: \"{asked}\"";
        (bool allow, string question, string reason) = (asked is not null, asked ?? string.Empty, string.Empty);
        if (draft is not null)
        {
            drafted = new FigureChip("drafted", draft.MayGive ? $"Drafted by {draft.DraftedBy}: a household may give its own" : $"Drafted by {draft.DraftedBy}: a household may no longer give its own");
            (allow, question, reason) = (draft.MayGive, draft.Question ?? question, draft.Reason);
        }

        string? refusal = null;
        if (refused is not null && refused.Id == parameter.Id)
        {
            (allow, question, reason, refusal) = (refused.Allow, refused.Question, refused.Reason, refused.Message);
        }

        return new AskBox("_FiguresAsk", stands, allow, question, reason, refusal) { InForce = asked is not null };
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
