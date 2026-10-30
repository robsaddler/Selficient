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

    /// <summary>PVGIS 5.2, the European Commission's solar service, free and keyless.</summary>
    [Required]
    public Uri PvgisBaseUrl { get; init; } = new("https://re.jrc.ec.europa.eu/");

    /// <summary>Google Solar API; only called with a key and consent.</summary>
    [Required]
    public Uri GoogleSolarBaseUrl { get; init; } = new("https://solar.googleapis.com/");

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
        foreach ((string name, Uri uri) in new[] { (nameof(PostcodesBaseUrl), PostcodesBaseUrl), (nameof(ElevationBaseUrl), ElevationBaseUrl), (nameof(PvgisBaseUrl), PvgisBaseUrl), (nameof(GoogleSolarBaseUrl), GoogleSolarBaseUrl) })
        {
            // https everywhere, except a loopback stub on the developer's own machine or in a test.
            bool allowed = uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
            if (!allowed)
            {
                yield return new ValidationResult($"{name} must be an absolute https URL.", [name]);
            }
        }
    }
}
