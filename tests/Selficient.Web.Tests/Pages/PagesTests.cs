using System.Net;
using System.Text.Encodings.Web;
using Selficient.TestKit;
using Selficient.Web.Pages;

namespace Selficient.Web.Tests.Pages;

public sealed class PagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task The_landing_page_shows_the_days_ready_figure_and_the_invitation()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        html.ShouldContain($"<days-ready value=\"{IndexModel.TypicalBoard.DaysReady:0.#}\"", customMessage: "the figure on the landing page is the engine's for a typical house");
        html.ShouldContain("Your house could stand on its own for");
        html.ShouldContain("Start your plan");
        html.ShouldContain("<html lang=\"en-GB\" data-sw-build=\"sw-");
        // The framework's import-map tag helper strips the type attribute if allowed to run.
        html.ShouldContain("<script type=\"importmap\" nonce=\"");
        html.ShouldContain("<script type=\"module\" src=\"/js/app.js?v=");
    }

    [Fact]
    public async Task The_styleguide_renders_in_british_english()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/styleguide", UriKind.Relative));

        html.ShouldContain("£1,234.50");
        html.ShouldNotContain("$1,234.50");
        html.ShouldContain("--c-lantern");
        html.ShouldContain("<dialog");
    }

    [Fact]
    public async Task An_unknown_address_renders_the_not_found_explanation_with_a_404()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/nothing-here", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // Razor encodes the apostrophe, so compare against the encoded sentence.
        html.ShouldContain(HtmlEncoder.Default.Encode("We couldn't find that page."));
        html.ShouldContain("Your plan is still safe in your browser.");
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
    }

    [Fact]
    public async Task The_error_page_defaults_to_a_server_error_when_no_code_is_given()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/error", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        html.ShouldContain("Something went wrong on our side.");
    }

    [Fact]
    public async Task The_error_page_explains_rate_limiting()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/error/429", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        html.ShouldContain("That was a little too fast.");
    }

    [Theory]
    [InlineData(404, "We couldn't find that page.")]
    [InlineData(429, "That was a little too fast.")]
    [InlineData(500, "Something went wrong on our side.")]
    [InlineData(503, "Something went wrong on our side.")]
    public void Every_status_has_a_whole_sentence_title(int code, string expected)
    {
        (string title, string message) = ErrorModel.Describe(code);

        title.ShouldBe(expected);
        message.ShouldEndWith(".");
    }

    [Fact]
    public async Task The_health_endpoint_answers_ok_and_nothing_else()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("ok");
    }

    [Fact]
    public async Task Static_assets_carry_the_right_cache_policy()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage css = await client.GetAsync(new Uri("/css/app.css", UriKind.Relative));
        using HttpResponseMessage js = await client.GetAsync(new Uri("/js/app.js", UriKind.Relative));
        using HttpResponseMessage font = await client.GetAsync(new Uri("/fonts/IBMPlexMono-Regular.woff2", UriKind.Relative));
        using HttpResponseMessage robots = await client.GetAsync(new Uri("/robots.txt", UriKind.Relative));

        css.Headers.CacheControl!.NoCache.ShouldBeTrue();
        js.Headers.CacheControl!.NoCache.ShouldBeTrue();
        font.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromDays(365));
        font.Headers.CacheControl.Extensions.ShouldContain(e => e.Name == "immutable");
        robots.StatusCode.ShouldBe(HttpStatusCode.OK);
        robots.Headers.CacheControl.ShouldBeNull();
    }

    [Theory]
    [InlineData("/", "br")]
    [InlineData("/css/app.css", "gzip")]
    [InlineData("/js/app.js", "br")]
    [InlineData("/manifest.webmanifest", "br")]
    [InlineData("/img/icon.svg", "br")]
    public async Task Text_responses_are_compressed_for_a_browser_that_asks(string path, string encoding)
    {
        using HttpClient client = _factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.AcceptEncoding.ParseAdd(encoding);

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldBe([encoding]);
        response.Headers.Vary.ShouldContain("Accept-Encoding");
    }

    [Fact]
    public async Task A_browser_that_does_not_ask_gets_the_page_as_it_is()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
    }

    public void Dispose() => _factory.Dispose();
}
