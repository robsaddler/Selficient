using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Selficient.Application.Operations;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Accounts;

/// <summary>
/// Signing a household in with a six-digit code sent to its address. The same two steps make an
/// account and sign in to one, and say the same sentences either way, so the pages tell nobody
/// which addresses have accounts. A code works for ten minutes, five tries, once, and only in the
/// browser that asked for it. The threat model is <c>docs/threat-models/household-accounts.md</c>.
/// </summary>
public sealed class AccountSignIn : IExpiring
{
    /// <summary>How long a code works.</summary>
    public static TimeSpan CodeLifetime => TimeSpan.FromMinutes(10);

    /// <summary>How many times a code may be tried, the right try included.</summary>
    public static int MaxAttempts => 5;

    /// <summary>How many codes one address is sent in fifteen minutes.</summary>
    public static int CodesPerQuarterHour => 3;

    /// <summary>How many codes one address is sent in an hour.</summary>
    public static int CodesPerHour => 6;

    /// <summary>How long a sent code's hashes are kept, to count against the address. After that they are swept.</summary>
    public static TimeSpan Remembered => TimeSpan.FromHours(1);

    /// <summary>The fault when what was typed is not one mailbox.</summary>
    public static Fault NotAnAddress => new("account.not-an-address", "That does not look like an email address. Check it and try again.");

    /// <summary>The fault when an address has been sent too many codes.</summary>
    public static Fault TooManyCodes => new("account.too-many-codes", "Too many codes have been sent to that address. Wait fifteen minutes and try again.");

    /// <summary>The fault when the mail provider did not take the message.</summary>
    public static Fault NotSent => new("account.not-sent", "The code could not be sent just now. Try again in a moment.");

    /// <summary>The fault when this server has no way to send mail.</summary>
    public static Fault Unavailable => new("account.unavailable", "Accounts are not available on this server yet. Your plan is saved on this device, as it always is.");

    /// <summary>The fault when there is no live code for this browser: never asked, used, or out of time.</summary>
    public static Fault CodeRunOut => new("account.code-run-out", "That code has run out or has already been used. Ask for a new one.");

    /// <summary>The fault when the code has had its five tries.</summary>
    public static Fault CodeBurnt => new("account.code-burnt", "That code has been tried too many times. Ask for a new one.");

    /// <summary>The fault when the code is not the one that was sent.</summary>
    public static Fault CodeWrong => new("account.code-wrong", "That code is not right. Check the email and try again.");

    private readonly IAccountStore _store;
    private readonly IEmailSender _mail;
    private readonly TimeProvider _clock;

    /// <summary>Creates the sign-in.</summary>
    public AccountSignIn(IAccountStore store, IEmailSender mail, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _mail = mail;
        _clock = clock;
    }

    /// <summary>Whether this server offers accounts at all.</summary>
    public bool IsAvailable => _mail.IsConfigured;

    /// <summary>
    /// The first step: sends a code to an address and answers what the asking browser must keep.
    /// Whether an account exists for the address makes no difference to anything this does.
    /// </summary>
    public async Task<Result<PendingSignIn>> RequestCodeAsync(string? typed, CancellationToken cancellationToken)
    {
        if (!_mail.IsConfigured)
        {
            return Result.Failure<PendingSignIn>(Unavailable);
        }

        string? email = EmailAddress.Normalise(typed);
        if (email is null)
        {
            return Result.Failure<PendingSignIn>(NotAnAddress);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        byte[] emailHash = EmailAddress.Hash(email);
        if (await _store.CountCodesAsync(emailHash, now - TimeSpan.FromMinutes(15), cancellationToken).ConfigureAwait(false) >= CodesPerQuarterHour
            || await _store.CountCodesAsync(emailHash, now - TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false) >= CodesPerHour)
        {
            return Result.Failure<PendingSignIn>(TooManyCodes);
        }

        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        // Sent before it is kept, so a message the provider refused is not counted against the address.
        if (!await _mail.SendAsync(SignInEmail.Compose(email, code), cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PendingSignIn>(NotSent);
        }

        await _store.AddCodeAsync(new SignInCode(emailHash, SHA256.HashData(nonce), HashCode(email, nonce, code), now, now + CodeLifetime), cancellationToken).ConfigureAwait(false);
        return Result.Success(new PendingSignIn(email, nonce));
    }

    /// <summary>
    /// The second step: the code, from the browser that asked. On success the account for the
    /// address is answered, made now if this is the first time.
    /// </summary>
    public async Task<Result<HouseholdAccount>> VerifyAsync(PendingSignIn? pending, string? typed, CancellationToken cancellationToken)
    {
        if (pending is null)
        {
            return Result.Failure<HouseholdAccount>(CodeRunOut);
        }

        // People type codes with a space in the middle, as the email writes them.
        string code = new([.. (typed ?? string.Empty).Where(static c => c is not (' ' or '-'))]);
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            // Not a code at all, so it does not cost one of the five tries.
            return Result.Failure<HouseholdAccount>(CodeWrong);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        CodeAttempt? attempt = await _store.AttemptCodeAsync(SHA256.HashData(pending.Nonce), now, cancellationToken).ConfigureAwait(false);
        if (attempt is null)
        {
            return Result.Failure<HouseholdAccount>(CodeRunOut);
        }

        if (attempt.Attempts > MaxAttempts)
        {
            return Result.Failure<HouseholdAccount>(CodeBurnt);
        }

        if (!CryptographicOperations.FixedTimeEquals(attempt.CodeHash, HashCode(pending.Email, pending.Nonce, code)))
        {
            return Result.Failure<HouseholdAccount>(attempt.Attempts == MaxAttempts ? CodeBurnt : CodeWrong);
        }

        if (!await _store.SpendCodeAsync(attempt.Id, now, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<HouseholdAccount>(CodeRunOut);
        }

        return Result.Success(await _store.FindOrCreateAsync(pending.Email, now, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public Task<int> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken) => _store.SweepCodesAsync(now - Remembered, cancellationToken);

    /// <inheritdoc />
    public string Describe(int removed) => removed == 1 ? "1 sign-in code that had run out" : $"{removed} sign-in codes that had run out";

    /// <summary>
    /// The hash a code is kept as: keyed by the value only the asking browser holds, so the row
    /// cannot be checked, or a six-digit space searched, by anyone who has the database alone.
    /// </summary>
    public static byte[] HashCode(string email, byte[] nonce, string code)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(code);
        return HMACSHA256.HashData(nonce, Encoding.UTF8.GetBytes($"{email}\n{code}"));
    }
}

/// <summary>The message a sign-in code travels in. Short, in the house voice, and it says what to do if you did not ask.</summary>
public static class SignInEmail
{
    /// <summary>The message for an address and a code.</summary>
    public static EmailMessage Compose(string email, string code)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(code);
        string spaced = $"{code[..3]} {code[3..]}";
        string text =
            $"Your Selficient sign-in code is {spaced}.\n\n" +
            "Type it into the page that asked for it. It works for ten minutes, once, and only in the browser that asked.\n\n" +
            "If you did not ask for a code, ignore this email. Nobody can sign in without it, and nothing has changed.\n\n" +
            "Selficient is made in Blackwood, South Wales.\n";
        string html =
            "<!doctype html><html lang=\"en-GB\"><body style=\"margin:0;padding:24px;background:#f3f5f4;font-family:system-ui,-apple-system,'Segoe UI',sans-serif;color:#12241f;\">" +
            "<div style=\"max-width:30rem;margin:0 auto;padding:24px;background:#ffffff;border-radius:8px;\">" +
            "<p style=\"margin:0 0 16px;font-size:16px;\">Your Selficient sign-in code is</p>" +
            $"<p style=\"margin:0 0 16px;font-size:32px;font-weight:700;letter-spacing:0.12em;\">{spaced}</p>" +
            "<p style=\"margin:0 0 16px;font-size:16px;\">Type it into the page that asked for it. It works for ten minutes, once, and only in the browser that asked.</p>" +
            "<p style=\"margin:0 0 16px;font-size:14px;color:#44534e;\">If you did not ask for a code, ignore this email. Nobody can sign in without it, and nothing has changed.</p>" +
            "<p style=\"margin:0;font-size:14px;color:#44534e;\">Selficient is made in Blackwood, South Wales.</p>" +
            "</div></body></html>";
        return new EmailMessage(email, $"{code} is your Selficient sign-in code", text, html);
    }
}
