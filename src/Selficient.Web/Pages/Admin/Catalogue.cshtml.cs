using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Catalogue;

using Selficient.Web.Security;

namespace Selficient.Web.Pages.Admin;

/// <summary>
/// The reviewer: every offer with its last check, what it earned, and the operator's three hands
/// (show, hide, verify now). Behind the operator key; without one configured the page does not
/// exist. Twenty minutes on a Sunday evening is the whole operating cost of the catalogue.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class CatalogueModel : PageModel
{
    private readonly ICatalogueStore _store;
    private readonly CatalogueOperations _operations;
    private readonly OperatorSession _session;
    private readonly TimeProvider _clock;

    /// <summary>Creates the page over the store, the catalogue's hands and the session.</summary>
    public CatalogueModel(ICatalogueStore store, CatalogueOperations operations, OperatorSession session, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _operations = operations;
        _session = session;
        _clock = clock;
    }

    /// <summary>The key, typed once.</summary>
    [BindProperty]
    public string? Key { get; set; }

    /// <summary>Whether the operator is signed in.</summary>
    public bool SignedIn { get; private set; }

    /// <summary>A sentence about what the last hand did.</summary>
    [TempData]
    public string? Notice { get; set; }

    /// <summary>Every offer, with its latest check and its clicks.</summary>
    public IReadOnlyList<OfferRow> Rows { get; private set; } = [];

    /// <summary>When verification last finished, or null.</summary>
    public DateTimeOffset? LastVerified { get; private set; }

    /// <summary>The last verification as the page says it: a local date and time, or "never".</summary>
    public string LastVerifiedText => LastVerifiedSentence(LastVerified);

    /// <summary>The last verification as a phrase for the page.</summary>
    public static string LastVerifiedSentence(DateTimeOffset? at) =>
        at is { } when ? when.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.GetCultureInfo("en-GB")) : "never";

    /// <summary>How many readers' tips wait, as the page says it.</summary>
    public string ProposalsText => ProposalsSentence(Rows.Count(static row => row.Offer.Status == OfferStatus.Proposed));

    /// <summary>The sentence for a count of waiting proposals.</summary>
    public static string ProposalsSentence(int waiting) => waiting switch
    {
        0 => "No suggestions from readers wait for you.",
        1 => "One suggestion from a reader waits for you at the foot of the table: the checker has read it where it could, and Show approves it.",
        _ => string.Create(CultureInfo.InvariantCulture, $"{waiting} suggestions from readers wait for you at the foot of the table: the checker has read them where it could, and Show approves each."),
    };

    /// <summary>Renders the sign-in or the review.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsConfigured)
        {
            return NotFound();
        }

        SignedIn = _session.IsValid(Request.Cookies[OperatorSession.CookieName]);
        if (SignedIn)
        {
            await LoadAsync(cancellationToken);
        }

        return Page();
    }

    /// <summary>Signs in with the key.</summary>
    public IActionResult OnPostSignIn()
    {
        if (!_session.IsConfigured)
        {
            return NotFound();
        }

        if (!_session.Matches(Key))
        {
            Notice = "That is not the operator's key.";
            return RedirectToPage();
        }

        Response.Cookies.Append(OperatorSession.CookieName, _session.Issue(), _session.CookieOptions());
        return RedirectToPage();
    }

    /// <summary>Signs out.</summary>
    public IActionResult OnPostSignOut()
    {
        Response.Cookies.Delete(OperatorSession.CookieName, _session.CookieOptions());
        return RedirectToPage();
    }

    /// <summary>Hides an offer by hand.</summary>
    public Task<IActionResult> OnPostHideAsync(long id, CancellationToken cancellationToken) => HandAsync(id, OfferStatus.Hidden, "hidden", cancellationToken);

    /// <summary>Shows an offer by hand, which also forgives its failures.</summary>
    public Task<IActionResult> OnPostShowAsync(long id, CancellationToken cancellationToken) => HandAsync(id, OfferStatus.Live, "shown", cancellationToken);

    /// <summary>Opens every shown link now rather than waiting for the night.</summary>
    public async Task<IActionResult> OnPostVerifyAsync(CancellationToken cancellationToken)
    {
        if (!Allowed())
        {
            return NotFound();
        }

        VerificationReport report = await _operations.VerifyAsync(cancellationToken);
        Notice = $"Opened {report.Checked}: {report.Landed} landed, {report.Walled} behind a robot wall, {report.Hidden.Count} hidden.";
        return RedirectToPage();
    }

    private async Task<IActionResult> HandAsync(long id, OfferStatus status, string said, CancellationToken cancellationToken)
    {
        if (!Allowed())
        {
            return NotFound();
        }

        Offer? offer = await _store.FindAsync(id, cancellationToken);
        if (offer is null)
        {
            Notice = "There is no such offer.";
            return RedirectToPage();
        }

        if (status == OfferStatus.Live && offer.Price.Pence == 0)
        {
            Notice = $"{offer.Name} has no price yet, so it cannot be shown. Wait for the checker to read one, or hide it.";
            return RedirectToPage();
        }

        await _store.SetStatusAsync(id, status, cancellationToken);
        await _operations.ReloadAsync(cancellationToken);
        Notice = $"{offer.Name} is {said}.";
        return RedirectToPage();
    }

    private bool Allowed() => _session.IsConfigured && _session.IsValid(Request.Cookies[OperatorSession.CookieName]);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Offer> offers = await _store.AllAsync(cancellationToken);
        IReadOnlyDictionary<long, long> clicks = await _store.ClicksAsync(30, DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime), cancellationToken);
        List<OfferRow> rows = [];
        foreach (Offer offer in offers)
        {
            IReadOnlyList<OfferCheck> checks = await _store.ChecksAsync(offer.Id, 1, cancellationToken);
            rows.Add(new OfferRow(offer, checks.Count > 0 ? checks[0] : null, clicks.TryGetValue(offer.Id, out long count) ? count : 0));
        }

        Rows = rows;
        LastVerified = await _store.LastVerifiedAsync(cancellationToken);
    }

    /// <summary>One line of the review, with its phrases decided here rather than in the view.</summary>
    public sealed record OfferRow(Offer Offer, OfferCheck? LastCheck, long Clicks)
    {
        /// <summary>The status, with the failure count when there is one.</summary>
        public string StatusText => Offer.Failures > 0 ? $"{Offer.Status}, {Offer.Failures} failed" : Offer.Status.ToString();

        /// <summary>The last check's verdict, judge and evidence, or that there has been none.</summary>
        public string CheckText => LastCheck is { } check ? $"{check.Verdict} ({check.JudgedBy}): {check.Evidence}" : "Not yet checked.";

        /// <summary>Whether the hand on this row shows it (hidden or proposed) rather than hides it.</summary>
        public bool CanShow => Offer.Status is OfferStatus.Hidden or OfferStatus.Proposed;

        /// <summary>The hand's name, which is both the handler and the button's word.</summary>
        public string Hand => CanShow ? "Show" : "Hide";
    }
}
