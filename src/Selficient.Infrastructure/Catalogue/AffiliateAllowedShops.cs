using Microsoft.Extensions.Options;
using Selficient.Application.Catalogue;
using Selficient.Infrastructure.Kit;

namespace Selficient.Infrastructure.Catalogue;

/// <summary>The catalogue's own host allowlist, as the shops a reader may point at.</summary>
public sealed class AffiliateAllowedShops : IAllowedShops
{
    private readonly HashSet<string> _hosts;

    /// <summary>Creates the allowlist from the affiliate options.</summary>
    public AffiliateAllowedShops(IOptions<AffiliateOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _hosts = new HashSet<string>(options.Value.AllowedHosts, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool Allows(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return link.IsAbsoluteUri && _hosts.Contains(link.Host);
    }
}
