using System.Text;
using System.Text.Json;

namespace Selficient.Application.Security;

/// <summary>
/// Signing an operator in, in the two steps it takes: the password, then the authenticator's code
/// or a recovery code. Every refusal says the same sentence and costs the same work, so the page
/// tells nobody which usernames exist. Nothing here writes a password or a code anywhere.
/// </summary>
public sealed class OperatorSignIn
{
    /// <summary>The one sentence every refusal says.</summary>
    public const string RefusedSentence = "That username and password do not match an operator.";

    /// <summary>The sentence a locked account gets.</summary>
    public const string LockedSentence = "That account is locked for a while after too many wrong passwords. Try again later.";

    /// <summary>The sentence a code that is wrong, stale or already used gets.</summary>
    public const string CodeRefusedSentence = "That code is not right, or it has already been used. Codes last thirty seconds.";

    private readonly IOperatorStore _store;
    private readonly ISecretProtector _protector;
    private readonly TimeProvider _clock;

    /// <summary>Creates the sign-in.</summary>
    public OperatorSignIn(IOperatorStore store, ISecretProtector protector, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _protector = protector;
        _clock = clock;
    }

    /// <summary>The first step: the username and the password.</summary>
    public async Task<OperatorSignInResult> PasswordAsync(string? username, string? password, SignInOrigin origin, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        string name = (username ?? string.Empty).Trim();
        OperatorAccount? account = name.Length == 0 ? null : await _store.FindAsync(name, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            // The same work as a real verification, so an unknown name and a wrong password are
            // indistinguishable from the outside.
            PasswordHasher.SpendTheSameTime(password);
            await RecordAsync(name, "refused", "No operator of that name, or the password was wrong.", now, origin, cancellationToken).ConfigureAwait(false);
            return new OperatorSignInResult(SignInOutcome.Refused, null);
        }

        if (!account.IsEnabled)
        {
            PasswordHasher.SpendTheSameTime(password);
            await RecordAsync(name, "disabled", "The account is turned off.", now, origin, cancellationToken).ConfigureAwait(false);
            return new OperatorSignInResult(SignInOutcome.Disabled, null);
        }

        if (account.LockedUntil is { } until && until > now)
        {
            PasswordHasher.SpendTheSameTime(password);
            await RecordAsync(name, "locked", "The account is locked after too many wrong passwords.", now, origin, cancellationToken).ConfigureAwait(false);
            return new OperatorSignInResult(SignInOutcome.LockedOut, null);
        }

        (bool matches, bool needsRehash) = PasswordHasher.Verify(password, account.PasswordHash);
        if (!matches)
        {
            int failures = account.FailedAttempts + 1;
            await _store.RecordFailureAsync(account.Id, failures, LockoutPolicy.LockedUntil(failures, now), cancellationToken).ConfigureAwait(false);
            await RecordAsync(name, "refused", "The password was wrong.", now, origin, cancellationToken).ConfigureAwait(false);
            return new OperatorSignInResult(SignInOutcome.Refused, null);
        }

        if (needsRehash)
        {
            // The password is right and was stored to a weaker policy than today's; it is written
            // again at the current cost, without the operator being asked for anything.
            await _store.SetPasswordAsync(account.Id, PasswordHasher.Hash(password!), account.SecurityStamp, now, cancellationToken).ConfigureAwait(false);
        }

        await RecordAsync(name, "password", "The password was right; the second factor is next.", now, origin, cancellationToken).ConfigureAwait(false);
        return new OperatorSignInResult(account.HasSecondFactor ? SignInOutcome.NeedsSecondFactor : SignInOutcome.NeedsEnrolment, account);
    }

    /// <summary>
    /// The second step: a code from the authenticator, or one of the recovery codes. A code is
    /// spent when it works, so neither kind is ever accepted twice.
    /// </summary>
    public async Task<OperatorSignInResult> SecondFactorAsync(long operatorId, string? code, SignInOrigin origin, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        OperatorAccount? account = await _store.FindAsync(operatorId, cancellationToken).ConfigureAwait(false);
        if (account is null || !account.IsEnabled || !account.HasSecondFactor)
        {
            return new OperatorSignInResult(SignInOutcome.Refused, null);
        }

        byte[]? secret = _protector.Unprotect(account.TotpSecretProtected);
        if (Totp.Matches(secret, code, now, account.LastTotpStep, out long step))
        {
            await _store.SetLastTotpStepAsync(account.Id, step, cancellationToken).ConfigureAwait(false);
            return await CompleteAsync(account, "code", "Signed in with the authenticator's code.", now, origin, cancellationToken).ConfigureAwait(false);
        }

        if (SpendRecoveryCode(account, code, out byte[]? remaining))
        {
            await _store.SetRecoveryCodesAsync(account.Id, remaining!, cancellationToken).ConfigureAwait(false);
            return await CompleteAsync(account, "recovery", "Signed in with a recovery code, which is now spent.", now, origin, cancellationToken).ConfigureAwait(false);
        }

        await RecordAsync(account.Username, "refused", "The second factor was wrong.", now, origin, cancellationToken).ConfigureAwait(false);
        return new OperatorSignInResult(SignInOutcome.Refused, account);
    }

    /// <summary>Reads the codes an account has left, for the account page's count.</summary>
    public IReadOnlyList<string> RemainingCodes(OperatorAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        byte[]? plain = _protector.Unprotect(account.RecoveryCodesProtected);
        return plain is null ? [] : Read(plain);
    }

    /// <summary>Encrypts a set of codes for storage, as their fingerprints and never as themselves.</summary>
    public byte[] ProtectCodes(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        string[] fingerprints = [.. codes.Select(RecoveryCodes.Fingerprint)];
        return _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(fingerprints, OperatorJson.Default.StringArray));
    }

    /// <summary>Encrypts an authenticator's seed for storage.</summary>
    public byte[] ProtectSecret(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return _protector.Protect(secret);
    }

    /// <summary>Reads an authenticator's seed back, for the enrolment check and nothing else.</summary>
    public byte[]? ReadSecret(byte[]? protectedSecret) => _protector.Unprotect(protectedSecret);

    private bool SpendRecoveryCode(OperatorAccount account, string? code, out byte[]? remaining)
    {
        remaining = null;
        byte[]? plain = _protector.Unprotect(account.RecoveryCodesProtected);
        if (plain is null)
        {
            return false;
        }

        if (!RecoveryCodes.Spend(code, Read(plain), out IReadOnlyList<string> left))
        {
            return false;
        }

        remaining = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes([.. left], OperatorJson.Default.StringArray));
        return true;
    }

    private async Task<OperatorSignInResult> CompleteAsync(OperatorAccount account, string outcome, string detail, DateTimeOffset now, SignInOrigin origin, CancellationToken cancellationToken)
    {
        await _store.RecordSuccessAsync(account.Id, now, cancellationToken).ConfigureAwait(false);
        await RecordAsync(account.Username, outcome, detail, now, origin, cancellationToken).ConfigureAwait(false);
        return new OperatorSignInResult(account.MustChangePassword ? SignInOutcome.NeedsNewPassword : SignInOutcome.SignedIn, account);
    }

    private Task RecordAsync(string username, string outcome, string detail, DateTimeOffset now, SignInOrigin origin, CancellationToken cancellationToken) =>
        _store.RecordAttemptAsync(new SignInAttempt(now, username.Length == 0 ? "(no name)" : username, outcome, detail, origin.Address, origin.Agent), cancellationToken);

    private static string[] Read(byte[] plain) =>
        JsonSerializer.Deserialize(Encoding.UTF8.GetString(plain), OperatorJson.Default.StringArray) ?? [];
}

/// <summary>
/// The serialiser for the one shape the operator's secrets are stored in. Source-generated, so the
/// stored lists survive trimming and ahead-of-time compilation without a suppression.
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[System.Text.Json.Serialization.JsonSerializable(typeof(string[]))]
internal sealed partial class OperatorJson : System.Text.Json.Serialization.JsonSerializerContext;
