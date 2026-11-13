using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Streets;
using Selficient.TestKit;
using Selficient.Web.Endpoints;

namespace Selficient.Web.Tests.Endpoints;

/// <summary>
/// Street groups through the real pipeline, the real sign-in and a real database. The cases are
/// stage 1 of <c>docs/threat-models/street-groups.md</c>. The server is sent only what a browser
/// encrypted, so every body here is random bytes: there is nothing for it to understand.
/// </summary>
public sealed class StreetEndpointsTests : IDisposable
{
    private static readonly byte[] PassBytes = RandomNumberGenerator.GetBytes(32);
    private static readonly string Pass = Convert.ToBase64String(PassBytes);
    private static readonly string PassHash = Convert.ToBase64String(SHA256.HashData(PassBytes));

    private readonly SelficientWebApplicationFactory _factory = new SelficientWebApplicationFactory().WithSetting("RateLimits:SyncPerMinute", "10000").WithSetting("RateLimits:SignInPerMinute", "10000");

    private static string Blob() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(96));

    private async Task<HttpClient> SignedInAsync(string email)
    {
        HttpClient client = _factory.CreateClient();
        await _factory.SignInHouseholdAsync(client, email);
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body, bool fromOurPages = true)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative));
        if (body is string raw)
        {
            request.Content = new StringContent(raw, Encoding.UTF8, "application/json");
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body);
            await request.Content.LoadIntoBufferAsync();
        }

        if (fromOurPages)
        {
            request.Headers.Add(AccountEndpoints.SyncHeader, "1");
        }

        return await client.SendAsync(request);
    }

    private static async Task<StreetJoined> StartAsync(HttpClient client, string? record = null)
    {
        using HttpResponseMessage made = await SendAsync(client, HttpMethod.Post, "/api/street", new { passHash = PassHash, about = Blob(), record = record ?? Blob() });
        made.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await made.Content.ReadFromJsonAsync<StreetJoined>())!;
    }

    // Buffered first, so a sentence can be read twice: once for itself and once to hold another against it.
    private static async Task<string> SentenceAsync(HttpResponseMessage response)
    {
        await response.Content.LoadIntoBufferAsync();
        using System.Text.Json.JsonDocument answer = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return answer.RootElement.GetProperty("message").GetString()!;
    }

    [Fact]
    public async Task A_group_is_answered_only_to_its_members_and_a_stranger_cannot_tell_it_exists()
    {
        using HttpClient rob = await SignedInAsync("rob@example.com");
        using HttpClient doris = await SignedInAsync("doris@example.com");
        using HttpClient stranger = await SignedInAsync("stranger@example.com");
        using HttpClient guest = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string robsRecord = Blob();
        string dorisRecord = Blob();

        StreetJoined started = await StartAsync(rob, robsRecord);
        using HttpResponseMessage joined = await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/join", new { pass = Pass, record = dorisRecord });
        StreetJoined dorisIn = (await joined.Content.ReadFromJsonAsync<StreetJoined>())!;
        using HttpResponseMessage read = await doris.GetAsync(new Uri($"/api/street/{started.GroupId}", UriKind.Relative));
        string raw = await read.Content.ReadAsStringAsync();
        StreetRead group = (await doris.GetFromJsonAsync<StreetRead>(new Uri($"/api/street/{started.GroupId}", UriKind.Relative)))!;
        List<StreetMine> robsGroups = (await rob.GetFromJsonAsync<List<StreetMine>>(new Uri("/api/street", UriKind.Relative)))!;
        using HttpResponseMessage theirs = await stranger.GetAsync(new Uri($"/api/street/{started.GroupId}", UriKind.Relative));
        using HttpResponseMessage nothing = await stranger.GetAsync(new Uri($"/api/street/{Guid.NewGuid()}", UriKind.Relative));
        using HttpResponseMessage notSignedIn = await guest.GetAsync(new Uri($"/api/street/{started.GroupId}", UriKind.Relative));
        using HttpResponseMessage notAnId = await rob.GetAsync(new Uri("/api/street/maple-road", UriKind.Relative));

        joined.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.CacheControl!.NoStore.ShouldBeTrue("what a street shares is kept by no cache");
        group.GroupId.ShouldBe(started.GroupId);
        group.You.ShouldBe(dorisIn.MemberId);
        group.Homes.Select(home => (home.MemberId, home.Record, home.MayRemove)).ShouldBe([(started.MemberId, robsRecord, true), (dorisIn.MemberId, dorisRecord, false)]);
        raw.ShouldNotContain("example.com", customMessage: "no address reaches another member");
        raw.ShouldNotContain("accountId", Case.Insensitive);
        robsGroups.ShouldBe([new StreetMine(started.GroupId, started.MemberId, true)]);
        theirs.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        nothing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SentenceAsync(theirs)).ShouldBe(await SentenceAsync(nothing), "a group somebody is not in and a group that is not there say the same");
        (await SentenceAsync(theirs)).ShouldBe("There is no such group, or you are not in it.");
        ((int)notSignedIn.StatusCode).ShouldBeOneOf(302, 401);
        notAnId.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Joining_takes_the_pass_from_the_invitation_and_a_wrong_one_opens_nothing()
    {
        using HttpClient rob = await SignedInAsync("rob@example.com");
        using HttpClient doris = await SignedInAsync("doris@example.com");
        StreetJoined started = await StartAsync(rob);

        using HttpResponseMessage wrong = await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/join", new { pass = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), record = Blob() });
        using HttpResponseMessage noGroup = await SendAsync(doris, HttpMethod.Post, $"/api/street/{Guid.NewGuid()}/join", new { pass = Pass, record = Blob() });
        using HttpResponseMessage first = await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/join", new { pass = Pass, record = Blob() });
        using HttpResponseMessage again = await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/join", new { pass = Pass, record = Blob() });

        wrong.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SentenceAsync(wrong)).ShouldBe("That invitation does not open a group. Ask your neighbour for a new one.");
        (await SentenceAsync(noGroup)).ShouldBe(await SentenceAsync(wrong));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        StreetJoined firstTime = (await first.Content.ReadFromJsonAsync<StreetJoined>())!;
        (await again.Content.ReadFromJsonAsync<StreetJoined>()).ShouldBe(firstTime, "opening the link twice is the same membership");
        (await rob.GetFromJsonAsync<StreetRead>(new Uri($"/api/street/{started.GroupId}", UriKind.Relative)))!.Homes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_member_writes_only_its_own_record()
    {
        using HttpClient rob = await SignedInAsync("rob@example.com");
        using HttpClient doris = await SignedInAsync("doris@example.com");
        using HttpClient stranger = await SignedInAsync("stranger@example.com");
        string robsRecord = Blob();
        StreetJoined started = await StartAsync(rob, robsRecord);
        await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/join", new { pass = Pass, record = Blob() });
        string changed = Blob();

        using HttpResponseMessage written = await SendAsync(doris, HttpMethod.Put, $"/api/street/{started.GroupId}/record", new { record = changed, memberId = started.MemberId });
        using HttpResponseMessage outsider = await SendAsync(stranger, HttpMethod.Put, $"/api/street/{started.GroupId}/record", new { record = Blob() });
        StreetRead group = (await rob.GetFromJsonAsync<StreetRead>(new Uri($"/api/street/{started.GroupId}", UriKind.Relative)))!;

        written.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await written.Content.ReadFromJsonAsync<StreetDone>())!.Done.ShouldBeTrue();
        outsider.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        group.Homes[0].Record.ShouldBe(robsRecord, "naming another member in the body changes nothing of theirs");
        group.Homes[1].Record.ShouldBe(changed);
    }

    [Fact]
    public async Task The_longest_standing_member_removes_a_home_and_the_old_invitation_dies_with_it()
    {
        using HttpClient rob = await SignedInAsync("rob@example.com");
        using HttpClient doris = await SignedInAsync("doris@example.com");
        using HttpClient third = await SignedInAsync("third@example.com");
        StreetJoined started = await StartAsync(rob);
        using HttpResponseMessage dorisJoined = await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/join", new { pass = Pass, record = Blob() });
        StreetJoined dorisIn = (await dorisJoined.Content.ReadFromJsonAsync<StreetJoined>())!;
        byte[] newPass = RandomNumberGenerator.GetBytes(32);
        string newHash = Convert.ToBase64String(SHA256.HashData(newPass));

        using HttpResponseMessage notHers = await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/remove", new { memberId = started.MemberId, passHash = newHash });
        using HttpResponseMessage himself = await SendAsync(rob, HttpMethod.Post, $"/api/street/{started.GroupId}/remove", new { memberId = started.MemberId, passHash = newHash });
        using HttpResponseMessage nobody = await SendAsync(rob, HttpMethod.Post, $"/api/street/{started.GroupId}/remove", new { passHash = newHash });
        using HttpResponseMessage noHash = await SendAsync(rob, HttpMethod.Post, $"/api/street/{started.GroupId}/remove", new { memberId = dorisIn.MemberId });
        using HttpResponseMessage removed = await SendAsync(rob, HttpMethod.Post, $"/api/street/{started.GroupId}/remove", new { memberId = dorisIn.MemberId, passHash = newHash });
        using HttpResponseMessage gone = await doris.GetAsync(new Uri($"/api/street/{started.GroupId}", UriKind.Relative));
        using HttpResponseMessage back = await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/join", new { pass = Pass, record = Blob() });
        using HttpResponseMessage invited = await SendAsync(third, HttpMethod.Post, $"/api/street/{started.GroupId}/join", new { pass = Convert.ToBase64String(newPass), record = Blob() });

        notHers.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SentenceAsync(notHers)).ShouldBe("Only the person who has been in the group longest can remove a home from it, and nobody removes themselves: leave instead.");
        himself.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        nobody.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        noHash.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound, "the server stops answering a home that was removed");
        back.StatusCode.ShouldBe(HttpStatusCode.NotFound, "and the link it had opens nothing");
        invited.StatusCode.ShouldBe(HttpStatusCode.OK, "while the new invitation lets a neighbour in");
    }

    [Fact]
    public async Task When_the_starter_leaves_the_longest_standing_member_may_remove_and_an_empty_group_is_swept()
    {
        using HttpClient rob = await SignedInAsync("rob@example.com");
        using HttpClient doris = await SignedInAsync("doris@example.com");
        StreetJoined started = await StartAsync(rob);
        await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/join", new { pass = Pass, record = Blob() });

        using HttpResponseMessage left = await SendAsync(rob, HttpMethod.Post, $"/api/street/{started.GroupId}/leave", new { });
        using HttpResponseMessage twice = await SendAsync(rob, HttpMethod.Post, $"/api/street/{started.GroupId}/leave", new { });
        List<StreetMine> dorisGroups = (await doris.GetFromJsonAsync<List<StreetMine>>(new Uri("/api/street", UriKind.Relative)))!;
        using HttpResponseMessage last = await SendAsync(doris, HttpMethod.Post, $"/api/street/{started.GroupId}/leave", new { });

        left.StatusCode.ShouldBe(HttpStatusCode.OK);
        twice.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        dorisGroups.ShouldHaveSingleItem().MayRemove.ShouldBeTrue();
        last.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _factory.Services.GetRequiredService<IStreetStore>().PassHashAsync(started.GroupId, CancellationToken.None)).ShouldBeNull("the last one out takes the group with them");
        _factory.Services.GetServices<Selficient.Application.Operations.IExpiring>().ShouldContain(expiring => expiring is StreetGroups, "and the housekeeper sweeps a group an account's deletion emptied");
    }

    [Fact]
    public async Task A_write_that_did_not_come_from_our_pages_changes_nothing()
    {
        using HttpClient rob = await SignedInAsync("rob@example.com");
        StreetJoined started = await StartAsync(rob);

        using HttpResponseMessage start = await SendAsync(rob, HttpMethod.Post, "/api/street", new { passHash = PassHash, about = Blob(), record = Blob() }, fromOurPages: false);
        using HttpResponseMessage leave = await SendAsync(rob, HttpMethod.Post, $"/api/street/{started.GroupId}/leave", new { }, fromOurPages: false);

        start.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SentenceAsync(start)).ShouldBe(AccountEndpoints.NotFromHere);
        leave.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await rob.GetFromJsonAsync<List<StreetMine>>(new Uri("/api/street", UriKind.Relative)))!.ShouldHaveSingleItem("the group is still there and still the only one");
    }

    [Fact]
    public async Task What_is_too_large_too_many_or_not_ciphertext_is_refused_with_a_sentence()
    {
        using HttpClient rob = await SignedInAsync("rob@example.com");

        using HttpResponseMessage words = await SendAsync(rob, HttpMethod.Post, "/api/street", new { passHash = PassHash, about = "Maple Road", record = Blob() });
        using HttpResponseMessage notJson = await SendAsync(rob, HttpMethod.Post, "/api/street", "{ this is not json");
        using HttpResponseMessage nothing = await SendAsync(rob, HttpMethod.Post, "/api/street", "null");
        using HttpResponseMessage huge = await SendAsync(rob, HttpMethod.Post, "/api/street", new { passHash = PassHash, about = Blob(), record = new string('A', StreetEndpoints.MaxBodyBytes) });
        using HttpResponseMessage noBody = await SendAsync(rob, HttpMethod.Post, "/api/street", null);
        for (int i = 0; i < StreetGroups.MostGroups; i++)
        {
            await StartAsync(rob);
        }

        using HttpResponseMessage sixth = await SendAsync(rob, HttpMethod.Post, "/api/street", new { passHash = PassHash, about = Blob(), record = Blob() });

        words.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SentenceAsync(words)).ShouldBe("That could not be kept: it is not something this page wrote, or it is too large.");
        notJson.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        nothing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        huge.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        ((int)noBody.StatusCode).ShouldBeOneOf(400, 413);
        sixth.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SentenceAsync(sixth)).ShouldBe("An account can be in five groups. Leave one to join another.");
        Should.Throw<ArgumentNullException>(() => StreetEndpoints.MapStreetEndpoints(null!));
    }

    public void Dispose() => _factory.Dispose();
}
