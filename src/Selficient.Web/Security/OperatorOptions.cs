using System.ComponentModel.DataAnnotations;

namespace Selficient.Web.Security;

/// <summary>
/// The operator's way into the reviewer. Accounts live in the database; these settings say only how
/// long a session lasts and how the very first account comes into being on a fresh server, which
/// cannot come from the database because the database is empty.
/// </summary>
public sealed class OperatorOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Operator";

    /// <summary>
    /// Whether the reviewer exists at all. Off, the pages are not found, which is what a server
    /// that only serves the public site wants.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How long a signed-in operator stays signed in without using the reviewer.</summary>
    [Range(5, 240)]
    public int IdleMinutes { get; init; } = 30;

    /// <summary>How long a session lasts however much it is used, after which the password is asked for again.</summary>
    [Range(1, 24)]
    public int SessionHours { get; init; } = 8;

    /// <summary>How long the half-signed-in state between the password and the code lasts.</summary>
    [Range(1, 30)]
    public int SecondFactorMinutes { get; init; } = 5;

    /// <summary>
    /// The username of the first account, made at start-up when no operator exists. It must change
    /// its password and enrol an authenticator before it can do anything, so a bootstrap credential
    /// left in place is not a way in.
    /// </summary>
    [MaxLength(64)]
    public string? BootstrapUsername { get; init; }

    /// <summary>The first account's password. An environment variable, never a value in the repository.</summary>
    [MinLength(14)]
    public string? BootstrapPassword { get; init; }

    /// <summary>Whether a first account should be made when none exists.</summary>
    public bool HasBootstrap => !string.IsNullOrWhiteSpace(BootstrapUsername) && !string.IsNullOrWhiteSpace(BootstrapPassword);
}
