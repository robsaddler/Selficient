namespace Selficient.Application.Catalogue;

/// <summary>
/// The catalogue's hands, for the worker on its clock and the reviewer at the page: reload what the
/// pages read, verify every shown offer, and say whether the night's run is due.
/// </summary>
public sealed class CatalogueOperations
{
    private readonly StoredCatalogue _catalogue;
    private readonly OfferVerifier _verifier;
    private readonly ICatalogueStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Creates the operations over the catalogue, the verifier and the store.</summary>
    public CatalogueOperations(StoredCatalogue catalogue, OfferVerifier verifier, ICatalogueStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        _catalogue = catalogue;
        _verifier = verifier;
        _store = store;
        _clock = clock;
    }

    /// <summary>Seeds the store if empty and takes the snapshot the pages read.</summary>
    public Task ReloadAsync(CancellationToken cancellationToken) => _catalogue.ReloadAsync(cancellationToken);

    /// <summary>Opens every shown offer now and reloads the catalogue after.</summary>
    public async Task<VerificationReport> VerifyAsync(CancellationToken cancellationToken)
    {
        VerificationReport report = await _verifier.RunAsync(cancellationToken);
        await _catalogue.ReloadAsync(cancellationToken);
        return report;
    }

    /// <summary>Opens every proposal not yet opened and records the judges' view; how many were opened.</summary>
    public Task<int> JudgeProposalsAsync(CancellationToken cancellationToken) => _verifier.JudgeProposalsAsync(cancellationToken);

    /// <summary>Verifies only if the night's run has not happened since the hour it is due; null when it has.</summary>
    public async Task<VerificationReport?> VerifyIfDueAsync(int hourOfDay, CancellationToken cancellationToken)
    {
        DateTimeOffset? last = await _store.LastVerifiedAsync(cancellationToken);
        return OfferVerifier.IsDue(last, _clock.GetLocalNow(), hourOfDay) ? await VerifyAsync(cancellationToken) : null;
    }
}
