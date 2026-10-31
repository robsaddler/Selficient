using Selficient.Application.Ai;
using Selficient.Domain.Plans;

namespace Selficient.Application.Catalogue;

/// <summary>What became of a reader's tip.</summary>
public enum TipOutcome
{
    /// <summary>Kept as a proposal for the night's judge and the reviewer.</summary>
    Taken,

    /// <summary>The action is not one the plan buys for.</summary>
    NotAnAction,

    /// <summary>Not an https web address we could open.</summary>
    NotALink,

    /// <summary>A shop the catalogue does not link to.</summary>
    NotAShop,

    /// <summary>The link is already in the catalogue.</summary>
    AlreadyListed,

    /// <summary>Too many tips wait to be read.</summary>
    Full,
}

/// <summary>
/// "Found it cheaper?" A reader pastes a shop's link under an action; nothing else is asked, nothing
/// about them is kept. The link must be https to a shop on the allowlist, for an action the plan
/// buys for, and not already known. It goes in as a proposal: the worker opens and judges it, the
/// reviewer decides. Nothing a reader sends is ever shown to another household without a person.
/// </summary>
public sealed class TipBox
{
    /// <summary>Proposals waiting at once before new tips are declined, which bounds the judge's work.</summary>
    public const int MaxPending = 50;

    /// <summary>Longest link accepted.</summary>
    public const int MaxLinkLength = 500;

    private readonly ICatalogueStore _store;
    private readonly IAllowedShops _shops;
    private readonly TimeProvider _clock;

    /// <summary>Creates the box over the store and the allowlist.</summary>
    public TipBox(ICatalogueStore store, IAllowedShops shops, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(shops);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _shops = shops;
        _clock = clock;
    }

    /// <summary>Takes a tip, or says why not.</summary>
    public async Task<TipOutcome> SuggestAsync(string? actionId, string? url, CancellationToken cancellationToken)
    {
        if (!ActionId.IsValid(actionId) || !KitCandidates.All.Any(candidate => candidate.Id.Value == actionId))
        {
            return TipOutcome.NotAnAction;
        }

        if (!TryParseLink(url, out Uri? link))
        {
            return TipOutcome.NotALink;
        }

        if (!_shops.Allows(link))
        {
            return TipOutcome.NotAShop;
        }

        IReadOnlyList<Offer> pending = await _store.ProposedAsync(cancellationToken);
        if (pending.Count >= MaxPending)
        {
            return TipOutcome.Full;
        }

        DateOnly today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        long? id = await _store.ProposeAsync(ActionId.From(actionId!), link, Retailer(link), today, cancellationToken);
        return id is null ? TipOutcome.AlreadyListed : TipOutcome.Taken;
    }

    /// <summary>An absolute https address with nobody's name in it and no fragment, or nothing.</summary>
    public static bool TryParseLink(string? url, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? link)
    {
        link = null;
        string? trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLinkLength || !Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps || parsed.UserInfo.Length > 0)
        {
            return false;
        }

        link = new UriBuilder(parsed) { Fragment = string.Empty }.Uri;
        return true;
    }

    /// <summary>The shop as its host reads: "amazon.co.uk", "argos.co.uk".</summary>
    public static string Retailer(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        string host = link.Host;
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}

/// <summary>The sentence for each outcome, in the interface's voice.</summary>
public static class TipCopy
{
    private static readonly Dictionary<TipOutcome, string> Sentences = new()
    {
        [TipOutcome.Taken] = "Thank you. We will open it tonight and, if it is the item at a better price, it joins the list.",
        [TipOutcome.NotAnAction] = "That is not an action on the plan, so we have nothing to compare it with.",
        [TipOutcome.NotALink] = "That is not a web address we can open. Paste the link from the shop's address bar, starting https.",
        [TipOutcome.NotAShop] = "That shop is not one we link to yet, so we cannot open it. The shops we do link to are on every pick.",
        [TipOutcome.AlreadyListed] = "That link is already in the catalogue.",
        [TipOutcome.Full] = "Thank you. We have as many suggestions as we can read this week; try again in a few days.",
    };

    /// <summary>The sentence.</summary>
    public static string Sentence(TipOutcome outcome) => Sentences[outcome];
}
