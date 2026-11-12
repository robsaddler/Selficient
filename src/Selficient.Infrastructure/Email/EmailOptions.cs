using System.ComponentModel.DataAnnotations;

namespace Selficient.Infrastructure.Email;

/// <summary>
/// How mail leaves this server. On the local machine it goes by SMTP to Papercut on localhost
/// port 25, which shows the message as a household would receive it; in production it goes through
/// the Postmark API, whose server token is an environment variable (<c>Email__PostmarkServerToken</c>)
/// and never a value in the repository. With no provider there are no accounts, and the pages say so.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Email";

    /// <summary>No mail: the server offers no accounts.</summary>
    public const string None = "None";

    /// <summary>SMTP, for the local machine and any relay.</summary>
    public const string Smtp = "Smtp";

    /// <summary>The Postmark API.</summary>
    public const string Postmark = "Postmark";

    /// <summary>Which adapter sends: <see cref="None"/>, <see cref="Smtp"/> or <see cref="Postmark"/>. Unset means SMTP under Development and none anywhere else.</summary>
    [RegularExpression("^(None|Smtp|Postmark)?$", ErrorMessage = "Email:Provider must be None, Smtp or Postmark.")]
    public string Provider { get; init; } = string.Empty;

    /// <summary>The address mail is from.</summary>
    [Required]
    [EmailAddress]
    public string FromAddress { get; init; } = "hello@selficient.app";

    /// <summary>The name mail is from.</summary>
    [Required]
    [MaxLength(64)]
    public string FromName { get; init; } = "Selficient";

    /// <summary>The SMTP host. Papercut listens on localhost.</summary>
    [Required]
    public string SmtpHost { get; init; } = "localhost";

    /// <summary>The SMTP port. Papercut listens on 25.</summary>
    [Range(1, 65535)]
    public int SmtpPort { get; init; } = 25;

    /// <summary>Whether the SMTP connection is encrypted. Papercut's is not.</summary>
    public bool SmtpEnableSsl { get; init; }

    /// <summary>The SMTP user, for a relay that wants one. An environment variable.</summary>
    public string? SmtpUsername { get; init; }

    /// <summary>The SMTP password. An environment variable.</summary>
    public string? SmtpPassword { get; init; }

    /// <summary>The Postmark server token. An environment variable, never in the repository.</summary>
    public string? PostmarkServerToken { get; init; }

    /// <summary>Where the Postmark API answers.</summary>
    public Uri PostmarkBaseUrl { get; init; } = new("https://api.postmarkapp.com/");

    /// <summary>The Postmark message stream the codes go out on.</summary>
    [Required]
    public string PostmarkMessageStream { get; init; } = "outbound";

    /// <summary>How long one send may take before it is given up.</summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>The provider in force: what was configured, or SMTP on a development machine and none elsewhere when nothing was.</summary>
    public static string Resolve(string? configured, bool development) =>
        string.IsNullOrWhiteSpace(configured) ? (development ? Smtp : None) : configured;

    /// <summary>Whether the Postmark settings are complete when Postmark is the provider.</summary>
    public bool PostmarkIsComplete => Provider != Postmark || !string.IsNullOrWhiteSpace(PostmarkServerToken);

    /// <summary>Whether the Postmark address is one mail may be sent to: https, or plain http to this machine for a stand-in.</summary>
    public bool PostmarkUrlIsSafe => PostmarkBaseUrl.IsAbsoluteUri && (PostmarkBaseUrl.Scheme == Uri.UriSchemeHttps || (PostmarkBaseUrl.Scheme == Uri.UriSchemeHttp && PostmarkBaseUrl.IsLoopback));
}
