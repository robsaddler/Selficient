using Selficient.Application.Ai;

namespace Selficient.Web.Pages.Plan;

/// <summary>One photo moment's copy and wiring.</summary>
/// <param name="Kind">Which moment.</param>
/// <param name="Handler">The page handler name.</param>
/// <param name="Title">The heading.</param>
/// <param name="Hint">How to take the photo.</param>
/// <param name="Verb">The button.</param>
/// <param name="JustAnswered">Whether this moment's answer is on the page.</param>
public sealed record PhotoMomentView(AnalysisKind Kind, string Handler, string Title, string Hint, string Verb, bool JustAnswered)
{
    /// <summary>An id-safe name.</summary>
    public string Id => Kind.ToString().ToLowerInvariant();
}
