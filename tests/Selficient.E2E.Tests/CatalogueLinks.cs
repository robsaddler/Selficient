using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Selficient.Domain.Kit;
using Selficient.Infrastructure.Kit;

namespace Selficient.E2E.Tests;

/// <summary>
/// Every link in the kit catalogue, opened in a real browser, must land on the item: a page that
/// answers, that does not say it found nothing, and that names what the pick names. A page that does
/// all that and no longer shows the pick's price anywhere is reported as drifted: the link has not
/// failed, but the pick is no longer something anybody has seen, and it is the list of what to open
/// by hand. (Every link "landed" on 13 Nov 2026 while a third of the picks named a product or a
/// price the page did not show; the search box echoes the words whatever the shop sells.) This is the
/// commercial surface of the site, so it runs on a schedule as well as on demand, and never in
/// the ordinary gate because it depends on thirty-odd shops being up. The report it writes is the
/// list of what to fix.
/// </summary>
[Trait("Category", "Links")]
public sealed partial class CatalogueLinks : IAsyncLifetime
{
    private static readonly string[] Stopwords = ["with", "and", "the", "for", "pack", "of", "litre", "litres", "x", "a", "an", "on", "to", "kit", "home", "cells", "metre"];
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }

    [Fact]
    public async Task Every_pick_link_lands_on_the_item()
    {
        EmbeddedKitCatalogue catalogue = EmbeddedKitCatalogue.Load(new AffiliateOptions());
        await using IBrowserContext context = await _browser!.NewContextAsync(new()
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
            Locale = "en-GB",
            ViewportSize = new() { Width = 1280, Height = 900 },
        });

        List<(KitPick Pick, string Verdict, string Detail)> results = [];
        foreach (KitPick pick in catalogue.All)
        {
            results.Add(await CheckAsync(context, pick));
        }

        string report = Report(results);
        string folder = Path.Combine(SolutionRoot(), "artifacts", "links");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "report.md"), report);

        results.Where(r => r.Verdict == "fail").ShouldBeEmpty(report);
    }

    private static async Task<(KitPick, string, string)> CheckAsync(IBrowserContext context, KitPick pick, bool retried = false)
    {
        IPage page = await context.NewPageAsync();
        try
        {
            IResponse? response = await page.GotoAsync(pick.Link.ToString(), new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(4_000);
            // Shops throttle a browser that asks for many pages a minute; a pause between visits keeps the check honest.
            await Task.Delay(pick.Link.Host.Contains("amazon", StringComparison.Ordinal) ? 6_000 : 1_500);
            int status = response?.Status ?? 0;
            string title = await page.TitleAsync();
            string text = await page.EvaluateAsync<string>("() => (document.body && document.body.innerText) ? document.body.innerText.slice(0, 80000) : ''");
            string flat = Whitespace().Replace(text, " ");

            if (RobotWall().IsMatch(flat[..Math.Min(flat.Length, 3000)]) || status == 503)
            {
                // The shop showed a browser that is not a person its robot page. The link is not wrong; this run cannot see past it.
                return (pick, "blocked", $"the shop walled off the check ({Trim(title)}); open it by hand");
            }

            if (status >= 400)
            {
                return (pick, "fail", $"HTTP {status}");
            }

            // A shop that counts its results has spoken; only a page with no count is read for a "nothing found".
            bool counted = ResultsCounted().IsMatch(title) || ResultsCounted().IsMatch(flat[..Math.Min(flat.Length, 3000)]);
            if (!counted && (NoResults().IsMatch(flat[..Math.Min(flat.Length, 6000)]) || NoResults().IsMatch(title)))
            {
                return (pick, "fail", $"the page says it found nothing ({Trim(title)}; the page begins: {flat[..Math.Min(flat.Length, 240)]})");
            }

            string[] words = Words(pick.Name);
            string[] missing = words.Where(word => !flat.Contains(word, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (words.Length > 0 && missing.Length == words.Length)
            {
                return (pick, "fail", $"nothing on the page names the item (looked for {string.Join(", ", words)}; title {Trim(title)}; the page begins: {flat[..Math.Min(flat.Length, 240)]})");
            }

            if (!PriceShown(flat, pick.Price.Pence))
            {
                return (pick, "drift", $"the page does not show {Pounds(pick.Price.Pence)} any more; open it and see what the item costs now ({Trim(title)})");
            }

            return (pick, "ok", $"{Trim(title)}; {page.Url}");
        }
        catch (PlaywrightException exception)
        {
            return (pick, "fail", $"did not load: {exception.Message.Split('\n')[0]}");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>Whether the page shows the price the pick claims, as a shop writes it: £39, £39.00, £1,049 or £1049.</summary>
    internal static bool PriceShown(string text, long pence)
    {
        long pounds = pence / 100;
        string grouped = pounds.ToString("N0", CultureInfo.GetCultureInfo("en-GB"));
        string plain = pounds.ToString(CultureInfo.InvariantCulture);
        string pennies = (pence % 100).ToString("00", CultureInfo.InvariantCulture);
        string[] whole = pence % 100 == 0 ? [$"£{grouped}", $"£{plain}"] : [];
        string[] exact = [$"£{grouped}.{pennies}", $"£{plain}.{pennies}", $"£ {plain}.{pennies}"];
        return exact.Any(text.Contains) || whole.Any(form => Regex.IsMatch(text, Regex.Escape(form) + @"(?!\d|[,.]\d)"));
    }

    private static string Pounds(long pence) => (pence / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-GB"));

    private static string SolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Selficient.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static string[] Words(string name) =>
        name.Split([' ', ',', '+', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('.', '(', ')'))
            .Where(word => word.Length > 2 && !Stopwords.Contains(word, StringComparer.OrdinalIgnoreCase) && !word.All(char.IsDigit))
            .Take(3)
            .ToArray();

    private static string Trim(string title) => title.Length > 70 ? title[..70] + "…" : title;

    private static string Report(IReadOnlyList<(KitPick Pick, string Verdict, string Detail)> results)
    {
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"# Catalogue links, checked {DateTime.UtcNow:d MMM yyyy HH:mm} UTC");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"{results.Count(r => r.Verdict == "ok")} of {results.Count} land on the item; {results.Count(r => r.Verdict == "blocked")} could not be seen past a robot wall and need a hand; {results.Count(r => r.Verdict == "drift")} no longer show the price the pick gives and need opening.");
        report.AppendLine();
        report.AppendLine("| | Action | Pick | Shop | Detail |");
        report.AppendLine("|---|---|---|---|---|");
        foreach ((KitPick pick, string verdict, string detail) in results)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"| {verdict.ToUpperInvariant()} | {pick.Action.Value} | [{pick.Name}]({pick.Link}) | {pick.Retailer} | {detail.Replace("|", "/", StringComparison.Ordinal)} |");
        }

        return report.ToString();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\b[1-9]\d* (results?|items?|products?)( found)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResultsCounted();

    [GeneratedRegex(@"not a robot|enter the characters you see|human verification|access denied|verify you are human|unusual traffic", RegexOptions.IgnoreCase)]
    private static partial Regex RobotWall();

    [GeneratedRegex(@"no results for|no results found|b0 results|couldn.t find any|didn.t find any|nothing found|no matches|no products found|0 items|0 products|page not found|we can.t find", RegexOptions.IgnoreCase)]
    private static partial Regex NoResults();
}
