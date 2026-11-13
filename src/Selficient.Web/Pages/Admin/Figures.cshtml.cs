using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Figures;
using Selficient.Domain.Parameters;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Admin;

/// <summary>
/// The assumptions desk: every figure the board and the plan work from, laid out as a household
/// meets them, where somebody who knows better corrects one with a reason and a source. A correction
/// goes into one shared draft and changes nothing. The draft is tried on the reference households,
/// and an operator who may publish puts it into force or puts an earlier set back. Who may be here
/// is the policy's business; who may publish is asked of the desk on every post, never of the page.
/// </summary>
[Authorize(Policy = OperatorAuth.Policy)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class FiguresModel : PageModel
{
    /// <summary>The longest search the page reads.</summary>
    public const int MaxQueryLength = 80;

    private const string Work = "work";
    private const string Prices = "prices";
    private const string Draft = "draft";
    private const string History = "history";
    private const int SetsShown = 50;
    private const int ChangesShown = 100;

    private static readonly Dictionary<string, string> Sheets = new(StringComparer.Ordinal)
    {
        [Work] = "Every figure the board and the plan work from, grouped as a household meets them, with the words the board says built from the figures in force. Open a figure to correct it. A correction goes into the draft and changes nothing until it is published.",
        [Prices] = "What each thing on the plan costs, at least and at most, for one of it; the plan multiplies by what a household needs. Open a price to correct it. A correction goes into the draft and changes nothing until it is published.",
    };

    private readonly FigureDesk _desk;
    private readonly IFigureStore _store;
    private RefusedCorrection? _refused;

    /// <summary>Creates the page over the desk and the store it reads the history from.</summary>
    public FiguresModel(FigureDesk desk, IFigureStore store)
    {
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(store);
        _desk = desk;
        _store = store;
    }

    /// <summary>The operator reading the page, which every draft and publication is recorded against.</summary>
    public string OperatorName => OperatorAuth.NameOf(User);

    /// <summary>Which tab was asked for.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    /// <summary>What is being searched for, across both tabs of figures.</summary>
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    /// <summary>The figure whose history was asked for.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Figure { get; set; }

    /// <summary>The published set whose changes were asked for.</summary>
    [BindProperty(SupportsGet = true, Name = "set")]
    public string? Publication { get; set; }

    /// <summary>A sentence about what the last post did.</summary>
    [TempData]
    public string? Notice { get; set; }

    /// <summary>Why the last post was refused, when the refusal belongs to no one figure on the page.</summary>
    public string? Refusal { get; private set; }

    /// <summary>The desk as this operator finds it.</summary>
    public DeskView Desk { get; private set; } = null!;

    /// <summary>The tab in view: the one asked for when there is such a tab, the first otherwise.</summary>
    public string TabId => Tab is Prices or Draft or History ? Tab : Work;

    /// <summary>The search as the page reads it: trimmed and no longer than it will read.</summary>
    public string Search => Clip((Query ?? string.Empty).Trim());

    /// <summary>The view that draws the tab.</summary>
    public string TabView { get; private set; } = "_FiguresSheet";

    /// <summary>The tabs, with the one in view marked.</summary>
    public IReadOnlyList<TabLink> Tabs =>
    [
        new(Work, "How things work", Current(Work)),
        new(Prices, "What things cost", Current(Prices)),
        new(Draft, DraftTabName(Desk.Drafts.Count), Current(Draft)),
        new(History, "History", Current(History)),
    ];

    /// <summary>Which set is in force, who published it and when.</summary>
    public string InForceText => InForceSentence(Desk.Published);

    /// <summary>How much is drafted.</summary>
    public string DraftText => DraftSentence(Desk.Drafts.Count);

    /// <summary>What the sheet says above its cards: what the tab is, or what the search found.</summary>
    public string SheetLede { get; private set; } = string.Empty;

    /// <summary>The figures in view, grouped.</summary>
    public IReadOnlyList<FigureGroup> Groups { get; private set; } = [];

    /// <summary>The drafted corrections, each with whether this operator may take it out.</summary>
    public IReadOnlyList<DraftLine> DraftLines { get; private set; } = [];

    /// <summary>What publishing the draft would change, or null when nothing is drafted or the draft cannot be run.</summary>
    public FigurePreview? Preview { get; private set; }

    /// <summary>How much of what was compared would move.</summary>
    public string PreviewText { get; private set; } = string.Empty;

    /// <summary>The published sets, newest first, and the shipped figures last.</summary>
    public IReadOnlyList<SetLine> Sets { get; private set; } = [];

    /// <summary>The heading over a list of changes.</summary>
    public string ChangesTitle { get; private set; } = string.Empty;

    /// <summary>The sentence under it.</summary>
    public string ChangesLede { get; private set; } = string.Empty;

    /// <summary>The changes to one figure, or in one set.</summary>
    public IReadOnlyList<ChangeLine> Changes { get; private set; } = [];

    /// <summary>Renders the desk. Who may see it was settled before this ran.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Page();
    }

    /// <summary>Drafts a correction to one figure.</summary>
    public async Task<IActionResult> OnPostDraftAsync(string? id, string? value, string? reason, string? source, CancellationToken cancellationToken)
    {
        if (ParameterRegistry.Find(id ?? string.Empty) is not { } parameter)
        {
            Refusal = "That is not one of the figures the plan works from.";
            await LoadAsync(cancellationToken);
            return Page();
        }

        Result<FigureDraft> drafted = decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out decimal number)
            ? await _desk.DraftAsync(OperatorName, parameter.Id, number, reason ?? string.Empty, source ?? string.Empty, cancellationToken)
            : Result.Failure<FigureDraft>(new Fault("figures.number", "Give the new value as a number, such as 2 or 0.5."));
        if (drafted.IsFailure)
        {
            // The figure's own tab is drawn with its box open and what was typed still in it.
            _refused = new RefusedCorrection(parameter.Id, Clip(value ?? string.Empty), reason ?? string.Empty, source ?? string.Empty, drafted.Error.Message);
            Tab = FigureSheet.IsPrice(parameter) ? Prices : Work;
            Query = null;
            await LoadAsync(cancellationToken);
            return Page();
        }

        Notice = $"\"{parameter.Name}\" is drafted as {FigureSheet.Plain(number)}. Nothing changes until the draft is published.";
        return RedirectToPage(null, null, new { tab = TabId, q = Query }, FigureSheet.AnchorOf(parameter.Id));
    }

    /// <summary>Takes a drafted correction out of the draft.</summary>
    public async Task<IActionResult> OnPostDiscardAsync(string? id, CancellationToken cancellationToken)
    {
        Result<FigureDraft> discarded = await _desk.DiscardAsync(OperatorName, id ?? string.Empty, cancellationToken);
        return await AnsweredAsync(discarded.IsSuccess, discarded.IsSuccess ? "The correction is out of the draft." : discarded.Error.Message, Draft, Draft, cancellationToken);
    }

    /// <summary>Puts the draft into force.</summary>
    public async Task<IActionResult> OnPostPublishAsync(string? note, CancellationToken cancellationToken)
    {
        Result<PublishedFigures> published = await _desk.PublishAsync(OperatorName, note ?? string.Empty, cancellationToken);
        return await AnsweredAsync(published.IsSuccess, published.IsSuccess ? InForceNotice(published.Value) : published.Error.Message, History, Draft, cancellationToken);
    }

    /// <summary>Puts an earlier set back into force.</summary>
    public async Task<IActionResult> OnPostRestoreAsync(string? version, string? note, CancellationToken cancellationToken)
    {
        int number = int.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : -1;
        Result<PublishedFigures> restored = await _desk.RestoreAsync(OperatorName, number, note ?? string.Empty, cancellationToken);
        return await AnsweredAsync(restored.IsSuccess, restored.IsSuccess ? InForceNotice(restored.Value) : restored.Error.Message, History, History, cancellationToken);
    }

    /// <summary>The sentence for the set in force.</summary>
    public static string InForceSentence(PublishedFigures? published) => published is null
        ? "The shipped figures are in force. Nothing has been published."
        : string.Create(CultureInfo.InvariantCulture, $"Set {published.Version} is in force, published by {published.By} on {Stamp(published.At)}.");

    /// <summary>The sentence for a count of drafted corrections.</summary>
    public static string DraftSentence(int drafted) => drafted switch
    {
        0 => "Nothing is drafted.",
        1 => "One correction is drafted and waits to be published.",
        _ => string.Create(CultureInfo.InvariantCulture, $"{drafted} corrections are drafted and wait to be published."),
    };

    /// <summary>The draft tab's name, with how much is in it.</summary>
    public static string DraftTabName(int drafted) => drafted == 0 ? "The draft" : string.Create(CultureInfo.InvariantCulture, $"The draft ({drafted})");

    /// <summary>What a search found, as a sentence.</summary>
    public static string FoundSentence(int found, string search) => found switch
    {
        0 => $"No figure matches \"{search}\".",
        1 => $"One figure matches \"{search}\".",
        _ => string.Create(CultureInfo.InvariantCulture, $"{found} figures match \"{search}\", from both tabs."),
    };

    private static string InForceNotice(PublishedFigures set) => string.Create(CultureInfo.InvariantCulture, $"Set {set.Version} is in force. Every plan built from now on is worked out from it.");

    private static string Stamp(DateTimeOffset at) => at.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.GetCultureInfo("en-GB"));

    private static string Clip(string text) => text.Length > MaxQueryLength ? text[..MaxQueryLength] : text;

    private string? Current(string tab) => tab == TabId ? "page" : null;

    // A post that worked goes back to its tab by a redirect, so a reload does not post again; one that was refused is drawn with the sentence.
    private async Task<IActionResult> AnsweredAsync(bool worked, string said, string onward, string back, CancellationToken cancellationToken)
    {
        if (worked)
        {
            Notice = said;
            return RedirectToPage(null, null, new { tab = onward });
        }

        Refusal = said;
        Tab = back;
        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Desk = await _desk.ReadAsync(OperatorName, cancellationToken);
        if (TabId == Draft)
        {
            LoadDraft();
        }
        else if (TabId == History)
        {
            await LoadHistoryAsync(cancellationToken);
        }
        else
        {
            LoadSheet();
        }
    }

    private void LoadSheet()
    {
        string search = Search;
        List<ParameterDefinition> shown = search.Length == 0
            ? [.. ParameterRegistry.All.Where(parameter => FigureSheet.IsPrice(parameter) == (TabId == Prices))]
            : [.. ParameterRegistry.All.Where(parameter => Matches(parameter, search))];
        SheetLede = search.Length == 0 ? Sheets[TabId] : FoundSentence(shown.Count, search);
        Groups = FigureSheet.Build(shown, Desk, _refused);
    }

    private static bool Matches(ParameterDefinition parameter, string search) =>
        parameter.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || parameter.Group.Contains(search, StringComparison.OrdinalIgnoreCase)
        || parameter.Id.Contains(search, StringComparison.OrdinalIgnoreCase);

    private void LoadDraft()
    {
        TabView = "_FiguresDraft";
        DraftLines = [.. Desk.Drafts.Select(draft => DraftLine.Of(draft, Desk, OperatorName))];
        if (Desk.Drafts.Count > 0 && _desk.Preview(Desk) is { } preview)
        {
            Preview = preview;
            PreviewText = string.Create(CultureInfo.InvariantCulture, $"Tried on {ReferenceHouseholds.All.Count} reference households in every scenario, with nothing done and with this week's actions done: {Preview.Lines.Count} of {Preview.Compared} Days Ready figures and {Preview.Costs.Count} of {ReferenceHouseholds.All.Count} plan costs would move.");
        }
    }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken)
    {
        if (Figure is { Length: > 0 } figure)
        {
            TabView = "_FiguresChanges";
            ParameterDefinition? parameter = ParameterRegistry.Find(figure);
            ChangesTitle = parameter?.Name ?? "Not a figure";
            ChangesLede = parameter is null
                ? "That is not one of the figures the plan works from."
                : $"Shipped as {FigureSheet.Plain(parameter.Default)} {parameter.Unit}; {FigureSheet.Plain(Desk.InForce[parameter.Id])} is in force. Every publication that changed it is below, newest first. {parameter.Shown}.";
            Changes = parameter is null ? [] : [.. (await _store.ChangesAsync(parameter.Id, ChangesShown, cancellationToken)).Select(ChangeLine.InHistory)];
        }
        else if (int.TryParse(Publication, NumberStyles.None, CultureInfo.InvariantCulture, out int version))
        {
            TabView = "_FiguresChanges";
            ChangesTitle = version == 0 ? "The shipped figures" : string.Create(CultureInfo.InvariantCulture, $"Set {version}");
            Changes = [.. (await _store.ChangesInAsync(version, cancellationToken)).Select(ChangeLine.InSet)];
            ChangesLede = Changes.Count == 0
                ? "No publication with that number changed a figure. The shipped figures are the values in the code, and no publication made them."
                : string.Create(CultureInfo.InvariantCulture, $"Every figure this publication changed: {Changes.Count} in all.");
        }
        else
        {
            TabView = "_FiguresSets";
            IReadOnlyList<PublishedFigures> sets = await _store.HistoryAsync(SetsShown, cancellationToken);
            Sets = [.. sets.Select(set => SetLine.Of(set, Desk)), SetLine.Shipped(Desk)];
        }
    }

    /// <summary>A tab as the page links to it.</summary>
    /// <param name="Id">What the address calls it.</param>
    /// <param name="Name">What the page calls it.</param>
    /// <param name="Current">"page" on the tab in view, null on the others.</param>
    public sealed record TabLink(string Id, string Name, string? Current);

    /// <summary>A drafted correction as the draft tab lists it.</summary>
    /// <param name="Draft">The correction.</param>
    /// <param name="Name">The figure's name, or its id when a release has since removed it.</param>
    /// <param name="Standing">The value in force and the value drafted.</param>
    /// <param name="By">Who drafted it and when.</param>
    /// <param name="CanDiscard">Whether this operator may take it out of the draft.</param>
    public sealed record DraftLine(FigureDraft Draft, string Name, string Standing, string By, bool CanDiscard)
    {
        /// <summary>The view that draws the hand, or what stands in its place.</summary>
        public string HandView => CanDiscard ? "_FiguresDiscard" : "_FiguresNotYours";

        /// <summary>The line for a draft, as one operator sees it.</summary>
        public static DraftLine Of(FigureDraft draft, DeskView desk, string operatorName)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(desk);
            ParameterDefinition? parameter = ParameterRegistry.Find(draft.ParameterId);
            string standing = parameter is null
                ? "This is no longer a figure the plan works from. Take it out of the draft."
                : $"{FigureSheet.Plain(desk.InForce[parameter.Id])} {parameter.Unit} in force; {FigureSheet.Plain(draft.Value)} drafted.";
            return new DraftLine(draft, parameter?.Name ?? draft.ParameterId, standing, $"Drafted by {draft.DraftedBy} on {Stamp(draft.DraftedAt)}.", desk.MayPublish || string.Equals(draft.DraftedBy, operatorName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>A published set as the history lists it.</summary>
    /// <param name="Version">Its number; nought for the shipped figures.</param>
    /// <param name="Title">What the page calls it.</param>
    /// <param name="Detail">Who published it, when and why.</param>
    /// <param name="Note">What the publisher said the publication was for.</param>
    /// <param name="State">"in-force" on the set in force, "earlier" on the others.</param>
    /// <param name="CanPutBack">Whether this operator may put it back into force.</param>
    public sealed record SetLine(int Version, string Title, string Detail, string Note, string State, bool CanPutBack)
    {
        /// <summary>The view that draws the hand, or what stands in its place.</summary>
        public string HandView => CanPutBack ? "_FiguresPutBack" : "_FiguresStanding";

        /// <summary>Why the set is not offered to be put back: it is the one in force, or this operator may not publish.</summary>
        public string Standing => State == "in-force" ? "This is the set in force." : "Only an operator who may publish figures can put a set back.";

        /// <summary>The line for a published set.</summary>
        public static SetLine Of(PublishedFigures set, DeskView desk)
        {
            ArgumentNullException.ThrowIfNull(set);
            ArgumentNullException.ThrowIfNull(desk);
            string restores = set.Restores is { } earlier ? Restored(earlier) : string.Empty;
            string detail = string.Create(CultureInfo.InvariantCulture, $"Published by {set.By} on {Stamp(set.At)}, with {Differing(set.Corrections.Count)}.{restores}");
            return Line(set.Version, string.Create(CultureInfo.InvariantCulture, $"Set {set.Version}"), detail, set.Note, desk);
        }

        /// <summary>The line for the shipped figures, which are set nought.</summary>
        public static SetLine Shipped(DeskView desk)
        {
            ArgumentNullException.ThrowIfNull(desk);
            return Line(0, "The shipped figures", "The values in the code, which the product works from until something is published.", string.Empty, desk);
        }

        private static string Differing(int corrections) => corrections switch
        {
            0 => "every figure as it shipped",
            1 => "one figure differing from the shipped ones",
            _ => string.Create(CultureInfo.InvariantCulture, $"{corrections} figures differing from the shipped ones"),
        };

        private static string Restored(int earlier) => earlier == 0
            ? " It put the shipped figures back."
            : string.Create(CultureInfo.InvariantCulture, $" It put set {earlier} back.");

        private static SetLine Line(int version, string title, string detail, string note, DeskView desk)
        {
            bool inForce = desk.InForce.Version == version;
            return new SetLine(version, title, detail, note, inForce ? "in-force" : "earlier", desk.MayPublish && !inForce);
        }
    }

    /// <summary>One figure changing in one publication, as a line.</summary>
    /// <param name="Head">What the line is about: the set and its date in a figure's history, the figure in a set's changes.</param>
    /// <param name="Moved">What it was and what it became.</param>
    /// <param name="Why">The reason given.</param>
    /// <param name="Source">The source given.</param>
    /// <param name="By">Who drafted it and when.</param>
    public sealed record ChangeLine(string Head, string Moved, string Why, string Source, string By)
    {
        /// <summary>A change as a line of one figure's history.</summary>
        public static ChangeLine InHistory(FigureChange change)
        {
            ArgumentNullException.ThrowIfNull(change);
            return Line(string.Create(CultureInfo.InvariantCulture, $"Set {change.Version}"), change);
        }

        /// <summary>A change as a line of what one set changed.</summary>
        public static ChangeLine InSet(FigureChange change)
        {
            ArgumentNullException.ThrowIfNull(change);
            return Line(ParameterRegistry.Find(change.ParameterId)?.Name ?? change.ParameterId, change);
        }

        private static ChangeLine Line(string head, FigureChange change) =>
            new(head, $"{FigureSheet.Plain(change.Before)} to {FigureSheet.Plain(change.After)}", change.Reason, change.Source, $"Drafted by {change.DraftedBy} on {Stamp(change.DraftedAt)}.");
    }
}
