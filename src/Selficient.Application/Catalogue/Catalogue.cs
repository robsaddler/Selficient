using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;

namespace Selficient.Application.Catalogue;

/// <summary>Where an offer stands.</summary>
public enum OfferStatus
{
    /// <summary>Shown on the plan.</summary>
    Live,

    /// <summary>Shown, but its last check failed; one more and it hides.</summary>
    Failing,

    /// <summary>Not shown: failed twice running, or hidden by hand.</summary>
    Hidden,

    /// <summary>Proposed by a reader or the discoverer; not shown until approved.</summary>
    Proposed,
}

/// <summary>What a check found when it opened an offer's link.</summary>
public enum Verdict
{
    /// <summary>The page answered and names the item.</summary>
    Landed,

    /// <summary>The shop said it found nothing.</summary>
    NoResults,

    /// <summary>The page answered but nothing on it is the item.</summary>
    WrongItem,

    /// <summary>The shop showed its robot page; the link is not known to be wrong.</summary>
    RobotWall,

    /// <summary>No answer, or an error status.</summary>
    Dead,
}

/// <summary>One thing for sale for one action: a pick with a home in the store.</summary>
public sealed record Offer(long Id, ActionId Action, string Name, string Retailer, Money Price, DateOnly Seen, Uri Link, string Note, OfferStatus Status, int Failures)
{
    /// <summary>The pick the page draws, carrying the offer's id so its link goes through the counter.</summary>
    public KitPick ToPick() => KitPick.Create(Action.Value, Name, Retailer, Price.Pence / 100m, Seen, Link, Note).Value.WithOffer(Id);
}

/// <summary>One opening of an offer's link.</summary>
public sealed record OfferCheck(DateTimeOffset At, Verdict Verdict, int Status, string Evidence, Money? PriceSeen, string JudgedBy);

/// <summary>What a page said when it was read.</summary>
public sealed record PageRead(int Status, string Title, string Text, Uri Final);

/// <summary>What the judge concluded about a page for an offer.</summary>
public sealed record Judgement(Verdict Verdict, string Why, Money? PriceSeen, string JudgedBy);

/// <summary>The lowest and highest price the checks read for an offer over a window.</summary>
public sealed record PriceSpan(Money Low, Money High);

/// <summary>Which shops a link may point to: the catalogue's allowlist, so a reader's tip can never send the checker anywhere else.</summary>
public interface IAllowedShops
{
    /// <summary>Whether the link's host is one of the shops.</summary>
    bool Allows(Uri link);
}

/// <summary>The catalogue store: offers, their checks, and the clicks they earn.</summary>
public interface ICatalogueStore
{
    /// <summary>How many offers the store holds, of any status.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Puts the shipped picks in as live offers, once, when the store is empty.</summary>
    Task SeedAsync(IReadOnlyList<KitPick> picks, CancellationToken cancellationToken);

    /// <summary>Every offer a household may see, cheapest first within each action.</summary>
    Task<IReadOnlyList<Offer>> ShownAsync(CancellationToken cancellationToken);

    /// <summary>Every offer, of any status, for the reviewer.</summary>
    Task<IReadOnlyList<Offer>> AllAsync(CancellationToken cancellationToken);

    /// <summary>One offer, or null.</summary>
    Task<Offer?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>Adds the counted clicks to each offer's day. No person, no address.</summary>
    Task RecordClicksAsync(IReadOnlyList<ClickBatch> batches, CancellationToken cancellationToken);

    /// <summary>Clicks per offer over the last days, for the reviewer.</summary>
    Task<IReadOnlyDictionary<long, long>> ClicksAsync(int days, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Records a check and moves the offer: a landing makes it live again and refreshes what was seen; two failures running hide it.</summary>
    Task RecordCheckAsync(long id, OfferCheck check, CancellationToken cancellationToken);

    /// <summary>The latest checks on an offer, newest first.</summary>
    Task<IReadOnlyList<OfferCheck>> ChecksAsync(long id, int take, CancellationToken cancellationToken);

    /// <summary>The reviewer's hand: show, hide, approve.</summary>
    Task SetStatusAsync(long id, OfferStatus status, CancellationToken cancellationToken);

    /// <summary>When the last verification run finished, or null if never.</summary>
    Task<DateTimeOffset?> LastVerifiedAsync(CancellationToken cancellationToken);

    /// <summary>Marks a verification run finished.</summary>
    Task MarkVerifiedAsync(DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Puts a reader's link in as a proposed offer with no name or price yet; null when the link is already in the catalogue.</summary>
    Task<long?> ProposeAsync(ActionId action, Uri link, string retailer, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Every proposed offer, oldest first.</summary>
    Task<IReadOnlyList<Offer>> ProposedAsync(CancellationToken cancellationToken);

    /// <summary>Gives a proposed offer the name the page had and, when one was read, its price.</summary>
    Task DescribeAsync(long id, string name, Money? price, DateOnly seen, CancellationToken cancellationToken);

    /// <summary>The lowest and highest price a check read for each offer over the last days.</summary>
    Task<IReadOnlyDictionary<long, PriceSpan>> PriceSpansAsync(int days, DateOnly today, CancellationToken cancellationToken);
}

/// <summary>Opens a page and returns what it said.</summary>
public interface IPageReader
{
    /// <summary>Reads the page; a page that cannot be reached is a status of zero, never an exception.</summary>
    Task<PageRead> ReadAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>Decides whether a page is the offer's item.</summary>
public interface IOfferJudge
{
    /// <summary>Whether the judge can be asked at all (a model needs a key).</summary>
    bool IsAvailable { get; }

    /// <summary>Judges the page for the offer.</summary>
    Task<Judgement> JudgeAsync(Offer offer, PageRead page, CancellationToken cancellationToken);
}
