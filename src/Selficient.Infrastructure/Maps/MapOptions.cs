using System.ComponentModel.DataAnnotations;

namespace Selficient.Infrastructure.Maps;

/// <summary>
/// Where the OS Open Zoomstack archive lives. The default matches where the deploy workflow puts
/// it, beside the site and outside the mirrored publish output, so no setting is needed on the
/// VPS. Absence is handled, not fatal.
/// </summary>
public sealed class MapOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Map";

    /// <summary>Path to the MBTiles file; relative paths resolve against the content root.</summary>
    [Required]
    [MinLength(1)]
    public string MbTilesPath { get; init; } = "tiles/os-open-zoomstack.mbtiles";

    /// <summary>
    /// How long an absent or unreadable archive is remembered before the disk is checked again.
    /// The deploy fetches the archive after the site has started, so this is what lets the map
    /// appear without a restart.
    /// </summary>
    [Range(5, 3600)]
    public int RetryAfterSeconds { get; init; } = 60;
}
