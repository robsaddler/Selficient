using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Selficient.TestKit;
using Selficient.Web.Endpoints;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Endpoints;

/// <summary>
/// Passkeys through the real pipeline, with an authenticator that makes real keys and signatures.
/// The cases are section 6 of <c>docs/threat-models/household-accounts.md</c>.
/// </summary>
public sealed class PasskeyEndpointsTests : IDisposable
{
    private const string Windows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    // Every call of a ceremony spends the sign-in allowance; the tests that are about that allowance host their own.
    private readonly SelficientWebApplicationFactory _factory = new SelficientWebApplicationFactory().WithSetting("RateLimits:SignInPerMinute", "1000");

    private HttpClient Client(string agent = Windows)
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
        return client;
    }

    [Fact]
    public async Task A_passkey_is_added_to_the_account_that_is_signed_in_and_signs_it_in_afterwards()
    {
        using TestAuthenticator authenticator = new();
        using HttpClient client = Client();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");

        PasskeyLine[] none = (await client.GetFromJsonAsync<PasskeyLine[]>(new Uri("/api/account/passkeys", UriKind.Relative)))!;
        using HttpResponseMessage begun = await PostAsync(client, "/api/account/passkeys/options");
        string options = await begun.Content.ReadAsStringAsync();
        using HttpResponseMessage added = await PostAsync(client, "/api/account/passkeys", authenticator.Create(options));
        PasskeyLine line = (await added.Content.ReadFromJsonAsync<PasskeyLine>())!;
        PasskeyLine[] one = (await client.GetFromJsonAsync<PasskeyLine[]>(new Uri("/api/account/passkeys", UriKind.Relative)))!;

        // Another browser, with nothing but the device that holds the passkey.
        using HttpClient elsewhere = Client();
        using HttpResponseMessage asked = await PostAsync(elsewhere, "/api/passkey/options");
        string signInOptions = await asked.Content.ReadAsStringAsync();
        using HttpResponseMessage signedIn = await PostAsync(elsewhere, "/api/passkey", authenticator.Assert(signInOptions));
        AccountSummary who = (await elsewhere.GetFromJsonAsync<AccountSummary>(new Uri("/api/account", UriKind.Relative)))!;
        PasskeyLine[] used = (await elsewhere.GetFromJsonAsync<PasskeyLine[]>(new Uri("/api/account/passkeys", UriKind.Relative)))!;

        none.ShouldBeEmpty();
        begun.StatusCode.ShouldBe(HttpStatusCode.OK);
        begun.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        begun.Headers.CacheControl!.NoStore.ShouldBeTrue();
        string cookie = begun.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("selficient.passkey=", StringComparison.Ordinal));
        cookie.ShouldEndWith("; max-age=300; path=/api; secure; samesite=strict; httponly", Case.Insensitive, "five minutes, the API only, never to script, never cross-site");
        added.StatusCode.ShouldBe(HttpStatusCode.OK);
        added.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("selficient.passkey=; ", StringComparison.Ordinal), "the ceremony's cookie goes when it is answered");
        string today = DateTimeOffset.UtcNow.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        line.Line.ShouldBe($"Windows computer, added {today}, not used yet.");
        one.ShouldBe([line]);
        asked.StatusCode.ShouldBe(HttpStatusCode.OK);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        signedIn.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("selficient.household=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase), "the same session the emailed code gives");
        signedIn.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("selficient.signed-in=1", StringComparison.Ordinal));
        who.Email.ShouldBe("rob@example.com");
        used.Single().Line.ShouldBe($"Windows computer, added {today}, last used {today}.");
    }

    [Fact]
    public async Task A_registration_answered_for_another_challenge_or_twice_is_refused_and_a_credential_cannot_be_registered_twice()
    {
        using TestAuthenticator authenticator = new();
        using HttpClient client = Client();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");
        using HttpResponseMessage first = await PostAsync(client, "/api/account/passkeys/options");
        string stale = await first.Content.ReadAsStringAsync();
        using HttpResponseMessage second = await PostAsync(client, "/api/account/passkeys/options");
        string current = await second.Content.ReadAsStringAsync();

        using HttpResponseMessage toStale = await PostAsync(client, "/api/account/passkeys", authenticator.Create(stale));
        Sentence refused = (await toStale.Content.ReadFromJsonAsync<Sentence>())!;
        using HttpResponseMessage afterwards = await PostAsync(client, "/api/account/passkeys", authenticator.Create(current));
        using HttpResponseMessage again = await PostAsync(client, "/api/account/passkeys/options");
        string third = await again.Content.ReadAsStringAsync();
        using HttpResponseMessage honest = await PostAsync(client, "/api/account/passkeys", authenticator.Create(third));
        using HttpResponseMessage replayed = await PostAsync(client, "/api/account/passkeys", authenticator.Create(third));
        using HttpResponseMessage oncemore = await PostAsync(client, "/api/account/passkeys/options");
        using HttpResponseMessage sameCredential = await PostAsync(client, "/api/account/passkeys", authenticator.Create(await oncemore.Content.ReadAsStringAsync()));
        using HttpResponseMessage garbage = await PostAsync(client, "/api/account/passkeys", "not an answer");
        using HttpResponseMessage tooLarge = await PostAsync(client, "/api/account/passkeys", new string('x', PasskeyEndpoints.MaxAnswerBytes + 1));

        toStale.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "an answer to a challenge this browser is no longer holding");
        refused.Message.ShouldBe("That passkey could not be added. Try again, or go on signing in with a code.");
        afterwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a refused ceremony is over: its cookie went with the refusal");
        honest.StatusCode.ShouldBe(HttpStatusCode.OK);
        replayed.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a challenge is answered once");
        sameCredential.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "the same credential is not kept twice");
        garbage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<PasskeyLine[]>(new Uri("/api/account/passkeys", UriKind.Relative)))!.Length.ShouldBe(1);
    }

    [Fact]
    public async Task Every_refused_passkey_sign_in_says_the_same_thing_and_signs_nobody_in()
    {
        using TestAuthenticator known = new();
        using TestAuthenticator stranger = new();
        using HttpClient owner = Client();
        await _factory.SignInHouseholdAsync(owner, "rob@example.com");
        using HttpResponseMessage begun = await PostAsync(owner, "/api/account/passkeys/options");
        using HttpResponseMessage added = await PostAsync(owner, "/api/account/passkeys", known.Create(await begun.Content.ReadAsStringAsync()));
        using HttpClient client = Client();
        List<string> sentences = [];

        async Task TryAsync(Func<string, string> answer, bool ask = true)
        {
            string options = "{}";
            if (ask)
            {
                using HttpResponseMessage asked = await PostAsync(client, "/api/passkey/options");
                options = await asked.Content.ReadAsStringAsync();
            }

            using HttpResponseMessage response = await PostAsync(client, "/api/passkey", answer(options));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            response.Headers.Contains("Set-Cookie").ShouldBe(ask, "a refusal sets no session; it only takes the ceremony's cookie away");
            sentences.Add((await response.Content.ReadFromJsonAsync<Sentence>())!.Message);
        }

        string otherChallenge = await (await PostAsync(client, "/api/passkey/options")).Content.ReadAsStringAsync();
        await TryAsync(_ => "not an answer");
        await TryAsync(stranger.Assert);
        await TryAsync(_ => known.Assert(otherChallenge));
        known.VerifiesUser = false;
        await TryAsync(known.Assert);
        known.VerifiesUser = true;
        known.Origin = "https://selficient.app.evil.example";
        await TryAsync(known.Assert);
        known.Origin = "https://localhost";

        sentences.Distinct().ShouldBe(["That passkey did not sign you in. Try again, or sign in with a code sent to your email address."]);
        (await client.GetAsync(new Uri("/api/account", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using HttpResponseMessage asked = await PostAsync(client, "/api/passkey/options");
        (await PostAsync(client, "/api/passkey", known.Assert(await asked.Content.ReadAsStringAsync()))).StatusCode.ShouldBe(HttpStatusCode.NoContent, "and the honest answer still signs in");
    }

    [Fact]
    public async Task A_household_removes_its_own_passkeys_and_nobody_elses_and_deleting_the_account_removes_them()
    {
        using TestAuthenticator authenticator = new();
        using HttpClient rob = Client("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15");
        using HttpClient neighbour = Client();
        await _factory.SignInHouseholdAsync(rob, "rob@example.com");
        await _factory.SignInHouseholdAsync(neighbour, "neighbour@example.com");
        using HttpResponseMessage begun = await PostAsync(rob, "/api/account/passkeys/options");
        using HttpResponseMessage added = await PostAsync(rob, "/api/account/passkeys", authenticator.Create(await begun.Content.ReadAsStringAsync()));
        PasskeyLine line = (await added.Content.ReadFromJsonAsync<PasskeyLine>())!;

        using HttpResponseMessage byNeighbour = await DeleteAsync(neighbour, line.Id);
        using HttpResponseMessage byRob = await DeleteAsync(rob, line.Id);
        using HttpResponseMessage again = await DeleteAsync(rob, line.Id);
        using HttpClient elsewhere = Client();
        using HttpResponseMessage asked = await PostAsync(elsewhere, "/api/passkey/options");
        using HttpResponseMessage withRemoved = await PostAsync(elsewhere, "/api/passkey", authenticator.Assert(await asked.Content.ReadAsStringAsync()));

        line.Line.ShouldStartWith("iPhone, added ", Case.Sensitive, "a passkey is labelled by the kind of device and nothing typed");
        byNeighbour.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        byRob.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        withRemoved.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a removed passkey opens nothing");
        (await rob.GetAsync(new Uri("/api/account", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK, "removing every passkey leaves the session and the emailed code");
    }

    [Fact]
    public async Task The_passkey_endpoints_refuse_what_did_not_come_from_our_pages_and_what_is_not_signed_in()
    {
        using HttpClient guest = Client();
        using HttpClient member = Client();
        await _factory.SignInHouseholdAsync(member, "rob@example.com");

        foreach (string path in new[] { "/api/account/passkeys/options", "/api/account/passkeys", "/api/passkey/options", "/api/passkey" })
        {
            using HttpResponseMessage bare = await PostAsync(member, path, "{}", header: false);
            using HttpResponseMessage crossSite = await PostAsync(member, path, "{}", fetchSite: "cross-site");

            bare.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            (await bare.Content.ReadFromJsonAsync<Sentence>())!.Message.ShouldBe(AccountEndpoints.NotFromHere);
            crossSite.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        }

        using HttpRequestMessage bareDelete = new(HttpMethod.Delete, new Uri("/api/account/passkeys/1", UriKind.Relative));
        (await member.SendAsync(bareDelete)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await guest.GetAsync(new Uri("/api/account/passkeys", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PostAsync(guest, "/api/account/passkeys/options")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PostAsync(guest, "/api/account/passkeys", "{}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await DeleteAsync(guest, 1)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_account_at_its_limit_is_told_so_and_passkey_sign_in_is_rate_limited()
    {
        using SelficientWebApplicationFactory limited = new SelficientWebApplicationFactory().WithSetting("RateLimits:SignInPerMinute", "20");
        using HttpClient client = limited.CreateClient();
        await limited.SignInHouseholdAsync(client, "rob@example.com");
        for (int i = 0; i < 8; i++)
        {
            using TestAuthenticator device = new();
            using HttpResponseMessage begun = await PostAsync(client, "/api/account/passkeys/options");
            using HttpResponseMessage added = await PostAsync(client, "/api/account/passkeys", device.Create(await begun.Content.ReadAsStringAsync()));
            added.StatusCode.ShouldBe(HttpStatusCode.OK, $"passkey {i + 1}");
        }

        using HttpResponseMessage spent = await PostAsync(client, "/api/passkey/options");
        using HttpResponseMessage spentToo = await PostAsync(client, "/api/passkey/options");
        using HttpResponseMessage refused = await PostAsync(client, "/api/passkey/options");

        spent.StatusCode.ShouldBe(HttpStatusCode.OK);
        spentToo.StatusCode.ShouldBe(HttpStatusCode.OK);
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, "two for signing in by code and eighteen ceremonies' calls make twenty");
    }

    [Fact]
    public async Task The_eleventh_passkey_is_refused_before_a_ceremony_begins()
    {
        using SelficientWebApplicationFactory roomy = new SelficientWebApplicationFactory().WithSetting("RateLimits:SignInPerMinute", "1000");
        using HttpClient client = roomy.CreateClient();
        await roomy.SignInHouseholdAsync(client, "rob@example.com");
        for (int i = 0; i < 10; i++)
        {
            using TestAuthenticator device = new();
            using HttpResponseMessage begun = await PostAsync(client, "/api/account/passkeys/options");
            (await PostAsync(client, "/api/account/passkeys", device.Create(await begun.Content.ReadAsStringAsync()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using HttpResponseMessage eleventh = await PostAsync(client, "/api/account/passkeys/options");

        eleventh.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await eleventh.Content.ReadFromJsonAsync<Sentence>())!.Message.ShouldBe("This account already has ten passkeys. Remove one you no longer use and try again.");
        eleventh.Headers.Contains("Set-Cookie").ShouldBeFalse("no ceremony was begun");
    }

    [Fact]
    public void The_ceremony_cookie_is_taken_once_by_the_ceremony_it_was_for_and_reads_nothing_it_did_not_write()
    {
        PasskeyCeremonyCookie cookie = new(DataProtectionProvider.Create("Selficient.Tests"));
        DefaultHttpContext writing = new();
        cookie.Write(writing.Response, PasskeyCeremonyCookie.Adding, """{"challenge":"abc"}""");
        string set = writing.Response.Headers.SetCookie.ToString();
        string value = set["selficient.passkey=".Length..set.IndexOf(';', StringComparison.Ordinal)];

        DefaultHttpContext taking = With(value);
        string? options = cookie.Take(taking, PasskeyCeremonyCookie.Adding);

        options.ShouldBe("""{"challenge":"abc"}""");
        set.ShouldNotContain("abc", customMessage: "the challenge is not readable in the browser");
        taking.Response.Headers.SetCookie.ToString().ShouldStartWith("selficient.passkey=; ", Case.Sensitive, "taken away as it is read");
        cookie.Take(With(value), PasskeyCeremonyCookie.SigningIn).ShouldBeNull("options for adding a passkey do not serve to sign in");
        cookie.Take(new DefaultHttpContext(), PasskeyCeremonyCookie.Adding).ShouldBeNull();
        cookie.Take(With("not base64url!"), PasskeyCeremonyCookie.Adding).ShouldBeNull();
        cookie.Take(With(value[..^4] + "AAAA"), PasskeyCeremonyCookie.Adding).ShouldBeNull();
        new PasskeyCeremonyCookie(DataProtectionProvider.Create("Another.Ring")).Take(With(value), PasskeyCeremonyCookie.Adding).ShouldBeNull();
        PasskeyCeremonyCookie.Lifetime.ShouldBe(TimeSpan.FromMinutes(5));
        Should.Throw<ArgumentNullException>(() => new PasskeyCeremonyCookie(null!));
        Should.Throw<ArgumentNullException>(() => cookie.Write(null!, PasskeyCeremonyCookie.Adding, "{}"));
        Should.Throw<ArgumentException>(() => cookie.Write(writing.Response, " ", "{}"));
        Should.Throw<ArgumentNullException>(() => cookie.Write(writing.Response, PasskeyCeremonyCookie.Adding, null!));
        Should.Throw<ArgumentNullException>(() => cookie.Take(null!, PasskeyCeremonyCookie.Adding));
        Should.Throw<ArgumentException>(() => cookie.Take(taking, " "));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)", "iPhone")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X)", "iPad")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8)", "Android device")]
    [InlineData("Mozilla/5.0 (X11; CrOS x86_64 14541.0.0)", "Chromebook")]
    [InlineData(Windows, "Windows computer")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)", "Mac")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64)", "Linux computer")]
    [InlineData("<script>alert(1)</script>", "Passkey")]
    [InlineData("", "Passkey")]
    [InlineData(null, "Passkey")]
    public void A_passkey_is_labelled_by_the_kind_of_device_and_nothing_typed(string? agent, string label) =>
        DeviceLabel.From(agent).ShouldBe(label);

    public void Dispose() => _factory.Dispose();

    private static DefaultHttpContext With(string cookie)
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Cookie = $"selficient.passkey={cookie}";
        return context;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string? body = null, bool header = true, string? fetchSite = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json");
        if (header)
        {
            request.Headers.Add(AccountEndpoints.SyncHeader, "1");
        }

        if (fetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", fetchSite);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, long id)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, new Uri($"/api/account/passkeys/{id}", UriKind.Relative));
        request.Headers.Add(AccountEndpoints.SyncHeader, "1");
        return await client.SendAsync(request);
    }
}
