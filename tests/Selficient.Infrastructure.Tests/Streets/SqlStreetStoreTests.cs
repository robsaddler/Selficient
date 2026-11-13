using Selficient.Application.Streets;
using Selficient.Infrastructure.Accounts;
using Selficient.Infrastructure.Data;
using Selficient.Infrastructure.Streets;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Streets;

/// <summary>Street groups in a real database: who is in what, who may remove, and what goes when somebody goes.</summary>
public sealed class SqlStreetStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 13, 19, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Pass = [.. Enumerable.Repeat((byte)7, 32)];
    private static readonly byte[] NewPass = [.. Enumerable.Repeat((byte)9, 32)];

    private readonly TestDatabase _database = TestDatabase.Create();
    private SqlStreetStore _store = null!;
    private SqlAccountStore _accounts = null!;
    private long _rob;
    private long _doris;
    private long _third;

    public async ValueTask InitializeAsync()
    {
        await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);
        _store = new SqlStreetStore(_database.ConnectionString);
        _accounts = new SqlAccountStore(_database.ConnectionString);
        _rob = (await _accounts.FindOrCreateAsync("rob@example.com", Evening, CancellationToken.None)).Id;
        _doris = (await _accounts.FindOrCreateAsync("doris@example.com", Evening, CancellationToken.None)).Id;
        _third = (await _accounts.FindOrCreateAsync("third@example.com", Evening, CancellationToken.None)).Id;
    }

    private async Task<(Guid Group, Guid Member)> StartAsync(long account, int most = 5)
    {
        (Guid group, Guid member) = (Guid.NewGuid(), Guid.NewGuid());
        (await _store.CreateAsync(account, group, member, Pass, "about-ciphertext", "robs-record", Evening, most, CancellationToken.None)).ShouldBeTrue();
        return (group, member);
    }

    [Fact]
    public void The_store_needs_a_connection_string()
    {
        Should.Throw<ArgumentException>(() => new SqlStreetStore(" "));
    }

    [Fact]
    public async Task A_group_is_read_only_by_its_members_longest_standing_first()
    {
        (Guid group, Guid robsId) = await StartAsync(_rob);
        Guid dorisId = Guid.NewGuid();

        (await _store.JoinAsync(_doris, group, dorisId, "doris-record", Evening.AddMinutes(5), 100, 5, CancellationToken.None)).ShouldBe(JoinOutcome.Joined);
        (await _store.JoinAsync(_doris, group, Guid.NewGuid(), "again", Evening.AddMinutes(6), 100, 5, CancellationToken.None)).ShouldBe(JoinOutcome.AlreadyIn);
        StreetGroup forRob = (await _store.ReadAsync(_rob, group, CancellationToken.None))!;
        StreetGroup forDoris = (await _store.ReadAsync(_doris, group, CancellationToken.None))!;

        forRob.About.ShouldBe("about-ciphertext");
        forRob.You.ShouldBe(robsId);
        forDoris.You.ShouldBe(dorisId);
        forRob.Members.ShouldBe([new StreetMember(robsId, "robs-record", Evening, true), new StreetMember(dorisId, "doris-record", Evening.AddMinutes(5), false)]);
        (await _store.ReadAsync(_third, group, CancellationToken.None)).ShouldBeNull("somebody who is not in it is told nothing");
        (await _store.ReadAsync(_rob, Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        (await _store.PassHashAsync(group, CancellationToken.None)).ShouldBe(Pass);
        (await _store.PassHashAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        (await _store.MembershipsAsync(_rob, CancellationToken.None)).ShouldBe([new StreetMembership(group, robsId, true)]);
        (await _store.MembershipsAsync(_doris, CancellationToken.None)).ShouldBe([new StreetMembership(group, dorisId, false)]);
        (await _store.MembershipsAsync(_third, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_member_writes_only_its_own_record()
    {
        (Guid group, Guid robsId) = await StartAsync(_rob);
        await _store.JoinAsync(_doris, group, Guid.NewGuid(), "doris-record", Evening, 100, 5, CancellationToken.None);

        (await _store.PutRecordAsync(_doris, group, "doris-again", Evening.AddHours(1), CancellationToken.None)).ShouldBeTrue();
        (await _store.PutRecordAsync(_third, group, "an-outsider", Evening.AddHours(1), CancellationToken.None)).ShouldBeFalse();

        StreetGroup read = (await _store.ReadAsync(_rob, group, CancellationToken.None))!;
        read.Members[0].ShouldBe(new StreetMember(robsId, "robs-record", Evening, true), "another member's write leaves this one alone");
        read.Members[1].Record.ShouldBe("doris-again");
        read.Members[1].UpdatedAt.ShouldBe(Evening.AddHours(1));
    }

    [Fact]
    public async Task A_full_group_and_an_account_in_too_many_are_refused_at_the_count()
    {
        (Guid group, _) = await StartAsync(_rob);

        (await _store.JoinAsync(_doris, group, Guid.NewGuid(), "record", Evening, 1, 5, CancellationToken.None)).ShouldBe(JoinOutcome.Full);
        (await _store.JoinAsync(_doris, group, Guid.NewGuid(), "record", Evening, 2, 0, CancellationToken.None)).ShouldBe(JoinOutcome.TooManyGroups);
        (await _store.CreateAsync(_rob, Guid.NewGuid(), Guid.NewGuid(), Pass, "about", "record", Evening, 1, CancellationToken.None)).ShouldBeFalse("an account already in as many groups as it may be starts no more");
        (await _store.MembershipsAsync(_rob, CancellationToken.None)).Count.ShouldBe(1);
        (await _store.ReadAsync(_doris, group, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task The_longest_standing_member_removes_a_home_and_the_pass_changes_with_it()
    {
        (Guid group, Guid robsId) = await StartAsync(_rob);
        (Guid dorisId, Guid thirdId) = (Guid.NewGuid(), Guid.NewGuid());
        await _store.JoinAsync(_doris, group, dorisId, "doris", Evening, 100, 5, CancellationToken.None);
        await _store.JoinAsync(_third, group, thirdId, "third", Evening, 100, 5, CancellationToken.None);

        (await _store.RemoveAsync(_doris, group, thirdId, NewPass, CancellationToken.None)).ShouldBeFalse("only the longest-standing member removes");
        (await _store.RemoveAsync(_rob, group, robsId, NewPass, CancellationToken.None)).ShouldBeFalse("and never themselves");
        (await _store.RemoveAsync(_rob, group, Guid.NewGuid(), NewPass, CancellationToken.None)).ShouldBeFalse();
        (await _store.PassHashAsync(group, CancellationToken.None)).ShouldBe(Pass, "a removal that did not happen changes no pass");

        (await _store.RemoveAsync(_rob, group, thirdId, NewPass, CancellationToken.None)).ShouldBeTrue();

        (await _store.PassHashAsync(group, CancellationToken.None)).ShouldBe(NewPass);
        (await _store.ReadAsync(_third, group, CancellationToken.None)).ShouldBeNull();
        (await _store.ReadAsync(_rob, group, CancellationToken.None))!.Members.Select(member => member.MemberId).ShouldBe([robsId, dorisId]);
    }

    [Fact]
    public async Task When_the_starter_goes_the_next_longest_standing_may_remove_and_an_empty_group_goes_too()
    {
        (Guid group, _) = await StartAsync(_rob);
        Guid dorisId = Guid.NewGuid();
        await _store.JoinAsync(_doris, group, dorisId, "doris", Evening, 100, 5, CancellationToken.None);
        await _store.JoinAsync(_third, group, Guid.NewGuid(), "third", Evening, 100, 5, CancellationToken.None);

        (await _store.LeaveAsync(_rob, group, CancellationToken.None)).ShouldBeTrue();
        (await _store.LeaveAsync(_rob, group, CancellationToken.None)).ShouldBeFalse();
        (await _store.MembershipsAsync(_doris, CancellationToken.None)).ShouldBe([new StreetMembership(group, dorisId, true)], "Doris has now been in longest");

        // An account that is deleted takes its membership with it.
        (await _accounts.DeleteAsync(_doris, CancellationToken.None)).ShouldBeTrue();
        (await _store.ReadAsync(_third, group, CancellationToken.None))!.Members.ShouldHaveSingleItem().MayRemove.ShouldBeTrue();

        // The last one out takes the group with them.
        (await _store.LeaveAsync(_third, group, CancellationToken.None)).ShouldBeTrue();
        (await _store.PassHashAsync(group, CancellationToken.None)).ShouldBeNull();

        // A group emptied by its only member's account being deleted is swept.
        (Guid orphan, _) = await StartAsync(_third);
        (await _store.SweepEmptyAsync(CancellationToken.None)).ShouldBe(0);
        await _accounts.DeleteAsync(_third, CancellationToken.None);
        (await _store.PassHashAsync(orphan, CancellationToken.None)).ShouldNotBeNull();
        (await _store.SweepEmptyAsync(CancellationToken.None)).ShouldBe(1);
        (await _store.PassHashAsync(orphan, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task The_store_checks_its_arguments()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => _store.CreateAsync(_rob, Guid.NewGuid(), Guid.NewGuid(), null!, "about", "record", Evening, 5, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.CreateAsync(_rob, Guid.NewGuid(), Guid.NewGuid(), Pass, null!, "record", Evening, 5, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.CreateAsync(_rob, Guid.NewGuid(), Guid.NewGuid(), Pass, "about", null!, Evening, 5, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.JoinAsync(_rob, Guid.NewGuid(), Guid.NewGuid(), null!, Evening, 100, 5, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.PutRecordAsync(_rob, Guid.NewGuid(), null!, Evening, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _store.RemoveAsync(_rob, Guid.NewGuid(), Guid.NewGuid(), null!, CancellationToken.None));
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }
}
