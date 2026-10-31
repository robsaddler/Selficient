using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Selficient.TestKit;
using Selficient.Web.Endpoints;
using Selficient.Web.Pages;

namespace Selficient.Web.Tests.Pages;

public sealed partial class MarketingPagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task The_landing_page_makes_its_case_in_the_house_voice()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        html.ShouldContain("A plan for your house, not a pile for the cupboard");
        html.ShouldContain("<ol class=\"how__steps\">");
        html.ShouldContain("Everything stays with you");
        html.ShouldContain("What it covers");
        html.ShouldContain("Questions people ask");
        html.ShouldContain("Start with the postcode");
        foreach (IndexModel.Faq faq in IndexModel.Questions)
        {
            html.ShouldContain($"<summary>{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(faq.Question)}</summary>");
        }

        html.ShouldContain("href=\"/privacy\"");
        html.ShouldContain("href=\"/terms\"");
        html.ShouldContain("href=\"/terms#advice\"");
        html.ShouldContain("mailto:hello@selficient.app");
        html.ShouldContain("Selficient is made in Blackwood, South Wales.");
    }

    [Fact]
    public async Task Structured_data_repeats_the_questions_word_for_word()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        Match block = JsonLd().Match(html);

        block.Success.ShouldBeTrue();
        block.Groups["nonce"].Value.ShouldMatch("^[A-Za-z0-9_-]{16,}$");
        JsonDocument json = JsonDocument.Parse(block.Groups["json"].Value);
        JsonElement[] items = json.RootElement.EnumerateArray().ToArray();
        items.Select(i => i.GetProperty("@type").GetString()).ShouldBe(["WebApplication", "Organization", "FAQPage"]);
        items[0].GetProperty("offers").GetProperty("price").GetString().ShouldBe("0");
        items[1].GetProperty("email").GetString().ShouldBe(LegalCopy.ContactEmail);
        JsonElement[] questions = items[2].GetProperty("mainEntity").EnumerateArray().ToArray();
        questions.Length.ShouldBe(IndexModel.Questions.Count);
        for (int i = 0; i < questions.Length; i++)
        {
            questions[i].GetProperty("name").GetString().ShouldBe(IndexModel.Questions[i].Question);
            questions[i].GetProperty("acceptedAnswer").GetProperty("text").GetString().ShouldBe(IndexModel.Questions[i].Answer);
        }
    }

    [Theory]
    [InlineData("/", "https://selficient.app/")]
    [InlineData("/privacy", "https://selficient.app/privacy")]
    [InlineData("/terms", "https://selficient.app/terms")]
    public async Task Public_pages_carry_a_canonical_and_share_metadata(string path, string canonical)
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri(path, UriKind.Relative));

        html.ShouldContain($"<link rel=\"canonical\" href=\"{canonical}\">");
        html.ShouldContain("<meta property=\"og:image\" content=\"https://selficient.app/img/og.png\">");
        html.ShouldContain("<meta name=\"twitter:card\" content=\"summary_large_image\">");
        html.ShouldContain("<meta property=\"og:locale\" content=\"en_GB\">");
        html.ShouldNotContain("noindex");
    }

    [Theory]
    [InlineData("/plan")]
    [InlineData("/offline")]
    [InlineData("/does-not-exist")]
    [InlineData("/styleguide")]
    public async Task Private_and_transient_pages_are_kept_out_of_search(string path)
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        html.ShouldContain("<meta name=\"robots\" content=\"noindex, nofollow\">");
        html.ShouldNotContain("rel=\"canonical\"");
        html.ShouldNotContain("og:image");
    }

    [Fact]
    public async Task The_sitemap_lists_only_public_pages_and_each_one_answers()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/sitemap.xml", UriKind.Relative));
        string xml = await response.Content.ReadAsStringAsync();
        XDocument document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        string[] locations = document.Root!.Elements(ns + "url").Select(u => u.Element(ns + "loc")!.Value).ToArray();

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=86400");
        xml.ShouldStartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        locations.ShouldBe(SeoEndpoints.PublicPages.Select(p => SeoEndpoints.Origin + p.Path).ToArray());
        locations.ShouldNotContain(l => l.Contains("/plan/explain", StringComparison.Ordinal) || l.Contains("/offline", StringComparison.Ordinal) || l.Contains("/api/", StringComparison.Ordinal));
        foreach (string location in locations)
        {
            using HttpResponseMessage page = await client.GetAsync(new Uri(location.Replace(SeoEndpoints.Origin, "", StringComparison.Ordinal), UriKind.Relative));
            page.StatusCode.ShouldBe(HttpStatusCode.OK, location);
            (await page.Content.ReadAsStringAsync()).ShouldNotContain("noindex", customMessage: location);
        }
    }

    [Fact]
    public async Task Robots_and_llms_say_what_crawlers_may_read()
    {
        using HttpClient client = _factory.CreateClient();

        string robots = await client.GetStringAsync(new Uri("/robots.txt", UriKind.Relative));
        using HttpResponseMessage llms = await client.GetAsync(new Uri("/llms.txt", UriKind.Relative));
        using HttpResponseMessage og = await client.GetAsync(new Uri("/img/og.png", UriKind.Relative));

        robots.ShouldContain("Sitemap: https://selficient.app/sitemap.xml");
        robots.ShouldContain("Disallow: /api/");
        robots.ShouldContain("Disallow: /plan/explain");
        robots.ShouldContain("Disallow: /admin/");
        robots.ShouldContain("Disallow: /styleguide");
        llms.StatusCode.ShouldBe(HttpStatusCode.OK);
        llms.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        (await llms.Content.ReadAsStringAsync()).ShouldContain("# Selficient");
        og.StatusCode.ShouldBe(HttpStatusCode.OK);
        og.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
    }

    [Fact]
    public async Task The_legal_pages_say_what_leaves_the_browser_and_who_to_write_to()
    {
        using HttpClient client = _factory.CreateClient();

        string privacy = await client.GetStringAsync(new Uri("/privacy", UriKind.Relative));
        string terms = await client.GetStringAsync(new Uri("/terms", UriKind.Relative));

        privacy.ShouldContain("no accounts, sets no cookies, runs no analytics");
        foreach ((string service, _, _) in LegalCopy.Upstreams)
        {
            privacy.ShouldContain(service);
        }

        privacy.ShouldContain("Reviewed 30 October 2026.");
        privacy.ShouldContain("never written to disk");
        terms.ShouldContain("id=\"advice\"");
        terms.ShouldContain("id=\"affiliate\"");
        terms.ShouldContain("Contains OS data © Crown copyright and database right 2026");
        terms.ShouldContain("law of England and Wales");
        terms.ShouldContain("SIL Open Font Licence");
        privacy.ShouldContain(LegalCopy.ContactEmail);
        terms.ShouldContain(LegalCopy.ContactEmail);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/privacy")]
    [InlineData("/terms")]
    [InlineData("/plan/start")]
    [InlineData("/plan/solar")]
    [InlineData("/plan/photos")]
    [InlineData("/offline")]
    public async Task Every_public_page_keeps_the_copy_rules(string path)
    {
        using HttpClient client = _factory.CreateClient();

        string html = await client.GetStringAsync(new Uri(path, UriKind.Relative));
        string main = MainContent().Match(html).Groups["main"].Value;
        string text = Regex.Replace(Regex.Replace(main, "<[^>]+>", " "), @"\s+", " ");

        main.ShouldNotBeEmpty();
        text.ShouldNotContain("!");
        text.ShouldNotContain("oops", Case.Insensitive);
        text.ShouldNotContain("sorry", Case.Insensitive);
        text.ShouldNotContain("Submit");
        text.ShouldNotContain("Click here");
    }

    public void Dispose() => _factory.Dispose();

    [GeneratedRegex("<script type=\"application/ld\\+json\" nonce=\"(?<nonce>[^\"]+)\">(?<json>.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLd();

    [GeneratedRegex("<main[^>]*>(?<main>.*?)</main>", RegexOptions.Singleline)]
    private static partial Regex MainContent();
}
