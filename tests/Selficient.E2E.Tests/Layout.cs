using System.Globalization;
using Microsoft.Playwright;
using Selficient.TestKit;

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

    // A laptop and the 2000px window Rob reviews on: the design has to hold at both, and the second
    // is where a page that stops at a fixed column width shows a third of the screen as empty ground.
    [Theory]
    [InlineData(1440)]
    [InlineData(2000)]
    public async Task The_desktop_hero_stands_in_two_columns_at_the_mockups_split(int width)
    {
        await using IBrowserContext context = await _browser.SizedAsync(width, 900, mobile: false, theme: "light");
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");

        string columns = await page.Locator(".hero").EvaluateAsync<string>("element => getComputedStyle(element).gridTemplateColumns");
        double[] tracks = [.. columns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(track => double.Parse(track.TrimEnd('p', 'x'), CultureInfo.InvariantCulture))];

        double page_ = await page.Locator(".hero").EvaluateAsync<double>("element => element.getBoundingClientRect().width");

        tracks.Length.ShouldBe(2, customMessage: $"the desktop hero is the words beside the kitchen table, and the browser resolved '{columns}'");
        (page_ / width).ShouldBeGreaterThan(0.84, customMessage: $"the column grows with the window rather than stopping dead: it is {page_:0} px of a {width} px window");
        (tracks[0] / tracks[1]).ShouldBe(7d / 5d, 0.05, "the words take seven twelfths of the row and the table five");
        double table = await page.Locator(".hero .still-life").EvaluateAsync<double>("element => element.getBoundingClientRect().width");
        table.ShouldBeGreaterThan(tracks[1] * 0.9, customMessage: $"the still life fills its column rather than sitting small inside it: it is {table:0} px in a column of {tracks[1]:0} px");
    }

    [Fact]
    public async Task The_questions_start_level_with_each_other_however_many_columns_they_take()
    {
        // A per-item margin that was only cleared for items following another left the first
        // question in one column a margin lower than the first in the next. The list is a grid and
        // its gap owns the spacing, so every item in the first row starts at the same height.
        foreach (int width in new[] { 1440, 2000 })
        {
            await using IBrowserContext context = await _browser.SizedAsync(width, 900, mobile: false, theme: "light");
            IPage page = await context.NewPageAsync();
            await page.GotoAsync("/");

            // The top of the first item in each column, whatever the column count turns out to be.
            double[] topOfEachColumn = await page.EvalOnSelectorAllAsync<double[]>(
                ".faq__item",
                """
                items => {
                  const byColumn = new Map();
                  for (const item of items) {
                    const box = item.getBoundingClientRect();
                    const left = Math.round(box.left);
                    const top = Math.round(box.top);
                    if (!byColumn.has(left) || top < byColumn.get(left)) {
                      byColumn.set(left, top);
                    }
                  }
                  return [...byColumn.values()];
                }
                """);

            topOfEachColumn.Length.ShouldBeGreaterThan(0);
            topOfEachColumn.Distinct().Count().ShouldBe(1, customMessage: $"at {width}px the columns of questions start at {string.Join(", ", topOfEachColumn)}");
        }
    }

    [Fact]
    public async Task No_button_on_the_operators_pages_sits_against_the_box_above_it()
    {
        // Twice now a submit button has ended up hard against the box above it, because the panel's
        // own gap reaches only its direct children and a nested form had nothing but a small margin.
        // Every button on these pages is measured against whatever sits above it instead.
        await _browser.Application.CreateOperatorAsync(username: "spacing", enrolled: false);
        await using IBrowserContext context = await _browser.SizedAsync(1440, 900, mobile: false, theme: "light");
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/admin/sign-in");
        await AssertRoomAboveEveryButtonAsync(page, "the sign-in");

        await page.Locator("#Username").FillAsync("spacing");
        await page.Locator("#Password").FillAsync(OperatorSignInHelper.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await page.Locator(".enrol__qr svg").WaitForAsync();
        await AssertRoomAboveEveryButtonAsync(page, "the enrolment");
    }

    private static async Task AssertRoomAboveEveryButtonAsync(IPage page, string which)
    {
        // The smallest vertical gap between a submit button and whatever element ends just above it.
        double[] gaps = await page.EvalOnSelectorAllAsync<double[]>(
            ".panel button[type=submit]",
            """
            buttons => buttons.map(button => {
              const box = button.getBoundingClientRect();
              let nearest = Infinity;
              for (const other of document.querySelectorAll('.panel input, .panel label, .panel p, .panel h1, .panel h2')) {
                const above = other.getBoundingClientRect();
                if (above.height === 0 || above.bottom > box.top) { continue; }
                nearest = Math.min(nearest, box.top - above.bottom);
              }
              return nearest === Infinity ? 999 : Math.round(nearest);
            })
            """);

        gaps.Length.ShouldBeGreaterThan(0, $"{which} has a button to measure");
        foreach (double gap in gaps)
        {
            gap.ShouldBeGreaterThanOrEqualTo(12, customMessage: $"on {which} a button sits {gap} px under the thing above it, which reads as touching");
        }
    }

    [Theory]
    [InlineData("/plan/solar")]
    [InlineData("/plan/start")]
    public async Task No_text_box_is_stretched_taller_than_a_line_of_typing_needs(string path)
    {
        // A field beside a taller neighbour stretched its rows and the number boxes on the solar
        // form grew to several times their height. Every single-line box is measured instead.
        foreach (int width in new[] { 1440, 2000 })
        {
            await using IBrowserContext context = await _browser.SizedAsync(width, 900, mobile: false, theme: "light");
            IPage page = await context.NewPageAsync();
            await page.GotoAsync(path);

            double[] heights = await page.EvalOnSelectorAllAsync<double[]>(
                "input.input, select.input",
                "boxes => boxes.map(box => Math.round(box.getBoundingClientRect().height)).filter(height => height > 0)");

            heights.Length.ShouldBeGreaterThan(0);
            heights.Max().ShouldBeLessThanOrEqualTo(60, customMessage: $"on {path} at {width}px the boxes are {string.Join(", ", heights)} px tall");
        }
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

    [Theory]
    [InlineData(1440)]
    [InlineData(2000)]
    public async Task The_days_ready_figure_is_set_in_the_light_narrow_face_the_subset_carries(int width)
    {
        await using IBrowserContext context = await _browser.SizedAsync(width, 900, mobile: false, theme: "light");
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

        double column = await page.Locator(".hero__copy").EvaluateAsync<double>("element => element.getBoundingClientRect().width");

        weight.ShouldBe("300");
        stretch.ShouldBe("75%");
        size.ShouldBeGreaterThan(110, customMessage: $"the figure is the page: at least seven rem of it, and it is {size:0} px");
        // Sized against its column rather than the window, so a wide monitor does not leave the hero
        // half empty. The mockup drew it at about a quarter of the column.
        (size / column).ShouldBeGreaterThan(0.2, customMessage: $"the figure is {size:0} px in a {column:0} px column, which is {size / column:P0} of it");
        widths[0].ShouldBeLessThan(widths[1], "the subset carries the weight axis, so light is narrower than heavy");
        widths[0].ShouldBeLessThan(widths[2], "the subset carries the width axis, so seventy-five per cent is narrower than full");
    }
}
