namespace Selficient.Application.Security;

/// <summary>
/// Where the operators live. Implemented over SQL Server in the infrastructure; a port here so the
/// sign-in itself can be driven and proved without a database, and so the infrastructure never
/// needs to know what a cookie is.
/// </summary>
public interface IOperatorStore
{
    /// <summary>How many accounts exist. Nothing exists until the first one does.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Every account, oldest first, for the reviewer's own list.</summary>
    Task<IReadOnlyList<OperatorAccount>> AllAsync(CancellationToken cancellationToken);

    /// <summary>The account with this name, compared without case, or nothing.</summary>
    Task<OperatorAccount?> FindAsync(string username, CancellationToken cancellationToken);

    /// <summary>The account with this number, or nothing.</summary>
    Task<OperatorAccount?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>Makes an account and answers its number.</summary>
    Task<long> CreateAsync(string username, string displayName, string passwordHash, bool mustChangePassword, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Writes a new password, clears the must-change flag and moves the security stamp, which ends every other session.</summary>
    Task SetPasswordAsync(long id, string passwordHash, Guid stamp, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Writes the enrolment: the protected seed, the protected recovery codes and a new stamp.</summary>
    Task SetSecondFactorAsync(long id, byte[] totpSecretProtected, DateTimeOffset confirmedAt, byte[] recoveryCodesProtected, Guid stamp, CancellationToken cancellationToken);

    /// <summary>Writes the codes that are left after one has been spent.</summary>
    Task SetRecoveryCodesAsync(long id, byte[] recoveryCodesProtected, CancellationToken cancellationToken);

    /// <summary>Remembers the last step a code was accepted for, so the same code is never taken twice.</summary>
    Task SetLastTotpStepAsync(long id, long lastStep, CancellationToken cancellationToken);

    /// <summary>Turns an account on or off, moving the stamp so its sessions end at once.</summary>
    Task SetEnabledAsync(long id, bool enabled, Guid stamp, CancellationToken cancellationToken);

    /// <summary>Counts one wrong password and, past the threshold, shuts the account for a while.</summary>
    Task RecordFailureAsync(long id, int failedAttempts, DateTimeOffset? lockedUntil, CancellationToken cancellationToken);

    /// <summary>Forgets the failures and remembers the moment of a completed sign-in.</summary>
    Task RecordSuccessAsync(long id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Writes an attempt to the trail, right or wrong. The password is never a parameter here.</summary>
    Task RecordAttemptAsync(SignInAttempt attempt, CancellationToken cancellationToken);

    /// <summary>The last attempts, newest first, for the reviewer.</summary>
    Task<IReadOnlyList<SignInAttempt>> AttemptsAsync(int take, CancellationToken cancellationToken);
}

/// <summary>
/// Encrypts the two secrets an account holds, the authenticator's seed and the recovery codes, so
/// that a database backup read by someone else is not a second factor as well as a list of hashes.
/// Implemented in the web layer over the Data Protection key ring, which is itself in the database
/// and wrapped by the machine; a port here because the dependency rule keeps ASP.NET Core out of
/// the infrastructure.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypts.</summary>
    byte[] Protect(byte[] plaintext);

    /// <summary>Decrypts, or answers nothing when the value was not written by this key ring.</summary>
    byte[]? Unprotect(byte[]? ciphertext);
}
