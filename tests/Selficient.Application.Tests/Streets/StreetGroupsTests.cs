using System.Security.Cryptography;
using NSubstitute;
using Selficient.Application.Streets;
using Selficient.Domain.Primitives;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Streets;

/// <summary>
/// What the server decides about a street group without being able to read it: who is let in, what
/// is too large or not ciphertext-shaped, and what each refusal says.
/// </summary>
public sealed class StreetGroupsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 19, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PassBytes = [.. Enumerable.Range(1, 32).Select(static i => (byte)i)];
    private static readonly string Pass = Convert.ToBase64String(PassBytes);
    private static readonly string PassHash = Convert.ToBase64String(SHA256.HashData(PassBytes));
    private static readonly string Blob = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static readonly Guid Group = Guid.NewGuid();

    private readonly IStreetStore _store = Substitute.For<IStreetStore>();
    private readonly StreetGroups _groups;

    public StreetGroupsTests()
    {
        _groups = new StreetGroups(_store, new ManualClock(Now));
    }

    [Fact]
    public async Task Starting_a_group_keeps_the_hash_of_the_pass_and_two_blobs_and_never_a_key()
    {
        _store.CreateAsync(7, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<byte[]>(), Blob, Blob, Now, StreetGroups.MostGroups, Arg.Any<CancellationToken>()).Returns(true);

        Result<StreetMembership> made = await _groups.StartAsync(7, PassHash, Blob, Blob, CancellationToken.None);

        made.Value.MayRemove.ShouldBeTrue();
        made.Value.GroupId.ShouldNotBe(Guid.Empty);
        made.Value.MemberId.ShouldNotBe(made.Value.GroupId);
        await _store.Received(1).CreateAsync(7, made.Value.GroupId, made.Value.MemberId, Arg.Is<byte[]>(hash => hash.SequenceEqual(SHA256.HashData(PassBytes))), Blob, Blob, Now, 5, Arg.Any<CancellationToken>());

        _store.CreateAsync(8, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(false);
        (await _groups.StartAsync(8, PassHash, Blob, Blob, CancellationToken.None)).Error.Message.ShouldBe("An account can be in five groups. Leave one to join another.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Maple Road")]
    [InlineData("not base64 at all!")]
    [InlineData("QUJD")]
    public async Task What_is_too_large_too_short_or_not_ciphertext_shaped_is_refused_with_a_sentence(string? bad)
    {
        string tooLong = Convert.ToBase64String(new byte[StreetGroups.MaxRecordLength]);
        string longAbout = Convert.ToBase64String(new byte[StreetGroups.MaxAboutLength]);

        (await _groups.StartAsync(7, PassHash, Blob, bad, CancellationToken.None)).Error.ShouldBe(StreetGroups.NotARecord);
        (await _groups.StartAsync(7, PassHash, bad, Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.NotARecord);
        (await _groups.StartAsync(7, bad, Blob, Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.NotARecord);
        (await _groups.StartAsync(7, Blob, Blob, Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.NotARecord, "a hash is thirty-two bytes and nothing else");
        (await _groups.StartAsync(7, PassHash, Blob, tooLong, CancellationToken.None)).Error.ShouldBe(StreetGroups.NotARecord);
        (await _groups.StartAsync(7, PassHash, longAbout, Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.NotARecord);
        (await _groups.JoinAsync(7, Group, Pass, bad, CancellationToken.None)).Error.ShouldBe(StreetGroups.NotARecord);
        (await _groups.ShareAsync(7, Group, bad, CancellationToken.None)).Error.ShouldBe(StreetGroups.NotARecord);
        (await _groups.RemoveAsync(7, Group, Guid.NewGuid(), bad, CancellationToken.None)).Error.Message.ShouldBe("That could not be kept: it is not something this page wrote, or it is too large.");
        await _store.DidNotReceiveWithAnyArgs().CreateAsync(default, default, default, default!, default!, default!, default, default, default);
        StreetGroups.IsCiphertext(Blob, StreetGroups.MaxRecordLength).ShouldBeTrue();
        StreetGroups.IsCiphertext(Blob + "\n", StreetGroups.MaxRecordLength).ShouldBeFalse("white space is not something our script sends");
    }

    [Fact]
    public async Task Joining_takes_the_pass_from_the_invitation_and_a_wrong_one_opens_nothing()
    {
        Guid dorisId = Guid.NewGuid();
        _store.PassHashAsync(Group, Arg.Any<CancellationToken>()).Returns(SHA256.HashData(PassBytes));
        _store.JoinAsync(9, Group, Arg.Any<Guid>(), Blob, Now, StreetGroups.MostMembers, StreetGroups.MostGroups, Arg.Any<CancellationToken>()).Returns(JoinOutcome.Joined);
        _store.MembershipsAsync(9, Arg.Any<CancellationToken>()).Returns([new StreetMembership(Guid.NewGuid(), Guid.NewGuid(), true), new StreetMembership(Group, dorisId, false)]);

        Result<StreetMembership> joined = await _groups.JoinAsync(9, Group, Pass, Blob, CancellationToken.None);

        joined.Value.ShouldBe(new StreetMembership(Group, dorisId, false), "the member id the store holds, which for a member who was in already is not the one just made");

        string wrong = Convert.ToBase64String(new byte[32]);
        (await _groups.JoinAsync(9, Group, wrong, Blob, CancellationToken.None)).Error.Message.ShouldBe("That invitation does not open a group. Ask your neighbour for a new one.");
        (await _groups.JoinAsync(9, Group, "not a pass", Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.BadInvitation);
        (await _groups.JoinAsync(9, Group, null, Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.BadInvitation);
        (await _groups.JoinAsync(9, Guid.NewGuid(), Pass, Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.BadInvitation, "a group that is not there says the same as a wrong pass");
        (await _groups.JoinAsync(9, Guid.NewGuid(), null, Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.BadInvitation, "and no pass at all does not match a group that is not there");
        await _store.ReceivedWithAnyArgs(1).JoinAsync(default, default, default, default!, default, default, default, default);

        _store.JoinAsync(9, Group, Arg.Any<Guid>(), Blob, Now, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(JoinOutcome.Full, JoinOutcome.TooManyGroups, JoinOutcome.AlreadyIn);
        (await _groups.JoinAsync(9, Group, Pass, Blob, CancellationToken.None)).Error.Message.ShouldBe("This group is full: it holds a hundred homes.");
        (await _groups.JoinAsync(9, Group, Pass, Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.TooMany);
        (await _groups.JoinAsync(9, Group, Pass, Blob, CancellationToken.None)).Value.MemberId.ShouldBe(dorisId, "opening the link a second time is not an error");
    }

    [Fact]
    public async Task A_group_is_answered_only_to_its_members_and_every_write_is_the_accounts_own()
    {
        StreetGroup group = new(Group, Blob, Guid.NewGuid(), []);
        _store.ReadAsync(7, Group, Arg.Any<CancellationToken>()).Returns(group);
        _store.PutRecordAsync(7, Group, Blob, Now, Arg.Any<CancellationToken>()).Returns(true);
        _store.RemoveAsync(7, Group, Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(true);
        _store.LeaveAsync(7, Group, Arg.Any<CancellationToken>()).Returns(true);
        _store.MembershipsAsync(7, Arg.Any<CancellationToken>()).Returns([new StreetMembership(Group, group.You, true)]);

        (await _groups.ReadAsync(7, Group, CancellationToken.None)).Value.ShouldBeSameAs(group);
        (await _groups.ReadAsync(8, Group, CancellationToken.None)).Error.Message.ShouldBe("There is no such group, or you are not in it.");
        (await _groups.MineAsync(7, CancellationToken.None)).ShouldHaveSingleItem();
        (await _groups.ShareAsync(7, Group, Blob, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await _groups.ShareAsync(8, Group, Blob, CancellationToken.None)).Error.ShouldBe(StreetGroups.NoSuchGroup);
        (await _groups.RemoveAsync(7, Group, Guid.NewGuid(), PassHash, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await _groups.RemoveAsync(8, Group, Guid.NewGuid(), PassHash, CancellationToken.None)).Error.Message.ShouldBe("Only the person who has been in the group longest can remove a home from it, and nobody removes themselves: leave instead.");
        (await _groups.LeaveAsync(7, Group, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await _groups.LeaveAsync(8, Group, CancellationToken.None)).Error.ShouldBe(StreetGroups.NoSuchGroup);
    }

    [Fact]
    public async Task Groups_nobody_is_left_in_are_swept_and_the_run_log_says_how_many()
    {
        _store.SweepEmptyAsync(Arg.Any<CancellationToken>()).Returns(3);

        (await _groups.SweepAsync(Now, CancellationToken.None)).ShouldBe(3);
        _groups.Describe(1).ShouldBe("1 street group nobody was left in");
        _groups.Describe(3).ShouldBe("3 street groups nobody was left in");
        Should.Throw<ArgumentNullException>(() => new StreetGroups(null!, new ManualClock(Now)));
        Should.Throw<ArgumentNullException>(() => new StreetGroups(_store, null!));
    }
}
