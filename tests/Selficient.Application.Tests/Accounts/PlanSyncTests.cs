using System.Text;
using NSubstitute;
using Selficient.Application.Accounts;
using Selficient.Domain.Primitives;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Accounts;

/// <summary>
/// The copy of a plan an account keeps: what counts as a plan, and what reading and writing it
/// answer. The store's promise that a stale write never lands is proved against a real database in
/// the Infrastructure tests.
/// </summary>
public sealed class PlanSyncTests
{
    private const string Plan = """
        {"plan":{"profile":{"Postcode":"NP12 1GW","People":"4"},"ticks":["headtorches","water-bottled-three-days"],"planned":["power-banks"],"skipped":[],
        "amounts":{"food-no-cook-three-days":10},"assumptions":{"cashRate":15},"house":{"lat":51.66,"lng":-3.2},
        "dependencies":[{"name":"CPAP machine","kind":"PoweredDevice","watts":40,"hoursPerDay":8,"daysOfStock":0,"mustStayCold":false}]},"detailsAt":1794510000000}
        """;

    private static readonly DateTimeOffset Evening = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);

    private readonly IPlanStore _store = Substitute.For<IPlanStore>();
    private readonly IPlanProtector _protector = Substitute.For<IPlanProtector>();
    private readonly ManualClock _clock = new(Evening);

    public PlanSyncTests()
    {
        // A protector that only reverses the bytes, so the tests can see what went to the store.
        _protector.Protect(Arg.Any<byte[]>()).Returns(call => call.Arg<byte[]>().Reverse().ToArray());
        _protector.Unprotect(Arg.Any<byte[]>()).Returns(call => call.Arg<byte[]>().Reverse().ToArray());
    }

    private PlanSync Sync => new(_store, _protector, _clock);

    [Fact]
    public async Task An_account_with_no_copy_reads_as_version_nought()
    {
        SyncedPlan none = await Sync.ReadAsync(41, CancellationToken.None);

        none.ShouldBe(new SyncedPlan(0, null));
        await _store.Received(1).GetAsync(41, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_plan_is_encrypted_on_the_way_in_and_read_back_as_it_was_written()
    {
        byte[]? kept = null;
        _store.TryPutAsync(41, Arg.Do<byte[]>(bytes => kept = bytes), 0, Evening, Arg.Any<CancellationToken>()).Returns(1L);

        Result<long> written = await Sync.WriteAsync(41, Plan, 0, CancellationToken.None);
        _store.GetAsync(41, Arg.Any<CancellationToken>()).Returns(new StoredPlan(kept!, 1, Evening));
        SyncedPlan read = await Sync.ReadAsync(41, CancellationToken.None);

        written.Value.ShouldBe(1);
        kept.ShouldBe(Encoding.UTF8.GetBytes(Plan).Reverse().ToArray(), "what reaches the store has been through the protector");
        read.ShouldBe(new SyncedPlan(1, Plan));
    }

    [Fact]
    public async Task A_stored_plan_the_key_ring_cannot_open_reads_as_its_version_with_no_body()
    {
        _store.GetAsync(41, Arg.Any<CancellationToken>()).Returns(new StoredPlan([1, 2, 3], 7, Evening));
        _protector.Unprotect(Arg.Any<byte[]>()).Returns((byte[]?)null);

        SyncedPlan read = await Sync.ReadAsync(41, CancellationToken.None);

        read.ShouldBe(new SyncedPlan(7, null), "the browser still has the plan, and is told the version to write it back over");
    }

    [Fact]
    public async Task A_write_the_copy_has_moved_on_from_is_stale_and_one_that_is_not_a_plan_never_reaches_the_store()
    {
        _store.TryPutAsync(41, Arg.Any<byte[]>(), 3, Evening, Arg.Any<CancellationToken>()).Returns((long?)null);

        Result<long> stale = await Sync.WriteAsync(41, Plan, 3, CancellationToken.None);
        Result<long> notAPlan = await Sync.WriteAsync(41, """{"hello":"world"}""", 3, CancellationToken.None);
        Result<long> nothing = await Sync.WriteAsync(41, null, 3, CancellationToken.None);
        Result<long> negative = await Sync.WriteAsync(41, Plan, -1, CancellationToken.None);

        stale.Error.ShouldBe(PlanSync.Stale);
        stale.Error.Message.ShouldBe("Another device has changed the plan since this one last looked.");
        notAPlan.Error.ShouldBe(PlanSync.NotAPlan);
        notAPlan.Error.Message.ShouldBe("That is not a Selficient plan, so it was not kept.");
        nothing.Error.ShouldBe(PlanSync.NotAPlan);
        negative.Error.ShouldBe(PlanSync.NotAPlan);
        await _store.Received(1).TryPutAsync(Arg.Any<long>(), Arg.Any<byte[]>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        Should.Throw<ArgumentNullException>(() => new PlanSync(null!, _protector, _clock));
        Should.Throw<ArgumentNullException>(() => new PlanSync(_store, null!, _clock));
        Should.Throw<ArgumentNullException>(() => new PlanSync(_store, _protector, null!));
    }

    [Fact]
    public void A_whole_plan_and_the_smallest_plan_are_both_plans()
    {
        PlanGuard.IsSyncBody(Plan).ShouldBeTrue();
        PlanGuard.IsSyncBody("""{"plan":{"profile":null,"ticks":[]},"detailsAt":0}""").ShouldBeTrue("a device that cleared its plan has an empty one, and that is a plan too");
        PlanGuard.IsSyncBody("""{"detailsAt":5,"plan":{"ticks":["a"],"profile":{"People":"1"},"house":null}}""").ShouldBeTrue("the order of the members does not matter");
        PlanGuard.IsSyncBody(Body(ticks: string.Join(',', Enumerable.Range(0, 200).Select(i => $"\"id-{i}\"")))).ShouldBeTrue("two hundred ids is the most");
        PlanGuard.IsSyncBody(Body(profile: "{" + string.Join(',', Enumerable.Range(0, 40).Select(i => $"\"{Letters(i)}\":\"x\"")) + "}")).ShouldBeTrue("forty answers is the most");
        PlanGuard.MaxBytes.ShouldBe(65_536);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"a plan\"")]
    [InlineData("""{"plan":{"profile":null,"ticks":[]}}""")]
    [InlineData("""{"detailsAt":1}""")]
    [InlineData("""{"plan":{"profile":null,"ticks":[]},"detailsAt":"now"}""")]
    [InlineData("""{"plan":{"profile":null,"ticks":[]},"detailsAt":-1}""")]
    [InlineData("""{"plan":{"profile":null,"ticks":[]},"detailsAt":1e400}""")]
    [InlineData("""{"plan":{"profile":null,"ticks":[]},"detailsAt":99999999999999}""")]
    [InlineData("""{"plan":{"profile":null,"ticks":[]},"detailsAt":1,"owner":42}""")]
    [InlineData("""{"plan":[],"detailsAt":1}""")]
    [InlineData("""{"plan":{"ticks":[]},"detailsAt":1}""")]
    [InlineData("""{"plan":{"profile":null},"detailsAt":1}""")]
    [InlineData("""{"plan":{"profile":null,"ticks":[],"notes":"x"},"detailsAt":1}""")]
    [InlineData("""{"plan":{"profile":{"a":{"b":{"c":{"d":{"e":{"f":{"g":1}}}}}}},"ticks":[]},"detailsAt":1}""")]
    public void What_is_not_the_shape_of_a_sync_body_is_refused(string? body) =>
        PlanGuard.IsSyncBody(body).ShouldBeFalse();

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"four people\"")]
    [InlineData("""{"People":4}""")]
    [InlineData("""{"People":null}""")]
    [InlineData("""{"number of people":"4"}""")]
    [InlineData("""{"People2":"4"}""")]
    [InlineData("""{"People":"12345678901234567890123456789012345678901"}""")]
    [InlineData("""{"<script>":"x"}""")]
    public void A_profile_is_a_flat_map_of_short_names_to_short_strings(string profile) =>
        PlanGuard.IsSyncBody(Body(profile: profile)).ShouldBeFalse();

    [Fact]
    public void A_profile_of_more_than_forty_answers_and_a_list_of_more_than_two_hundred_ids_are_refused()
    {
        string profile = "{" + string.Join(',', Enumerable.Range(0, 41).Select(i => $"\"{Letters(i)}\":\"x\"")) + "}";
        string ticks = string.Join(',', Enumerable.Range(0, 201).Select(i => $"\"id-{i}\""));
        string amounts = "{" + string.Join(',', Enumerable.Range(0, 21).Select(i => $"\"stock-{i}\":1")) + "}";
        string twenty = "{" + string.Join(',', Enumerable.Range(0, 20).Select(i => $"\"stock-{i}\":1")) + "}";
        string dependency = """{"name":"Fridge","kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""";

        PlanGuard.IsSyncBody(Body(profile: profile)).ShouldBeFalse();
        PlanGuard.IsSyncBody(Body(ticks: ticks)).ShouldBeFalse();
        PlanGuard.IsSyncBody(Body(extra: $"\"amounts\":{amounts}")).ShouldBeFalse();
        PlanGuard.IsSyncBody(Body(extra: $"\"amounts\":{twenty}")).ShouldBeTrue();
        PlanGuard.IsSyncBody(Body(extra: $"\"dependencies\":[{string.Join(',', Enumerable.Repeat(dependency, 13))}]")).ShouldBeFalse();
        PlanGuard.IsSyncBody(Body(extra: $"\"dependencies\":[{string.Join(',', Enumerable.Repeat(dependency, 12))}]")).ShouldBeTrue();
        PlanGuard.IsSyncBody(Body(extra: $"\"shortlist\":[{string.Join(',', Enumerable.Range(1, 101).Select(i => $"\"{i}\""))}]")).ShouldBeFalse("a hundred things saved for later is the most");
        PlanGuard.IsSyncBody(Body(extra: $"\"shortlist\":[{string.Join(',', Enumerable.Range(1, 100).Select(i => $"\"{i}\""))}]")).ShouldBeTrue();
        PlanGuard.IsSyncBody(Body(extra: "\"shortlist\":[]")).ShouldBeTrue();
        PlanGuard.IsSyncBody(Body(ticks: $"\"{new string('a', 81)}\"")).ShouldBeFalse("an id is eighty characters at most");
        PlanGuard.IsSyncBody(Body(ticks: $"\"{new string('a', 80)}\"")).ShouldBeTrue();
        PlanGuard.IsSyncBody("{\"plan\":{\"profile\":null,\"ticks\":[]},\"detailsAt\":1}" + new string(' ', PlanGuard.MaxBytes)).ShouldBeFalse("the cap is on the bytes, whatever they are");
    }

    [Theory]
    [InlineData("\"ticks\":\"headtorches\"")]
    [InlineData("\"ticks\":[1]")]
    [InlineData("\"ticks\":[\"Head Torches\"]")]
    [InlineData("\"ticks\":[\"-headtorches\"]")]
    [InlineData("\"ticks\":[\"<script>\"]")]
    [InlineData("\"ticks\":[],\"planned\":{}")]
    [InlineData("\"ticks\":[],\"skipped\":[null]")]
    [InlineData("\"ticks\":[],\"amounts\":[]")]
    [InlineData("\"ticks\":[],\"amounts\":{\"Food\":1}")]
    [InlineData("\"ticks\":[],\"amounts\":{\"food\":\"ten\"}")]
    [InlineData("\"ticks\":[],\"amounts\":{\"food\":1.5}")]
    [InlineData("\"ticks\":[],\"amounts\":{\"food\":-1}")]
    [InlineData("\"ticks\":[],\"amounts\":{\"food\":10001}")]
    [InlineData("\"ticks\":[],\"assumptions\":[]")]
    [InlineData("\"ticks\":[],\"assumptions\":{\"cashRate\":0}")]
    [InlineData("\"ticks\":[],\"assumptions\":{\"cashRate\":101}")]
    [InlineData("\"ticks\":[],\"assumptions\":{\"cashRate\":\"ten\"}")]
    [InlineData("\"ticks\":[],\"assumptions\":{\"discount\":10}")]
    [InlineData("\"ticks\":[],\"house\":[]")]
    [InlineData("\"ticks\":[],\"house\":{\"lat\":51.6}")]
    [InlineData("\"ticks\":[],\"house\":{\"lng\":-3.2}")]
    [InlineData("\"ticks\":[],\"house\":{\"lat\":\"51.6\",\"lng\":-3.2}")]
    [InlineData("\"ticks\":[],\"house\":{\"lat\":48.8,\"lng\":2.3}")]
    [InlineData("\"ticks\":[],\"house\":{\"lat\":62,\"lng\":-3.2}")]
    [InlineData("\"ticks\":[],\"house\":{\"lat\":51.6,\"lng\":-9.5}")]
    [InlineData("\"ticks\":[],\"house\":{\"lat\":51.6,\"lng\":3}")]
    [InlineData("\"ticks\":[],\"house\":{\"lat\":51.6,\"lng\":-3.2,\"street\":\"x\"}")]
    [InlineData("\"ticks\":[],\"shortlist\":\"12\"")]
    [InlineData("\"ticks\":[],\"shortlist\":[12]")]
    [InlineData("\"ticks\":[],\"shortlist\":[\"twelve\"]")]
    [InlineData("\"ticks\":[],\"shortlist\":[\"<script>\"]")]
    [InlineData("\"ticks\":[],\"shortlist\":[\"-1\"]")]
    [InlineData("\"ticks\":[],\"shortlist\":[\"1234567890123456789\"]")]
    [InlineData("\"ticks\":[],\"dependencies\":{}")]
    [InlineData("\"ticks\":[],\"dependencies\":[\"Fridge\"]")]
    public void Lists_figures_and_the_pin_are_held_to_their_bounds(string members) =>
        PlanGuard.IsSyncBody($$"""{"plan":{"profile":null,{{members}}},"detailsAt":1}""").ShouldBeFalse(members);

    [Theory]
    [InlineData("""{"kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":7,"kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"   ","kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"12345678901234567890123456789012345678901","kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":1,"watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Reactor","watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":"1","hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":-1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":3001,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":1,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":1,"hoursPerDay":25,"daysOfStock":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":1,"hoursPerDay":1,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":1.5,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":366,"mustStayCold":true}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":1}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":"yes"}""")]
    [InlineData("""{"name":"Fridge","kind":"Supply","watts":1,"hoursPerDay":1,"daysOfStock":1,"mustStayCold":true,"owner":"x"}""")]
    public void Something_a_household_relies_on_is_held_to_the_bounds_the_plan_form_holds_it_to(string dependency) =>
        PlanGuard.IsSyncBody(Body(extra: $"\"dependencies\":[{dependency}]")).ShouldBeFalse(dependency);

    [Fact]
    public void A_dependency_that_need_not_stay_cold_and_a_pin_at_the_edge_of_the_kingdom_are_allowed()
    {
        PlanGuard.IsSyncBody(Body(extra: """ "dependencies":[{"name":"Insulin","kind":"Medicine","watts":0,"hoursPerDay":0,"daysOfStock":365,"mustStayCold":false}]""")).ShouldBeTrue();
        PlanGuard.IsSyncBody(Body(extra: """ "dependencies":[{"name":"Oxygen","kind":"PoweredDevice","watts":3000,"hoursPerDay":24,"daysOfStock":0,"mustStayCold":true}]""")).ShouldBeTrue();
        PlanGuard.IsSyncBody(Body(extra: """ "house":{"lat":49,"lng":2.5}""")).ShouldBeTrue();
        PlanGuard.IsSyncBody(Body(extra: """ "house":{"lat":61,"lng":-9}""")).ShouldBeTrue();
        PlanGuard.IsSyncBody(Body(extra: """ "assumptions":{}""")).ShouldBeTrue("no assumption changed is the plan's own");
        PlanGuard.IsSyncBody(Body(extra: """ "assumptions":{"cashRate":1}""")).ShouldBeTrue();
        PlanGuard.IsSyncBody(Body(extra: """ "amounts":{"water-bottled-three-days":0,"cash-in-small-notes":10000}""")).ShouldBeTrue();
    }

    private static string Body(string profile = "{\"People\":\"4\"}", string ticks = "", string? extra = null) =>
        $$"""{"plan":{"profile":{{profile}},"ticks":[{{ticks}}]{{(extra is null ? string.Empty : "," + extra)}}},"detailsAt":1}""";

    /// <summary>A field name made only of letters, different for each number.</summary>
    private static string Letters(int i) => $"Field{(char)('A' + (i / 26))}{(char)('a' + (i % 26))}";
}
