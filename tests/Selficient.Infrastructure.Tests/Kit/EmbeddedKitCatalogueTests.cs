using System.Text;
using Selficient.Application.Composition;
using Selficient.Domain.Households;
using Selficient.Domain.Kit;
using Selficient.Domain.Plans;
using Selficient.Domain.Risks;
using Selficient.Infrastructure.Kit;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Kit;

public sealed class EmbeddedKitCatalogueTests
{
    private static Stream Json(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    private static string Entry(string link, string action = "headtorches", string name = "Thing") =>
        $$"""[{"action":"{{action}}","name":"{{name}}","retailer":"Shop","pricePounds":9.5,"seen":"2026-10-29","link":"{{link}}","note":"Why."}]""";

    [Fact]
    public void The_embedded_catalogue_loads_and_groups_cheapest_first()
    {
        EmbeddedKitCatalogue catalogue = EmbeddedKitCatalogue.Load(new AffiliateOptions());

        catalogue.All.Count.ShouldBeGreaterThan(30);
        IReadOnlyList<KitPick> radios = catalogue.PicksFor(ActionId.From("radio-dab-fm-with-crank"));
        radios.Count.ShouldBe(3);
        radios.Select(p => p.Price.Pence).ShouldBeInOrder();
        catalogue.PicksFor(ActionId.From("save-emergency-numbers")).ShouldBeEmpty();
    }

    [Fact]
    public void The_no_flush_kit_and_the_router_backup_have_picks_inside_the_cost_the_plan_quotes()
    {
        EmbeddedKitCatalogue catalogue = EmbeddedKitCatalogue.Load(new AffiliateOptions());

        IReadOnlyList<KitPick> liners = catalogue.PicksFor(ActionId.From("no-flush-toilet-kit"));
        IReadOnlyList<KitPick> backups = catalogue.PicksFor(ActionId.From("landline-battery-backup"));

        liners.Select(p => p.Name).ShouldBe(["Portable toilet bags with absorbent gel powder, pack of 30", "CarpLife Bivvy Loo refill pack: 12 liners, 12 powder sachets and 40 wipes", "CarpLife Bivvy Loo absorbent powder, 1 kg"]);
        liners.ShouldAllBe(p => p.Price.Pence >= 1000 &&p.Price.Pence <= 3400);
        backups.Count.ShouldBe(2);
        backups.ShouldAllBe(p => p.Price.Pence <= 6200);
    }

    [Fact]
    public void No_pick_links_to_a_bare_homepage()
    {
        EmbeddedKitCatalogue catalogue = EmbeddedKitCatalogue.Load(new AffiliateOptions { AmazonTag = "selficient-21" });

        foreach (KitPick pick in catalogue.All)
        {
            (pick.Link.AbsolutePath.Length > 1 || pick.Link.Query.Length > 0).ShouldBeTrue($"{pick.Name} links to {pick.Link}, which is a homepage and not the item");
        }

        KitPick amazon = catalogue.All.First(pick => pick.Link.Host == "www.amazon.co.uk");
        amazon.Link.Query.ShouldStartWith("?k=");
        amazon.Link.Query.ShouldEndWith("&tag=selficient-21");
    }

    [Fact]
    public void Every_pick_names_a_real_action()
    {
        HashSet<string> known = EveryActionId();

        EmbeddedKitCatalogue catalogue = EmbeddedKitCatalogue.Load(new AffiliateOptions());

        catalogue.All.Select(p => p.Action.Value).Distinct().ShouldAllBe(id => known.Contains(id));
    }

    [Fact]
    public void Every_purchasable_action_has_a_pick()
    {
        EmbeddedKitCatalogue catalogue = EmbeddedKitCatalogue.Load(new AffiliateOptions());
        HashSet<string> withPicks = [.. catalogue.All.Select(p => p.Action.Value)];
        // Actions whose cost is the household's own money or time, or professional work, rather than a product.
        HashSet<string> exempt = ["cash-in-small-notes", "tree-work-if-advised", "solar-quotes", "rooftop-solar-and-battery", "boiler-plug-and-socket", "lights-out-drill", "priority-services-register", "save-emergency-numbers", "clear-leaf-litter-under-decking", "flood-warnings-and-plan"];

        IEnumerable<string> missing = PurchasableActionIds().Where(id => !withPicks.Contains(id) && !exempt.Contains(id));

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void No_price_is_older_than_the_review_window()
    {
        EmbeddedKitCatalogue catalogue = EmbeddedKitCatalogue.Load(new AffiliateOptions());
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        catalogue.All.ShouldAllBe(p => today.DayNumber - p.Seen.DayNumber <= 120, "a price over 120 days old is a reminder to review the catalogue");
    }

    [Fact]
    public void Picks_refuse_hosts_outside_the_allowlist()
    {
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.Parse(Json(Entry("https://evil.example/")), new AffiliateOptions()));

        exception.Message.ShouldBe("The kit catalogue links to 'evil.example', which is not an allowed host.");
    }

    [Theory]
    [InlineData("http://www.argos.co.uk/")]
    [InlineData("not a url")]
    public void Picks_refuse_anything_but_https_at_load(string link)
    {
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.Parse(Json(Entry(link)), new AffiliateOptions()));

        exception.Message.ShouldContain("must be an absolute https address");
    }

    [Fact]
    public void Entries_with_missing_fields_fail_loudly()
    {
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.Parse(Json("[{}]"), new AffiliateOptions())).Message.ShouldContain("has no valid 'seen' date");
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.Parse(Json("""[{"seen":"2026-10-29","link":"https://www.argos.co.uk/"}]"""), new AffiliateOptions())).Message.ShouldContain("needs a name and a retailer");
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.Parse(Json(Entry("javascript:alert(1)")), new AffiliateOptions())).Message.ShouldContain("must be an absolute https address");
    }

    [Fact]
    public void A_missing_embedded_resource_is_reported()
    {
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.Load(new AffiliateOptions(), "Selficient.Infrastructure.Kit.no-such-file.json"))
            .Message.ShouldBe("The kit catalogue resource is missing from the build.");
    }

    [Fact]
    public void An_empty_file_and_a_null_literal_are_refused()
    {
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.Parse(Json("null"), new AffiliateOptions())).Message.ShouldBe("The kit catalogue is empty.");
        EmbeddedKitCatalogue.Parse(Json("[]"), new AffiliateOptions()).All.ShouldBeEmpty();
    }

    [Fact]
    public void The_amazon_tag_is_appended_safely_and_nowhere_else()
    {
        AffiliateOptions tagged = new() { AmazonTag = "selficient-21" };

        EmbeddedKitCatalogue.Tagged(new Uri("https://www.amazon.co.uk/dp/B0"), "selficient-21")!.ToString().ShouldBe("https://www.amazon.co.uk/dp/B0?tag=selficient-21");
        EmbeddedKitCatalogue.Tagged(new Uri("https://www.amazon.co.uk/s?k=torch"), "selficient-21")!.ToString().ShouldBe("https://www.amazon.co.uk/s?k=torch&tag=selficient-21");
        EmbeddedKitCatalogue.Tagged(new Uri("https://www.argos.co.uk/"), "selficient-21")!.ToString().ShouldBe("https://www.argos.co.uk/");
        EmbeddedKitCatalogue.Tagged(new Uri("https://www.amazon.co.uk/"), null).ShouldBe(new Uri("https://www.amazon.co.uk/"));
        EmbeddedKitCatalogue.Tagged(new Uri("https://www.amazon.co.uk/"), "").ShouldBe(new Uri("https://www.amazon.co.uk/"));
        EmbeddedKitCatalogue.Tagged(new Uri("https://AMAZON.co.uk.evil.example/"), "x").ShouldBe(new Uri("https://amazon.co.uk.evil.example/"));
        EmbeddedKitCatalogue.Tagged(null, "x").ShouldBeNull();
        EmbeddedKitCatalogue.Tagged(null, null).ShouldBeNull();
        EmbeddedKitCatalogue.Parse(Json(Entry("https://www.amazon.co.uk/dp/B0")), tagged).All.Single().Link.Query.ShouldBe("?tag=selficient-21");
    }

    [Fact]
    public void Withdrawn_picks_are_read_with_the_tag_their_links_were_stored_under()
    {
        const string File = """[{"action":"candles-and-matches","link":"https://www.wickes.co.uk/search?text=tea+lights"},{"action":"bath-liner","link":"https://www.amazon.co.uk/s?k=WaterBOB"}]""";

        IReadOnlyList<Selficient.Application.Kit.RetiredPick> retired = EmbeddedKitCatalogue.ParseRetired(Json(File), new AffiliateOptions { AmazonTag = "selficient-21" });

        retired.ShouldBe([
            new(ActionId.From("candles-and-matches"), new Uri("https://www.wickes.co.uk/search?text=tea+lights")),
            new(ActionId.From("bath-liner"), new Uri("https://www.amazon.co.uk/s?k=WaterBOB&tag=selficient-21")),
        ]);
        EmbeddedKitCatalogue.ParseRetired(Json("[]"), new AffiliateOptions()).ShouldBeEmpty();
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.ParseRetired(Json("null"), new AffiliateOptions())).Message.ShouldBe("The list of withdrawn picks is empty.");
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.ParseRetired(Json("""[{"action":"bath-liner","link":"not a link"}]"""), new AffiliateOptions())).Message.ShouldBe("A withdrawn pick has no valid action or link: 'bath-liner', 'not a link'.");
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.ParseRetired(Json("""[{"action":"Not An Action","link":"https://www.amazon.co.uk/s?k=x"}]"""), new AffiliateOptions())).Message.ShouldStartWith("A withdrawn pick has no valid action or link");
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.ParseRetired(Json("""[{"link":"https://www.amazon.co.uk/s?k=x"}]"""), new AffiliateOptions()));
        Should.Throw<InvalidOperationException>(() => EmbeddedKitCatalogue.Retired(new AffiliateOptions(), "Selficient.Infrastructure.Kit.no-such-file.json")).Message.ShouldBe("The list of withdrawn picks is missing from the build.");
        Should.Throw<ArgumentNullException>(() => EmbeddedKitCatalogue.Retired(null!));
        Should.Throw<ArgumentNullException>(() => EmbeddedKitCatalogue.ParseRetired(null!, new AffiliateOptions()));
        Should.Throw<ArgumentNullException>(() => EmbeddedKitCatalogue.ParseRetired(Json("[]"), null!));
    }

    [Fact]
    public void Nothing_withdrawn_is_still_shipped_and_everything_withdrawn_was_a_real_action()
    {
        AffiliateOptions options = new();
        EmbeddedKitCatalogue catalogue = EmbeddedKitCatalogue.Load(options);
        IReadOnlyList<Selficient.Application.Kit.RetiredPick> retired = EmbeddedKitCatalogue.Retired(options);
        HashSet<(string, string)> shipped = [.. catalogue.All.Select(p => (p.Action.Value, p.Link.ToString()))];

        retired.ShouldNotBeEmpty();
        retired.ShouldAllBe(pick => !shipped.Contains(ValueTuple.Create(pick.Action.Value, pick.Link.ToString())), "a pick cannot be shipped and withdrawn in the same release");
        retired.Select(pick => (pick.Action, pick.Link)).ShouldBeUnique();
    }

    [Fact]
    public void Nothing_in_the_catalogue_promotes_infant_formula()
    {
        EmbeddedKitCatalogue catalogue = EmbeddedKitCatalogue.Load(new AffiliateOptions());

        IReadOnlyList<KitPick> infants = catalogue.PicksFor(ActionId.From("formula-and-nappies"));

        infants.ShouldNotBeEmpty();
        catalogue.All.Where(pick => Formula(pick.Name) || Formula(pick.Note) || Formula(Uri.UnescapeDataString(pick.Link.ToString()))).ShouldBeEmpty("promoting formula for babies under six months is restricted in law, and a tagged link is a promotion");

        static bool Formula(string text) => text.Contains("formula", StringComparison.OrdinalIgnoreCase) || text.Contains("first milk", StringComparison.OrdinalIgnoreCase) || text.Contains("infant milk", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guards_reject_nulls()
    {
        Should.Throw<ArgumentNullException>(() => EmbeddedKitCatalogue.Load(null!));
        Should.Throw<ArgumentNullException>(() => EmbeddedKitCatalogue.Parse(null!, new AffiliateOptions()));
        Should.Throw<ArgumentNullException>(() => EmbeddedKitCatalogue.Parse(Json("[]"), null!));
    }

    private static HashSet<string> EveryActionId()
    {
        HashSet<string> ids = [];
        PlanBuilder builder = new(ApplicationServiceCollectionExtensions.Rules());
        RiskAssessmentPipeline risks = new(ApplicationServiceCollectionExtensions.Assessors());
        foreach (HouseholdProfile profile in Households())
        {
            foreach (PlanAction action in builder.Build(profile, risks.Run(profile)).Value.Actions)
            {
                ids.Add(action.Id.Value);
            }
        }

        return ids;
    }

    /// <summary>Households that between them are offered every action the plan can sell: a house, a flat, a bungalow in a flood zone with pets and a stove, and a house with a baby.</summary>
    private static HouseholdProfile[] Households() =>
    [
        Profiles.RobsHouse(), Profiles.Flat(), Profiles.FloodZoneBungalow(),
        Profiles.RobsHouse() with { Particulars = new Particulars(ElectricCar: false, WoodStove: false, WaterButt: false, Infants: 1) },
    ];

    private static IEnumerable<string> PurchasableActionIds()
    {
        PlanBuilder builder = new(ApplicationServiceCollectionExtensions.Rules());
        RiskAssessmentPipeline risks = new(ApplicationServiceCollectionExtensions.Assessors());
        return Households()
            .SelectMany(profile => builder.Build(profile, risks.Run(profile)).Value.Actions)
            .Where(a => a.Cost.To.Pence > 0)
            .Select(a => a.Id.Value)
            .Distinct();
    }
}
