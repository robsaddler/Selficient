using System.Net;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Accounts;
using Selficient.TestKit;
using Selficient.Web.Endpoints;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Endpoints;

/// <summary>
/// The copy of a plan an account keeps, through the real pipeline, the real key ring and a real
/// database. The cases are section 5 of <c>docs/threat-models/household-accounts.md</c>.
/// </summary>
public sealed class PlanSyncEndpointsTests : IDisposable
{
    private const string Plan = """{"plan":{"profile":{"Postcode":"NP12 1GW","People":"4"},"ticks":["headtorches"],"planned":[],"skipped":[]},"detailsAt":1794510000000}""";
    private const string Changed = """{"plan":{"profile":{"Postcode":"NP12 1GW","People":"4"},"ticks":["headtorches","power-banks"],"planned":[],"skipped":[]},"detailsAt":1794510000000}""";

    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task A_household_keeps_a_plan_in_its_account_and_reads_it_back_as_it_wrote_it()
    {
        using HttpClient client = _factory.CreateClient();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");

        using HttpResponseMessage empty = await client.GetAsync(new Uri("/api/account/plan", UriKind.Relative));
        PlanAnswer none = (await empty.Content.ReadFromJsonAsync<PlanAnswer>())!;
        using HttpResponseMessage written = await PutAsync(client, 0, Plan);
        PlanAnswer first = (await written.Content.ReadFromJsonAsync<PlanAnswer>())!;
        PlanAnswer read = (await client.GetFromJsonAsync<PlanAnswer>(new Uri("/api/account/plan", UriKind.Relative)))!;
        using HttpResponseMessage rewritten = await PutAsync(client, 1, Changed);
        PlanAnswer second = (await rewritten.Content.ReadFromJsonAsync<PlanAnswer>())!;

        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        empty.Headers.CacheControl!.NoStore.ShouldBeTrue("a plan is never kept by any cache");
        none.ShouldBe(new PlanAnswer(0, null));
        written.StatusCode.ShouldBe(HttpStatusCode.OK);
        written.Headers.CacheControl!.NoStore.ShouldBeTrue();
        first.ShouldBe(new PlanAnswer(1, null));
        read.ShouldBe(new PlanAnswer(1, Plan));
        second.ShouldBe(new PlanAnswer(2, null));
    }

    [Fact]
    public async Task A_write_based_on_a_stale_version_is_refused_with_the_current_copy()
    {
        using HttpClient phone = _factory.CreateClient();
        using HttpClient laptop = _factory.CreateClient();
        await _factory.SignInHouseholdAsync(phone, "rob@example.com");
        await _factory.SignInHouseholdAsync(laptop, "rob@example.com");
        using HttpResponseMessage first = await PutAsync(phone, 0, Plan);

        using HttpResponseMessage clash = await PutAsync(laptop, 0, Changed);
        PlanAnswer current = (await clash.Content.ReadFromJsonAsync<PlanAnswer>())!;
        using HttpResponseMessage retried = await PutAsync(laptop, current.Version, Changed);

        clash.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        current.ShouldBe(new PlanAnswer(1, Plan), "the device that lost is handed what is there now, to merge and write again");
        retried.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await phone.GetFromJsonAsync<PlanAnswer>(new Uri("/api/account/plan", UriKind.Relative)))!.ShouldBe(new PlanAnswer(2, Changed), "the two sessions are one account");
    }

    [Fact]
    public async Task One_household_never_sees_or_writes_anothers_plan()
    {
        using HttpClient rob = _factory.CreateClient();
        using HttpClient neighbour = _factory.CreateClient();
        using HttpClient guest = _factory.CreateClient();
        await _factory.SignInHouseholdAsync(rob, "rob@example.com");
        await _factory.SignInHouseholdAsync(neighbour, "neighbour@example.com");
        using HttpResponseMessage robs = await PutAsync(rob, 0, Plan);

        PlanAnswer theirs = (await neighbour.GetFromJsonAsync<PlanAnswer>(new Uri("/api/account/plan", UriKind.Relative)))!;
        using HttpResponseMessage theirWrite = await PutAsync(neighbour, 0, Changed);
        using HttpResponseMessage guestRead = await guest.GetAsync(new Uri("/api/account/plan", UriKind.Relative));
        using HttpResponseMessage guestWrite = await PutAsync(guest, 0, Plan);
        using HttpResponseMessage byAddress = await rob.GetAsync(new Uri("/api/account/plan?account=2&email=neighbour@example.com", UriKind.Relative));

        theirs.ShouldBe(new PlanAnswer(0, null), "a neighbour's account has no copy of Rob's plan");
        theirWrite.StatusCode.ShouldBe(HttpStatusCode.OK, "their first write is their own first copy, not a clash with his");
        guestRead.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        guestWrite.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await byAddress.Content.ReadFromJsonAsync<PlanAnswer>())!.ShouldBe(new PlanAnswer(1, Plan), "nothing in an address chooses the account; the session does");
        (await rob.GetFromJsonAsync<PlanAnswer>(new Uri("/api/account/plan", UriKind.Relative)))!.Body.ShouldBe(Plan);
    }

    [Fact]
    public async Task A_write_without_the_sync_header_or_from_another_site_is_refused()
    {
        using HttpClient client = _factory.CreateClient();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");

        using HttpResponseMessage bare = await PutAsync(client, 0, Plan, header: false);
        Sentence sentence = (await bare.Content.ReadFromJsonAsync<Sentence>())!;
        using HttpResponseMessage crossSite = await PutAsync(client, 0, Plan, fetchSite: "cross-site");
        using HttpResponseMessage sameSite = await PutAsync(client, 0, Plan, fetchSite: "same-site");
        using HttpResponseMessage sameOrigin = await PutAsync(client, 0, Plan, fetchSite: "same-origin");

        bare.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        sentence.Message.ShouldBe("That request did not come from a Selficient page, so nothing was changed.");
        crossSite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        sameSite.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "another site on a neighbouring name is still another site");
        sameOrigin.StatusCode.ShouldBe(HttpStatusCode.OK);
        DefaultHttpContext none = new();
        AccountEndpoints.IsFromOurPages(none.Request).ShouldBeFalse();
        AccountEndpoints.SyncHeader.ShouldBe("X-Selficient-Sync");
    }

    [Fact]
    public async Task What_is_not_a_plan_is_refused_before_it_is_stored()
    {
        using HttpClient client = _factory.CreateClient();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");

        using HttpResponseMessage notAPlan = await PutAsync(client, 0, """{"plan":{"profile":{"<script>":"x"},"ticks":[]},"detailsAt":1}""");
        Sentence refused = (await notAPlan.Content.ReadFromJsonAsync<Sentence>())!;
        using HttpResponseMessage notJson = await PutAsync(client, 0, "anything at all");
        using HttpResponseMessage notText = await PutBytesAsync(client, "0", [0xC3, 0x28, 0xFF]);
        using HttpResponseMessage noBase = await PutBytesAsync(client, null, Encoding.UTF8.GetBytes(Plan));
        Sentence missing = (await noBase.Content.ReadFromJsonAsync<Sentence>())!;
        using HttpResponseMessage wordBase = await PutBytesAsync(client, "latest", Encoding.UTF8.GetBytes(Plan));
        using HttpResponseMessage negativeBase = await PutBytesAsync(client, "-1", Encoding.UTF8.GetBytes(Plan));
        using HttpResponseMessage tooLarge = await PutBytesAsync(client, "0", new byte[PlanGuard.MaxBytes + 1]);
        Sentence large = (await tooLarge.Content.ReadFromJsonAsync<Sentence>())!;

        notAPlan.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        refused.Message.ShouldBe("That is not a Selficient plan, so it was not kept.");
        notJson.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        notText.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noBase.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missing.Message.ShouldBe("The write did not say which version of the plan it was based on, so nothing was changed.");
        wordBase.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        negativeBase.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        large.Message.ShouldBe("That is not a Selficient plan, so it was not kept.");
        (await client.GetFromJsonAsync<PlanAnswer>(new Uri("/api/account/plan", UriKind.Relative)))!.ShouldBe(new PlanAnswer(0, null), "nothing was kept");
        AccountEndpoints.Text([0xC3, 0x28]).ShouldBeNull();
        AccountEndpoints.Text("plan"u8.ToArray()).ShouldBe("plan");
    }

    [Fact]
    public async Task The_stored_plan_is_not_readable_in_the_database_and_goes_with_the_account()
    {
        using HttpClient client = _factory.CreateClient();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");
        using HttpResponseMessage written = await PutAsync(client, 0, Plan);
        await using SqlConnection connection = new(_factory.Services.GetRequiredService<IConfiguration>()["Database:ConnectionString"]!);

        byte[] stored = (await connection.QuerySingleAsync<byte[]>("SELECT snapshot FROM dbo.household_plans"))!;
        using HttpResponseMessage deleted = await client.PostAsync(new Uri("/account/delete", UriKind.Relative), OperatorSignInHelper.Form(await OperatorSignInHelper.TokenAsync(client, "/account/delete")));
        int left = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.household_plans");

        string asText = Encoding.Latin1.GetString(stored);
        asText.ShouldNotContain("NP12", customMessage: "the postcode is not in the table");
        asText.ShouldNotContain("headtorches");
        asText.ShouldNotContain("profile");
        stored.Length.ShouldBeGreaterThan(Plan.Length, "ciphertext, with its key id and its tag");
        left.ShouldBe(0, "deleting the account deletes the plan it kept");
    }

    [Fact]
    public async Task A_stored_plan_the_key_ring_cannot_open_reads_as_none_and_is_replaced()
    {
        using HttpClient client = _factory.CreateClient();
        await _factory.SignInHouseholdAsync(client, "rob@example.com");
        using HttpResponseMessage written = await PutAsync(client, 0, Plan);
        await using SqlConnection connection = new(_factory.Services.GetRequiredService<IConfiguration>()["Database:ConnectionString"]!);
        await connection.ExecuteAsync("UPDATE dbo.household_plans SET snapshot = 0x0102030405");

        PlanAnswer unreadable = (await client.GetFromJsonAsync<PlanAnswer>(new Uri("/api/account/plan", UriKind.Relative)))!;
        using HttpResponseMessage replaced = await PutAsync(client, unreadable.Version, Changed);
        PlanAnswer read = (await client.GetFromJsonAsync<PlanAnswer>(new Uri("/api/account/plan", UriKind.Relative)))!;

        unreadable.ShouldBe(new PlanAnswer(1, null), "the version, so the browser that still has the plan can write it back");
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.ShouldBe(new PlanAnswer(2, Changed));
    }

    [Fact]
    public async Task Sync_is_rate_limited_on_a_policy_of_its_own()
    {
        using SelficientWebApplicationFactory limited = new SelficientWebApplicationFactory().WithSetting("RateLimits:SyncPerMinute", "3");
        using HttpClient client = limited.CreateClient();
        await limited.SignInHouseholdAsync(client, "rob@example.com");
        List<HttpStatusCode> statuses = [];

        for (int i = 0; i < 4; i++)
        {
            using HttpResponseMessage read = await client.GetAsync(new Uri("/api/account/plan", UriKind.Relative));
            statuses.Add(read.StatusCode);
        }

        using HttpResponseMessage write = await PutAsync(client, 0, Plan);
        using HttpResponseMessage who = await client.GetAsync(new Uri("/api/account", UriKind.Relative));

        statuses.ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests]);
        write.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, "reads and writes spend the same allowance");
        who.StatusCode.ShouldBe(HttpStatusCode.OK, "and asking who is signed in spends none of it");
        new RateLimitOptions().SyncPerMinute.ShouldBe(60);
    }

    [Fact]
    public void The_plan_protector_reads_back_what_it_wrote_and_nothing_else()
    {
        DataProtectionPlanProtector protector = new(DataProtectionProvider.Create("Selficient.Tests"));
        byte[] plan = Encoding.UTF8.GetBytes(Plan);

        byte[] sealed_ = protector.Protect(plan);

        protector.Unprotect(sealed_).ShouldBe(plan);
        sealed_.ShouldNotBe(plan);
        protector.Unprotect([9, 9, 9]).ShouldBeNull("a value this ring did not write is not a plan, and not an exception either");
        new DataProtectionPlanProtector(DataProtectionProvider.Create("Another.Ring")).Unprotect(sealed_).ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => protector.Protect(null!));
        Should.Throw<ArgumentNullException>(() => protector.Unprotect(null!));
        Should.Throw<ArgumentNullException>(() => new DataProtectionPlanProtector(null!));
    }

    public void Dispose() => _factory.Dispose();

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, long baseVersion, string body, bool header = true, string? fetchSite = null) =>
        PutBytesAsync(client, baseVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(body), header, fetchSite);

    private static async Task<HttpResponseMessage> PutBytesAsync(HttpClient client, string? baseVersion, byte[] body, bool header = true, string? fetchSite = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(baseVersion is null ? "/api/account/plan" : $"/api/account/plan?base={baseVersion}", UriKind.Relative));
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
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
}
