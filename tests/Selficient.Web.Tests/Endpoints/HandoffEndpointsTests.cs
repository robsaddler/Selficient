using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Selficient.Application.Handoff;
using Selficient.TestKit;
using Selficient.Web.Endpoints;
using Selficient.Web.Pages;

namespace Selficient.Web.Tests.Endpoints;

/// <summary>
/// The relay behind "Send a copy", through the real pipeline and a real database. The cases are
/// the ones in <c>docs/threat-models/handoff.md</c>.
/// </summary>
public sealed class HandoffEndpointsTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task A_copy_is_left_collected_once_and_never_cached()
    {
        using HttpClient client = _factory.CreateClient();
        byte[] copy = [.. Enumerable.Range(0, 900).Select(i => (byte)(i * 7))];

        using HttpResponseMessage left = await client.PostAsync(new Uri("/api/handoff", UriKind.Relative), Body(copy));
        HandoffTicket ticket = (await left.Content.ReadFromJsonAsync<HandoffTicket>())!;
        using HttpResponseMessage first = await client.GetAsync(new Uri($"/api/handoff/{ticket.Id}", UriKind.Relative));
        byte[] collected = await first.Content.ReadAsByteArrayAsync();
        using HttpResponseMessage second = await client.GetAsync(new Uri($"/api/handoff/{ticket.Id}", UriKind.Relative));
        Sentence gone = (await second.Content.ReadFromJsonAsync<Sentence>())!;

        left.StatusCode.ShouldBe(HttpStatusCode.Created);
        left.Headers.Location!.ToString().ShouldBe($"/api/handoff/{ticket.Id}");
        left.Headers.CacheControl!.NoStore.ShouldBeTrue();
        left.Headers.Contains("Set-Cookie").ShouldBeFalse("there is no session on the relay");
        ticket.Id.ShouldMatch("^[A-Za-z0-9_-]{22}$");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
        first.Headers.CacheControl!.NoStore.ShouldBeTrue("a copy is never kept by any cache");
        collected.ShouldBe(copy);
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound, "a link works once");
        second.Headers.CacheControl!.NoStore.ShouldBeTrue();
        gone.Message.ShouldBe("That link has already been used or has run out. Send a copy again from the other device.");
        gone.Message.ShouldBe(HandoffEndpoints.Gone);
    }

    [Fact]
    public async Task The_sender_can_ask_whether_the_copy_is_still_waiting()
    {
        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage left = await client.PostAsync(new Uri("/api/handoff", UriKind.Relative), Body(new byte[64]));
        string id = (await left.Content.ReadFromJsonAsync<HandoffTicket>())!.Id;

        using HttpResponseMessage asked = await client.GetAsync(new Uri($"/api/handoff/{id}/waiting", UriKind.Relative));
        HandoffWaiting before = (await asked.Content.ReadFromJsonAsync<HandoffWaiting>())!;
        HandoffWaiting again = (await client.GetFromJsonAsync<HandoffWaiting>(new Uri($"/api/handoff/{id}/waiting", UriKind.Relative)))!;
        using HttpResponseMessage collected = await client.GetAsync(new Uri($"/api/handoff/{id}", UriKind.Relative));
        HandoffWaiting after = (await client.GetFromJsonAsync<HandoffWaiting>(new Uri($"/api/handoff/{id}/waiting", UriKind.Relative)))!;
        HandoffWaiting never = (await client.GetFromJsonAsync<HandoffWaiting>(new Uri("/api/handoff/not-an-id/waiting", UriKind.Relative)))!;

        asked.Headers.CacheControl!.NoStore.ShouldBeTrue();
        before.Waiting.ShouldBeTrue();
        again.Waiting.ShouldBeTrue("asking does not collect");
        collected.StatusCode.ShouldBe(HttpStatusCode.OK);
        after.Waiting.ShouldBeFalse();
        never.Waiting.ShouldBeFalse();
    }

    [Fact]
    public async Task A_body_over_the_cap_is_refused_however_it_is_declared()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage atCap = await client.PostAsync(new Uri("/api/handoff", UriKind.Relative), Body(new byte[HandoffDesk.MaxBytes]));
        using HttpResponseMessage over = await client.PostAsync(new Uri("/api/handoff", UriKind.Relative), Body(new byte[HandoffDesk.MaxBytes + 1]));
        using HttpResponseMessage chunked = await client.PostAsync(new Uri("/api/handoff", UriKind.Relative), new UndeclaredContent(HandoffDesk.MaxBytes * 3));
        Sentence sentence = (await over.Content.ReadFromJsonAsync<Sentence>())!;

        atCap.StatusCode.ShouldBe(HttpStatusCode.Created);
        over.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        sentence.Message.ShouldBe("That copy is too large to send. Use \"Export as a file\" on the plan instead.");
        chunked.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge, "the cap is on what arrives, not on the length a client declares");
        (await HandoffEndpoints.ReadCappedAsync(new MemoryStream(new byte[10]), 10, CancellationToken.None))!.Length.ShouldBe(10);
        (await HandoffEndpoints.ReadCappedAsync(new MemoryStream(new byte[11]), 10, CancellationToken.None)).ShouldBeNull();
        (await HandoffEndpoints.ReadCappedAsync(new MemoryStream(), 10, CancellationToken.None))!.Length.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(28)]
    public async Task A_body_too_short_to_be_a_copy_is_refused(int length)
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/handoff", UriKind.Relative), Body(new byte[length]));
        Sentence sentence = (await response.Content.ReadFromJsonAsync<Sentence>())!;

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        sentence.Message.ShouldBe("That is not a copy of a plan. Send a copy again from the other device.");
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("short")]
    [InlineData("%27%3B%20DROP%20TABLE%20handoffs")]
    public async Task An_id_nothing_was_left_under_is_a_404_with_a_sentence(string id)
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri($"/api/handoff/{id}", UriKind.Relative));
        using HttpResponseMessage posted = await client.PostAsync(new Uri($"/api/handoff/{id}", UriKind.Relative), Body(new byte[64]));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<Sentence>())!.Message.ShouldBe(HandoffEndpoints.Gone);
        posted.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed, "nobody chooses an id");
    }

    [Fact]
    public async Task Leaving_and_collecting_are_rate_limited_on_a_policy_of_their_own()
    {
        using SelficientWebApplicationFactory limited = new SelficientWebApplicationFactory().WithSetting("RateLimits:HandoffPerMinute", "3");
        using HttpClient client = limited.CreateClient();
        List<HttpStatusCode> statuses = [];

        for (int i = 0; i < 2; i++)
        {
            using HttpResponseMessage left = await client.PostAsync(new Uri("/api/handoff", UriKind.Relative), Body(new byte[64]));
            statuses.Add(left.StatusCode);
        }

        using HttpResponseMessage collected = await client.GetAsync(new Uri("/api/handoff/AAAAAAAAAAAAAAAAAAAAAA", UriKind.Relative));
        using HttpResponseMessage refusedPost = await client.PostAsync(new Uri("/api/handoff", UriKind.Relative), Body(new byte[64]));
        using HttpResponseMessage refusedGet = await client.GetAsync(new Uri("/api/handoff/AAAAAAAAAAAAAAAAAAAAAA", UriKind.Relative));
        using HttpResponseMessage waiting = await client.GetAsync(new Uri("/api/handoff/AAAAAAAAAAAAAAAAAAAAAA/waiting", UriKind.Relative));
        using HttpResponseMessage page = await client.GetAsync(new Uri("/", UriKind.Relative));

        statuses.ShouldBe([HttpStatusCode.Created, HttpStatusCode.Created]);
        collected.StatusCode.ShouldBe(HttpStatusCode.NotFound, "the third of three");
        refusedPost.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refusedGet.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, "guessing ids spends the same allowance");
        waiting.StatusCode.ShouldBe(HttpStatusCode.OK, "the sender's question is on the general policy, so a waiting screen is never the thing that locks it out");
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_full_desk_is_a_503_with_a_sentence()
    {
        IHandoffStore full = Substitute.For<IHandoffStore>();
        full.CountAsync(Arg.Any<CancellationToken>()).Returns(HandoffDesk.MaxWaiting);
        using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory().Configuring(builder => builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(full))));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/handoff", UriKind.Relative), Body(new byte[64]));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<Sentence>())!.Message.ShouldBe("Too many copies are waiting to be collected at the moment. Try again in a few minutes.");
    }

    [Fact]
    public async Task The_receive_page_sends_no_referrer_and_is_kept_out_of_search()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/receive", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse("a guest gets no cookie for receiving a copy");
        html.ShouldContain("<meta name=\"robots\" content=\"noindex, nofollow\">");
        html.ShouldContain("<section class=\"notice receive\" data-receive=\"nothing\" aria-live=\"polite\">");
        // Without script, and without a link, the page says what it is for and nothing else is shown.
        html.ShouldContain("<div class=\"receive__state\" data-receive-state=\"nothing\">");
        html.ShouldContain("<h1 tabindex=\"-1\">Receive a plan from another device</h1>");
        html.ShouldContain("This page receives a copy of a plan sent from another device. On the device that has your plan, open &quot;Where your plan is kept&quot; at the top of any page and choose &quot;Send a copy&quot;.");
        ReceiveCopy.State[] hidden = [ReceiveCopy.Reading, ReceiveCopy.Arrived, ReceiveCopy.Choose, ReceiveCopy.Later, .. ReceiveCopy.Endings];
        foreach (ReceiveCopy.State state in hidden)
        {
            html.ShouldContain($"<div class=\"receive__state\" data-receive-state=\"{state.Key}\" hidden>");
            html.ShouldContain($"<h1 tabindex=\"-1\">{state.Heading}</h1>");
            html.ShouldContain($"<p>{state.Sentence}</p>");
        }

        html.ShouldContain("<button type=\"button\" class=\"button button--primary\" data-receive-put>Put this plan on this device</button>");
        html.ShouldContain("<button type=\"button\" class=\"button button--quiet\" data-receive-keep>Do not put it here</button>");
        html.ShouldContain("<button type=\"button\" class=\"button button--primary\" data-receive-put>Replace it with the one I sent</button>");
        html.ShouldContain("<button type=\"button\" class=\"button\" data-receive-keep>Keep the one that is here</button>");
        ReceiveCopy.Choose.Sentence.ShouldBe("Replace it with the one you sent, or keep the one that is here. The two are never mixed.");
        ReceiveCopy.Done.Sentence.ShouldBe("This device has its own copy now. Changes you make here will not appear on the other device, and changes there will not appear here.");
        ReceiveCopy.Used.Sentence.ShouldBe("A link works once, for ten minutes, and this one has been used or has run out. Send a copy again from the other device.");
        ReceiveCopy.Later.Sentence.ShouldBe("The copy could not be collected just now, and it has not been used up. Check the connection and try again within ten minutes of making it.");
        ReceiveCopy.Endings.Select(s => s.Key).ShouldBe(["done", "kept", "used", "incomplete", "unreadable"]);
    }

    public void Dispose() => _factory.Dispose();

    private static ByteArrayContent Body(byte[] bytes)
    {
        ByteArrayContent content = new(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    /// <summary>A body that does not say how long it is, as a chunked upload does not.</summary>
    private sealed class UndeclaredContent(int length) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            byte[] chunk = new byte[8192];
            for (int sent = 0; sent < length; sent += chunk.Length)
            {
                await stream.WriteAsync(chunk);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
