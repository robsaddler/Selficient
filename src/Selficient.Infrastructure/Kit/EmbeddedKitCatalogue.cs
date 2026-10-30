using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Selficient.Application.Kit;
using Selficient.Domain.Kit;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Kit;

/// <summary>
/// The catalogue shipped inside the assembly. Every entry is validated when loaded, so a bad entry
/// stops a deploy rather than reaching a reader. Affiliate tags are applied here, once.
/// </summary>
public sealed class EmbeddedKitCatalogue : IKitCatalogue
{
    private const string ResourceName = "Selficient.Infrastructure.Kit.kit-catalogue.json";

    private readonly Dictionary<ActionId, IReadOnlyList<KitPick>> _byAction;

    private EmbeddedKitCatalogue(IReadOnlyList<KitPick> picks)
    {
        All = picks;
        _byAction = picks
            .GroupBy(static p => p.Action)
            .ToDictionary(static g => g.Key, static g => (IReadOnlyList<KitPick>)[.. g.OrderBy(static p => p.Price.Pence)]);
    }

    /// <inheritdoc />
    public IReadOnlyList<KitPick> All { get; }

    /// <inheritdoc />
    public IReadOnlyList<KitPick> PicksFor(ActionId action) => _byAction.TryGetValue(action, out IReadOnlyList<KitPick>? picks) ? picks : [];

    /// <summary>Loads and validates the embedded catalogue. Throws on any invalid entry.</summary>
    public static EmbeddedKitCatalogue Load(AffiliateOptions options) => Load(options, ResourceName);

    internal static EmbeddedKitCatalogue Load(AffiliateOptions options, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(options);
        using Stream stream = typeof(EmbeddedKitCatalogue).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The kit catalogue resource is missing from the build.");
        return Parse(stream, options);
    }

    /// <summary>Parses a catalogue from JSON, for tests and for the loader.</summary>
    public static EmbeddedKitCatalogue Parse(Stream json, AffiliateOptions options)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(options);

        CatalogueEntry[] entries = JsonSerializer.Deserialize(json, CatalogueJson.Default.CatalogueEntryArray)
            ?? throw new InvalidOperationException("The kit catalogue is empty.");

        HashSet<string> hosts = new(options.AllowedHosts, StringComparer.OrdinalIgnoreCase);
        List<KitPick> picks = new(entries.Length);
        foreach (CatalogueEntry entry in entries)
        {
            Uri? link = Uri.TryCreate(entry.Link, UriKind.Absolute, out Uri? parsed) ? parsed : null;
            if (link is not null && link.Scheme == Uri.UriSchemeHttps && !hosts.Contains(link.Host))
            {
                throw new InvalidOperationException($"The kit catalogue links to '{link.Host}', which is not an allowed host.");
            }

            link = Tagged(link, options.AmazonTag);
            if (!DateOnly.TryParseExact(entry.Seen, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly seen))
            {
                throw new InvalidOperationException($"The kit catalogue entry '{entry.Name}' has no valid 'seen' date (yyyy-MM-dd).");
            }

            Result<KitPick> pick = KitPick.Create(entry.Action ?? string.Empty, entry.Name ?? string.Empty, entry.Retailer ?? string.Empty, entry.PricePounds, seen, link, entry.Note);
            if (pick.IsFailure)
            {
                throw new InvalidOperationException($"The kit catalogue entry '{entry.Name}' is invalid: {pick.Error.Message}");
            }

            picks.Add(pick.Value);
        }

        return new EmbeddedKitCatalogue(picks);
    }

    /// <summary>Appends the Amazon tag as one query parameter, on amazon.co.uk only, never twice.</summary>
    internal static Uri? Tagged(Uri? link, string? tag)
    {
        if (link is null || string.IsNullOrEmpty(tag) || !link.Host.Equals("www.amazon.co.uk", StringComparison.OrdinalIgnoreCase))
        {
            return link;
        }

        UriBuilder builder = new(link);
        string existing = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(existing) ? "tag=" + Uri.EscapeDataString(tag) : existing + "&tag=" + Uri.EscapeDataString(tag);
        return builder.Uri;
    }
}

/// <summary>One line of the catalogue file.</summary>
public sealed record CatalogueEntry(
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("retailer")] string? Retailer,
    [property: JsonPropertyName("pricePounds")] decimal PricePounds,
    [property: JsonPropertyName("seen")] string? Seen,
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("note")] string? Note);

/// <summary>Source-generated serializer context for the catalogue.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(CatalogueEntry[]))]
public sealed partial class CatalogueJson : JsonSerializerContext;
