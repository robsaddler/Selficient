using System.Buffers.Text;
using System.Security.Cryptography;
using Selficient.Application.Operations;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Handoff;

/// <summary>
/// Sending a copy of a plan to another device. One browser leaves bytes it encrypted with a key we
/// never see; the other collects them once, within ten minutes. The desk makes the id, holds the
/// limits and keeps no note of who came.
/// </summary>
public sealed class HandoffDesk : IExpiring
{
    /// <summary>The largest copy accepted, in bytes. A whole plan, compressed, is a few thousand.</summary>
    public static int MaxBytes => 64 * 1024;

    /// <summary>The smallest thing that could be a copy: a nonce, a tag and one byte of plan.</summary>
    public static int MinBytes => 12 + 16 + 1;

    /// <summary>How long a copy waits.</summary>
    public static TimeSpan Lifetime => TimeSpan.FromMinutes(10);

    /// <summary>How many copies may wait at once before the desk refuses another. At the cap that is 320 MB, which the disk can hold.</summary>
    public static int MaxWaiting => 5_000;

    /// <summary>The id's length in bytes: 128 bits from the system's generator.</summary>
    public static int IdBytes => 16;

    /// <summary>The fault when the body is not the size of a copy.</summary>
    public static Fault Malformed => new("handoff.malformed", "That is not a copy of a plan. Send a copy again from the other device.");

    /// <summary>The fault when the body is over the cap.</summary>
    public static Fault TooLarge => new("handoff.too-large", "That copy is too large to send. Use \"Export as a file\" on the plan instead.");

    /// <summary>The fault when too many copies are already waiting.</summary>
    public static Fault Full => new("handoff.full", "Too many copies are waiting to be collected at the moment. Try again in a few minutes.");

    private readonly IHandoffStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Creates the desk over the store.</summary>
    public HandoffDesk(IHandoffStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _clock = clock;
    }

    /// <summary>Keeps a copy for ten minutes and answers the id it is under, as it is written in a link.</summary>
    public async Task<Result<string>> LeaveAsync(byte[] ciphertext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (ciphertext.Length < MinBytes)
        {
            return Result.Failure<string>(Malformed);
        }

        if (ciphertext.Length > MaxBytes)
        {
            return Result.Failure<string>(TooLarge);
        }

        if (await _store.CountAsync(cancellationToken).ConfigureAwait(false) >= MaxWaiting)
        {
            return Result.Failure<string>(Full);
        }

        byte[] id = RandomNumberGenerator.GetBytes(IdBytes);
        DateTimeOffset now = _clock.GetUtcNow();
        await _store.PutAsync(id, ciphertext, now, now + Lifetime, cancellationToken).ConfigureAwait(false);
        return Result.Success(Base64Url.EncodeToString(id));
    }

    /// <summary>Hands over the copy under an id, once. Nothing when the id is not one, was used, or has run out.</summary>
    public async Task<byte[]?> CollectAsync(string? id, CancellationToken cancellationToken) =>
        Parse(id) is { } bytes ? await _store.TakeAsync(bytes, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false) : null;

    /// <summary>Whether the copy under an id is still waiting, for the sender's screen.</summary>
    public async Task<bool> IsWaitingAsync(string? id, CancellationToken cancellationToken) =>
        Parse(id) is { } bytes && await _store.IsWaitingAsync(bytes, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<int> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken) => _store.SweepAsync(now, cancellationToken);

    /// <inheritdoc />
    public string Describe(int removed) => removed == 1 ? "1 copy of a plan that was never collected" : $"{removed} copies of a plan that were never collected";

    /// <summary>The id's bytes from the way a link writes it, or nothing when it is not an id at all, so a stranger's text never reaches the store.</summary>
    public static byte[]? Parse(string? id) =>
        id is { Length: 22 } && Base64Url.IsValid(id) ? Base64Url.DecodeFromChars(id) : null;
}
