namespace Selficient.Application.Operations;

/// <summary>
/// Something the platform keeps only for a while: a copy waiting to be collected, a sign-in code.
/// Each is refused by its own reader once its time is up; the housekeeper calls this so that the
/// rows go as well, and so that the reviewer can say how many went.
/// </summary>
public interface IExpiring
{
    /// <summary>Removes what has run out as of <paramref name="now"/> and answers how many went.</summary>
    Task<int> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The count as the run log says it: "3 copies that ran out".</summary>
    string Describe(int removed);
}
