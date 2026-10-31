using System.ComponentModel.DataAnnotations;

namespace Selficient.Infrastructure.Data;

/// <summary>The platform database: SQL Server, reached through Dapper, migrated at start-up.</summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Database";

    /// <summary>The SQL Server connection string. Required; the host refuses to start without one.</summary>
    [Required]
    [MinLength(16)]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Whether the embedded migrations run before the site answers. Off only for a test that owns its schema.</summary>
    public bool MigrateOnStart { get; init; } = true;
}
