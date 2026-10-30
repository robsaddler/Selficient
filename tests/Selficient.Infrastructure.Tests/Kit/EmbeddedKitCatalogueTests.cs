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
        HashSet<string> exempt = ["cash-in-small-notes", "tree-work-if-advised", "solar-quotes", "rooftop-solar-and-battery", "boiler-plug-and-socket", "heating-fallback", "lights-out-drill", "priority-services-register", "save-emergency-numbers", "clear-leaf-litter-under-decking", "flood-warnings-and-plan"];

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
        foreach (HouseholdProfile profile in new[] { Profiles.RobsHouse(), Profiles.Flat(), Profiles.FloodZoneBungalow() })
        {
            foreach (PlanAction action in builder.Build(profile, risks.Run(profile)).Value.Actions)
            {
                ids.Add(action.Id.Value);
            }
        }

        return ids;
    }

    private static IEnumerable<string> PurchasableActionIds()
    {
        PlanBuilder builder = new(ApplicationServiceCollectionExtensions.Rules());
        RiskAssessmentPipeline risks = new(ApplicationServiceCollectionExtensions.Assessors());
        return new[] { Profiles.RobsHouse(), Profiles.Flat(), Profiles.FloodZoneBungalow() }
            .SelectMany(profile => builder.Build(profile, risks.Run(profile)).Value.Actions)
            .Where(a => a.Cost.To.Pence > 0)
            .Select(a => a.Id.Value)
            .Distinct();
    }
}
