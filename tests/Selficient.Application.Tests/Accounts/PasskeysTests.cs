using NSubstitute;
using Selficient.Application.Accounts;
using Selficient.Domain.Primitives;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Accounts;

/// <summary>
/// The rules around the ceremonies: whose account a passkey is kept for, how many it may hold,
/// and that every refusal says one sentence. The ceremonies themselves are proved against real
/// signatures in the Infrastructure tests.
/// </summary>
public sealed class PasskeysTests
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);
    private static readonly VerifiedCredential Credential = new([1, 2, 3], [9, 9], 0);

    private readonly IPasskeyStore _store = Substitute.For<IPasskeyStore>();
    private readonly IAccountStore _accounts = Substitute.For<IAccountStore>();
    private readonly IPasskeyCeremony _ceremony = Substitute.For<IPasskeyCeremony>();
    private readonly ManualClock _clock = new(Evening);

    public PasskeysTests()
    {
        _store.ForAccountAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private Passkeys Passkeys => new(_store, _accounts, _ceremony, _clock);

    private static Passkey Kept(long id = 7, long account = 41, DateTimeOffset? used = null) => new(id, account, [1, 2, 3], [9, 9], 4, "iPhone", Evening, used);

    [Fact]
    public async Task Adding_begins_with_options_that_name_the_ones_the_account_has()
    {
        _store.ForAccountAsync(41, Arg.Any<CancellationToken>()).Returns([Kept()]);
        _ceremony.RegistrationOptions(41, "rob@example.com", Arg.Is<IReadOnlyList<byte[]>>(ids => ids.Single().SequenceEqual(new byte[] { 1, 2, 3 }))).Returns("{options}");

        Result<string> begun = await Passkeys.BeginAddingAsync(41, "rob@example.com", CancellationToken.None);

        begun.Value.ShouldBe("{options}");
        await Should.ThrowAsync<ArgumentException>(() => Passkeys.BeginAddingAsync(41, " ", CancellationToken.None));
    }

    [Fact]
    public async Task A_verified_credential_is_kept_for_the_account_that_is_signed_in()
    {
        _ceremony.VerifyRegistrationAsync("{options}", "{answer}", Arg.Any<CancellationToken>()).Returns(Credential);
        _store.AddAsync(41, Credential, "iPhone", Evening, Arg.Any<CancellationToken>()).Returns(7L);

        Result<Passkey> added = await Passkeys.FinishAddingAsync(41, "{options}", "{answer}", "iPhone", CancellationToken.None);

        added.Value.ShouldBe(new Passkey(7, 41, Credential.CredentialId, Credential.PublicKey, 0, "iPhone", Evening, null));
        await Should.ThrowAsync<ArgumentException>(() => Passkeys.FinishAddingAsync(41, "{options}", "{answer}", " ", CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "{answer}")]
    [InlineData("", "{answer}")]
    [InlineData("{options}", null)]
    [InlineData("{options}", "")]
    public async Task With_no_ceremony_in_progress_or_no_answer_nothing_is_verified(string? options, string? answer)
    {
        Result<Passkey> refused = await Passkeys.FinishAddingAsync(41, options, answer, "iPhone", CancellationToken.None);
        Result<HouseholdAccount> notSignedIn = await Passkeys.FinishSigningInAsync(options, answer, CancellationToken.None);

        refused.Error.ShouldBe(Passkeys.NotAdded);
        notSignedIn.Error.ShouldBe(Passkeys.NotSignedIn);
        await _ceremony.DidNotReceiveWithAnyArgs().VerifyRegistrationAsync(default!, default!, default);
        _ceremony.DidNotReceiveWithAnyArgs().CredentialOf(default!);
    }

    [Fact]
    public async Task An_answer_that_does_not_hold_or_a_credential_already_kept_is_not_added()
    {
        _ceremony.VerifyRegistrationAsync("{options}", "{forged}", Arg.Any<CancellationToken>()).Returns((VerifiedCredential?)null);
        _ceremony.VerifyRegistrationAsync("{options}", "{answer}", Arg.Any<CancellationToken>()).Returns(Credential);
        _store.AddAsync(41, Credential, "iPhone", Evening, Arg.Any<CancellationToken>()).Returns((long?)null);

        Result<Passkey> forged = await Passkeys.FinishAddingAsync(41, "{options}", "{forged}", "iPhone", CancellationToken.None);
        Result<Passkey> twice = await Passkeys.FinishAddingAsync(41, "{options}", "{answer}", "iPhone", CancellationToken.None);

        forged.Error.ShouldBe(Passkeys.NotAdded);
        twice.Error.ShouldBe(Passkeys.NotAdded, "a credential cannot be registered twice");
        Passkeys.NotAdded.Message.ShouldBe("That passkey could not be added. Try again, or go on signing in with a code.");
    }

    [Fact]
    public async Task An_account_holds_ten_passkeys_and_no_more()
    {
        _store.ForAccountAsync(41, Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(1, 10).Select(i => Kept(i))]);
        _ceremony.VerifyRegistrationAsync("{options}", "{answer}", Arg.Any<CancellationToken>()).Returns(Credential);

        Result<string> begun = await Passkeys.BeginAddingAsync(41, "rob@example.com", CancellationToken.None);
        Result<Passkey> finished = await Passkeys.FinishAddingAsync(41, "{options}", "{answer}", "iPhone", CancellationToken.None);

        begun.Error.ShouldBe(Passkeys.TooMany);
        finished.Error.ShouldBe(Passkeys.TooMany, "ten ceremonies begun at once still end with ten");
        Passkeys.TooMany.Message.ShouldBe("This account already has ten passkeys. Remove one you no longer use and try again.");
        Passkeys.MostPerAccount.ShouldBe(10);
        await _store.DidNotReceiveWithAnyArgs().AddAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task A_verified_assertion_signs_in_the_account_the_passkey_is_kept_for_and_notes_its_use()
    {
        HouseholdAccount account = new(41, "rob@example.com", Guid.NewGuid(), Evening, Evening);
        _ceremony.SignInOptions().Returns("{options}");
        _ceremony.CredentialOf("{answer}").Returns([1, 2, 3]);
        _store.FindAsync(Arg.Is<byte[]>(id => id.SequenceEqual(new byte[] { 1, 2, 3 })), Arg.Any<CancellationToken>()).Returns(Kept());
        _ceremony.VerifySignInAsync("{options}", "{answer}", Kept(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new VerifiedAssertion([1, 2, 3], 5));
        _accounts.FindAsync(41, Arg.Any<CancellationToken>()).Returns(account);

        string options = Passkeys.BeginSigningIn();
        Result<HouseholdAccount> signedIn = await Passkeys.FinishSigningInAsync(options, "{answer}", CancellationToken.None);

        signedIn.Value.ShouldBe(account);
        await _store.Received(1).UsedAsync(7, 5, Evening, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Every_refused_passkey_sign_in_says_the_same_thing()
    {
        _ceremony.CredentialOf("{not an assertion}").Returns((byte[]?)null);
        _ceremony.CredentialOf("{unknown}").Returns([4, 4]);
        _ceremony.CredentialOf("{forged}").Returns([1, 2, 3]);
        _ceremony.CredentialOf("{orphan}").Returns([5, 5]);
        _store.FindAsync(Arg.Is<byte[]>(id => id.SequenceEqual(new byte[] { 1, 2, 3 })), Arg.Any<CancellationToken>()).Returns(Kept());
        _store.FindAsync(Arg.Is<byte[]>(id => id.SequenceEqual(new byte[] { 5, 5 })), Arg.Any<CancellationToken>()).Returns(Kept(id: 8, account: 99));
        _ceremony.VerifySignInAsync("{options}", "{forged}", Arg.Any<Passkey>(), Arg.Any<CancellationToken>()).Returns((VerifiedAssertion?)null);
        _ceremony.VerifySignInAsync("{options}", "{orphan}", Arg.Any<Passkey>(), Arg.Any<CancellationToken>()).Returns(new VerifiedAssertion([5, 5], 1));

        Fault[] refusals =
        [
            (await Passkeys.FinishSigningInAsync("{options}", "{not an assertion}", CancellationToken.None)).Error,
            (await Passkeys.FinishSigningInAsync("{options}", "{unknown}", CancellationToken.None)).Error,
            (await Passkeys.FinishSigningInAsync("{options}", "{forged}", CancellationToken.None)).Error,
            (await Passkeys.FinishSigningInAsync("{options}", "{orphan}", CancellationToken.None)).Error,
        ];

        refusals.ShouldAllBe(refusal => refusal == Passkeys.NotSignedIn, "not an assertion, a credential we do not hold, a signature that does not hold, an account that has gone: one sentence");
        Passkeys.NotSignedIn.Message.ShouldBe("That passkey did not sign you in. Try again, or sign in with a code sent to your email address.");
        await _store.DidNotReceiveWithAnyArgs().UsedAsync(default, default, default, default);
    }

    [Fact]
    public async Task A_household_lists_and_removes_its_own_passkeys_and_each_is_said_in_a_sentence()
    {
        _store.ForAccountAsync(41, Arg.Any<CancellationToken>()).Returns([Kept()]);
        _store.RemoveAsync(41, 7, Arg.Any<CancellationToken>()).Returns(true);

        IReadOnlyList<Passkey> listed = await Passkeys.ListAsync(41, CancellationToken.None);
        bool removed = await Passkeys.RemoveAsync(41, 7, CancellationToken.None);
        bool notTheirs = await Passkeys.RemoveAsync(99, 7, CancellationToken.None);

        listed.Single().Id.ShouldBe(7);
        removed.ShouldBeTrue();
        notTheirs.ShouldBeFalse();
        Passkeys.Describe(Kept()).ShouldBe("iPhone, added 12 November 2026, not used yet.");
        Passkeys.Describe(Kept(used: new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero))).ShouldBe("iPhone, added 12 November 2026, last used 1 December 2026.");
        Should.Throw<ArgumentNullException>(() => Passkeys.Describe(null!));
        Should.Throw<ArgumentNullException>(() => new Passkeys(null!, _accounts, _ceremony, _clock));
        Should.Throw<ArgumentNullException>(() => new Passkeys(_store, null!, _ceremony, _clock));
        Should.Throw<ArgumentNullException>(() => new Passkeys(_store, _accounts, null!, _clock));
        Should.Throw<ArgumentNullException>(() => new Passkeys(_store, _accounts, _ceremony, null!));
    }
}
