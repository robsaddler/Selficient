using System.ComponentModel.DataAnnotations;

namespace Selficient.Web.Security;

/// <summary>The operator's way into the reviewer pages. Unset means the pages do not exist.</summary>
public sealed class OperatorOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Operator";

    /// <summary>The key the operator types once; a signed cookie carries the session after that. At least twenty characters, or nothing.</summary>
    [MinLength(20)]
    public string? Key { get; init; }

    /// <summary>How long a signed-in operator stays signed in.</summary>
    [Range(1, 72)]
    public int SessionHours { get; init; } = 12;
}
