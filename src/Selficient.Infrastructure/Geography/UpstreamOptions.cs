using System.ComponentModel.DataAnnotations;

namespace Selficient.Infrastructure.Geography;

/// <summary>
/// Where the upstream services live. Hosts are fixed here and validated at startup; nothing from a
/// request ever reaches a URL except the validated postcode segment.
/// </summary>
public sealed class UpstreamOptions : IValidatableObject
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Upstreams";

    /// <summary>postcodes.io, free and keyless.</summary>
    [Required]
    public Uri PostcodesBaseUrl { get; init; } = new("https://api.postcodes.io/");

    /// <summary>Open-Meteo elevation, free and keyless.</summary>
    [Required]
    public Uri ElevationBaseUrl { get; init; } = new("https://api.open-meteo.com/");

    /// <summary>Per-call timeout.</summary>
    [Range(1, 30)]
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// Replace every upstream with a deterministic fake. For development without network and for
    /// tests. Refused in Production by the startup check.
    /// </summary>
    public bool UseFakes { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach ((string name, Uri uri) in new[] { (nameof(PostcodesBaseUrl), PostcodesBaseUrl), (nameof(ElevationBaseUrl), ElevationBaseUrl) })
        {
            if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
            {
                yield return new ValidationResult($"{name} must be an absolute https URL.", [name]);
            }
        }
    }
}
