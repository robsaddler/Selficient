using Selficient.Domain.Measures;
using Selficient.Domain.Plans;

namespace Selficient.Application.Catalogue;

/// <summary>What one verification run did.</summary>
/// <param name="Checked">Offers opened.</param>
/// <param name="Landed">Offers whose page named the item.</param>
/// <param name="Hidden">Offers hidden by this run after a second failure.</param>
/// <param name="Walled">Offers whose shop showed a robot wall, which count as neither.</param>
public sealed record VerificationReport(int Checked, int Landed, IReadOnlyList<Offer> Hidden, int Walled);

/// <summary>
/// Opens every shown offer, asks the rule judge, asks the model judge where the rules let a page
/// through, and records what was found. The store moves the offer: a landing keeps it live and
/// refreshes the price seen; two failures running hide it. A robot wall is recorded and changes
/// nothing, because the link is not known to be wrong.
/// </summary>
public sealed class OfferVerifier
{
    private readonly ICatalogueStore _store;
    private readonly IPageReader _reader;
    private readonly IOfferJudge _judge;
    private readonly TimeProvider _clock;

    /// <summary>Creates the verifier over its ports.</summary>
    public OfferVerifier(ICatalogueStore store, IPageReader reader, IOfferJudge judge, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _reader = reader;
        _judge = judge;
        _clock = clock;
    }

    /// <summary>Verifies every shown offer and marks the run.</summary>
    public async Task<VerificationReport> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Offer> offers = await _store.ShownAsync(cancellationToken);
        int landed = 0;
        int walled = 0;
        List<Offer> hidden = [];
        foreach (Offer offer in offers)
        {
            Judgement judgement = await JudgeAsync(offer, cancellationToken);
            OfferCheck check = new(_clock.GetUtcNow(), judgement.Verdict, 0, judgement.Why, judgement.PriceSeen, judgement.JudgedBy);
            await _store.RecordCheckAsync(offer.Id, check, cancellationToken);
            switch (judgement.Verdict)
            {
                case Verdict.Landed:
                    landed++;
                    break;
                case Verdict.RobotWall:
                    walled++;
                    break;
                default:
                    Offer? after = await _store.FindAsync(offer.Id, cancellationToken);
                    if (after is { Status: OfferStatus.Hidden })
                    {
                        hidden.Add(after);
                    }

                    break;
            }
        }

        await _store.MarkVerifiedAsync(_clock.GetUtcNow(), cancellationToken);
        return new VerificationReport(offers.Count, landed, hidden, walled);
    }

    /// <summary>
    /// Opens every proposal that has not been opened yet and records what the judges made of it, giving
    /// it the page's name and any price read. Nothing goes live here: the reviewer decides. Returns how
    /// many were opened.
    /// </summary>
    public async Task<int> JudgeProposalsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Offer> proposals = await _store.ProposedAsync(cancellationToken);
        int opened = 0;
        foreach (Offer proposal in proposals)
        {
            if ((await _store.ChecksAsync(proposal.Id, 1, cancellationToken)).Count > 0)
            {
                continue;
            }

            // A proposal has no name yet, so the judges are asked about the action it was sent for.
            Offer asked = proposal with { Name = ActionTitle(proposal.Action) };
            PageRead page = await _reader.ReadAsync(asked.Link, cancellationToken);
            Judgement judgement = await JudgePageAsync(asked, page, cancellationToken);
            await _store.RecordCheckAsync(proposal.Id, new OfferCheck(_clock.GetUtcNow(), judgement.Verdict, page.Status, judgement.Why, judgement.PriceSeen, judgement.JudgedBy), cancellationToken);
            if (judgement.Verdict == Verdict.Landed)
            {
                await _store.DescribeAsync(proposal.Id, PageName(page, asked.Name), judgement.PriceSeen, DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime), cancellationToken);
            }

            opened++;
        }

        return opened;
    }

    /// <summary>The action's title as the plan says it, or its id as words when the plan does not buy for it.</summary>
    internal static string ActionTitle(ActionId action) =>
        Ai.KitCandidates.All.FirstOrDefault(candidate => candidate.Id == action)?.Title ?? action.Value.Replace('-', ' ');

    /// <summary>The page's title clipped to a pick's length, or the name the judges were given when the page has none.</summary>
    internal static string PageName(PageRead page, string fallback)
    {
        string title = page.Title.Trim();
        if (title.Length == 0)
        {
            return fallback;
        }

        return title.Length > 120 ? title[..120] : title;
    }

    /// <summary>Judges one offer: opens its link, then the rules first, then the model where the rules let the page through and a model is there to ask.</summary>
    public async Task<Judgement> JudgeAsync(Offer offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        PageRead page = await _reader.ReadAsync(offer.Link, cancellationToken);
        return await JudgePageAsync(offer, page, cancellationToken);
    }

    private async Task<Judgement> JudgePageAsync(Offer offer, PageRead page, CancellationToken cancellationToken)
    {
        Judgement byRule = RuleJudge.Judge(offer, page);
        if (byRule.Verdict != Verdict.Landed || !_judge.IsAvailable)
        {
            return byRule;
        }

        Judgement byModel = await _judge.JudgeAsync(offer, page, cancellationToken);
        // The model may read a price the rules cannot; it may also say the page is the wrong item. A
        // model that cannot answer leaves the rules' verdict standing.
        return byModel.Verdict == Verdict.Dead ? byRule : byModel with { PriceSeen = byModel.PriceSeen ?? byRule.PriceSeen };
    }

    /// <summary>Whether a run is due: never run, or run before the last scheduled hour.</summary>
    public static bool IsDue(DateTimeOffset? lastRun, DateTimeOffset now, int hourOfDay)
    {
        if (lastRun is null)
        {
            return true;
        }

        DateTimeOffset dueToday = new(now.Year, now.Month, now.Day, hourOfDay, 0, 0, now.Offset);
        DateTimeOffset lastDue = dueToday <= now ? dueToday : dueToday.AddDays(-1);
        return lastRun.Value < lastDue;
    }

    /// <summary>A price read by a judge that is not a sensible shop price is ignored.</summary>
    public static Money? Sensible(long? pence) => pence is > 0 and < 10_000_00 ? Money.FromPence(pence.Value) : null;
}
