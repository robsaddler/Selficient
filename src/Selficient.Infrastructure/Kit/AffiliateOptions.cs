using System.ComponentModel.DataAnnotations;

namespace Selficient.Infrastructure.Kit;

/// <summary>Which hosts the catalogue may link to, and the affiliate tags to append.</summary>
public sealed class AffiliateOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Affiliate";

    /// <summary>Hosts a pick may link to. Anything else fails the load.</summary>
    public IReadOnlyList<string> AllowedHosts { get; init; } =
    [
        "www.amazon.co.uk",
        "www.argos.co.uk",
        "www.currys.co.uk",
        "www.screwfix.com",
        "www.toolstation.com",
        "www.halfords.com",
        "www.wickes.co.uk",
        "www.safelincs.co.uk",
        "www.costco.co.uk",
        "uk.ecoflow.com",
        "www.bluettipower.co.uk",
        "www.ankersolix.com",
        "www.ledlenser.co.uk",
        "www.robertsradio.com",
        "www.thebugout.co.uk",
        "www.doulton.com",
        "www.starlink.com",
        "www.trees.org.uk",
    ];

    /// <summary>Amazon Associates tag, appended as <c>tag=</c> on amazon.co.uk links when set.</summary>
    [RegularExpression("^[A-Za-z0-9-]{1,40}$", ErrorMessage = "The Amazon tag may contain only letters, digits and hyphens.")]
    public string? AmazonTag { get; init; }
}
