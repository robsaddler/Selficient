using System.Net;
using Selficient.Application.Catalogue;
using Selficient.Infrastructure.Catalogue;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Infrastructure.Tests.Catalogue;

public sealed class HttpPageReaderTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private static HttpPageReader Reader(int pageBytes = 64 * 1024, int timeoutSeconds = 30) =>
        new(new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) }, pageBytes);

    private Uri At(string path) => new(_server.Url! + path);

    [Fact]
    public void The_reader_needs_a_client_and_room_for_a_page()
    {
        Should.Throw<ArgumentNullException>(() => new HttpPageReader(null!, 65536));
        Should.Throw<ArgumentOutOfRangeException>(() => new HttpPageReader(new HttpClient(), 1023));
    }

    [Fact]
    public async Task A_page_is_read_as_its_status_its_title_and_its_text_without_scripts_or_tags()
    {
        _server.Given(Request.Create().WithPath("/s").WithParam("k", "Petzl Tikkina").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/html; charset=utf-8")
                .WithBody("""
                    <html><head><title>  Amazon.co.uk : Petzl&nbsp;Tikkina
                    </title><style>.x{color:red}</style><script>var a = "<b>not text</b>";</script></head>
                    <body><noscript>Turn on script</noscript><svg><text>drawn</text></svg>
                    <h1>Results</h1><p>Petzl Tikkina headtorch &pound;22.00 &amp; free delivery</p>
                    <div>In   stock</div></body></html>
                    """));

        PageRead page = await Reader().ReadAsync(At("/s?k=Petzl%20Tikkina"), CancellationToken.None);

        page.Status.ShouldBe(200);
        page.Title.ShouldBe("Amazon.co.uk : Petzl Tikkina");
        page.Text.ShouldBe("Amazon.co.uk : Petzl Tikkina Results Petzl Tikkina headtorch £22.00 & free delivery In stock");
        page.Final.ShouldBe(At("/s?k=Petzl%20Tikkina"));
        WireMock.IRequestMessage request = _server.LogEntries.Single().RequestMessage.ShouldNotBeNull();
        request.Headers!["Accept"].Single().ShouldStartWith("text/html");
        request.Headers["Accept-Language"].Single().ShouldStartWith("en-GB");
    }

    [Fact]
    public async Task A_redirect_is_followed_and_the_final_address_is_the_one_reported()
    {
        _server.Given(Request.Create().WithPath("/old").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", _server.Url! + "/new"));
        _server.Given(Request.Create().WithPath("/new").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("<html><body>Moved here</body></html>"));

        PageRead page = await Reader().ReadAsync(At("/old"), CancellationToken.None);

        page.Status.ShouldBe(200);
        page.Text.ShouldBe("Moved here");
        page.Final.ShouldBe(At("/new"));
    }

    [Fact]
    public async Task An_error_page_keeps_its_status_and_a_page_with_no_title_has_none()
    {
        _server.Given(Request.Create().WithPath("/gone").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404).WithBody("<p>Page not found</p>"));

        PageRead page = await Reader().ReadAsync(At("/gone"), CancellationToken.None);

        page.Status.ShouldBe(404);
        page.Title.ShouldBeEmpty();
        page.Text.ShouldBe("Page not found");
    }

    [Fact]
    public async Task Only_the_first_part_of_a_long_page_is_read()
    {
        string body = "<html><title>Long</title><body>" + new string('a', 200_000) + "</body></html>";
        _server.Given(Request.Create().WithPath("/long").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body));

        PageRead page = await Reader(pageBytes: 16 * 1024).ReadAsync(At("/long"), CancellationToken.None);

        page.Status.ShouldBe(200);
        page.Title.ShouldBe("Long");
        page.Text.Length.ShouldBeLessThanOrEqualTo(16 * 1024);
        page.Text.Length.ShouldBeGreaterThan(15_000);
    }

    [Fact]
    public async Task A_shop_that_does_not_answer_or_takes_too_long_reads_as_status_nought()
    {
        _server.Given(Request.Create().WithPath("/slow").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)).WithBody("late"));
        using WireMockServer closed = WireMockServer.Start();
        Uri nowhere = new(closed.Url! + "/x");
        closed.Stop();

        PageRead refused = await Reader().ReadAsync(nowhere, CancellationToken.None);
        PageRead slow = await Reader(timeoutSeconds: 1).ReadAsync(At("/slow"), CancellationToken.None);

        refused.ShouldBe(new PageRead(0, "", "", nowhere));
        slow.ShouldBe(new PageRead(0, "", "", At("/slow")));
        await Should.ThrowAsync<ArgumentNullException>(() => Reader().ReadAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void The_final_address_falls_back_to_the_one_asked_for_when_the_response_has_no_request()
    {
        Uri asked = new("https://shop.example/item");
        using HttpResponseMessage bare = new(HttpStatusCode.OK);
        using HttpRequestMessage request = new(HttpMethod.Get, "https://shop.example/moved");
        using HttpResponseMessage withRequest = new(HttpStatusCode.OK) { RequestMessage = request };

        HttpPageReader.FinalAddress(bare, asked).ShouldBe(asked);
        HttpPageReader.FinalAddress(withRequest, asked).ShouldBe(new Uri("https://shop.example/moved"));
    }

    [Theory]
    [InlineData("<TITLE lang=\"en\">Upper &amp; Case</TITLE>", "Upper & Case")]
    [InlineData("no title here", "")]
    [InlineData("<title></title>", "")]
    public void Titles_are_found_whatever_the_case_and_decoded(string html, string title) => HttpPageReader.Title(html).ShouldBe(title);

    [Fact]
    public void Flattening_strips_everything_but_the_words() =>
        HttpPageReader.Flatten("<div>\n  A <b>bold</b>&nbsp;word <script type=\"x\">gone()</script>\t<STYLE>p{}</STYLE> end </div>").ShouldBe("A bold word end");

    public void Dispose() => _server.Dispose();
}
