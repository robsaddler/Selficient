using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Selficient.Application.Catalogue;
using Selficient.Application.Operations;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Admin;

/// <summary>
/// The reviewer: every offer with its last check, what it earned, and the operator's three hands
/// (show, hide, verify now). Behind a signed-in operator account with its second factor: the
/// policy decides who may be here, not the page, and every hand is recorded against the name of
/// the person who took it. Twenty minutes on a Sunday evening is the whole operating cost.
/// </summary>
[Authorize(Policy = OperatorAuth.Policy)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class CatalogueModel : PageModel
{
    private readonly ICatalogueStore _store;
    private readonly CatalogueOperations _operations;
    private readonly TimeProvider _clock;
    private readonly IOperationsLog _log;
    private readonly HealthCheckService _health;
    private readonly Selficient.Application.Security.IOperatorStore _operators;

    /// <summary>Creates the page over the store, the catalogue's hands and the session.</summary>
    public CatalogueModel(ICatalogueStore store, CatalogueOperations operations, TimeProvider clock, IOperationsLog log, HealthCheckService health, Selficient.Application.Security.IOperatorStore operators)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(operators);
        _store = store;
        _operations = operations;
        _clock = clock;
        _log = log;
        _health = health;
        _operators = operators;
    }

    /// <summary>The operator reading the page, which every hand is recorded against.</summary>
    public string OperatorName => OperatorAuth.NameOf(User);

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

    /// <summary>Renders the review. Who may see it was settled before this ran.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Page();
    }

    /// <summary>Hides an offer by hand.</summary>
    public Task<IActionResult> OnPostHideAsync(long id, CancellationToken cancellationToken) => HandAsync(id, OfferStatus.Hidden, "hidden", cancellationToken);

    /// <summary>Shows an offer by hand, which also forgives its failures.</summary>
    public Task<IActionResult> OnPostShowAsync(long id, CancellationToken cancellationToken) => HandAsync(id, OfferStatus.Live, "shown", cancellationToken);

    /// <summary>Opens every shown link now rather than waiting for the night.</summary>
    public async Task<IActionResult> OnPostVerifyAsync(CancellationToken cancellationToken)
    {
        VerificationReport report = await _operations.VerifyAsync(cancellationToken);
        Notice = $"Opened {report.Checked}: {report.Landed} landed, {report.Walled} behind a robot wall, {report.Hidden.Count} hidden.";
        await _log.RecordHandAsync("verify", null, Notice, OperatorName, cancellationToken);
        return RedirectToPage();
    }

    private async Task<IActionResult> HandAsync(long id, OfferStatus status, string said, CancellationToken cancellationToken)
    {
        Offer? offer = await _store.FindAsync(id, cancellationToken);
        if (offer is null)
        {
            Notice = "There is no such offer.";
            await _log.RecordHandAsync(said, id, Notice, OperatorName, cancellationToken);
            return RedirectToPage();
        }

        if (status == OfferStatus.Live && offer.Price.Pence == 0)
        {
            Notice = $"{offer.Name} has no price yet, so it cannot be shown. Wait for the checker to read one, or hide it.";
            await _log.RecordHandAsync(said, id, Notice, OperatorName, cancellationToken);
            return RedirectToPage();
        }

        await _store.SetStatusAsync(id, status, cancellationToken);
        await _operations.ReloadAsync(cancellationToken);
        Notice = $"{offer.Name} is {said}.";
        await _log.RecordHandAsync(said, id, Notice, OperatorName, cancellationToken);
        return RedirectToPage();
    }

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
        Hands = await _log.RecentHandsAsync(12, cancellationToken);
        Attempts = await _operators.AttemptsAsync(12, cancellationToken);
        Runs = await _log.RecentRunsAsync(12, cancellationToken);
        HealthReport report = await _health.CheckHealthAsync(cancellationToken);
        Health = report.Entries.Select(static entry => new HealthLine(entry.Key, entry.Value.Status.ToString(), entry.Value.Description ?? string.Empty)).ToList();
    }

    /// <summary>The latest hands, newest first.</summary>
    public IReadOnlyList<OperatorAction> Hands { get; private set; } = [];

    /// <summary>The latest sign-in attempts, newest first, right and wrong.</summary>
    public IReadOnlyList<Selficient.Application.Security.SignInAttempt> Attempts { get; private set; } = [];

    /// <summary>The latest job runs, newest first.</summary>
    public IReadOnlyList<JobRun> Runs { get; private set; } = [];

    /// <summary>What each readiness check says, for the operator alone.</summary>
    public IReadOnlyList<HealthLine> Health { get; private set; } = [];

    /// <summary>One readiness check as the page lists it.</summary>
    public sealed record HealthLine(string Name, string Status, string Description);

    /// <summary>A run as one line: when, the job, how it went, and the detail.</summary>
    public static string RunLine(JobRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        string ended = run.FinishedAt is { } at ? Stamp(at) : "still running";
        return $"{Stamp(run.StartedAt)} {run.Job}: {run.Outcome.ToString().ToLowerInvariant()}, {ended}. {run.Detail}".TrimEnd();
    }

    /// <summary>
    /// A sign-in attempt as one line: when, who was named, how it went, and where from. The
    /// address is here because an operator is staff and a run of refusals from one place is the
    /// thing you need to see; it is never a reader's.
    /// </summary>
    public static string AttemptLine(Selficient.Application.Security.SignInAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        string from = string.IsNullOrWhiteSpace(attempt.Address) ? "no address recorded" : attempt.Address;
        return $"{Stamp(attempt.At)} {attempt.Username}, {attempt.Outcome}, from {from}: {attempt.Detail}";
    }

    /// <summary>A hand as one line: when, which, on what, and what came of it.</summary>
    public static string HandLine(OperatorAction hand)
    {
        ArgumentNullException.ThrowIfNull(hand);
        string target = hand.OfferId is { } id ? $" on offer {id}" : string.Empty;
        // The one shared key could not say who; an account can, and the trail is worth little
        // without it. A hand from before M20 has no name and says so rather than inventing one.
        string who = string.IsNullOrWhiteSpace(hand.OperatorName) ? "nobody recorded" : hand.OperatorName;
        return $"{Stamp(hand.At)} {who}, {hand.Hand}{target}: {hand.Outcome}";
    }

    private static string Stamp(DateTimeOffset at) => at.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.GetCultureInfo("en-GB"));

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
