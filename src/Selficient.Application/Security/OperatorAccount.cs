namespace Selficient.Application.Security;

/// <summary>
/// An operator of the reviewer. The two secrets on it, the authenticator's seed and the recovery
/// codes, are held as the ciphertext the store keeps; nothing in this layer can read them without
/// the <see cref="ISecretProtector"/> the composition root supplies.
/// </summary>
/// <param name="Id">The account's number.</param>
/// <param name="Username">What they type to sign in. Compared without case.</param>
/// <param name="DisplayName">What the audit trail and the reviewer call them.</param>
/// <param name="PasswordHash">The stored hash, in <see cref="PasswordHasher"/>'s own format.</param>
/// <param name="PasswordSetAt">When the password was last written.</param>
/// <param name="TotpSecretProtected">The authenticator's seed, encrypted, or nothing before enrolment.</param>
/// <param name="TotpConfirmedAt">When a code from the app was first accepted, or nothing.</param>
/// <param name="LastTotpStep">The last thirty-second step spent, so a code is never taken twice.</param>
/// <param name="RecoveryCodesProtected">The remaining codes' fingerprints, encrypted, or nothing.</param>
/// <param name="SecurityStamp">Changes whenever the password, the second factor or the enabled flag does; every live session carries it and dies with it.</param>
/// <param name="IsEnabled">Whether the account may be used at all.</param>
/// <param name="MustChangePassword">Set on the bootstrap account, cleared by a change.</param>
/// <param name="FailedAttempts">Consecutive wrong passwords since the last right one.</param>
/// <param name="LockedUntil">When the account may try again, or nothing.</param>
/// <param name="LastSignInAt">The last completed sign-in.</param>
/// <param name="CreatedAt">When the account was made.</param>
public sealed record OperatorAccount(
    long Id,
    string Username,
    string DisplayName,
    string PasswordHash,
    DateTimeOffset PasswordSetAt,
    byte[]? TotpSecretProtected,
    DateTimeOffset? TotpConfirmedAt,
    long LastTotpStep,
    byte[]? RecoveryCodesProtected,
    Guid SecurityStamp,
    bool IsEnabled,
    bool MustChangePassword,
    int FailedAttempts,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>Whether a second factor has been set up and proved.</summary>
    public bool HasSecondFactor => TotpSecretProtected is { Length: > 0 } && TotpConfirmedAt is not null;
}

/// <summary>What happened when an operator tried to sign in.</summary>
public enum SignInOutcome
{
    /// <summary>The name or the password was wrong. The sentence never says which.</summary>
    Refused,

    /// <summary>Too many wrong passwords; the account is shut for a while.</summary>
    LockedOut,

    /// <summary>The account exists but has been turned off.</summary>
    Disabled,

    /// <summary>The password was right and the authenticator's code is wanted next.</summary>
    NeedsSecondFactor,

    /// <summary>The password was right and no authenticator has been set up yet.</summary>
    NeedsEnrolment,

    /// <summary>Everything checked out, but this password has to be replaced before anything else.</summary>
    NeedsNewPassword,

    /// <summary>Signed in.</summary>
    SignedIn,
}

/// <summary>The result of a step of signing in: what happened, and who it happened to.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Account">The account, when the password was right; otherwise nothing.</param>
public readonly record struct OperatorSignInResult(SignInOutcome Outcome, OperatorAccount? Account);

/// <summary>A hand taken on the reviewer or a sign-in attempt, as the audit trail keeps it.</summary>
/// <param name="At">When.</param>
/// <param name="Username">The name typed, or the operator's own.</param>
/// <param name="Outcome">One word for what happened.</param>
/// <param name="Detail">A whole sentence for a person reading the trail.</param>
public sealed record SignInAttempt(DateTimeOffset At, string Username, string Outcome, string Detail);
