namespace Selficient.Application.Handoff;

/// <summary>
/// Where a copy waits between two devices. The store holds an id, bytes it cannot read and two
/// times, and nothing about who left them.
/// </summary>
public interface IHandoffStore
{
    /// <summary>Keeps a copy under an id until it expires.</summary>
    Task PutAsync(byte[] id, byte[] ciphertext, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>
    /// Hands over the copy under an id and deletes it in the same statement, so two readers racing
    /// get one copy between them. Nothing when there is none, or when it has run out.
    /// </summary>
    Task<byte[]?> TakeAsync(byte[] id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Whether a copy is still waiting under an id. Reads only.</summary>
    Task<bool> IsWaitingAsync(byte[] id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>How many copies are waiting, run out or not.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Deletes every copy that has run out and answers how many went.</summary>
    Task<int> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
