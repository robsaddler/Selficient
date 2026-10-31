using NSubstitute;
using Selficient.Application.Catalogue;
using Selficient.Application.Kit;
using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Catalogue;

public sealed class CatalogueTests
{
    private static Offer AnOffer(long id = 1, string name = "Petzl Tikkina headtorch", OfferStatus status = OfferStatus.Live, int failures = 0) =>
        new(id, ActionId.From("headtorches"), name, "Amazon", Money.FromPence(2200), new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=" + Uri.EscapeDataString(name)), "Why.", status, failures);

    [Theory]
    [InlineData(0, "", "anything", Verdict.Dead)]
    [InlineData(403, "Access Denied", "Access denied", Verdict.RobotWall)]
    [InlineData(200, "Robot check", "Sorry, we just need to make sure you're not a robot. Enter the characters you see below", Verdict.RobotWall)]
    [InlineData(503, "Service", "Service unavailable", Verdict.RobotWall)]
    [InlineData(404, "Not here", "Page not found", Verdict.Dead)]
    [InlineData(200, "Search", "No results for Petzl Tikkina. Try again.", Verdict.NoResults)]
    [InlineData(200, "Search: 30 results found", "0 results in a hidden template but 30 results found for Petzl", Verdict.Landed)]
    [InlineData(200, "Search", "Lots of garden hoses and nothing else £9.99", Verdict.WrongItem)]
    [InlineData(200, "Petzl Tikkina", "Petzl Tikkina headtorch £22.00 Add to basket", Verdict.Landed)]
    [InlineData(403, "Forbidden", "Forbidden", Verdict.RobotWall)]
    [InlineData(200, "Search", "0 results in a hidden template; 30 results found for Petzl", Verdict.Landed)]
    public void The_rule_judge_reads_the_page_for_what_it_is(int status, string title, string text, Verdict verdict)
    {
        Judgement judgement = RuleJudge.Judge(AnOffer(), new PageRead(status, title, text, new Uri("https://example.test/")));

        judgement.Verdict.ShouldBe(verdict);
        judgement.JudgedBy.ShouldBe("rule");
        judgement.Why.ShouldEndWith(".");
    }

    [Fact]
    public void The_rule_judge_reads_only_the_head_of_a_long_page_for_walls_and_counts_but_the_whole_of_it_for_the_name()
    {
        string filler = new('x', 7000);
        PageRead wallAtTheEnd = new(200, "Search", filler + " not a robot", new Uri("https://example.test/"));
        PageRead nameAtTheEnd = new(200, "Search", filler + " Petzl Tikkina", new Uri("https://example.test/"));
        PageRead countInTheHead = new(200, "Search", "30 results found " + filler + " Petzl", new Uri("https://example.test/"));

        RuleJudge.Judge(AnOffer(), wallAtTheEnd).Verdict.ShouldBe(Verdict.WrongItem, "a wall sentence past the first three thousand characters is page text, not a wall");
        RuleJudge.Judge(AnOffer(), nameAtTheEnd).Verdict.ShouldBe(Verdict.Landed, "the name counts wherever it is on the page");
        RuleJudge.Judge(AnOffer(), countInTheHead).Verdict.ShouldBe(Verdict.Landed);
    }

    [Fact]
    public void The_rule_judge_takes_the_distinctive_words_and_refuses_null()
    {
        RuleJudge.Words("Still water, 6 x 2 litre multipack").ShouldBe(["water", "multipack"]);
        RuleJudge.Words("Ugreen Nexode 25,000 mAh 145 W").ShouldBe(["Ugreen", "Nexode", "mAh"]);
        RuleJudge.Words("A").ShouldBeEmpty();
        RuleJudge.Judge(AnOffer(name: "A"), new PageRead(200, "t", "text", new Uri("https://example.test/"))).Verdict.ShouldBe(Verdict.Landed, "no words to look for means nothing to miss");
        Should.Throw<ArgumentNullException>(() => RuleJudge.Words(null!));
        Should.Throw<ArgumentNullException>(() => RuleJudge.Judge(null!, new PageRead(200, "", "", new Uri("https://example.test/"))));
        Should.Throw<ArgumentNullException>(() => RuleJudge.Judge(AnOffer(), null!));
    }

    [Fact]
    public async Task The_verifier_asks_the_rules_then_the_model_and_records_what_moved()
    {
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IPageReader reader = Substitute.For<IPageReader>();
        IOfferJudge judge = Substitute.For<IOfferJudge>();
        ManualClock clock = new(new DateTimeOffset(2026, 10, 31, 2, 10, 0, TimeSpan.Zero));
        Offer good = AnOffer(1);
        Offer walled = AnOffer(2, "Campingaz Bistro 300");
        Offer dead = AnOffer(3, "Kidde fire blanket", OfferStatus.Failing, 1);
        store.ShownAsync(Arg.Any<CancellationToken>()).Returns([good, walled, dead]);
        reader.ReadAsync(good.Link, Arg.Any<CancellationToken>()).Returns(new PageRead(200, "Petzl Tikkina", "Petzl Tikkina headtorch £22.00", good.Link));
        reader.ReadAsync(walled.Link, Arg.Any<CancellationToken>()).Returns(new PageRead(200, "Robot", "verify you are human", walled.Link));
        reader.ReadAsync(dead.Link, Arg.Any<CancellationToken>()).Returns(new PageRead(0, "", "", dead.Link));
        judge.IsAvailable.Returns(true);
        judge.JudgeAsync(good, Arg.Any<PageRead>(), Arg.Any<CancellationToken>()).Returns(new Judgement(Verdict.Landed, "The model saw it.", Money.FromPence(2150), "model"));
        store.FindAsync(3, Arg.Any<CancellationToken>()).Returns(dead with { Status = OfferStatus.Hidden, Failures = 2 });

        VerificationReport report = await new OfferVerifier(store, reader, judge, clock).RunAsync(CancellationToken.None);

        report.Checked.ShouldBe(3);
        report.Landed.ShouldBe(1);
        report.Walled.ShouldBe(1);
        report.Hidden.Select(o => o.Id).ShouldBe([3L]);
        await store.Received().RecordCheckAsync(1, Arg.Is<OfferCheck>(c => c.Verdict == Verdict.Landed && c.JudgedBy == "model" && c.PriceSeen == Money.FromPence(2150) && c.At == clock.GetUtcNow()), Arg.Any<CancellationToken>());
        await store.Received().RecordCheckAsync(2, Arg.Is<OfferCheck>(c => c.Verdict == Verdict.RobotWall && c.JudgedBy == "rule"), Arg.Any<CancellationToken>());
        await store.Received().RecordCheckAsync(3, Arg.Is<OfferCheck>(c => c.Verdict == Verdict.Dead), Arg.Any<CancellationToken>());
        await store.Received().MarkVerifiedAsync(clock.GetUtcNow(), Arg.Any<CancellationToken>());
        await judge.DidNotReceive().JudgeAsync(walled, Arg.Any<PageRead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_model_that_cannot_answer_leaves_the_rules_standing_and_no_model_is_never_asked()
    {
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IPageReader reader = Substitute.For<IPageReader>();
        IOfferJudge judge = Substitute.For<IOfferJudge>();
        Offer offer = AnOffer();
        reader.ReadAsync(offer.Link, Arg.Any<CancellationToken>()).Returns(new PageRead(200, "Petzl Tikkina", "Petzl Tikkina headtorch", offer.Link));
        judge.IsAvailable.Returns(true);
        judge.JudgeAsync(offer, Arg.Any<PageRead>(), Arg.Any<CancellationToken>()).Returns(new Judgement(Verdict.Dead, "The model did not answer.", null, "model"));
        OfferVerifier verifier = new(store, reader, judge, new ManualClock());

        Judgement silent = await verifier.JudgeAsync(offer, CancellationToken.None);
        judge.IsAvailable.Returns(false);
        Judgement unasked = await verifier.JudgeAsync(offer, CancellationToken.None);
        judge.IsAvailable.Returns(true);
        judge.JudgeAsync(offer, Arg.Any<PageRead>(), Arg.Any<CancellationToken>()).Returns(new Judgement(Verdict.WrongItem, "It is a lantern.", null, "model"));
        Judgement overruled = await verifier.JudgeAsync(offer, CancellationToken.None);

        silent.JudgedBy.ShouldBe("rule");
        silent.Verdict.ShouldBe(Verdict.Landed);
        unasked.JudgedBy.ShouldBe("rule");
        overruled.Verdict.ShouldBe(Verdict.WrongItem);
        overruled.PriceSeen.ShouldBeNull();
        await Should.ThrowAsync<ArgumentNullException>(() => verifier.JudgeAsync(null!, CancellationToken.None));
        Should.Throw<ArgumentNullException>(() => new OfferVerifier(null!, reader, judge, new ManualClock()));
        Should.Throw<ArgumentNullException>(() => new OfferVerifier(store, null!, judge, new ManualClock()));
        Should.Throw<ArgumentNullException>(() => new OfferVerifier(store, reader, null!, new ManualClock()));
        Should.Throw<ArgumentNullException>(() => new OfferVerifier(store, reader, judge, null!));
    }

    [Theory]
    [InlineData(null, "2026-10-31T03:00:00+00:00", 2, true)]
    [InlineData("2026-10-30T02:30:00+00:00", "2026-10-31T03:00:00+00:00", 2, true)]
    [InlineData("2026-10-31T02:30:00+00:00", "2026-10-31T03:00:00+00:00", 2, false)]
    [InlineData("2026-10-30T01:00:00+00:00", "2026-10-31T01:30:00+00:00", 2, true)]
    [InlineData("2026-10-30T02:30:00+00:00", "2026-10-31T01:30:00+00:00", 2, false)]
    public void A_run_is_due_once_a_day_after_the_hour(string? last, string now, int hour, bool due)
    {
        DateTimeOffset? lastRun = last is null ? null : DateTimeOffset.Parse(last, System.Globalization.CultureInfo.InvariantCulture);

        OfferVerifier.IsDue(lastRun, DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), hour).ShouldBe(due);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0L, null)]
    [InlineData(-5L, null)]
    [InlineData(2200L, 2200L)]
    [InlineData(999_999L, 999_999L)]
    [InlineData(1_000_000L, null)]
    public void A_price_a_judge_read_is_kept_only_when_it_is_a_shop_price(long? pence, long? kept) =>
        OfferVerifier.Sensible(pence).ShouldBe(kept is { } p ? Money.FromPence(p) : null);

    [Fact]
    public async Task The_stored_catalogue_seeds_an_empty_store_and_serves_the_shown_offers_cheapest_first()
    {
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IKitCatalogue shipped = Substitute.For<IKitCatalogue>();
        KitPick seed = KitPick.Create("headtorches", "Petzl Tikkina headtorch", "Amazon", 22m, new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=Petzl"), null).Value;
        shipped.All.Returns([seed]);
        shipped.PicksFor(ActionId.From("headtorches")).Returns([seed]);
        store.CountAsync(Arg.Any<CancellationToken>()).Returns(0, 1);
        store.ShownAsync(Arg.Any<CancellationToken>()).Returns([AnOffer(2, "Ledlenser MH3 headtorch") with { Price = Money.FromPence(2600) }, AnOffer(1)]);
        using StoredCatalogue catalogue = new(store, shipped);

        catalogue.IsLoaded.ShouldBeFalse();
        catalogue.All.ShouldBe([seed], "the shipped picks stand in until the store is read");
        catalogue.PicksFor(ActionId.From("headtorches")).ShouldBe([seed]);
        await catalogue.ReloadAsync(CancellationToken.None);
        await catalogue.ReloadAsync(CancellationToken.None);

        catalogue.IsLoaded.ShouldBeTrue();
        await store.Received(1).SeedAsync(Arg.Is<IReadOnlyList<KitPick>>(p => p.Count == 1), Arg.Any<CancellationToken>());
        catalogue.All.Count.ShouldBe(2);
        catalogue.Version.ShouldBe(2, "one per reload");
        catalogue.Find(2)!.Name.ShouldBe("Ledlenser MH3 headtorch");
        catalogue.Find(99).ShouldBeNull();
        IReadOnlyList<KitPick> picks = catalogue.PicksFor(ActionId.From("headtorches"));
        picks.Select(p => p.OfferId).ShouldBe([1L, 2L], "cheapest first, each carrying its offer");
        catalogue.PicksFor(ActionId.From("water-cans")).ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => new StoredCatalogue(null!, shipped));
        Should.Throw<ArgumentNullException>(() => new StoredCatalogue(store, null!));
    }

    [Fact]
    public async Task The_operations_verify_reload_and_know_when_the_night_is_due()
    {
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IPageReader reader = Substitute.For<IPageReader>();
        IOfferJudge judge = Substitute.For<IOfferJudge>();
        IKitCatalogue shipped = Substitute.For<IKitCatalogue>();
        shipped.All.Returns([]);
        store.CountAsync(Arg.Any<CancellationToken>()).Returns(1);
        store.ShownAsync(Arg.Any<CancellationToken>()).Returns([]);
        ManualClock clock = new(new DateTimeOffset(2026, 10, 31, 3, 0, 0, TimeSpan.Zero));
        using StoredCatalogue catalogue = new(store, shipped);
        CatalogueOperations operations = new(catalogue, new OfferVerifier(store, reader, judge, clock), store, clock);

        store.LastVerifiedAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        VerificationReport? first = await operations.VerifyIfDueAsync(2, CancellationToken.None);
        store.LastVerifiedAsync(Arg.Any<CancellationToken>()).Returns(clock.GetUtcNow());
        VerificationReport? second = await operations.VerifyIfDueAsync(2, CancellationToken.None);
        VerificationReport forced = await operations.VerifyAsync(CancellationToken.None);
        await operations.ReloadAsync(CancellationToken.None);

        first.ShouldNotBeNull();
        second.ShouldBeNull("already run since two o\x27clock");
        forced.Checked.ShouldBe(0);
        catalogue.IsLoaded.ShouldBeTrue();
        Should.Throw<ArgumentNullException>(() => new CatalogueOperations(null!, new OfferVerifier(store, reader, judge, clock), store, clock));
        Should.Throw<ArgumentNullException>(() => new CatalogueOperations(catalogue, null!, store, clock));
        Should.Throw<ArgumentNullException>(() => new CatalogueOperations(catalogue, new OfferVerifier(store, reader, judge, clock), null!, clock));
        Should.Throw<ArgumentNullException>(() => new CatalogueOperations(catalogue, new OfferVerifier(store, reader, judge, clock), store, null!));
    }

    [Fact]
    public void An_offer_becomes_a_pick_that_carries_its_id()
    {
        KitPick pick = AnOffer(7).ToPick();

        pick.OfferId.ShouldBe(7L);
        pick.Name.ShouldBe("Petzl Tikkina headtorch");
        pick.Price.ShouldBe(Money.FromPence(2200));
    }
}
