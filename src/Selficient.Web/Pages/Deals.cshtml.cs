using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Catalogue;
using Selficient.Web.Security;

namespace Selficient.Web.Pages;

/// <summary>
/// The cheapest pick for every action the plan buys for, drops first. Public, because it is the
/// one page a household can share without sharing anything about their house; every link goes
/// through the counter like the plan's.
/// </summary>
[ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class DealsModel : PageModel
{
    private readonly IDealsReader _deals;

    /// <summary>Creates the page over the deals reader.</summary>
    public DealsModel(IDealsReader deals)
    {
        ArgumentNullException.ThrowIfNull(deals);
        _deals = deals;
    }

    /// <summary>The deals, drops first.</summary>
    public IReadOnlyList<Deal> Deals { get; private set; } = [];

    /// <summary>How many have come down in price.</summary>
    public int Drops => Deals.Count(static deal => deal.IsDrop);

    /// <summary>Renders the page.</summary>
    public async Task OnGetAsync(CancellationToken cancellationToken) => Deals = await _deals.ReadAsync(cancellationToken);

    /// <summary>The sentence under the heading.</summary>
    public static string Lede(int deals, int drops) => drops switch
    {
        0 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The cheapest we know for each of {deals} things a plan buys, every link opened by our checker. No price has come down this quarter."),
        1 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The cheapest we know for each of {deals} things a plan buys, every link opened by our checker. One has come down in price this quarter; it is first."),
        _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The cheapest we know for each of {deals} things a plan buys, every link opened by our checker. {drops} have come down in price this quarter; they are first."),
    };
}
