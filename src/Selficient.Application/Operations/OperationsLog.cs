namespace Selficient.Application.Operations;

/// <summary>One hand the operator took on the reviewer.</summary>
public sealed record OperatorAction(DateTimeOffset At, string Hand, long? OfferId, string Outcome);

/// <summary>How a job run ended.</summary>
public enum JobOutcome
{
    /// <summary>Still running, or the process died with it.</summary>
    Running,

    /// <summary>Finished as it should.</summary>
    Succeeded,

    /// <summary>Threw; the detail says what.</summary>
    Failed,

    /// <summary>Woke and found nothing due.</summary>
    Skipped,
}

/// <summary>One run of a background job.</summary>
public sealed record JobRun(long Id, string Job, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, JobOutcome Outcome, string Detail);

/// <summary>
/// The platform's memory of what was done to it: every hand the operator takes and every run of
/// every job. Read on the reviewer; written by the reviewer and the worker. Nothing in it names a
/// person; the operator is the one key.
/// </summary>
public interface IOperationsLog
{
    /// <summary>Records a hand on the reviewer.</summary>
    Task RecordHandAsync(string hand, long? offerId, string outcome, CancellationToken cancellationToken);

    /// <summary>The latest hands, newest first.</summary>
    Task<IReadOnlyList<OperatorAction>> RecentHandsAsync(int take, CancellationToken cancellationToken);

    /// <summary>Opens a job run; returns its id to finish it with.</summary>
    Task<long> StartJobAsync(string job, CancellationToken cancellationToken);

    /// <summary>Closes a job run with how it went.</summary>
    Task FinishJobAsync(long id, JobOutcome outcome, string detail, CancellationToken cancellationToken);

    /// <summary>The latest runs, newest first.</summary>
    Task<IReadOnlyList<JobRun>> RecentRunsAsync(int take, CancellationToken cancellationToken);
}
