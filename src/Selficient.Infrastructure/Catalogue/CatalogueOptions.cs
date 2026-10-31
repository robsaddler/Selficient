using System.ComponentModel.DataAnnotations;

namespace Selficient.Infrastructure.Catalogue;

/// <summary>The catalogue store and its worker.</summary>
public sealed class CatalogueOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Catalogue";

    /// <summary>Whether the worker runs its jobs on the clock. Off in tests; on on the vhost.</summary>
    public bool WorkerEnabled { get; init; }

    /// <summary>The hour of the day, local to the server, after which the nightly verification is due.</summary>
    [Range(0, 23)]
    public int VerifyAtHour { get; init; } = 2;

    /// <summary>How often the worker wakes to see whether a job is due.</summary>
    [Range(1, 1440)]
    public int WakeEveryMinutes { get; init; } = 30;

    /// <summary>The most a page read may be, in bytes; the rest is not read.</summary>
    [Range(16 * 1024, 4 * 1024 * 1024)]
    public int PageBytes { get; init; } = 512 * 1024;
}
