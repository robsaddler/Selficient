using System.Security.Cryptography;
using Selficient.Application.Operations;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Streets;

/// <summary>A group an account is in, as it is listed for that account.</summary>
/// <param name="GroupId">The group.</param>
/// <param name="MemberId">The account's own member id in that group, which is how the other members know it.</param>
/// <param name="MayRemove">Whether this account is the longest-standing member, and so the one who may remove a home.</param>
public sealed record StreetMembership(Guid GroupId, Guid MemberId, bool MayRemove);

/// <summary>One home in a group, as the other members are answered it: a random id and a record only they can read.</summary>
/// <param name="MemberId">The home's id in this group. Not an account number, and not the same in any other group.</param>
/// <param name="Record">What the home shares, encrypted in its browser.</param>
/// <param name="UpdatedAt">When it last wrote.</param>
/// <param name="MayRemove">Whether it is the longest-standing member.</param>
public sealed record StreetMember(Guid MemberId, string Record, DateTimeOffset UpdatedAt, bool MayRemove);

/// <summary>A group as one of its members is answered it.</summary>
/// <param name="GroupId">The group.</param>
/// <param name="About">The group's name, encrypted in the starter's browser.</param>
/// <param name="You">The asking account's member id.</param>
/// <param name="Members">Every home in it, longest-standing first.</param>
public sealed record StreetGroup(Guid GroupId, string About, Guid You, IReadOnlyList<StreetMember> Members);

/// <summary>How an attempt to join ended, as the store saw it.</summary>
public enum JoinOutcome
{
    /// <summary>The account is now a member.</summary>
    Joined,

    /// <summary>The account was a member already; nothing was changed.</summary>
    AlreadyIn,

    /// <summary>The group holds as many homes as a group may.</summary>
    Full,

    /// <summary>The account is in as many groups as an account may be.</summary>
    TooManyGroups,
}

/// <summary>
/// Where groups and their members are kept. The store holds who is in what and a blob for each; it
/// is never given a key and cannot read a blob. Who may remove is whoever joined first among those
/// still in, worked out when asked, so there is no role to be left behind when somebody goes.
/// </summary>
public interface IStreetStore
{
    /// <summary>Makes a group with its first member. False when the account is already in as many groups as it may be.</summary>
    Task<bool> CreateAsync(long accountId, Guid groupId, Guid memberId, byte[] passHash, string about, string record, DateTimeOffset now, int mostGroups, CancellationToken cancellationToken);

    /// <summary>The groups an account is in, oldest membership first.</summary>
    Task<IReadOnlyList<StreetMembership>> MembershipsAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>A group as one of its members sees it, or null when there is no such group or the account is not in it.</summary>
    Task<StreetGroup?> ReadAsync(long accountId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>The hash of the pass a group's invitation carries, or null when there is no such group.</summary>
    Task<byte[]?> PassHashAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>Adds an account to a group, in one transaction with the counts that may refuse it.</summary>
    Task<JoinOutcome> JoinAsync(long accountId, Guid groupId, Guid memberId, string record, DateTimeOffset now, int mostMembers, int mostGroups, CancellationToken cancellationToken);

    /// <summary>Replaces the account's own record in a group. False when it is not a member.</summary>
    Task<bool> PutRecordAsync(long accountId, Guid groupId, string record, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Removes another home from a group and sets the invitation's new pass, in one transaction, if
    /// the asking account is the longest-standing member. False, and nothing changed, otherwise.
    /// </summary>
    Task<bool> RemoveAsync(long accountId, Guid groupId, Guid memberId, byte[] newPassHash, CancellationToken cancellationToken);

    /// <summary>Takes the account out of a group, and the group away with it when it was the last. False when it was not a member.</summary>
    Task<bool> LeaveAsync(long accountId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Removes groups nobody is in, which an account's deletion can leave, and answers how many went.</summary>
    Task<int> SweepEmptyAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Street groups, as the server knows them: who is in what, and a record for each that only the
/// members can read. Every operation is for the account the session names. Nothing here is given a
/// key; what it checks about a blob is that it is ciphertext-shaped and not too large.
/// </summary>
public sealed class StreetGroups : IExpiring
{
    /// <summary>The most homes a group holds.</summary>
    public const int MostMembers = 100;

    /// <summary>The most groups an account may be in.</summary>
    public const int MostGroups = 5;

    /// <summary>The longest record, in characters of base64.</summary>
    public const int MaxRecordLength = 32_000;

    /// <summary>The longest name blob, in characters of base64.</summary>
    public const int MaxAboutLength = 2_000;

    /// <summary>The length of the invitation's pass and of its hash, in bytes.</summary>
    public const int PassBytes = 32;

    /// <summary>What a stranger and a member of some other group are both told.</summary>
    public static Fault NoSuchGroup => new("street.none", "There is no such group, or you are not in it.");

    /// <summary>What a wrong pass and a missing group are both told.</summary>
    public static Fault BadInvitation => new("street.invitation", "That invitation does not open a group. Ask your neighbour for a new one.");

    /// <summary>What a write that is not ciphertext, or is too large, is told.</summary>
    public static Fault NotARecord => new("street.record", "That could not be kept: it is not something this page wrote, or it is too large.");

    /// <summary>What a group that is full says.</summary>
    public static Fault Full => new("street.full", "This group is full: it holds a hundred homes.");

    /// <summary>What an account in too many groups is told.</summary>
    public static Fault TooMany => new("street.too-many", "An account can be in five groups. Leave one to join another.");

    /// <summary>What anyone but the longest-standing member is told when they try to remove a home.</summary>
    public static Fault MayNotRemove => new("street.forbidden", "Only the person who has been in the group longest can remove a home from it, and nobody removes themselves: leave instead.");

    private readonly IStreetStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Creates the service.</summary>
    public StreetGroups(IStreetStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _clock = clock;
    }

    /// <summary>Starts a group with the account as its first member, and answers the group's id and the account's member id in it.</summary>
    public async Task<Result<StreetMembership>> StartAsync(long accountId, string? passHash, string? about, string? record, CancellationToken cancellationToken)
    {
        if (Bytes(passHash, PassBytes) is not { } hash || !IsCiphertext(about, MaxAboutLength) || !IsCiphertext(record, MaxRecordLength))
        {
            return Result.Failure<StreetMembership>(NotARecord);
        }

        StreetMembership made = new(Guid.NewGuid(), Guid.NewGuid(), true);
        return await _store.CreateAsync(accountId, made.GroupId, made.MemberId, hash, about!, record!, _clock.GetUtcNow(), MostGroups, cancellationToken).ConfigureAwait(false)
            ? Result.Success(made)
            : Result.Failure<StreetMembership>(TooMany);
    }

    /// <summary>The groups the account is in.</summary>
    public Task<IReadOnlyList<StreetMembership>> MineAsync(long accountId, CancellationToken cancellationToken) => _store.MembershipsAsync(accountId, cancellationToken);

    /// <summary>A group, for one of its members.</summary>
    public async Task<Result<StreetGroup>> ReadAsync(long accountId, Guid groupId, CancellationToken cancellationToken)
    {
        StreetGroup? group = await _store.ReadAsync(accountId, groupId, cancellationToken).ConfigureAwait(false);
        return group is null ? Result.Failure<StreetGroup>(NoSuchGroup) : Result.Success(group);
    }

    /// <summary>
    /// Joins a group with the pass from its invitation. An account that is in already is answered
    /// its membership and nothing is changed, so opening the link twice is not an error.
    /// </summary>
    public async Task<Result<StreetMembership>> JoinAsync(long accountId, Guid groupId, string? pass, string? record, CancellationToken cancellationToken)
    {
        if (!IsCiphertext(record, MaxRecordLength))
        {
            return Result.Failure<StreetMembership>(NotARecord);
        }

        byte[]? held = await _store.PassHashAsync(groupId, cancellationToken).ConfigureAwait(false);
        // Compared in fixed time, and against something whether or not there is a group, so a wrong pass and a missing group take as long and say the same.
        byte[] offered = Bytes(pass, PassBytes) is { } given ? SHA256.HashData(given) : new byte[PassBytes];
        if (!CryptographicOperations.FixedTimeEquals(offered, held ?? new byte[PassBytes]) || held is null)
        {
            return Result.Failure<StreetMembership>(BadInvitation);
        }

        JoinOutcome outcome = await _store.JoinAsync(accountId, groupId, Guid.NewGuid(), record!, _clock.GetUtcNow(), MostMembers, MostGroups, cancellationToken).ConfigureAwait(false);
        if (outcome == JoinOutcome.Full)
        {
            return Result.Failure<StreetMembership>(Full);
        }

        if (outcome == JoinOutcome.TooManyGroups)
        {
            return Result.Failure<StreetMembership>(TooMany);
        }

        // Read back, so a member who was in already is told the id it has and not the one just made.
        IReadOnlyList<StreetMembership> mine = await _store.MembershipsAsync(accountId, cancellationToken).ConfigureAwait(false);
        return Result.Success(mine.First(membership => membership.GroupId == groupId));
    }

    /// <summary>Replaces the account's own record in a group.</summary>
    public async Task<Result<bool>> ShareAsync(long accountId, Guid groupId, string? record, CancellationToken cancellationToken)
    {
        if (!IsCiphertext(record, MaxRecordLength))
        {
            return Result.Failure<bool>(NotARecord);
        }

        return await _store.PutRecordAsync(accountId, groupId, record!, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false)
            ? Result.Success(true)
            : Result.Failure<bool>(NoSuchGroup);
    }

    /// <summary>Removes another home from a group and replaces the invitation's pass, so the link the removed home had opens nothing.</summary>
    public async Task<Result<bool>> RemoveAsync(long accountId, Guid groupId, Guid memberId, string? newPassHash, CancellationToken cancellationToken)
    {
        if (Bytes(newPassHash, PassBytes) is not { } hash)
        {
            return Result.Failure<bool>(NotARecord);
        }

        return await _store.RemoveAsync(accountId, groupId, memberId, hash, cancellationToken).ConfigureAwait(false)
            ? Result.Success(true)
            : Result.Failure<bool>(MayNotRemove);
    }

    /// <summary>Takes the account out of a group.</summary>
    public async Task<Result<bool>> LeaveAsync(long accountId, Guid groupId, CancellationToken cancellationToken) =>
        await _store.LeaveAsync(accountId, groupId, cancellationToken).ConfigureAwait(false)
            ? Result.Success(true)
            : Result.Failure<bool>(NoSuchGroup);

    /// <inheritdoc />
    public Task<int> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken) => _store.SweepEmptyAsync(cancellationToken);

    /// <inheritdoc />
    public string Describe(int removed) => removed == 1 ? "1 street group nobody was left in" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{removed} street groups nobody was left in");

    /// <summary>The fewest bytes an encrypted blob can be: the nonce and the tag of AES-GCM around nothing.</summary>
    public const int LeastCiphertextBytes = 28;

    /// <summary>
    /// Whether a value is shaped like what a browser's encryption gives: base64 with no spaces, at
    /// least as long as an encrypted blob can be, and not too long. It cannot prove encryption. It
    /// stops a mistake in our own script from sending words; a household that rewrites the script
    /// can tell the server what it likes about itself, and nothing here could prevent that.
    /// </summary>
    public static bool IsCiphertext(string? value, int mostLength) =>
        value is not null && value.Length <= mostLength && Bytes(value, null) is { Length: >= LeastCiphertextBytes };

    // The bytes a base64 value holds, or nothing when it is not base64, has white space in it, or is not the length asked for.
    private static byte[]? Bytes(string? value, int? length)
    {
        if (value is null || value.Length > MaxRecordLength || value.AsSpan().IndexOfAny(" \t\r\n") >= 0)
        {
            return null;
        }

        byte[] bytes = new byte[value.Length];
        return Convert.TryFromBase64String(value, bytes, out int written) && (length is null || written == length) ? bytes[..written] : null;
    }
}
