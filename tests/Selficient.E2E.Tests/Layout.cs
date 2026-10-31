using System.Globalization;
using Microsoft.Playwright;

namespace Selficient.E2E.Tests;

/// <summary>
/// The design, asserted rather than looked at. A desktop screenshot catches a drifted layout only
/// when a person opens it, and the landing page shipped for a day in its phone column because a
/// doubled <c>@media</c> token made the browser drop the whole desktop block without a word. These
/// tests pin the two things that made the mockup read the way it did: the hero stands in two
/// columns at the mockup's seven-to-five split, and the Days Ready figure is set in the light,
/// narrow face the subset was cut for.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class Layout
{
    private readonly Browser _browser;

    public Layout(Browser browser) => _browser = browser;

    [Fact]
    public async Task The_desktop_hero_stands_in_two_columns_at_the_mockups_split()
    {
        await using IBrowserContext context = await _browser.SizedAsync(1440, 900, mobile: false, theme: "light");
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");

        string columns = await page.Locator(".hero").EvaluateAsync<string>("element => getComputedStyle(element).gridTemplateColumns");
        double[] tracks = [.. columns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(track => double.Parse(track.TrimEnd('p', 'x'), CultureInfo.InvariantCulture))];

        tracks.Length.ShouldBe(2, customMessage: $"the desktop hero is the words beside the kitchen table, and the browser resolved '{columns}'");
        (tracks[0] / tracks[1]).ShouldBe(7d / 5d, 0.05, "the words take seven twelfths of the row and the table five");
        double table = await page.Locator(".hero .still-life").EvaluateAsync<double>("element => element.getBoundingClientRect().width");
        table.ShouldBeGreaterThan(tracks[1] * 0.9, customMessage: $"the still life fills its column rather than sitting small inside it: it is {table:0} px in a column of {tracks[1]:0} px");
    }

    [Fact]
    public async Task The_phone_hero_is_one_column()
    {
        await using IBrowserContext context = await _browser.SizedAsync(390, 844, mobile: true, theme: "light");
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");

        string columns = await page.Locator(".hero").EvaluateAsync<string>("element => getComputedStyle(element).gridTemplateColumns");

        columns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1, customMessage: "on a phone the table sits under the words");
    }

    [Fact]
    public async Task The_days_ready_figure_is_set_in_the_light_narrow_face_the_subset_carries()
    {
        await using IBrowserContext context = await _browser.SizedAsync(1440, 900, mobile: false, theme: "light");
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForFunctionAsync("() => document.fonts.status === 'loaded'");

        ILocator figure = page.Locator(".days-ready__figure").First;
        string weight = await figure.EvaluateAsync<string>("element => getComputedStyle(element).fontWeight");
        string stretch = await figure.EvaluateAsync<string>("element => getComputedStyle(element).fontStretch");
        double size = await figure.EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).fontSize)");
        // The declaration is only half of it: a subset cut without the axes renders every weight and
        // width identically, which is how the figure came out heavy and narrow against the mockup.
        // Two spans in the page's own display face, measured, are the proof that the axes are there.
        double[] widths = await page.EvaluateAsync<double[]>(
            """
            () => {
              const family = getComputedStyle(document.querySelector('.days-ready__figure')).fontFamily;
              const measure = (weight, stretch) => {
                const span = document.createElement('span');
                span.textContent = '888';
                span.style.position = 'absolute';
                span.style.visibility = 'hidden';
                span.style.fontFamily = family;
                span.style.fontSize = '200px';
                span.style.fontWeight = weight;
                span.style.fontStretch = stretch;
                document.body.append(span);
                const width = span.getBoundingClientRect().width;
                span.remove();
                return width;
              };
              return [measure('300', '75%'), measure('800', '75%'), measure('300', '100%')];
            }
            """);

        weight.ShouldBe("300");
        stretch.ShouldBe("75%");
        size.ShouldBeGreaterThan(110, "the figure is the page: at least seven rem of it");
        widths[0].ShouldBeLessThan(widths[1], "the subset carries the weight axis, so light is narrower than heavy");
        widths[0].ShouldBeLessThan(widths[2], "the subset carries the width axis, so seventy-five per cent is narrower than full");
    }
}
