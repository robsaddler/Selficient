using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Selficient.Application.Accounts;
using Selficient.Infrastructure.Email;
using Selficient.TestKit;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Infrastructure.Tests.Email;

/// <summary>
/// The mail adapters, each against a stand-in for the server it talks to: a socket that speaks
/// SMTP as Papercut does, and an HTTP server that answers as Postmark does. A failure is answered
/// as false and logged with the server's reason, and never with the recipient.
/// </summary>
public sealed class EmailSendersTests : IDisposable
{
    private static readonly EmailMessage Message = SignInEmail.Compose("rob@example.com", "246810");

    private readonly WireMockServer _postmark = WireMockServer.Start();
    private readonly ListLogger<SmtpEmailSender> _smtpLog = new();
    private readonly ListLogger<PostmarkEmailSender> _postmarkLog = new();

    [Fact]
    public async Task Smtp_delivers_the_message_as_text_and_html_with_no_sign_in_as_papercut_takes_it()
    {
        using StubSmtpServer server = new();
        SmtpEmailSender sender = new(Options.Create(new EmailOptions { SmtpPort = server.Port }), _smtpLog);

        bool sent = await sender.SendAsync(Message, CancellationToken.None);

        sent.ShouldBeTrue();
        sender.IsConfigured.ShouldBeTrue();
        StubMail mail = server.Received.Single();
        mail.From.ShouldContain("<hello@selficient.app>");
        mail.To.Single().ShouldContain("<rob@example.com>");
        mail.Credentials.ShouldBeEmpty("Papercut asks for none and is given none");
        mail.Data.ShouldContain("Subject: 246810 is your Selficient sign-in code");
        mail.Data.ShouldContain("From: \"Selficient\" <hello@selficient.app>");
        mail.Data.ShouldContain("text/plain");
        mail.Data.ShouldContain("text/html");
        _smtpLog.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Smtp_signs_in_to_a_relay_that_asks_for_it()
    {
        using StubSmtpServer server = new(offersSignIn: true);
        SmtpEmailSender sender = new(Options.Create(new EmailOptions { SmtpPort = server.Port, SmtpUsername = "relay-user", SmtpPassword = "relay-password", FromName = "Selficient test", FromAddress = "codes@example.org" }), _smtpLog);

        bool sent = await sender.SendAsync(Message, CancellationToken.None);

        sent.ShouldBeTrue();
        StubMail mail = server.Received.Single();
        mail.Credentials.ShouldBe(["relay-user", "relay-password"]);
        mail.From.ShouldContain("<codes@example.org>");
    }

    [Fact]
    public async Task A_failed_send_is_logged_without_the_address()
    {
        using StubSmtpServer refusing = new(refuseRecipientsWith: "550 5.1.1 No such mailbox here");
        int closedPort = refusing.Port;
        SmtpEmailSender toRefusal = new(Options.Create(new EmailOptions { SmtpPort = refusing.Port }), _smtpLog);

        bool refused = await toRefusal.SendAsync(Message, CancellationToken.None);
        refusing.Dispose();
        SmtpEmailSender toNothing = new(Options.Create(new EmailOptions { SmtpPort = closedPort, TimeoutSeconds = 2 }), _smtpLog);
        bool unreachable = await toNothing.SendAsync(Message, CancellationToken.None);

        refused.ShouldBeFalse();
        unreachable.ShouldBeFalse();
        _smtpLog.Lines.Count.ShouldBe(2);
        _smtpLog.Lines.ShouldAllBe(line => line.StartsWith($"Error: A message could not be sent through the SMTP server at localhost:{closedPort}: ", StringComparison.Ordinal));
        _smtpLog.Lines.ShouldAllBe(line => !line.Contains("rob@example.com", StringComparison.Ordinal), "the log says what failed, never who it was for");
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => toNothing.SendAsync(Message, cancelled.Token));
        await Should.ThrowAsync<ArgumentNullException>(() => toNothing.SendAsync(null!, CancellationToken.None));
        Should.Throw<ArgumentNullException>(() => new SmtpEmailSender(null!, _smtpLog));
        Should.Throw<ArgumentNullException>(() => new SmtpEmailSender(Options.Create(new EmailOptions()), null!));
    }

    [Fact]
    public async Task Postmark_is_sent_the_message_on_the_outbound_stream()
    {
        _postmark.Given(Request.Create().WithPath("/email").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("""{"To":"rob@example.com","SubmittedAt":"2026-11-12T19:00:00Z","MessageID":"b7bc2f4a","ErrorCode":0,"Message":"OK"}"""));
        PostmarkEmailSender sender = Postmark();

        bool sent = await sender.SendAsync(Message, CancellationToken.None);

        sent.ShouldBeTrue();
        sender.IsConfigured.ShouldBeTrue();
        WireMock.Logging.ILogEntry request = _postmark.LogEntries.Single();
        string body = request.RequestMessage!.Body!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(body);
        json.RootElement.GetProperty("From").GetString().ShouldBe("Selficient <hello@selficient.app>");
        json.RootElement.GetProperty("To").GetString().ShouldBe("rob@example.com");
        json.RootElement.GetProperty("Subject").GetString().ShouldBe("246810 is your Selficient sign-in code");
        json.RootElement.GetProperty("TextBody").GetString().ShouldBe(Message.Text);
        json.RootElement.GetProperty("HtmlBody").GetString().ShouldBe(Message.Html);
        json.RootElement.GetProperty("MessageStream").GetString().ShouldBe("outbound");
        json.RootElement.EnumerateObject().Count().ShouldBe(6);
        request.RequestMessage.Headers![PostmarkEmailSender.TokenHeader].Single().ShouldBe("a-test-token", "the token is a header, so it is in no address and no body");
        body.ShouldNotContain("a-test-token");
        _postmarkLog.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refusal_from_postmark_is_logged_with_its_reason_and_without_the_address()
    {
        _postmark.Given(Request.Create().WithPath("/email").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(422).WithHeader("Content-Type", "application/json").WithBody("""{"ErrorCode":406,"Message":"You tried to send to a recipient that has been marked as inactive."}"""));
        PostmarkEmailSender sender = Postmark();

        bool sent = await sender.SendAsync(Message, CancellationToken.None);

        sent.ShouldBeFalse();
        _postmarkLog.Lines.ShouldBe(["""Error: Postmark refused a message with status 422: {"ErrorCode":406,"Message":"You tried to send to a recipient that has been marked as inactive."}"""]);

        _postmark.Reset();
        _postmark.Given(Request.Create().WithPath("/email").UsingPost()).RespondWith(Response.Create().WithStatusCode(500).WithBody(new string('x', 2000)));
        (await sender.SendAsync(Message, CancellationToken.None)).ShouldBeFalse();
        _postmarkLog.Lines[1].ShouldBe($"Error: Postmark refused a message with status 500: {new string('x', 300)}", "a long answer is cut, so a page of HTML is not a log line");
        _postmarkLog.Lines.ShouldAllBe(line => !line.Contains("rob@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Postmark_being_away_or_slow_is_a_failed_send_and_a_cancelled_one_is_cancelled()
    {
        _postmark.Given(Request.Create().WithPath("/email").UsingPost()).RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)));
        PostmarkEmailSender slow = Postmark(timeout: TimeSpan.FromMilliseconds(200));
        using HttpClient nowhere = new() { BaseAddress = new Uri("http://127.0.0.1:9/"), Timeout = TimeSpan.FromSeconds(5) };
        PostmarkEmailSender away = new(nowhere, Options.Create(new EmailOptions()), _postmarkLog);

        bool timedOut = await slow.SendAsync(Message, CancellationToken.None);
        bool unreachable = await away.SendAsync(Message, CancellationToken.None);

        timedOut.ShouldBeFalse();
        unreachable.ShouldBeFalse();
        _postmarkLog.Lines.Count.ShouldBe(2);
        _postmarkLog.Lines.ShouldAllBe(line => line.StartsWith("Error: Postmark could not be reached to send a message: ", StringComparison.Ordinal));
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => slow.SendAsync(Message, cancelled.Token));
        await Should.ThrowAsync<ArgumentNullException>(() => slow.SendAsync(null!, CancellationToken.None));
        Should.Throw<ArgumentNullException>(() => new PostmarkEmailSender(null!, Options.Create(new EmailOptions()), _postmarkLog));
        Should.Throw<ArgumentNullException>(() => new PostmarkEmailSender(nowhere, null!, _postmarkLog));
        Should.Throw<ArgumentNullException>(() => new PostmarkEmailSender(nowhere, Options.Create(new EmailOptions()), null!));
    }

    [Fact]
    public async Task No_mailer_sends_nothing_and_says_it_cannot()
    {
        NoEmailSender none = new();

        none.IsConfigured.ShouldBeFalse();
        (await none.SendAsync(Message, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public void The_provider_is_one_of_three_and_defaults_to_papercut_on_a_development_machine_only()
    {
        EmailOptions.Resolve(null, development: true).ShouldBe("Smtp", "the local machine sends through Papercut without being told to");
        EmailOptions.Resolve("", development: true).ShouldBe("Smtp");
        EmailOptions.Resolve(" ", development: false).ShouldBe("None", "a server that was told nothing sends nothing and offers no accounts");
        EmailOptions.Resolve("Postmark", development: true).ShouldBe("Postmark", "what is configured wins");
        EmailOptions defaults = new();
        defaults.SmtpHost.ShouldBe("localhost");
        defaults.SmtpPort.ShouldBe(25);
        defaults.SmtpEnableSsl.ShouldBeFalse();
        defaults.SmtpUsername.ShouldBeNull();
        defaults.PostmarkBaseUrl.ShouldBe(new Uri("https://api.postmarkapp.com/"));
        defaults.PostmarkMessageStream.ShouldBe("outbound");
        defaults.PostmarkIsComplete.ShouldBeTrue("nothing is owed when Postmark is not the provider");
        defaults.PostmarkUrlIsSafe.ShouldBeTrue();
        new EmailOptions { Provider = "Postmark" }.PostmarkIsComplete.ShouldBeFalse("Postmark without its token cannot send");
        new EmailOptions { Provider = "Postmark", PostmarkServerToken = "t" }.PostmarkIsComplete.ShouldBeTrue();
        new EmailOptions { PostmarkBaseUrl = new Uri("http://api.postmarkapp.com/") }.PostmarkUrlIsSafe.ShouldBeFalse("a token does not travel in the clear");
        new EmailOptions { PostmarkBaseUrl = new Uri("http://localhost:5001/") }.PostmarkUrlIsSafe.ShouldBeTrue("a stand-in on this machine may");
        new EmailOptions { PostmarkBaseUrl = new Uri("/email", UriKind.Relative) }.PostmarkUrlIsSafe.ShouldBeFalse();
        new EmailOptions { PostmarkBaseUrl = new Uri("ftp://api.postmarkapp.com/") }.PostmarkUrlIsSafe.ShouldBeFalse();
        EmailOptions.SectionName.ShouldBe("Email");
    }

    public void Dispose() => _postmark.Dispose();

    private PostmarkEmailSender Postmark(TimeSpan? timeout = null)
    {
        HttpClient client = new() { BaseAddress = new Uri(_postmark.Url! + "/"), Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add(PostmarkEmailSender.TokenHeader, "a-test-token");
        return new PostmarkEmailSender(client, Options.Create(new EmailOptions()), _postmarkLog);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
            {
                Lines.Add($"{logLevel}: {formatter(state, exception)}");
            }
        }
    }
}
