using NSubstitute;
using Selficient.Application.Catalogue;
using Selficient.Application.Kit;
using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Catalogue;

public sealed class TipsAndDealsTests
{
    private static readonly ManualClock Clock = new(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero));

    private static Offer Proposal(long id, string link = "https://www.argos.co.uk/search/petzl-tikkina/") =>
        new(id, ActionId.From("headtorches"), "Suggested link", "argos.co.uk", Money.FromPence(0), new DateOnly(2026, 10, 31), new Uri(link), "Suggested by a reader.", OfferStatus.Proposed, 0);

    private static (TipBox Box, ICatalogueStore Store) Box(bool allows = true, int pending = 0, long? proposedId = 7)
    {
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IAllowedShops shops = Substitute.For<IAllowedShops>();
        shops.Allows(Arg.Any<Uri>()).Returns(allows);
        store.ProposedAsync(Arg.Any<CancellationToken>()).Returns(Enumerable.Range(0, pending).Select(i => Proposal(i)).ToList());
        store.ProposeAsync(Arg.Any<ActionId>(), Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(proposedId);
        return (new TipBox(store, shops, Clock), store);
    }

    [Fact]
    public async Task A_good_tip_is_taken_as_a_proposal_for_the_action_with_its_fragment_dropped()
    {
        (TipBox box, ICatalogueStore store) = Box();

        TipOutcome outcome = await box.SuggestAsync("headtorches", " https://www.argos.co.uk/product/123#reviews ", CancellationToken.None);

        outcome.ShouldBe(TipOutcome.Taken);
        await store.Received().ProposeAsync(ActionId.From("headtorches"), new Uri("https://www.argos.co.uk/product/123"), "argos.co.uk", new DateOnly(2026, 10, 31), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "https://www.argos.co.uk/product/123", TipOutcome.NotAnAction)]
    [InlineData("Headtorches", "https://www.argos.co.uk/product/123", TipOutcome.NotAnAction)]
    [InlineData("not-an-action-we-have", "https://www.argos.co.uk/product/123", TipOutcome.NotAnAction)]
    [InlineData("save-emergency-numbers", "https://www.argos.co.uk/product/123", TipOutcome.NotAnAction)]
    [InlineData("headtorches", null, TipOutcome.NotALink)]
    [InlineData("headtorches", "   ", TipOutcome.NotALink)]
    [InlineData("headtorches", "argos.co.uk/product/123", TipOutcome.NotALink)]
    [InlineData("headtorches", "http://www.argos.co.uk/product/123", TipOutcome.NotALink)]
    [InlineData("headtorches", "https://user:pass@www.argos.co.uk/product/123", TipOutcome.NotALink)]
    [InlineData("headtorches", "javascript:alert(1)", TipOutcome.NotALink)]
    public async Task A_tip_that_is_not_an_action_or_not_a_link_is_said_so(string? action, string? url, TipOutcome expected)
    {
        (TipBox box, ICatalogueStore store) = Box();

        (await box.SuggestAsync(action, url, CancellationToken.None)).ShouldBe(expected);

        await store.DidNotReceive().ProposeAsync(Arg.Any<ActionId>(), Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_link_too_long_a_shop_off_the_list_a_full_box_and_a_known_link_are_each_said_so()
    {
        (TipBox tooLong, _) = Box();
        (TipBox offList, ICatalogueStore offListStore) = Box(allows: false);
        (TipBox full, ICatalogueStore fullStore) = Box(pending: TipBox.MaxPending);
        (TipBox known, _) = Box(proposedId: null);

        (await tooLong.SuggestAsync("headtorches", "https://www.argos.co.uk/" + new string('a', 500), CancellationToken.None)).ShouldBe(TipOutcome.NotALink);
        (await offList.SuggestAsync("headtorches", "https://evil.example/product", CancellationToken.None)).ShouldBe(TipOutcome.NotAShop);
        (await full.SuggestAsync("headtorches", "https://www.argos.co.uk/product/9", CancellationToken.None)).ShouldBe(TipOutcome.Full);
        (await known.SuggestAsync("headtorches", "https://www.argos.co.uk/product/9", CancellationToken.None)).ShouldBe(TipOutcome.AlreadyListed);
        await offListStore.DidNotReceive().ProposedAsync(Arg.Any<CancellationToken>());
        await fullStore.DidNotReceive().ProposeAsync(Arg.Any<ActionId>(), Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_retailer_is_the_host_without_its_www_and_every_outcome_has_a_sentence()
    {
        TipBox.Retailer(new Uri("https://www.argos.co.uk/x")).ShouldBe("argos.co.uk");
        TipBox.Retailer(new Uri("https://uk.ecoflow.com/x")).ShouldBe("uk.ecoflow.com");
        Should.Throw<ArgumentNullException>(() => TipBox.Retailer(null!));
        foreach (TipOutcome outcome in Enum.GetValues<TipOutcome>())
        {
            TipCopy.Sentence(outcome).ShouldEndWith(".");
        }

        TipCopy.Sentence(TipOutcome.Taken).ShouldBe("Thank you. We will open it tonight and, if it is the item at a better price, it joins the list.");
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IAllowedShops shops = Substitute.For<IAllowedShops>();
        Should.Throw<ArgumentNullException>(() => new TipBox(null!, shops, Clock));
        Should.Throw<ArgumentNullException>(() => new TipBox(store, null!, Clock));
        Should.Throw<ArgumentNullException>(() => new TipBox(store, shops, null!));
    }

    [Fact]
    public async Task Proposals_are_opened_once_judged_and_the_landing_ones_named_and_priced()
    {
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IPageReader reader = Substitute.For<IPageReader>();
        IOfferJudge judge = Substitute.For<IOfferJudge>();
        Offer fresh = Proposal(1);
        Offer wrong = Proposal(2, "https://www.argos.co.uk/search/garden-hose/");
        Offer seen = Proposal(3, "https://www.argos.co.uk/search/seen-before/");
        store.ProposedAsync(Arg.Any<CancellationToken>()).Returns([fresh, wrong, seen]);
        store.ChecksAsync(Arg.Is<long>(id => id != 3), 1, Arg.Any<CancellationToken>()).Returns([]);
        store.ChecksAsync(3, 1, Arg.Any<CancellationToken>()).Returns([new OfferCheck(Clock.GetUtcNow(), Verdict.WrongItem, 200, "Seen.", null, "rule")]);
        reader.ReadAsync(fresh.Link, Arg.Any<CancellationToken>()).Returns(new PageRead(200, "  Petzl Tikkina head torch | Argos  ", "Petzl Tikkina headtorches £22.00", fresh.Link));
        reader.ReadAsync(wrong.Link, Arg.Any<CancellationToken>()).Returns(new PageRead(200, "Hoses", "Garden hoses from £9", wrong.Link));
        judge.IsAvailable.Returns(true);
        judge.JudgeAsync(Arg.Any<Offer>(), Arg.Any<PageRead>(), Arg.Any<CancellationToken>()).Returns(new Judgement(Verdict.Landed, "The model saw it.", Money.FromPence(2200), "model"));
        OfferVerifier verifier = new(store, reader, judge, Clock);

        int opened = await verifier.JudgeProposalsAsync(CancellationToken.None);

        opened.ShouldBe(2);
        await judge.Received(1).JudgeAsync(Arg.Is<Offer>(o => o.Id == 1 && o.Name.Contains("torch", StringComparison.OrdinalIgnoreCase)), Arg.Any<PageRead>(), Arg.Any<CancellationToken>());
        await store.Received().RecordCheckAsync(1, Arg.Is<OfferCheck>(c => c.Verdict == Verdict.Landed && c.Status == 200 && c.PriceSeen == Money.FromPence(2200)), Arg.Any<CancellationToken>());
        await store.Received().DescribeAsync(1, "Petzl Tikkina head torch | Argos", Money.FromPence(2200), new DateOnly(2026, 10, 31), Arg.Any<CancellationToken>());
        await store.Received().RecordCheckAsync(2, Arg.Is<OfferCheck>(c => c.Verdict == Verdict.WrongItem), Arg.Any<CancellationToken>());
        await store.DidNotReceive().DescribeAsync(2, Arg.Any<string>(), Arg.Any<Money?>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await reader.DidNotReceive().ReadAsync(seen.Link, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_proposal_is_judged_under_its_actions_title_and_named_after_its_page()
    {
        OfferVerifier.ActionTitle(ActionId.From("headtorches")).ShouldContain("torch", Case.Insensitive);
        OfferVerifier.ActionTitle(ActionId.From("no-such-action")).ShouldBe("no such action");
        OfferVerifier.PageName(new PageRead(200, "", "", new Uri("https://example.test/")), "The action").ShouldBe("The action");
        OfferVerifier.PageName(new PageRead(200, "  A title  ", "", new Uri("https://example.test/")), "x").ShouldBe("A title");
        OfferVerifier.PageName(new PageRead(200, new string('t', 200), "", new Uri("https://example.test/")), "x").Length.ShouldBe(120);
    }

    [Fact]
    public async Task The_deals_are_the_cheapest_per_action_with_the_drops_first()
    {
        ICatalogueStore store = Substitute.For<ICatalogueStore>();
        IKitCatalogue shipped = Substitute.For<IKitCatalogue>();
        shipped.All.Returns([]);
        Offer torch = new(1, ActionId.From("headtorches"), "Petzl Tikkina", "Amazon", Money.FromPence(2200), new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=Petzl"), "", OfferStatus.Live, 0);
        Offer torchDearer = torch with { Id = 2, Name = "Ledlenser MH3", Price = Money.FromPence(2600) };
        Offer water = new(3, ActionId.From("water-bottled-three-days"), "Still water", "Tesco", Money.FromPence(300), new DateOnly(2026, 10, 29), new Uri("https://www.tesco.com/search?query=water"), "", OfferStatus.Live, 0);
        store.ShownAsync(Arg.Any<CancellationToken>()).Returns([torch, torchDearer, water]);
        store.PriceSpansAsync(90, new DateOnly(2026, 10, 31), Arg.Any<CancellationToken>()).Returns(new Dictionary<long, PriceSpan>
        {
            [1] = new(Money.FromPence(2200), Money.FromPence(2500)),
            [3] = new(Money.FromPence(300), Money.FromPence(300)),
        });
        using StoredCatalogue catalogue = new(store, shipped);
        await catalogue.ReloadAsync(CancellationToken.None);
        DealsReader reader = new(catalogue, store, Clock);

        IReadOnlyList<Deal> deals = await reader.ReadAsync(CancellationToken.None);

        deals.Count.ShouldBe(2);
        deals[0].Action.ShouldBe(ActionId.From("headtorches"));
        deals[0].IsDrop.ShouldBeTrue();
        deals[0].WasHigh.ShouldBe(Money.FromPence(2500));
        deals[0].Cheapest.Name.ShouldBe("Petzl Tikkina");
        deals[0].Others.ShouldBe(1);
        deals[0].Title.ShouldContain("torch", Case.Insensitive);
        deals[1].Action.ShouldBe(ActionId.From("water-bottled-three-days"));
        deals[1].IsDrop.ShouldBeFalse("the same price all quarter is not a drop");
        deals[1].Others.ShouldBe(0);
        Should.Throw<ArgumentNullException>(() => new DealsReader(null!, store, Clock));
        Should.Throw<ArgumentNullException>(() => new DealsReader(catalogue, null!, Clock));
        Should.Throw<ArgumentNullException>(() => new DealsReader(catalogue, store, null!));
    }

    [Fact]
    public void A_pick_without_an_offer_or_without_a_span_has_no_drop()
    {
        KitPick shipped = KitPick.Create("headtorches", "Petzl", "Amazon", 22m, new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=Petzl"), null).Value;
        KitPick stored = shipped.WithOffer(9);
        Dictionary<long, PriceSpan> spans = new() { [9] = new(Money.FromPence(2000), Money.FromPence(2200)) };

        DealsReader.WasHigh(shipped, spans).ShouldBeNull("a shipped pick has no checks");
        DealsReader.WasHigh(stored, new Dictionary<long, PriceSpan>()).ShouldBeNull("no check read a price");
        DealsReader.WasHigh(stored, spans).ShouldBeNull("the high equals today's price");
        DealsReader.WasHigh(stored, new Dictionary<long, PriceSpan> { [9] = new(Money.FromPence(2000), Money.FromPence(2900)) }).ShouldBe(Money.FromPence(2900));
    }
}
