using System.Text;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Accounts;

/// <summary>The copy of a plan an account keeps, as the store holds it: ciphertext, a version and a time.</summary>
/// <param name="Protected">The plan, encrypted by the key ring.</param>
/// <param name="Version">Goes up by one with every write. A writer names the version it last saw.</param>
/// <param name="UpdatedAt">When it was last written.</param>
public sealed record StoredPlan(byte[] Protected, long Version, DateTimeOffset UpdatedAt);

/// <summary>The copy as a browser is answered it.</summary>
/// <param name="Version">The version to name in the next write. Nought when the account has never kept a plan.</param>
/// <param name="Body">The plan as the browser wrote it, or nothing when there is none, or none this server can read.</param>
public sealed record SyncedPlan(long Version, string? Body);

/// <summary>Where an account's copy of its plan lives. One row an account, replaced in place.</summary>
public interface IPlanStore
{
    /// <summary>The copy an account keeps, or nothing.</summary>
    Task<StoredPlan?> GetAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the copy if it is still at the version the writer last saw, in one statement, and
    /// answers the new version. Nothing when it has moved on, so a stale write never lands.
    /// A base of nought means the writer saw no copy, and writes the first.
    /// </summary>
    Task<long?> TryPutAsync(long accountId, byte[] protectedPlan, long baseVersion, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Encrypts a plan for storage and reads it back. The key ring, in the web layer.</summary>
public interface IPlanProtector
{
    /// <summary>Encrypts a plan.</summary>
    byte[] Protect(byte[] plan);

    /// <summary>Reads a plan back, or nothing when this key ring did not write it.</summary>
    byte[]? Unprotect(byte[] protectedPlan);
}

/// <summary>
/// Keeping a household's devices in step. The account holds one copy of the plan and a version;
/// a browser reads it, and writes it back naming the version it last saw, so a device that has
/// been away cannot overwrite what another wrote since. The merging is the browser's; nothing here
/// looks inside a plan beyond checking that it is one. The account's number always comes from the
/// session, never from the request.
/// </summary>
public sealed class PlanSync
{
    /// <summary>The fault when the body is not a plan, or is too large to be one.</summary>
    public static Fault NotAPlan => new("sync.not-a-plan", "That is not a Selficient plan, so it was not kept.");

    /// <summary>The fault when the copy has moved on since the writer last saw it.</summary>
    public static Fault Stale => new("sync.stale", "Another device has changed the plan since this one last looked.");

    private readonly IPlanStore _store;
    private readonly IPlanProtector _protector;
    private readonly TimeProvider _clock;

    /// <summary>Creates the sync.</summary>
    public PlanSync(IPlanStore store, IPlanProtector protector, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _protector = protector;
        _clock = clock;
    }

    /// <summary>
    /// The copy an account keeps. A copy this key ring cannot open is answered as its version with
    /// no body, so a browser that still has the plan writes it back over the top.
    /// </summary>
    public async Task<SyncedPlan> ReadAsync(long accountId, CancellationToken cancellationToken)
    {
        StoredPlan? stored = await _store.GetAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return new SyncedPlan(0, null);
        }

        byte[]? plain = _protector.Unprotect(stored.Protected);
        return new SyncedPlan(stored.Version, plain is null ? null : Encoding.UTF8.GetString(plain));
    }

    /// <summary>
    /// Replaces the copy with a body based on a version. Answers the new version; or
    /// <see cref="NotAPlan"/> for a body that is not one; or <see cref="Stale"/> when the copy has
    /// moved on, in which case the caller reads it again and merges.
    /// </summary>
    public async Task<Result<long>> WriteAsync(long accountId, string? body, long baseVersion, CancellationToken cancellationToken)
    {
        if (baseVersion < 0 || !PlanGuard.IsSyncBody(body))
        {
            return Result.Failure<long>(NotAPlan);
        }

        long? version = await _store.TryPutAsync(accountId, _protector.Protect(Encoding.UTF8.GetBytes(body!)), baseVersion, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return version is { } written ? Result.Success(written) : Result.Failure<long>(Stale);
    }
}
