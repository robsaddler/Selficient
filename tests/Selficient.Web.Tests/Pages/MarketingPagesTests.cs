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
        html.ShouldContain("<h2 id=\"ways-heading\">Three ways to keep your plan</h2>");
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
    public async Task Every_household_page_says_no_account_is_needed_where_sign_in_would_be()
    {
        using HttpClient client = _factory.CreateClient();

        string landing = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        string privacy = await client.GetStringAsync(new Uri("/privacy", UriKind.Relative));
        string backOffice = await client.GetStringAsync(new Uri("/admin/sign-in", UriKind.Relative));

        foreach (string page in new[] { landing, privacy })
        {
            // A link to the privacy page until the script runs, so the chip goes somewhere without it.
            page.ShouldContain("<a class=\"keep-chip\" href=\"/privacy\" data-keep-chip data-open-dialog=\"keep-dialog\" data-kept=\"none\">");
            page.ShouldContain("<svg class=\"mark-icon\" data-keep-mark=\"device\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/img/marks.svg#mark-device\"/></svg>");
            page.ShouldContain("<svg class=\"mark-icon\" data-keep-mark=\"account\" aria-hidden=\"true\" focusable=\"false\" hidden><use href=\"/img/marks.svg#mark-account\"/></svg>");
            page.ShouldContain("<span data-keep-label=\"none\">No account needed</span>");
            page.ShouldContain("<span data-keep-label=\"device\" hidden>Saved on this device</span>");
            page.ShouldContain("<h2 id=\"keep-heading\">Where your plan is kept</h2>");
            page.ShouldContain("<p data-keep-says=\"device-none\">Nothing is saved yet. When you make a plan it lives in this browser and nowhere else. You do not need an account.</p>");
            page.ShouldContain("<p data-keep-says=\"device\" hidden>Your plan lives in this browser and nowhere else. You do not need an account.</p>");
            page.ShouldContain("<p class=\"keep__warning\" data-keep-safari hidden>Safari clears a site&#x27;s data after a week without a visit. Add Selficient to your home screen, or make an account, to keep your plan.</p>");
            page.ShouldContain("<p>Your plan is kept in your own browser. What leaves it, and when, is on the <a href=\"/privacy\">privacy page</a>.</p>");
        }

        // The second row of the panel: sending a copy. Hidden until there is a plan to send, and every sentence it can say is on the page already.
        landing.ShouldContain("<section class=\"keep__row\" aria-labelledby=\"keep-another\" data-keep-another hidden>");
        landing.ShouldContain("<p>Send a copy of your plan to your phone or another computer. Changes you make afterwards will not follow it.</p>");
        landing.ShouldContain("<button type=\"button\" class=\"button\" data-send-copy>Send a copy</button>");
        landing.ShouldContain("<div class=\"keep__code\" data-send-code role=\"img\" aria-label=\"A code for your other device&#x27;s camera\"></div>");
        landing.ShouldContain("<p>Point your other device&#x27;s camera at this. It works for ten minutes, once.</p>");
        landing.ShouldContain("<button type=\"button\" class=\"button button--quiet\" data-send-link>Copy the link instead</button>");
        landing.ShouldContain("<p>Sent. Your other device has its own copy now. Changes you make on one will not appear on the other.<span data-keep-guest> An account would keep both in step.</span></p>");
        foreach ((string key, string sentence) in KeepCopy.SendSays)
        {
            landing.ShouldContain($"<p class=\"keep__says\" data-send-says=\"{key}\" hidden>{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(sentence)}</p>");
            sentence.ShouldEndWith(".");
        }

        KeepCopy.SendSays.Select(s => s.Key).ShouldBe(["making", "copied", "not-copied", "expired", "offline", "busy", "full", "large", "failed"]);
        KeepCopy.SendSays.Single(s => s.Key == "expired").Sentence.ShouldBe("That code has run out. Send a copy again.");
        KeepCopy.SendSays.Single(s => s.Key == "offline").Sentence.ShouldBe("A copy cannot be sent without a connection. Try again when you are back online.");
        KeepCopy.SendToPhone.ShouldBe("Send a copy to my phone");

        // Beside both start buttons on the landing page.
        // Under both start buttons: the headline first, then what an account would be for, as a choice and with a way to it.
        Regex.Count(landing, "<p class=\"start-note\"><strong>No account needed.</strong> Your plan is saved on this device.</p>").ShouldBe(2);
        Regex.Count(landing, "<p class=\"start-note start-note--account\">Want it on your phone too, or safe if this device is lost\\? <a href=\"/account/sign-in\">Make a free account whenever you like</a>: just an email address, no password.</p>").ShouldBe(2);

        // The section that says where a plan is kept, in the panel's own order: the default first, an account last and optional.
        landing.ShouldContain("<p class=\"ways__lede\">You start with the first and need nothing else. The other two are there if you want them, when you want them.</p>");
        KeepCopy.Ways(accounts: true).Select(w => w.Name).ShouldBe(["On this device", "Send a copy", "An account, if you want one"]);
        foreach (KeepCopy.Way way in KeepCopy.Ways(accounts: true))
        {
            landing.ShouldContain($"<li class=\"ways__card\" data-way=\"{way.Mark}\">");
            landing.ShouldContain($"<h3>{way.Name}</h3>");
            landing.ShouldContain($"<p>{way.Sentence}</p>");
        }

        KeepCopy.Ways(accounts: true)[2].Sentence.ShouldBe("Keeps every device you sign in on in step, and keeps your plan if a device is lost. An email address, no password, and you can delete it at any time.");
        KeepCopy.Ways(accounts: true)[1].Sentence.ShouldBe("Move your plan to your phone or another computer by scanning a code. It is a one-off copy: the two do not stay in step.");
        landing.ShouldContain("Whichever you choose, your postcode is sent once, to find your area, your height above sea level and what the flood map says there, and is not kept. There is no tracking and no analytics.");
        landing.ShouldContain("<summary>Do I need an account?</summary>");
        landing.ShouldContain("If you want one, an account does two things: it keeps every device you sign in on in step, and it keeps your plan if a device is lost.");
        backOffice.ShouldNotContain("keep-chip", customMessage: "the back office is not a household's page");
        KeepCopy.ShowsOn("/Admin/catalogue").ShouldBeFalse();
        KeepCopy.ShowsOn("/administration").ShouldBeTrue("only the back office's own segment");
        KeepCopy.FirstPlan.ShouldBe("Your plan is saved on this device. You do not need an account.");
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

        privacy.ShouldContain("Selficient needs no account, runs no analytics and keeps no record of who you are unless you choose to make an account.");
        privacy.ShouldNotContain("sets no cookies", customMessage: "a page with a form sets a security cookie, and the page now says so");
        privacy.ShouldContain("<h2 id=\"account\">If you make an account</h2>");
        privacy.ShouldContain("You never have to. Everything on Selficient works without one, and your plan is saved on your device either way.");
        privacy.ShouldContain("If you make one, we keep your email address and a copy of your plan, and nothing else about you: no name, no password, no profile.");
        privacy.ShouldContain("Postmark, a mail service delivers for us");
        privacy.ShouldContain("If you ask for a code and never use it, the request is forgotten within the hour.");
        privacy.ShouldContain("Deleting removes your address, the copy of your plan, your passkeys and every sign-in, straight away.");
        privacy.ShouldContain("<h2 id=\"cookies\">Cookies</h2>");
        privacy.ShouldContain("Selficient sets no cookie that follows you or counts you");
        privacy.ShouldContain("A page with a form on it sets one security cookie, which stops another website sending that form in your name");
        privacy.ShouldContain("If you sign in, a second cookie keeps you signed in on that device for ninety days");
        privacy.ShouldContain("<td>your email address and a sign-in code, only if you ask to sign in</td>");
        LegalRails.Privacy.Sections.Select(s => s.Id).ShouldBe(["stays", "leaves", "account", "street", "cookies", "server", "rights"]);
        LegalCopy.PrivacyAtAGlance.Select(c => c.Heading).ShouldBe(["No account needed", "An account is yours to make and to delete", "A street group is locked before it leaves", "No tracking, no analytics", "Your plan stays on your device", "Your postcode goes once", "Photographs never touch our disk"]);
        foreach ((string service, _, _) in LegalCopy.Upstreams)
        {
            privacy.ShouldContain(service);
        }

        privacy.ShouldContain("Reviewed 13 November 2026.");
        privacy.ShouldContain("<h2 id=\"street\">If you join a street group</h2>");
        privacy.ShouldContain("We never have that key. So for a home in a group we keep two things: that your account is in the group, and a record we cannot read.");
        privacy.ShouldContain("We do know which accounts are in a group together, because that is how we decide who is given the group's records.");
        privacy.ShouldContain("A group holds offers and never needs: there is nowhere to say who in a home is frail, what is kept there or when it is empty.");
        privacy.ShouldContain("Nothing was sent to find out, and nothing is offered until you choose it.");
        privacy.ShouldContain("A device that has opened a group keeps two things in its own storage: the group's key, and the street as it last saw it");
        privacy.ShouldContain("and the key and the copy from the device you leave on.");
        privacy.ShouldContain("It takes your home out of every street group you joined at the same moment.");
        privacy.ShouldContain("If you are in a street group we also hold that you are, and a record only your group can read");
        privacy.ShouldContain("<td>an encrypted copy of your plan that we cannot read, because the key stays in the code on your screen</td>");
        privacy.ShouldContain("<td>to hand it to your other device, once; it is deleted as it is collected, or after ten minutes if it never is</td>");
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
