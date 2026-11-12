using System.Globalization;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Accounts;

/// <summary>A passkey as an account keeps it. Everything here is public; the private key never leaves the device.</summary>
/// <param name="Id">The row's number, which the panel names to remove it.</param>
/// <param name="AccountId">Whose it is.</param>
/// <param name="CredentialId">The id the authenticator gave the credential.</param>
/// <param name="PublicKey">The credential's public key, as the authenticator encoded it.</param>
/// <param name="SignCount">The authenticator's signature counter at its last use. Nought for one that keeps none.</param>
/// <param name="Label">The kind of device it was made on, chosen by the server from a fixed list.</param>
/// <param name="CreatedAt">When it was added.</param>
/// <param name="LastUsedAt">When it last signed in, or nothing.</param>
public sealed record Passkey(long Id, long AccountId, byte[] CredentialId, byte[] PublicKey, long SignCount, string Label, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>A credential a registration ceremony has verified, ready to be kept.</summary>
public sealed record VerifiedCredential(byte[] CredentialId, byte[] PublicKey, long SignCount);

/// <summary>An assertion a sign-in ceremony has verified: which credential, and where its counter now stands.</summary>
public sealed record VerifiedAssertion(byte[] CredentialId, long SignCount);

/// <summary>Where passkeys live.</summary>
public interface IPasskeyStore
{
    /// <summary>An account's passkeys, oldest first.</summary>
    Task<IReadOnlyList<Passkey>> ForAccountAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>The passkey with this credential id, whoever's it is, or nothing.</summary>
    Task<Passkey?> FindAsync(byte[] credentialId, CancellationToken cancellationToken);

    /// <summary>Keeps a new passkey and answers its number. Nothing when the credential is already kept, for any account.</summary>
    Task<long?> AddAsync(long accountId, VerifiedCredential credential, string label, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Notes a sign-in: the counter and the time.</summary>
    Task UsedAsync(long id, long signCount, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Removes one of an account's own passkeys. False when it is not that account's, or not there.</summary>
    Task<bool> RemoveAsync(long accountId, long id, CancellationToken cancellationToken);
}

/// <summary>
/// The WebAuthn ceremonies themselves: making the options a browser is given and verifying what
/// its authenticator answers. A port, because the library that does it is infrastructure. Options
/// travel as the JSON the browser's own API takes, and the same JSON is what a ceremony in
/// progress is remembered by.
/// </summary>
public interface IPasskeyCeremony
{
    /// <summary>Options for making a credential for an account, excluding the ones it already has.</summary>
    string RegistrationOptions(long accountId, string email, IReadOnlyList<byte[]> existing);

    /// <summary>Verifies a registration against the options it answers. Nothing when it does not hold.</summary>
    Task<VerifiedCredential?> VerifyRegistrationAsync(string optionsJson, string responseJson, CancellationToken cancellationToken);

    /// <summary>Options for signing in with whichever passkey the device holds. The same for everyone.</summary>
    string SignInOptions();

    /// <summary>The credential id an assertion claims to be from, or nothing when the response is not one.</summary>
    byte[]? CredentialOf(string responseJson);

    /// <summary>Verifies an assertion against the options it answers and the passkey it names. Nothing when it does not hold.</summary>
    Task<VerifiedAssertion?> VerifySignInAsync(string optionsJson, string responseJson, Passkey passkey, CancellationToken cancellationToken);
}

/// <summary>
/// Passkeys for a household account: adding one, signing in with one, listing and removing them.
/// The emailed code is untouched and always works, so a passkey is never the only way in. The
/// threat model is section 6 of <c>docs/threat-models/household-accounts.md</c>.
/// </summary>
public sealed class Passkeys
{
    /// <summary>How many passkeys one account may hold.</summary>
    public static int MostPerAccount => 10;

    /// <summary>The one sentence every refused registration says.</summary>
    public static Fault NotAdded => new("passkey.not-added", "That passkey could not be added. Try again, or go on signing in with a code.");

    /// <summary>The fault when an account already has as many as it may.</summary>
    public static Fault TooMany => new("passkey.too-many", "This account already has ten passkeys. Remove one you no longer use and try again.");

    /// <summary>The one sentence every refused sign-in says, so the page tells nobody why.</summary>
    public static Fault NotSignedIn => new("passkey.not-signed-in", "That passkey did not sign you in. Try again, or sign in with a code sent to your email address.");

    private readonly IPasskeyStore _store;
    private readonly IAccountStore _accounts;
    private readonly IPasskeyCeremony _ceremony;
    private readonly TimeProvider _clock;

    /// <summary>Creates the use case.</summary>
    public Passkeys(IPasskeyStore store, IAccountStore accounts, IPasskeyCeremony ceremony, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(ceremony);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _accounts = accounts;
        _ceremony = ceremony;
        _clock = clock;
    }

    /// <summary>An account's passkeys, oldest first.</summary>
    public Task<IReadOnlyList<Passkey>> ListAsync(long accountId, CancellationToken cancellationToken) => _store.ForAccountAsync(accountId, cancellationToken);

    /// <summary>Begins adding a passkey: the options the browser hands its authenticator, which name the ones the account already has so that a second is not made for the same device.</summary>
    public async Task<Result<string>> BeginAddingAsync(long accountId, string email, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        IReadOnlyList<Passkey> existing = await _store.ForAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        return existing.Count >= MostPerAccount
            ? Result.Failure<string>(TooMany)
            : Result.Success(_ceremony.RegistrationOptions(accountId, email, [.. existing.Select(static p => p.CredentialId)]));
    }

    /// <summary>Finishes adding a passkey: the authenticator's answer is verified against the options this browser was given, and the credential kept for this account.</summary>
    public async Task<Result<Passkey>> FinishAddingAsync(long accountId, string? optionsJson, string? responseJson, string label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (string.IsNullOrEmpty(optionsJson) || string.IsNullOrEmpty(responseJson))
        {
            return Result.Failure<Passkey>(NotAdded);
        }

        VerifiedCredential? credential = await _ceremony.VerifyRegistrationAsync(optionsJson, responseJson, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return Result.Failure<Passkey>(NotAdded);
        }

        IReadOnlyList<Passkey> existing = await _store.ForAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (existing.Count >= MostPerAccount)
        {
            return Result.Failure<Passkey>(TooMany);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        long? id = await _store.AddAsync(accountId, credential, label, now, cancellationToken).ConfigureAwait(false);
        return id is { } added
            ? Result.Success(new Passkey(added, accountId, credential.CredentialId, credential.PublicKey, credential.SignCount, label, now, null))
            : Result.Failure<Passkey>(NotAdded);
    }

    /// <summary>Begins a sign-in: options that are the same for everyone, because the credential is found by the device.</summary>
    public string BeginSigningIn() => _ceremony.SignInOptions();

    /// <summary>Finishes a sign-in: the assertion is verified against the options this browser was given and the passkey it names, and the account answered.</summary>
    public async Task<Result<HouseholdAccount>> FinishSigningInAsync(string? optionsJson, string? responseJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(optionsJson) || string.IsNullOrEmpty(responseJson) || _ceremony.CredentialOf(responseJson) is not { } credentialId)
        {
            return Result.Failure<HouseholdAccount>(NotSignedIn);
        }

        Passkey? passkey = await _store.FindAsync(credentialId, cancellationToken).ConfigureAwait(false);
        if (passkey is null)
        {
            return Result.Failure<HouseholdAccount>(NotSignedIn);
        }

        VerifiedAssertion? assertion = await _ceremony.VerifySignInAsync(optionsJson, responseJson, passkey, cancellationToken).ConfigureAwait(false);
        HouseholdAccount? account = assertion is null ? null : await _accounts.FindAsync(passkey.AccountId, cancellationToken).ConfigureAwait(false);
        if (assertion is null || account is null)
        {
            return Result.Failure<HouseholdAccount>(NotSignedIn);
        }

        await _store.UsedAsync(passkey.Id, assertion.SignCount, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return Result.Success(account);
    }

    /// <summary>Removes one of an account's own passkeys.</summary>
    public Task<bool> RemoveAsync(long accountId, long id, CancellationToken cancellationToken) => _store.RemoveAsync(accountId, id, cancellationToken);

    /// <summary>A passkey as the panel says it: what it is, when it was added and when it last signed in.</summary>
    public static string Describe(Passkey passkey)
    {
        ArgumentNullException.ThrowIfNull(passkey);
        CultureInfo british = CultureInfo.GetCultureInfo("en-GB");
        string used = passkey.LastUsedAt is { } at ? $"last used {at.ToString("d MMMM yyyy", british)}" : "not used yet";
        return $"{passkey.Label}, added {passkey.CreatedAt.ToString("d MMMM yyyy", british)}, {used}.";
    }
}
