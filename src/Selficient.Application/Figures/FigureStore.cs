namespace Selficient.Application.Figures;

/// <summary>A set of corrections as it was published: its number, who published it and why, and every figure that differs from the shipped one.</summary>
/// <param name="Version">The set's number, from one; each publication takes the next.</param>
/// <param name="At">When it was published.</param>
/// <param name="By">The operator who published it.</param>
/// <param name="Note">What the publication was for, in the operator's words.</param>
/// <param name="Restores">The earlier set this one puts back, or null when it is new work.</param>
/// <param name="Corrections">Every figure that differs from the shipped one, by id.</param>
public sealed record PublishedFigures(int Version, DateTimeOffset At, string By, string Note, int? Restores, IReadOnlyDictionary<string, decimal> Corrections)
{
    /// <summary>Where the set differs from the product as shipped in which figures a household may give for itself: the question to ask, or null where one is switched off.</summary>
    public IReadOnlyDictionary<string, string?> Asks { get; init; } = new Dictionary<string, string?>();
}

/// <summary>What a draft or a change is about.</summary>
public enum FigureDraftKind
{
    /// <summary>The figure's value.</summary>
    Value,

    /// <summary>Whether a household may give its own figure here, and what it is asked.</summary>
    Ask,
}

/// <summary>A correction somebody has drafted and nobody has published: the figure, the value, and why and on what authority.</summary>
/// <param name="ParameterId">The figure.</param>
/// <param name="Value">The value proposed.</param>
/// <param name="Reason">Why it should change.</param>
/// <param name="Source">Where the value comes from.</param>
/// <param name="DraftedBy">The operator who drafted it.</param>
/// <param name="DraftedAt">When.</param>
public sealed record FigureDraft(string ParameterId, decimal Value, string Reason, string Source, string DraftedBy, DateTimeOffset DraftedAt)
{
    /// <summary>What is drafted: the value, or whether a household may give its own. For the second, the value is one for "it may" and nought for "it may not".</summary>
    public FigureDraftKind Kind { get; init; }

    /// <summary>What a household would be asked, where the draft switches its own figure on.</summary>
    public string? Question { get; init; }

    /// <summary>Whether the draft says a household may give its own figure.</summary>
    public bool MayGive => Value == 1m;
}

/// <summary>One figure changing in one publication, kept for ever: what it was, what it became, why, on what authority, and who drafted it.</summary>
/// <param name="Version">The publication it changed in.</param>
/// <param name="ParameterId">The figure.</param>
/// <param name="Before">The value in force before.</param>
/// <param name="After">The value in force after.</param>
/// <param name="Reason">Why it changed.</param>
/// <param name="Source">Where the value came from.</param>
/// <param name="DraftedBy">The operator who drafted the change.</param>
/// <param name="DraftedAt">When it was drafted.</param>
public sealed record FigureChange(int Version, string ParameterId, decimal Before, decimal After, string Reason, string Source, string DraftedBy, DateTimeOffset DraftedAt)
{
    /// <summary>What changed: the value, or whether a household may give its own, where one is "it may" and nought "it may not".</summary>
    public FigureDraftKind Kind { get; init; }

    /// <summary>What a household is asked after the change, where it may give its own.</summary>
    public string? Question { get; init; }
}

/// <summary>
/// Where published sets of figures, the draft in hand and the record of every change are kept. Sets
/// are only ever added: putting an old set back publishes a new one with the old one's corrections,
/// so the history is what happened and not what somebody wishes had.
/// </summary>
public interface IFigureStore
{
    /// <summary>The set in force, or null when nothing has been published and the shipped figures stand.</summary>
    Task<PublishedFigures?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>One published set, or null.</summary>
    Task<PublishedFigures?> FindAsync(int version, CancellationToken cancellationToken);

    /// <summary>The latest published sets, newest first.</summary>
    Task<IReadOnlyList<PublishedFigures>> HistoryAsync(int take, CancellationToken cancellationToken);

    /// <summary>The latest changes to one figure, newest first.</summary>
    Task<IReadOnlyList<FigureChange>> ChangesAsync(string parameterId, int take, CancellationToken cancellationToken);

    /// <summary>Every figure one publication changed, by figure.</summary>
    Task<IReadOnlyList<FigureChange>> ChangesInAsync(int version, CancellationToken cancellationToken);

    /// <summary>Every drafted correction, by figure.</summary>
    Task<IReadOnlyList<FigureDraft>> DraftsAsync(CancellationToken cancellationToken);

    /// <summary>Keeps a draft, replacing any earlier draft of the same kind for the same figure.</summary>
    Task SaveDraftAsync(FigureDraft draft, CancellationToken cancellationToken);

    /// <summary>Throws a draft away; false when there was none of that kind for that figure.</summary>
    Task<bool> DiscardDraftAsync(string parameterId, FigureDraftKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a set with the changes that make it, in one transaction, emptying the draft when
    /// asked. False, and nothing written, when a set with that number already exists: somebody else
    /// published first.
    /// </summary>
    Task<bool> PublishAsync(PublishedFigures published, IReadOnlyList<FigureChange> changes, bool clearDrafts, CancellationToken cancellationToken);

    /// <summary>Whether this operator may publish. Until somebody is named as a publisher, the first operator account made is the one who may.</summary>
    Task<bool> MayPublishAsync(string operatorName, CancellationToken cancellationToken);
}
