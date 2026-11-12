namespace Selficient.Application.Accounts;

/// <summary>
/// Where household accounts and their pending sign-ins live. A port, so the sign-in can be proved
/// without a database and the infrastructure never needs to know what a cookie is.
/// </summary>
public interface IAccountStore
{
    /// <summary>The account with this number, or nothing.</summary>
    Task<HouseholdAccount?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>The account for this address, made now if there is none. Two requests racing make one account.</summary>
    Task<HouseholdAccount> FindOrCreateAsync(string email, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Notes that a session of this account was used.</summary>
    Task TouchAsync(long id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Removes the account and everything kept for it: the plan, the passkeys. Answers whether there was one.</summary>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken);

    /// <summary>How many codes have been sent to an address since a moment, by the address's hash.</summary>
    Task<int> CountCodesAsync(byte[] emailHash, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Keeps a code that has been sent.</summary>
    Task AddCodeAsync(SignInCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Counts one try against the live code for a browser and answers what to compare, in one
    /// statement, so tries that race each take a number of their own. Nothing when there is no
    /// code for that browser, or it is spent, or it has run out.
    /// </summary>
    Task<CodeAttempt?> AttemptCodeAsync(byte[] bindingHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Marks a code used. False when something else used it first.</summary>
    Task<bool> SpendCodeAsync(long id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Deletes every code sent before a moment and answers how many went.</summary>
    Task<int> SweepCodesAsync(DateTimeOffset before, CancellationToken cancellationToken);
}

/// <summary>One message to one mailbox.</summary>
/// <param name="To">The address, already checked to be one mailbox.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Text">The message as plain text.</param>
/// <param name="Html">The same message as HTML.</param>
public sealed record EmailMessage(string To, string Subject, string Text, string Html);

/// <summary>
/// Sends mail. SMTP on the local machine, the Postmark API in production, a recorder in the tests;
/// which one is configuration, and the sign-in does not know.
/// </summary>
public interface IEmailSender
{
    /// <summary>Whether this server can send mail at all. Without it there are no accounts, and the pages say so.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends a message and answers whether the provider took it. A failure is logged by the adapter, without the address.</summary>
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
