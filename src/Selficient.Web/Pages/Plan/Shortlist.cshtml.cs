using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Catalogue;
using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.Web.Formatting;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// What a household saved for later, grouped by shop. The browser keeps the list, as numbers, with
/// the plan; it posts the numbers here and the page is drawn from the catalogue's own data, so
/// nothing a poster sends is ever shown. Every link goes out through the counter like the plan's.
/// The threat model is <c>docs/threat-models/shortlist.md</c>.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.PlanPolicy)]
public sealed class ShortlistModel : PageModel
{
    private readonly StoredCatalogue _catalogue;

    /// <summary>Creates the page over the catalogue the plan's own picks come from.</summary>
    public ShortlistModel(StoredCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        _catalogue = catalogue;
    }

    /// <summary>The offer numbers saved, as the browser posts them: digits and commas.</summary>
    [BindProperty]
    public string? Ids { get; set; }

    /// <summary>What was saved, drawn from the catalogue. Empty until the browser has posted its list.</summary>
    public ShortlistView List { get; private set; } = ShortlistView.Empty;

    /// <summary>Shows the page with nothing on it; the script posts the list if the browser holds one.</summary>
    public IActionResult OnGet() => Page();

    /// <summary>Draws the list for the numbers posted.</summary>
    public IActionResult OnPost()
    {
        List = ShortlistView.For(ShortlistView.Parse(Ids), _catalogue.Find);
        return Page();
    }
}

/// <summary>One shop's part of the list.</summary>
/// <param name="Retailer">The shop.</param>
/// <param name="Picks">What was saved from it, cheapest first.</param>
public sealed record ShortlistShop(string Retailer, IReadOnlyList<KitPick> Picks)
{
    /// <summary>What the shop's picks come to, at the prices seen.</summary>
    public Money Total => Money.FromPence(Picks.Sum(static pick => pick.Price.Pence));
}

/// <summary>The list as the page draws it.</summary>
/// <param name="Asked">Every number the browser posted, in the order it posted them.</param>
/// <param name="Shops">The picks the catalogue still shows, by shop, shops in alphabetical order.</param>
public sealed record ShortlistView(IReadOnlyList<long> Asked, IReadOnlyList<ShortlistShop> Shops)
{
    /// <summary>The most numbers read from one post.</summary>
    public static int Most => 100;

    /// <summary>The list before anything has been posted.</summary>
    public static ShortlistView Empty { get; } = new([], []);

    /// <summary>How many picks are shown.</summary>
    public int Count => Shops.Sum(static shop => shop.Picks.Count);

    /// <summary>How many numbers were posted that the catalogue no longer shows.</summary>
    public int Gone => Asked.Count - Count;

    /// <summary>What everything comes to, at the prices seen.</summary>
    public Money Total => Money.FromPence(Shops.Sum(static shop => shop.Total.Pence));

    /// <summary>The numbers the catalogue still shows, as the browser keeps them, so it can take the others off its list.</summary>
    public string Known => string.Join(',', Shops.SelectMany(static shop => shop.Picks).Select(static pick => KitLinks.OfferOf(pick)));

    /// <summary>The numbers posted, as the browser keeps them.</summary>
    public string AskedFor => string.Join(',', Asked.Select(static id => id.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// The numbers in what was posted: at most a hundred, each once, each at most eighteen digits.
    /// Anything that is not a number is passed over, so nothing else a poster sends goes any further.
    /// </summary>
    public static IReadOnlyList<long> Parse(string? posted) =>
        [.. (posted ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static part => part.Length <= 18 && part.All(char.IsAsciiDigit))
            .Select(static part => long.Parse(part, CultureInfo.InvariantCulture))
            .Distinct()
            .Take(Most)];

    /// <summary>The list for some numbers, looked up in the catalogue.</summary>
    public static ShortlistView For(IReadOnlyList<long> asked, Func<long, KitPick?> find)
    {
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(find);
        IReadOnlyList<ShortlistShop> shops =
        [
            .. asked
                .Select(find)
                .OfType<KitPick>()
                .GroupBy(static pick => pick.Retailer, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static shop => shop.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static shop => new ShortlistShop(shop.Key, [.. shop.OrderBy(static pick => pick.Price.Pence).ThenBy(static pick => pick.Name, StringComparer.OrdinalIgnoreCase)])),
        ];
        return new ShortlistView(asked, shops);
    }
}

/// <summary>What the saved-for-later page says.</summary>
public static class ShortlistCopy
{
    /// <summary>The page's heading.</summary>
    public static string Heading => "Saved for later";

    /// <summary>The words on the button beside a pick before it is saved.</summary>
    public static string Save => "Save for later";

    /// <summary>The words on the button once it is.</summary>
    public static string Saved => "Saved for later";

    /// <summary>The words on the button that takes a pick off the list.</summary>
    public static string Remove => "Remove";

    /// <summary>What the page says before anything is saved, and how to save something.</summary>
    public static string Nothing => "Nothing is saved for later yet. On your plan, open \"Where to look\" under an action and press \"Save for later\" beside anything you want to come back to.";

    /// <summary>The line under the heading: how much is saved, from how many shops, and what it comes to.</summary>
    public static string Lede(int count, int shops, Money total) =>
        $"{Things(count)} saved from {(shops == 1 ? "1 shop" : string.Create(CultureInfo.InvariantCulture, $"{shops} shops"))}, {MoneyFormat.Amount(total)} in all at the prices we saw. The list is kept with your plan.";

    /// <summary>The line under a shop's name.</summary>
    public static string Shop(ShortlistShop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);
        return $"{Things(shop.Picks.Count)}, {MoneyFormat.Amount(shop.Total)} at the prices we saw.";
    }

    /// <summary>Said when something saved has left the catalogue.</summary>
    public static string Gone(int gone) => gone == 1
        ? "1 thing you saved is no longer in the catalogue, and has been taken off your list."
        : string.Create(CultureInfo.InvariantCulture, $"{gone} things you saved are no longer in the catalogue, and have been taken off your list.");

    /// <summary>The link on the plan to this page, before its count.</summary>
    public static string LinkBefore => "Saved for later:";

    private static string Things(int count) => count == 1 ? "1 thing" : string.Create(CultureInfo.InvariantCulture, $"{count} things");
}
