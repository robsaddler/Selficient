using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Selficient.Application.Accounts;

namespace Selficient.Infrastructure.Email;

/// <summary>No mail. The server offers no accounts and the sign-in page says so.</summary>
public sealed class NoEmailSender : IEmailSender
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.FromResult(false);
}

/// <summary>
/// Mail by SMTP: the local machine's Papercut, or any relay. A failure is logged with the server's
/// reason and never with the recipient, and answered as false so the page can say the code was not
/// sent.
/// </summary>
public sealed partial class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    /// <summary>Creates the sender.</summary>
    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        bool sent = false;
        try
        {
            using MailMessage mail = new()
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = message.Subject,
                SubjectEncoding = Encoding.UTF8,
                Body = message.Text,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false,
            };
            mail.To.Add(new MailAddress(message.To));
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Html, Encoding.UTF8, "text/html"));
            using SmtpClient client = new(_options.SmtpHost, _options.SmtpPort)
            {
                EnableSsl = _options.SmtpEnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Timeout = _options.TimeoutSeconds * 1000,
            };
            if (!string.IsNullOrEmpty(_options.SmtpUsername))
            {
                client.Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword);
            }

            await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
            sent = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Failed(_logger, _options.SmtpHost, _options.SmtpPort, exception.Message);
        }

        return sent;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "A message could not be sent through the SMTP server at {Host}:{Port}: {Reason}")]
        public static partial void Failed(ILogger logger, string host, int port, string reason);
    }
}

/// <summary>
/// Mail through the Postmark API. The server token travels as a header. A refusal is logged with
/// Postmark's own code and reason and never with the recipient.
/// </summary>
public sealed partial class PostmarkEmailSender : IEmailSender
{
    /// <summary>The header the server token travels in.</summary>
    public const string TokenHeader = "X-Postmark-Server-Token";

    private readonly HttpClient _client;
    private readonly EmailOptions _options;
    private readonly ILogger<PostmarkEmailSender> _logger;

    /// <summary>Creates the sender over a client whose base address and token are already set.</summary>
    public PostmarkEmailSender(HttpClient client, IOptions<EmailOptions> options, ILogger<PostmarkEmailSender> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        PostmarkMessage body = new($"{_options.FromName} <{_options.FromAddress}>", message.To, message.Subject, message.Text, message.Html, _options.PostmarkMessageStream);
        bool sent = false;
        try
        {
            using HttpResponseMessage response = await _client.PostAsJsonAsync("email", body, PostmarkJson.Default.PostmarkMessage, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                sent = true;
            }
            else
            {
                string reason = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Log.Refused(_logger, (int)response.StatusCode, reason.Length > 300 ? reason[..300] : reason);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Unreachable, refused, or slower than the timeout, which arrives as a cancellation the caller did not ask for.
            Log.Failed(_logger, exception.Message);
        }

        return sent;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Postmark refused a message with status {Status}: {Reason}")]
        public static partial void Refused(ILogger logger, int status, string reason);

        [LoggerMessage(Level = LogLevel.Error, Message = "Postmark could not be reached to send a message: {Reason}")]
        public static partial void Failed(ILogger logger, string reason);
    }
}

/// <summary>One message as the Postmark API takes it.</summary>
public sealed record PostmarkMessage(
    [property: JsonPropertyName("From")] string From,
    [property: JsonPropertyName("To")] string To,
    [property: JsonPropertyName("Subject")] string Subject,
    [property: JsonPropertyName("TextBody")] string TextBody,
    [property: JsonPropertyName("HtmlBody")] string HtmlBody,
    [property: JsonPropertyName("MessageStream")] string MessageStream);

/// <summary>Serialisation for the Postmark message without reflection, as the trimming analyzers require.</summary>
[JsonSerializable(typeof(PostmarkMessage))]
internal sealed partial class PostmarkJson : JsonSerializerContext;
