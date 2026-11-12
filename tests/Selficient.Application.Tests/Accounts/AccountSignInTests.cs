using System.Security.Cryptography;
using NSubstitute;
using Selficient.Application.Accounts;
using Selficient.Domain.Primitives;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Accounts;

/// <summary>
/// Signing in by a code sent to an address, against a store that keeps its rows in a list and a
/// mailer that keeps its messages. The cases are the ones in
/// <c>docs/threat-models/household-accounts.md</c>; the store's own promises (one statement, no
/// shared tries) are proved against a real database in the Infrastructure tests.
/// </summary>
public sealed class AccountSignInTests
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);

    private readonly ListStore _store = new();
    private readonly RecordingEmailSender _mail = new();
    private readonly ManualClock _clock = new(Evening);

    private AccountSignIn SignIn => new(_store, _mail, _clock);

    [Fact]
    public async Task A_code_is_sent_kept_as_hashes_and_proves_the_address_once()
    {
        Result<PendingSignIn> asked = await SignIn.RequestCodeAsync("  Rob@Example.com ", CancellationToken.None);
        string code = _mail.CodeFor("rob@example.com");
        Result<HouseholdAccount> proved = await SignIn.VerifyAsync(asked.Value, code, CancellationToken.None);
        Result<HouseholdAccount> again = await SignIn.VerifyAsync(asked.Value, code, CancellationToken.None);

        asked.IsSuccess.ShouldBeTrue();
        asked.Value.Email.ShouldBe("rob@example.com", "trimmed and lower-cased, so one mailbox is one account");
        asked.Value.Nonce.Length.ShouldBe(32);
        code.ShouldMatch("^[0-9]{6}$");
        SignInCode kept = _store.Codes.Single().Code;
        kept.EmailHash.ShouldBe(SHA256.HashData("rob@example.com"u8));
        kept.BindingHash.ShouldBe(SHA256.HashData(asked.Value.Nonce));
        kept.CodeHash.ShouldBe(AccountSignIn.HashCode("rob@example.com", asked.Value.Nonce, code));
        kept.CreatedAt.ShouldBe(Evening);
        kept.ExpiresAt.ShouldBe(Evening.AddMinutes(10));
        proved.IsSuccess.ShouldBeTrue();
        proved.Value.Email.ShouldBe("rob@example.com");
        again.Error.ShouldBe(AccountSignIn.CodeRunOut, "a code works once");
        again.Error.Message.ShouldBe("That code has run out or has already been used. Ask for a new one.");
        SignIn.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task The_message_says_the_code_what_it_is_for_and_what_to_do_if_you_did_not_ask()
    {
        await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None);

        EmailMessage message = _mail.Sent.Single();
        string code = _mail.CodeFor("rob@example.com");
        string spaced = $"{code[..3]} {code[3..]}";

        message.To.ShouldBe("rob@example.com");
        message.Subject.ShouldBe($"{code} is your Selficient sign-in code");
        message.Text.ShouldBe(
            $"Your Selficient sign-in code is {spaced}.\n\n" +
            "Type it into the page that asked for it. It works for ten minutes, once, and only in the browser that asked.\n\n" +
            "If you did not ask for a code, ignore this email. Nobody can sign in without it, and nothing has changed.\n\n" +
            "Selficient is made in Blackwood, South Wales.\n");
        message.Html.ShouldContain($">{spaced}</p>");
        message.Html.ShouldContain("If you did not ask for a code, ignore this email.");
        message.Html.ShouldStartWith("<!doctype html>");
        Should.Throw<ArgumentNullException>(() => SignInEmail.Compose(null!, "123456"));
        Should.Throw<ArgumentNullException>(() => SignInEmail.Compose("rob@example.com", null!));
    }

    [Fact]
    public async Task A_known_address_and_a_new_one_are_told_the_same_thing()
    {
        Result<PendingSignIn> first = await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None);
        HouseholdAccount made = (await SignIn.VerifyAsync(first.Value, _mail.CodeFor("rob@example.com"), CancellationToken.None)).Value;

        Result<PendingSignIn> known = await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None);
        Result<PendingSignIn> stranger = await SignIn.RequestCodeAsync("someone@example.org", CancellationToken.None);
        HouseholdAccount back = (await SignIn.VerifyAsync(known.Value, _mail.CodeFor("rob@example.com"), CancellationToken.None)).Value;

        known.IsSuccess.ShouldBeTrue();
        stranger.IsSuccess.ShouldBeTrue();
        _mail.CountTo("rob@example.com").ShouldBe(2);
        _mail.CountTo("someone@example.org").ShouldBe(1, "a code goes to any address, whether or not it has an account");
        _mail.LastTo("someone@example.org").Text.Length.ShouldBe(_mail.LastTo("rob@example.com").Text.Length, "and the message is the same");
        back.Id.ShouldBe(made.Id, "signing in again finds the account, it does not make another");
        _store.Accounts.Count.ShouldBe(1, "an account is made when a code is proved, never when one is asked for");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rob")]
    [InlineData("rob@")]
    [InlineData("@example.com")]
    [InlineData("rob@example")]
    [InlineData("rob@@example.com")]
    [InlineData("rob@example..com")]
    [InlineData("rob@.example.com")]
    [InlineData("rob@example.com.")]
    [InlineData("rob@-example.com")]
    [InlineData("rob smith@example.com")]
    [InlineData("Rob <rob@example.com>")]
    [InlineData("rob@example.com, eve@example.org")]
    [InlineData("rob@example.com\r\nBcc: eve@example.org")]
    [InlineData("\"rob\"@example.com")]
    [InlineData("rob(comment)@example.com")]
    public async Task An_address_that_is_not_one_is_refused_before_anything_is_sent(string? typed)
    {
        Result<PendingSignIn> asked = await SignIn.RequestCodeAsync(typed, CancellationToken.None);

        asked.Error.ShouldBe(AccountSignIn.NotAnAddress);
        asked.Error.Message.ShouldBe("That does not look like an email address. Check it and try again.");
        _mail.Sent.ShouldBeEmpty();
        _store.Codes.ShouldBeEmpty();
        EmailAddress.Normalise(typed).ShouldBeNull();
    }

    [Fact]
    public void An_address_is_one_mailbox_in_lower_case_and_no_longer_than_the_standard_allows()
    {
        EmailAddress.Normalise("Rob.Saddler+plans@Mail.Example.CO.UK").ShouldBe("rob.saddler+plans@mail.example.co.uk");
        EmailAddress.Normalise("a@b.co").ShouldBe("a@b.co");
        EmailAddress.Normalise($"{new string('a', 64)}@example.com").ShouldNotBeNull();
        EmailAddress.Normalise($"{new string('a', 65)}@example.com").ShouldBeNull("a local part is sixty-four characters at most");
        string longest = $"{new string('a', 64)}@{string.Join('.', Enumerable.Repeat(new string('b', 61), 3))}.org";
        longest.Length.ShouldBe(254);
        EmailAddress.Normalise(longest).ShouldBe(longest);
        EmailAddress.Normalise($"{new string('a', 64)}@{string.Join('.', Enumerable.Repeat(new string('b', 61), 3))}.info").ShouldBeNull("255 characters is one too many");
        EmailAddress.MaxLength.ShouldBe(254);
        Should.Throw<ArgumentNullException>(() => EmailAddress.Hash(null!));
    }

    [Fact]
    public async Task An_address_is_sent_three_codes_in_fifteen_minutes_and_no_more()
    {
        for (int i = 0; i < 3; i++)
        {
            (await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        Result<PendingSignIn> fourth = await SignIn.RequestCodeAsync("ROB@example.com", CancellationToken.None);
        Result<PendingSignIn> other = await SignIn.RequestCodeAsync("someone@example.org", CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(13));
        Result<PendingSignIn> later = await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None);

        fourth.Error.ShouldBe(AccountSignIn.TooManyCodes);
        fourth.Error.Message.ShouldBe("Too many codes have been sent to that address. Wait fifteen minutes and try again.");
        other.IsSuccess.ShouldBeTrue("the limit is on the address, not on everybody");
        later.IsSuccess.ShouldBeTrue("the first code is fifteen minutes old by now");
        _mail.CountTo("rob@example.com").ShouldBe(4);
        AccountSignIn.CodesPerQuarterHour.ShouldBe(3);
    }

    [Fact]
    public async Task An_address_is_sent_six_codes_in_an_hour_and_no_more()
    {
        for (int i = 0; i < 6; i++)
        {
            (await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None)).IsSuccess.ShouldBeTrue($"code {i + 1}");
            _clock.Advance(TimeSpan.FromMinutes(8));
        }

        Result<PendingSignIn> seventh = await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(13));
        Result<PendingSignIn> later = await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None);

        seventh.Error.ShouldBe(AccountSignIn.TooManyCodes, "only two in the last quarter of an hour, but six in the hour");
        later.IsSuccess.ShouldBeTrue("the first is over an hour old");
        AccountSignIn.CodesPerHour.ShouldBe(6);
    }

    [Fact]
    public async Task Five_wrong_codes_burn_the_code_and_the_right_one_is_then_refused()
    {
        PendingSignIn pending = (await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None)).Value;
        string right = _mail.CodeFor("rob@example.com");
        string wrong = right == "000000" ? "000001" : "000000";
        List<Fault> refusals = [];

        for (int i = 0; i < 5; i++)
        {
            refusals.Add((await SignIn.VerifyAsync(pending, wrong, CancellationToken.None)).Error);
        }

        Result<HouseholdAccount> afterwards = await SignIn.VerifyAsync(pending, right, CancellationToken.None);

        refusals.Take(4).ShouldAllBe(f => f == AccountSignIn.CodeWrong);
        refusals[0].Message.ShouldBe("That code is not right. Check the email and try again.");
        refusals[4].ShouldBe(AccountSignIn.CodeBurnt, "the fifth wrong try is told it was the last");
        refusals[4].Message.ShouldBe("That code has been tried too many times. Ask for a new one.");
        afterwards.Error.ShouldBe(AccountSignIn.CodeBurnt, "the right code is too late");
        _store.Accounts.ShouldBeEmpty();
        AccountSignIn.MaxAttempts.ShouldBe(5);
    }

    [Fact]
    public async Task The_fifth_try_may_still_be_the_right_one_and_a_code_may_be_typed_as_the_email_writes_it()
    {
        PendingSignIn pending = (await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None)).Value;
        string right = _mail.CodeFor("rob@example.com");
        string wrong = right == "000000" ? "000001" : "000000";
        for (int i = 0; i < 4; i++)
        {
            await SignIn.VerifyAsync(pending, wrong, CancellationToken.None);
        }

        Result<HouseholdAccount> fifth = await SignIn.VerifyAsync(pending, $" {right[..3]} {right[3..]} ", CancellationToken.None);

        fifth.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("١٢٣٤٥٦")]
    public async Task What_is_not_six_digits_is_refused_without_costing_a_try(string? typed)
    {
        PendingSignIn pending = (await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None)).Value;

        Result<HouseholdAccount> refused = await SignIn.VerifyAsync(pending, typed, CancellationToken.None);

        refused.Error.ShouldBe(AccountSignIn.CodeWrong);
        _store.Codes.Single().Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task A_code_is_refused_in_a_browser_that_did_not_ask_for_it()
    {
        PendingSignIn asker = (await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None)).Value;
        string code = _mail.CodeFor("rob@example.com");
        PendingSignIn thief = (await SignIn.RequestCodeAsync("eve@example.org", CancellationToken.None)).Value;

        Result<HouseholdAccount> noBinding = await SignIn.VerifyAsync(null, code, CancellationToken.None);
        Result<HouseholdAccount> otherBrowser = await SignIn.VerifyAsync(new PendingSignIn("rob@example.com", RandomNumberGenerator.GetBytes(32)), code, CancellationToken.None);
        Result<HouseholdAccount> otherPending = await SignIn.VerifyAsync(thief, code, CancellationToken.None);
        Result<HouseholdAccount> swappedAddress = await SignIn.VerifyAsync(new PendingSignIn("eve@example.org", asker.Nonce), code, CancellationToken.None);

        noBinding.Error.ShouldBe(AccountSignIn.CodeRunOut);
        otherBrowser.Error.ShouldBe(AccountSignIn.CodeRunOut, "a browser that never asked has no code waiting");
        otherPending.Error.ShouldBe(AccountSignIn.CodeWrong, "a browser that asked for another address cannot use this one's code");
        swappedAddress.Error.ShouldBe(AccountSignIn.CodeWrong, "the address is part of what the code proves");
        _store.Accounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_code_stops_working_after_ten_minutes()
    {
        PendingSignIn pending = (await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None)).Value;
        string code = _mail.CodeFor("rob@example.com");
        _clock.Advance(TimeSpan.FromMinutes(10));

        Result<HouseholdAccount> late = await SignIn.VerifyAsync(pending, code, CancellationToken.None);

        late.Error.ShouldBe(AccountSignIn.CodeRunOut);
        AccountSignIn.CodeLifetime.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Two_tabs_typing_the_same_right_code_make_one_sign_in()
    {
        PendingSignIn pending = (await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None)).Value;
        string code = _mail.CodeFor("rob@example.com");
        _store.SpendFails = true;

        Result<HouseholdAccount> second = await SignIn.VerifyAsync(pending, code, CancellationToken.None);

        second.Error.ShouldBe(AccountSignIn.CodeRunOut, "the other tab spent it between the compare and the spend");
    }

    [Fact]
    public async Task A_code_that_could_not_be_sent_is_said_so_and_not_counted()
    {
        _mail.Failing = true;

        Result<PendingSignIn> asked = await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None);

        asked.Error.ShouldBe(AccountSignIn.NotSent);
        asked.Error.Message.ShouldBe("The code could not be sent just now. Try again in a moment.");
        _store.Codes.ShouldBeEmpty("a message the provider refused does not count against the address");
    }

    [Fact]
    public async Task A_server_with_no_mailer_offers_no_account()
    {
        IEmailSender none = Substitute.For<IEmailSender>();
        none.IsConfigured.Returns(false);
        AccountSignIn signIn = new(_store, none, _clock);

        Result<PendingSignIn> asked = await signIn.RequestCodeAsync("rob@example.com", CancellationToken.None);

        signIn.IsAvailable.ShouldBeFalse();
        asked.Error.ShouldBe(AccountSignIn.Unavailable);
        asked.Error.Message.ShouldBe("Accounts are not available on this server yet. Your plan is saved on this device, as it always is.");
        await none.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Codes_are_forgotten_an_hour_after_they_were_sent()
    {
        await SignIn.RequestCodeAsync("rob@example.com", CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(59));
        int early = await SignIn.SweepAsync(_clock.GetUtcNow(), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));
        int onTheHour = await SignIn.SweepAsync(_clock.GetUtcNow(), CancellationToken.None);

        early.ShouldBe(0);
        onTheHour.ShouldBe(1);
        _store.Codes.ShouldBeEmpty();
        SignIn.Describe(1).ShouldBe("1 sign-in code that had run out");
        SignIn.Describe(4).ShouldBe("4 sign-in codes that had run out");
        AccountSignIn.Remembered.ShouldBe(TimeSpan.FromHours(1));
        Should.Throw<ArgumentNullException>(() => new AccountSignIn(null!, _mail, _clock));
        Should.Throw<ArgumentNullException>(() => new AccountSignIn(_store, null!, _clock));
        Should.Throw<ArgumentNullException>(() => new AccountSignIn(_store, _mail, null!));
        Should.Throw<ArgumentNullException>(() => AccountSignIn.HashCode(null!, [1], "1"));
        Should.Throw<ArgumentNullException>(() => AccountSignIn.HashCode("a", null!, "1"));
        Should.Throw<ArgumentNullException>(() => AccountSignIn.HashCode("a", [1], null!));
    }

    /// <summary>The store as a list, with the same rules the SQL keeps: a try is counted before it is answered, and only a live code is found.</summary>
    private sealed class ListStore : IAccountStore
    {
        public List<HouseholdAccount> Accounts { get; } = [];

        public List<Row> Codes { get; } = [];

        public bool SpendFails { get; set; }

        public Task<HouseholdAccount?> FindAsync(long id, CancellationToken cancellationToken) => Task.FromResult(Accounts.SingleOrDefault(a => a.Id == id));

        public Task<HouseholdAccount> FindOrCreateAsync(string email, DateTimeOffset now, CancellationToken cancellationToken)
        {
            HouseholdAccount? account = Accounts.SingleOrDefault(a => a.Email == email);
            if (account is null)
            {
                account = new HouseholdAccount(Accounts.Count + 1, email, Guid.NewGuid(), now, now);
                Accounts.Add(account);
            }

            return Task.FromResult(account);
        }

        public Task TouchAsync(long id, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken) => Task.FromResult(Accounts.RemoveAll(a => a.Id == id) > 0);

        public Task<int> CountCodesAsync(byte[] emailHash, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Count(c => c.Code.EmailHash.SequenceEqual(emailHash) && c.Code.CreatedAt > since));

        public Task AddCodeAsync(SignInCode code, CancellationToken cancellationToken)
        {
            Codes.Add(new Row(Codes.Count + 1, code));
            return Task.CompletedTask;
        }

        public Task<CodeAttempt?> AttemptCodeAsync(byte[] bindingHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Row? row = Codes.SingleOrDefault(c => c.Code.BindingHash.SequenceEqual(bindingHash) && !c.Used && c.Code.ExpiresAt > now);
            if (row is null)
            {
                return Task.FromResult<CodeAttempt?>(null);
            }

            row.Attempts++;
            return Task.FromResult<CodeAttempt?>(new CodeAttempt(row.Id, row.Attempts, row.Code.CodeHash));
        }

        public Task<bool> SpendCodeAsync(long id, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Row row = Codes.Single(c => c.Id == id);
            if (SpendFails || row.Used)
            {
                return Task.FromResult(false);
            }

            row.Used = true;
            return Task.FromResult(true);
        }

        public Task<int> SweepCodesAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.RemoveAll(c => c.Code.CreatedAt <= before));

        public sealed class Row(long id, SignInCode code)
        {
            public long Id { get; } = id;

            public SignInCode Code { get; } = code;

            public int Attempts { get; set; }

            public bool Used { get; set; }
        }
    }
}
