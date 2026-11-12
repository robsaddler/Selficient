using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Selficient.Application.Accounts;
using Selficient.Infrastructure.Accounts;
using Selficient.Infrastructure.Data;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Accounts;

/// <summary>
/// The WebAuthn ceremonies against an authenticator that makes real keys and real signatures, and
/// the passkey store against a real SQL Server. The cases are section 6 of
/// <c>docs/threat-models/household-accounts.md</c>: each way an answer can fail to hold is tried.
/// </summary>
public sealed class PasskeyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly Fido2PasskeyCeremony _ceremony = new(Options.Create(new PasskeyOptions { RelyingPartyId = "localhost", Origins = ["https://localhost"] }));
    private SqlPasskeyStore _store = null!;
    private long _account;
    private long _neighbour;

    public async ValueTask InitializeAsync()
    {
        await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);
        _store = new SqlPasskeyStore(_database.ConnectionString);
        SqlAccountStore accounts = new(_database.ConnectionString);
        _account = (await accounts.FindOrCreateAsync("rob@example.com", Evening, CancellationToken.None)).Id;
        _neighbour = (await accounts.FindOrCreateAsync("neighbour@example.com", Evening, CancellationToken.None)).Id;
    }

    private static Passkey Kept(VerifiedCredential credential, long account = 41) => new(7, account, credential.CredentialId, credential.PublicKey, credential.SignCount, "iPhone", Evening, null);

    private async Task<VerifiedCredential> RegisterAsync(TestAuthenticator authenticator, long account = 41) =>
        (await _ceremony.VerifyRegistrationAsync(Registration(account), authenticator.Create(_lastOptions), CancellationToken.None))!;

    private string _lastOptions = string.Empty;

    private string Registration(long account = 41, params byte[][] existing) => _lastOptions = _ceremony.RegistrationOptions(account, "rob@example.com", existing);

    [Fact]
    public async Task A_credential_is_made_for_this_site_discoverable_and_with_the_person_verified_and_then_signs_in()
    {
        using TestAuthenticator authenticator = new();
        string options = Registration(41, [1, 2, 3]);
        using JsonDocument asked = JsonDocument.Parse(options);

        VerifiedCredential credential = (await _ceremony.VerifyRegistrationAsync(options, authenticator.Create(options), CancellationToken.None))!;
        string signIn = _ceremony.SignInOptions();
        using JsonDocument askedToSignIn = JsonDocument.Parse(signIn);
        string answer = authenticator.Assert(signIn);
        VerifiedAssertion assertion = (await _ceremony.VerifySignInAsync(signIn, answer, Kept(credential), CancellationToken.None))!;

        asked.RootElement.GetProperty("rp").GetProperty("id").GetString().ShouldBe("localhost");
        asked.RootElement.GetProperty("rp").GetProperty("name").GetString().ShouldBe("Selficient");
        asked.RootElement.GetProperty("user").GetProperty("name").GetString().ShouldBe("rob@example.com");
        asked.RootElement.GetProperty("user").GetProperty("id").GetString().ShouldBe("AAAAAAAAACk", "the account's number in eight bytes, and nothing that names a person");
        asked.RootElement.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString().ShouldBe("required", "so signing in needs no address typed");
        asked.RootElement.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString().ShouldBe("required");
        asked.RootElement.GetProperty("attestation").GetString().ShouldBe("none", "we do not ask what make of authenticator it is");
        asked.RootElement.GetProperty("excludeCredentials")[0].GetProperty("id").GetString().ShouldBe("AQID", "the ones the account has are named, so a second is not made on the same device");
        System.Buffers.Text.Base64Url.DecodeFromChars(asked.RootElement.GetProperty("challenge").GetString()!).Length.ShouldBe(32);
        credential.CredentialId.ShouldBe(authenticator.CredentialId);
        credential.PublicKey.Length.ShouldBeGreaterThan(64);
        credential.SignCount.ShouldBe(0);
        askedToSignIn.RootElement.GetProperty("rpId").GetString().ShouldBe("localhost");
        askedToSignIn.RootElement.GetProperty("userVerification").GetString().ShouldBe("required");
        askedToSignIn.RootElement.GetProperty("allowCredentials").GetArrayLength().ShouldBe(0, "the same for everyone: the device finds the credential");
        _ceremony.CredentialOf(answer).ShouldBe(authenticator.CredentialId);
        assertion.CredentialId.ShouldBe(authenticator.CredentialId);
        assertion.SignCount.ShouldBe(1);
        Fido2PasskeyCeremony.UserHandle(41).ShouldBe([0, 0, 0, 0, 0, 0, 0, 41]);
    }

    [Fact]
    public async Task A_registration_that_does_not_answer_these_options_from_this_site_is_refused()
    {
        using TestAuthenticator honest = new();
        using TestAuthenticator elsewhere = new("https://selficient.app.example.com");
        using TestAuthenticator otherParty = new() { ClaimsRelyingParty = "example.com" };
        using TestAuthenticator unverified = new() { VerifiesUser = false };
        string options = Registration();
        string other = _ceremony.RegistrationOptions(41, "rob@example.com", []);

        (await _ceremony.VerifyRegistrationAsync(other, honest.Create(options), CancellationToken.None)).ShouldBeNull("an answer to another challenge");
        (await _ceremony.VerifyRegistrationAsync(options, elsewhere.Create(options), CancellationToken.None)).ShouldBeNull("an answer from a page on another origin");
        (await _ceremony.VerifyRegistrationAsync(options, otherParty.Create(options), CancellationToken.None)).ShouldBeNull("a credential bound to another site");
        (await _ceremony.VerifyRegistrationAsync(options, unverified.Create(options), CancellationToken.None)).ShouldBeNull("the person was not verified");
        (await _ceremony.VerifyRegistrationAsync(options, "not json", CancellationToken.None)).ShouldBeNull();
        (await _ceremony.VerifyRegistrationAsync(options, "{}", CancellationToken.None)).ShouldBeNull();
        (await _ceremony.VerifyRegistrationAsync("not options", honest.Create(options), CancellationToken.None)).ShouldBeNull();
        (await _ceremony.VerifyRegistrationAsync(options, honest.Create(options), CancellationToken.None)).ShouldNotBeNull("and the honest answer holds");
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => _ceremony.VerifyRegistrationAsync(options, honest.Create(options), cancelled.Token));
        await Should.ThrowAsync<ArgumentNullException>(() => _ceremony.VerifyRegistrationAsync(null!, "{}", CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _ceremony.VerifyRegistrationAsync(options, null!, CancellationToken.None));
        Should.Throw<ArgumentException>(() => _ceremony.RegistrationOptions(41, " ", []));
        Should.Throw<ArgumentNullException>(() => _ceremony.RegistrationOptions(41, "rob@example.com", null!));
        Should.Throw<ArgumentNullException>(() => new Fido2PasskeyCeremony(null!));
    }

    [Fact]
    public async Task An_assertion_for_another_challenge_origin_key_or_account_is_refused()
    {
        using TestAuthenticator authenticator = new();
        using TestAuthenticator stranger = new();
        VerifiedCredential credential = await RegisterAsync(authenticator);
        VerifiedCredential strangers = await RegisterAsync(stranger);
        string options = _ceremony.SignInOptions();
        string other = _ceremony.SignInOptions();

        (await _ceremony.VerifySignInAsync(other, authenticator.Assert(options), Kept(credential), CancellationToken.None)).ShouldBeNull("an answer to another challenge");
        authenticator.Origin = "https://evil.example";
        (await _ceremony.VerifySignInAsync(options, authenticator.Assert(options), Kept(credential), CancellationToken.None)).ShouldBeNull("signed on a page that is not ours");
        authenticator.Origin = "https://localhost";
        authenticator.ClaimsRelyingParty = "evil.example";
        (await _ceremony.VerifySignInAsync(options, authenticator.Assert(options), Kept(credential), CancellationToken.None)).ShouldBeNull("signed for another site");
        authenticator.ClaimsRelyingParty = null;
        (await _ceremony.VerifySignInAsync(options, authenticator.Assert(options), Kept(strangers), CancellationToken.None)).ShouldBeNull("checked against a key that is not this credential's");
        (await _ceremony.VerifySignInAsync(options, authenticator.Assert(options), Kept(credential, account: 99), CancellationToken.None)).ShouldBeNull("the credential was made for another account than the one it is kept for");
        (await _ceremony.VerifySignInAsync(options, "{}", Kept(credential), CancellationToken.None)).ShouldBeNull();
        (await _ceremony.VerifySignInAsync("not options", authenticator.Assert(options), Kept(credential), CancellationToken.None)).ShouldBeNull();
        (await _ceremony.VerifySignInAsync(options, authenticator.Assert(options), Kept(credential), CancellationToken.None)).ShouldNotBeNull("and the honest answer holds");
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => _ceremony.VerifySignInAsync(options, authenticator.Assert(options), Kept(credential), cancelled.Token));
        await Should.ThrowAsync<ArgumentNullException>(() => _ceremony.VerifySignInAsync(null!, "{}", Kept(credential), CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _ceremony.VerifySignInAsync(options, null!, Kept(credential), CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _ceremony.VerifySignInAsync(options, "{}", null!, CancellationToken.None));
    }

    [Fact]
    public async Task An_assertion_without_user_verification_or_with_a_counter_that_goes_backwards_is_refused()
    {
        using TestAuthenticator authenticator = new();
        VerifiedCredential credential = await RegisterAsync(authenticator);
        string options = _ceremony.SignInOptions();

        authenticator.VerifiesUser = false;
        VerifiedAssertion? unverified = await _ceremony.VerifySignInAsync(options, authenticator.Assert(options), Kept(credential), CancellationToken.None);
        authenticator.VerifiesUser = true;
        authenticator.Counter = 10;
        VerifiedAssertion ahead = (await _ceremony.VerifySignInAsync(options, authenticator.Assert(options), Kept(credential), CancellationToken.None))!;
        authenticator.Counter = 4;
        VerifiedAssertion? cloned = await _ceremony.VerifySignInAsync(options, authenticator.Assert(options), Kept(credential) with { SignCount = ahead.SignCount }, CancellationToken.None);

        unverified.ShouldBeNull("a stolen, unlocked key is not enough");
        ahead.SignCount.ShouldBe(11);
        cloned.ShouldBeNull("a counter behind the one we hold is a second copy of the authenticator");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"rawId":""}""")]
    [InlineData("""{"rawId":"not base64url!"}""")]
    public void What_is_not_an_assertion_names_no_credential(string response) =>
        _ceremony.CredentialOf(response).ShouldBeNull();

    [Fact]
    public async Task A_passkey_is_kept_once_found_by_its_credential_and_removed_only_by_its_own_account()
    {
        VerifiedCredential credential = new([1, 2, 3, 4], [9, 9, 9], 0);
        VerifiedCredential another = new([5, 6, 7, 8], [8, 8, 8], 3);

        long id = (await _store.AddAsync(_account, credential, "iPhone", Evening, CancellationToken.None))!.Value;
        long? again = await _store.AddAsync(_account, credential, "iPhone", Evening, CancellationToken.None);
        long? elsewhere = await _store.AddAsync(_neighbour, credential, "Mac", Evening, CancellationToken.None);
        long second = (await _store.AddAsync(_account, another, new string('x', 60), Evening.AddDays(1), CancellationToken.None))!.Value;
        Passkey found = (await _store.FindAsync([1, 2, 3, 4], CancellationToken.None))!;
        await _store.UsedAsync(id, 5, Evening.AddDays(2), CancellationToken.None);
        IReadOnlyList<Passkey> mine = await _store.ForAccountAsync(_account, CancellationToken.None);
        bool notTheirs = await _store.RemoveAsync(_neighbour, id, CancellationToken.None);
        bool removed = await _store.RemoveAsync(_account, id, CancellationToken.None);
        bool removedAgain = await _store.RemoveAsync(_account, id, CancellationToken.None);

        again.ShouldBeNull("the same credential is not kept twice");
        elsewhere.ShouldBeNull("nor for a second account");
        found.ShouldBe(new Passkey(id, _account, found.CredentialId, found.PublicKey, 0, "iPhone", Evening, null));
        found.CredentialId.ShouldBe([1, 2, 3, 4]);
        found.PublicKey.ShouldBe([9, 9, 9]);
        mine.Select(p => p.Id).ShouldBe([id, second]);
        mine[0].SignCount.ShouldBe(5);
        mine[0].LastUsedAt.ShouldBe(Evening.AddDays(2));
        mine[1].Label.ShouldBe(new string('x', 40), "a label is forty characters at most");
        mine[1].LastUsedAt.ShouldBeNull();
        (await _store.ForAccountAsync(_neighbour, CancellationToken.None)).ShouldBeEmpty();
        (await _store.FindAsync([4, 3, 2, 1], CancellationToken.None)).ShouldBeNull();
        notTheirs.ShouldBeFalse("a household removes its own passkeys and nobody else's");
        removed.ShouldBeTrue();
        removedAgain.ShouldBeFalse();
        await Should.ThrowAsync<ArgumentNullException>(() => _store.FindAsync(null!, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.AddAsync(_account, null!, "iPhone", Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => _store.AddAsync(_account, credential, " ", Evening, CancellationToken.None));
        Should.Throw<ArgumentException>(() => new SqlPasskeyStore(" "));
    }

    [Fact]
    public async Task Deleting_the_account_removes_its_passkeys()
    {
        await _store.AddAsync(_account, new VerifiedCredential([1], [1], 0), "iPhone", Evening, CancellationToken.None);
        await _store.AddAsync(_neighbour, new VerifiedCredential([2], [2], 0), "Mac", Evening, CancellationToken.None);

        await new SqlAccountStore(_database.ConnectionString).DeleteAsync(_account, CancellationToken.None);

        (await _store.ForAccountAsync(_account, CancellationToken.None)).ShouldBeEmpty();
        (await _store.ForAccountAsync(_neighbour, CancellationToken.None)).Count.ShouldBe(1);
        await using SqlConnection connection = new(_database.ConnectionString);
        IEnumerable<string> columns = await connection.QueryAsync<string>("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.household_passkeys') ORDER BY column_id");
        columns.ShouldBe(["id", "account_id", "credential_id", "public_key", "sign_count", "label", "created_at", "last_used_at", "credential_hash"], "everything kept is public: no private key and nothing biometric can be here");
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }
}
