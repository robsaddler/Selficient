using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Selficient.Application.Accounts;

namespace Selficient.TestKit;

/// <summary>
/// Keeps what the application is asked to send, for a test to read the code from as a person reads
/// their inbox. It lives here and not in the product: no provider that holds a code in memory
/// ships.
/// </summary>
public sealed partial class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <summary>Set to make the next sends fail, as a provider that is down does.</summary>
    public bool Failing { get; set; }

    /// <summary>Everything sent, oldest first.</summary>
    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    /// <inheritdoc />
    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Failing)
        {
            return Task.FromResult(false);
        }

        _sent.Enqueue(message);
        return Task.FromResult(true);
    }

    /// <summary>The newest message to an address.</summary>
    public EmailMessage LastTo(string email) => _sent.Last(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>The code in the newest message to an address, as six digits.</summary>
    public string CodeFor(string email) => CodePattern().Match(LastTo(email).Subject).Value;

    /// <summary>How many messages have gone to an address.</summary>
    public int CountTo(string email) => _sent.Count(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("^[0-9]{6}")]
    private static partial Regex CodePattern();
}

/// <summary>
/// Signing a test client in as a household the way a person does: the address, the code from the
/// message that was sent, the code typed in. No test takes a short cut past the code.
/// </summary>
public static class HouseholdSignInHelper
{
    /// <summary>Asks for a code for an address and answers the response, having followed no redirect by hand.</summary>
    public static async Task<HttpResponseMessage> AskForCodeAsync(this HttpClient client, string email)
    {
        ArgumentNullException.ThrowIfNull(client);
        string token = await OperatorSignInHelper.TokenAsync(client, "/account/sign-in");
        return await client.PostAsync(new Uri("/account/sign-in", UriKind.Relative), OperatorSignInHelper.Form(token, ("Email", email)));
    }

    /// <summary>Types a code into the code page and answers the response.</summary>
    public static async Task<HttpResponseMessage> TypeCodeAsync(this HttpClient client, string code)
    {
        ArgumentNullException.ThrowIfNull(client);
        string token = await OperatorSignInHelper.TokenAsync(client, "/account/code");
        return await client.PostAsync(new Uri("/account/code", UriKind.Relative), OperatorSignInHelper.Form(token, ("Code", code)));
    }

    /// <summary>Takes both steps for an address on a client that keeps cookies, and fails loudly if either is refused.</summary>
    public static async Task SignInHouseholdAsync(this SelficientWebApplicationFactory factory, HttpClient client, string email)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using HttpResponseMessage asked = await client.AskForCodeAsync(email);
        if (asked.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Redirect))
        {
            throw new InvalidOperationException($"Asking for a code answered {(int)asked.StatusCode}.");
        }

        using HttpResponseMessage typed = await client.TypeCodeAsync(factory.Mail.CodeFor(email));
        if (typed.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Redirect))
        {
            throw new InvalidOperationException($"Typing the code answered {(int)typed.StatusCode}.");
        }
    }
}
