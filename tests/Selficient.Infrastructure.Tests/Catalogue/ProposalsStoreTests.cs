using Microsoft.Extensions.Options;
using Selficient.Application.Catalogue;
using Selficient.Domain.Kit;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Infrastructure.Catalogue;
using Selficient.Infrastructure.Data;
using Selficient.Infrastructure.Kit;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Catalogue;

public sealed class ProposalsStoreTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 10, 31);
    private static readonly DateTimeOffset Night = new(2026, 10, 31, 2, 15, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = TestDatabase.Create();
    private SqlServerCatalogueStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await new SqlMigrator(_database.ConnectionString).MigrateAsync(CancellationToken.None);
        _store = new SqlServerCatalogueStore(_database.ConnectionString);
    }

    [Fact]
    public async Task A_proposal_goes_in_nameless_and_priceless_once_per_link_and_is_not_shown()
    {
        await _store.SeedAsync([KitPick.Create("headtorches", "Petzl Tikkina", "Amazon", 22m, new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=Petzl"), "Why.").Value], CancellationToken.None);
        Uri link = new("https://www.argos.co.uk/product/123");

        long? first = await _store.ProposeAsync(ActionId.From("headtorches"), link, "argos.co.uk", Today, CancellationToken.None);
        long? again = await _store.ProposeAsync(ActionId.From("headtorches"), link, "argos.co.uk", Today, CancellationToken.None);
        long? known = await _store.ProposeAsync(ActionId.From("headtorches"), new Uri("https://www.amazon.co.uk/s?k=Petzl"), "amazon.co.uk", Today, CancellationToken.None);
        IReadOnlyList<Offer> proposed = await _store.ProposedAsync(CancellationToken.None);

        first.ShouldBe(2L);
        again.ShouldBeNull("the same link is not proposed twice");
        known.ShouldBeNull("a link already live is not proposed either");
        proposed.Count.ShouldBe(1);
        proposed[0].Status.ShouldBe(OfferStatus.Proposed);
        proposed[0].Name.ShouldBe("Suggested link");
        proposed[0].Retailer.ShouldBe("argos.co.uk");
        proposed[0].Price.ShouldBe(Money.FromPence(0));
        proposed[0].Seen.ShouldBe(Today);
        proposed[0].Note.ShouldBe("Suggested by a reader.");
        (await _store.ShownAsync(CancellationToken.None)).Select(o => o.Id).ShouldBe([1L], "a proposal is never shown");
        (await _store.AllAsync(CancellationToken.None)).Select(o => o.Id).ShouldBe([1L, 2L]);
        await Should.ThrowAsync<ArgumentNullException>(() => _store.ProposeAsync(ActionId.From("headtorches"), null!, "x", Today, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => _store.ProposeAsync(ActionId.From("headtorches"), link, " ", Today, CancellationToken.None));
    }

    [Fact]
    public async Task Describing_a_proposal_gives_it_the_pages_name_and_a_price_only_when_one_was_read()
    {
        long id = (await _store.ProposeAsync(ActionId.From("headtorches"), new Uri("https://www.argos.co.uk/product/123"), "argos.co.uk", Today, CancellationToken.None))!.Value;

        await _store.DescribeAsync(id, "Petzl Tikkina head torch | Argos", null, Today.AddDays(1), CancellationToken.None);
        Offer named = (await _store.FindAsync(id, CancellationToken.None))!;
        await _store.DescribeAsync(id, new string('n', 200), Money.FromPence(2150), Today.AddDays(2), CancellationToken.None);
        Offer priced = (await _store.FindAsync(id, CancellationToken.None))!;

        named.Name.ShouldBe("Petzl Tikkina head torch | Argos");
        named.Price.ShouldBe(Money.FromPence(0), "no price read keeps none");
        named.Seen.ShouldBe(Today, "the seen date moves only with a price");
        priced.Name.Length.ShouldBe(160, "a name is clipped to the column");
        priced.Price.ShouldBe(Money.FromPence(2150));
        priced.Seen.ShouldBe(Today.AddDays(2));
        priced.Status.ShouldBe(OfferStatus.Proposed, "describing never approves");
        await Should.ThrowAsync<ArgumentException>(() => _store.DescribeAsync(id, "", null, Today, CancellationToken.None));
    }

    [Fact]
    public async Task A_landing_check_on_a_proposal_keeps_it_proposed_and_takes_the_price()
    {
        long id = (await _store.ProposeAsync(ActionId.From("headtorches"), new Uri("https://www.argos.co.uk/product/123"), "argos.co.uk", Today, CancellationToken.None))!.Value;

        await _store.RecordCheckAsync(id, new OfferCheck(Night, Verdict.Landed, 200, "Named.", Money.FromPence(2300), "rule"), CancellationToken.None);
        Offer landed = (await _store.FindAsync(id, CancellationToken.None))!;
        await _store.RecordCheckAsync(id, new OfferCheck(Night, Verdict.Dead, 404, "Gone.", null, "rule"), CancellationToken.None);
        await _store.RecordCheckAsync(id, new OfferCheck(Night, Verdict.Dead, 404, "Gone.", null, "rule"), CancellationToken.None);
        Offer failed = (await _store.FindAsync(id, CancellationToken.None))!;

        landed.Status.ShouldBe(OfferStatus.Proposed);
        landed.Price.ShouldBe(Money.FromPence(2300));
        failed.Status.ShouldBe(OfferStatus.Proposed, "only live and failing offers hide themselves");
        failed.Failures.ShouldBe(2);
    }

    [Fact]
    public async Task Price_spans_come_from_the_checks_in_the_window_that_read_a_price()
    {
        await _store.SeedAsync(
            [
                KitPick.Create("headtorches", "Petzl Tikkina", "Amazon", 22m, new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=Petzl"), "Why.").Value,
                KitPick.Create("headtorches", "Ledlenser MH3", "Amazon", 26m, new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=Ledlenser"), "Why.").Value,
            ],
            CancellationToken.None);
        await _store.RecordCheckAsync(1, new OfferCheck(Night.AddDays(-100), Verdict.Landed, 200, "Old.", Money.FromPence(3000), "model"), CancellationToken.None);
        await _store.RecordCheckAsync(1, new OfferCheck(Night.AddDays(-10), Verdict.Landed, 200, "Then.", Money.FromPence(2500), "model"), CancellationToken.None);
        await _store.RecordCheckAsync(1, new OfferCheck(Night, Verdict.Landed, 200, "Now.", Money.FromPence(2200), "model"), CancellationToken.None);
        await _store.RecordCheckAsync(1, new OfferCheck(Night, Verdict.RobotWall, 403, "Wall.", null, "rule"), CancellationToken.None);
        await _store.RecordCheckAsync(2, new OfferCheck(Night, Verdict.Landed, 200, "Unpriced.", null, "rule"), CancellationToken.None);

        IReadOnlyDictionary<long, PriceSpan> spans = await _store.PriceSpansAsync(90, Today, CancellationToken.None);

        spans.Count.ShouldBe(1, "a check without a price is not a price");
        spans[1].ShouldBe(new PriceSpan(Money.FromPence(2200), Money.FromPence(2500)), "the hundred-day-old price is outside the window");
    }

    [Fact]
    public void The_allowlist_is_the_catalogues_own_hosts()
    {
        AffiliateAllowedShops shops = new(Options.Create(new AffiliateOptions()));

        shops.Allows(new Uri("https://www.argos.co.uk/product/1")).ShouldBeTrue();
        shops.Allows(new Uri("https://WWW.AMAZON.CO.UK/s?k=x")).ShouldBeTrue("hosts are not case sensitive");
        shops.Allows(new Uri("https://amazon.co.uk/s?k=x")).ShouldBeFalse("the bare host is not the listed one");
        shops.Allows(new Uri("https://www.amazon.co.uk.evil.example/s")).ShouldBeFalse();
        shops.Allows(new Uri("/relative", UriKind.Relative)).ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => shops.Allows(null!));
        Should.Throw<ArgumentNullException>(() => new AffiliateAllowedShops(null!));
    }

    [Fact]
    public void The_model_is_told_a_price_only_when_the_offer_has_one()
    {
        Offer priced = new(1, ActionId.From("headtorches"), "Petzl", "Amazon", Money.FromPence(2200), Today, new Uri("https://www.amazon.co.uk/s?k=Petzl"), "", OfferStatus.Live, 0);

        AnthropicOfferJudge.About(priced).ShouldBe(", about £22.00");
        AnthropicOfferJudge.About(priced with { Price = Money.FromPence(0) }).ShouldBeEmpty();
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }
}
