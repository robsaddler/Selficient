using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Figures;
using Selficient.Application.Operations;
using Selficient.Domain.Parameters;
using Selficient.TestKit;
using Selficient.Web.Pages.Admin;

namespace Selficient.Web.Tests.Pages;

/// <summary>
/// The assumptions desk, through the real sign-in and the real store. Every host here has a database
/// of its own, so a set published by one test is in force for that test and no other.
/// </summary>
public sealed class AdminFiguresTests : IDisposable
{
    private const string Desk = "/admin/figures";
    private const string Lift = "stairlift-batteries.stock";

    private readonly SelficientWebApplicationFactory _factory = new SelficientWebApplicationFactory().WithSetting("RateLimits:PlanPerMinute", "10000");

    private readonly HashSet<string> _made = [];

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    /// <summary>A signed-in client. Rob is made first, so on a new database he is the one who may publish.</summary>
    private async Task<HttpClient> SignedInAsync(string username = OperatorSignInHelper.Username)
    {
        // Making an account again gives it a new password and ends its sessions, so each is made once.
        foreach (string name in new[] { OperatorSignInHelper.Username, username })
        {
            if (_made.Add(name))
            {
                await _factory.CreateOperatorAsync(name);
            }
        }

        HttpClient client = Client();
        (await _factory.SignInAsync(client, username)).ShouldContain("review__offers", customMessage: "the sign-in opens the back office");
        return client;
    }

    private static async Task<string> GetAsync(HttpClient client, string query = "") =>
        WebUtility.HtmlDecode(await client.GetStringAsync(new Uri(Desk + query, UriKind.Relative)));

    private static async Task<string> PostAsync(HttpClient client, string handler, string tab, params (string Name, string Value)[] fields)
    {
        string token = await OperatorSignInHelper.TokenAsync(client, Desk);
        using HttpResponseMessage response = await client.PostAsync(new Uri($"{Desk}?handler={handler}&tab={tab}", UriKind.Relative), OperatorSignInHelper.Form(token, fields));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static Task<string> DraftAsync(HttpClient client, string id, string value, string reason = "An engineer measured it.", string source = "The maker's service manual", string tab = "work") =>
        PostAsync(client, "Draft", tab, ("id", id), ("value", value), ("reason", reason), ("source", source));

    private static string Row(string html, string id)
    {
        int start = html.IndexOf($"data-figure=\"{id}\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, $"the figure {id} is on the page");
        return html[start..html.IndexOf("</details>", start, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task The_desk_is_closed_to_anyone_who_is_not_signed_in()
    {
        using HttpClient client = Client();

        using HttpResponseMessage page = await client.GetAsync(new Uri(Desk, UriKind.Relative));
        using HttpResponseMessage post = await client.PostAsync(new Uri(Desk + "?handler=Publish", UriKind.Relative), OperatorSignInHelper.Form("forged", ("note", "Mine.")));

        page.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/admin/sign-in");
        (await page.Content.ReadAsStringAsync()).ShouldNotContain("figure-card");
        post.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/admin/sign-in", "a post with no session is sent to the sign-in, and publishes nothing");
        (await _factory.Services.GetRequiredService<IFigureStore>().CurrentAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task The_figures_are_laid_out_as_a_household_meets_them_with_the_boards_own_words_and_prices_on_a_tab_of_their_own()
    {
        using HttpClient client = await SignedInAsync();

        string work = await GetAsync(client);
        string prices = await GetAsync(client, "?tab=prices");
        string nonsense = await GetAsync(client, "?tab=nonsense");
        string catalogue = await client.GetStringAsync(new Uri("/admin/catalogue", UriKind.Relative));

        catalogue.ShouldContain("<a class=\"button button--quiet button--small\" href=\"/admin/figures\">The figures</a>");
        work.ShouldContain("<h1 id=\"desk-heading\">The figures</h1>");
        work.ShouldContain("data-in-force=\"0\">The shipped figures are in force. Nothing has been published. Nothing is drafted.</p>");
        work.ShouldContain("aria-current=\"page\" data-tab=\"work\"");
        work.ShouldContain(">The draft</a>");
        work.ShouldContain("Every figure the board and the plan work from, grouped as a household meets them");
        foreach (string group in new[] { "What people need", "Water to drink", "Getting upstairs", "Sources of electricity", "What the plan asks for" })
        {
            work.ShouldContain($"data-group=\"{group}\"");
        }

        work.ShouldContain("data-group=\"Scenario: ");
        // A heading is drawn once, and the needs come in the board's order whatever order the engine tries their providers in.
        string[] headings = [.. System.Text.RegularExpressions.Regex.Matches(work, "data-group=\"([^\"]+)\"").Select(match => match.Groups[1].Value)];
        headings.ShouldBeUnique();
        headings[0].ShouldBe("What people need");
        string[] needs = [.. headings.Where(heading => Selficient.Domain.Resilience.Needs.All.Any(need => need.Name == heading))];
        needs.ShouldBe([.. Selficient.Domain.Resilience.Needs.All.Select(need => need.Name).Where(needs.Contains)], "rung by rung, as the board draws them");
        needs.Length.ShouldBeGreaterThan(15);
        Array.IndexOf(headings, "Sources of electricity").ShouldBeGreaterThan(Array.IndexOf(headings, needs[^1]));
        System.Text.RegularExpressions.Regex.Count(work, "<h3>The water butt</h3>").ShouldBe(1, "and a thing has one card");
        work.ShouldContain("<h3>The stairlift's own batteries</h3>");
        work.ShouldContain("<p class=\"figure-card__says\" data-says>About two days of trips to bed and the bathroom on healthy batteries, fewer on a long stair or an old set.</p>");
        work.ShouldNotContain("data-drafted-says");
        string lift = Row(work, Lift);
        lift.ShouldContain("data-state=\"shipped\"");
        lift.ShouldContain("<span class=\"desk-figure__name\">How much there is</span>");
        lift.ShouldContain("<span class=\"data\" data-in-force-value>2</span> <span class=\"desk-figure__unit\">days</span>");
        lift.ShouldContain("<li class=\"desk-figure__chip\" data-chip=\"household\">A household may give its own</li>");
        lift.ShouldContain("<details class=\"desk-figure__edit\">", customMessage: "a figure's box is shut until it is opened");
        lift.ShouldContain("A correction must be between 0 and 100.");
        lift.ShouldContain("name=\"value\" class=\"input input--short\" inputmode=\"decimal\" autocomplete=\"off\" value=\"2\" required>");
        lift.ShouldContain("href=\"/admin/figures?tab=history&figure=stairlift-batteries.stock\">Its history</a>");
        Row(work, "demand.drinking-litres").ShouldContain("<span class=\"desk-figure__name\">Water a person drinks and cooks with in a day</span>", customMessage: "a figure under a heading that already names it keeps its whole name");
        Row(work, "demand.drinking-litres").ShouldNotContain("data-chip=");
        work.ShouldNotContain("data-figure=\"price.headtorches.low\"");

        prices.ShouldContain("aria-current=\"page\" data-tab=\"prices\"");
        prices.ShouldContain("What each thing on the plan costs, at least and at most, for one of it");
        prices.ShouldContain("<h3>A headtorch</h3>");
        Row(prices, "price.headtorches.low").ShouldContain("<span class=\"desk-figure__name\">The least it costs</span>");
        Row(prices, "price.headtorches.high").ShouldContain("<span class=\"data\" data-in-force-value>28</span> <span class=\"desk-figure__unit\">pounds</span>");
        prices.ShouldNotContain($"data-figure=\"{Lift}\"");
        prices.ShouldNotContain("data-says");

        nonsense.ShouldContain("aria-current=\"page\" data-tab=\"work\"", customMessage: "a tab there is no such tab for is the first");
    }

    [Fact]
    public async Task A_search_finds_figures_on_both_tabs_by_name_heading_or_id_and_says_what_it_found()
    {
        using HttpClient client = await SignedInAsync();

        string byName = await GetAsync(client, "?q=headtorch");
        string byGroup = await GetAsync(client, "?q=" + Uri.EscapeDataString("  sources of ELECTRICITY "));
        string byId = await GetAsync(client, "?q=demand.pet-water");
        string none = await GetAsync(client, "?tab=prices&q=" + Uri.EscapeDataString("<script>alert(1)</script>"));
        string raw = await client.GetStringAsync(new Uri(Desk + "?q=" + Uri.EscapeDataString("<script>alert(1)</script>"), UriKind.Relative));
        string longer = await GetAsync(client, "?q=" + new string('z', 200));

        byName.ShouldContain("figures match \"headtorch\", from both tabs.");
        byName.ShouldContain("data-figure=\"headtorches.stock\"");
        byName.ShouldContain("data-figure=\"price.headtorches.low\"");
        byName.ShouldContain("id=\"desk-q\" name=\"q\" type=\"search\" class=\"input\" value=\"headtorch\"");
        byGroup.ShouldContain("figures match \"sources of ELECTRICITY\", from both tabs.");
        byGroup.ShouldContain("data-group=\"Sources of electricity\"");
        byGroup.ShouldNotContain("data-group=\"Water to drink\"");
        byId.ShouldContain("<p class=\"desk__lede\" data-sheet-lede>One figure matches \"demand.pet-water\".</p>");
        byId.ShouldContain("data-figure=\"demand.pet-water-litres\"");
        none.ShouldContain("No figure matches \"<script>alert(1)</script>\".");
        none.ShouldNotContain("figure-card\"");
        raw.ShouldNotContain("<script>alert(1)</script>", customMessage: "what was searched for is written as text");
        longer.ShouldContain($"No figure matches \"{new string('z', FiguresModel.MaxQueryLength)}\".", customMessage: "a search is read only as far as the page will read");
    }

    [Fact]
    public async Task A_correction_goes_into_the_draft_with_its_reason_and_source_and_the_words_follow_it_while_nothing_in_force_moves()
    {
        using HttpClient client = await SignedInAsync();

        string drafted = await DraftAsync(client, Lift, " 3 ", "An engineer <b>measured</b> it.", "Stannah service manual, section 4");
        string rawDraftTab = await client.GetStringAsync(new Uri(Desk + "?tab=draft", UriKind.Relative));
        string draftTab = WebUtility.HtmlDecode(rawDraftTab);

        drafted.ShouldContain("<p class=\"callout\" role=\"status\">\"The stairlift's own batteries: how much there is\" is drafted as 3. Nothing changes until the draft is published.</p>");
        drafted.ShouldContain("The shipped figures are in force. Nothing has been published. One correction is drafted and waits to be published.");
        drafted.ShouldContain(">The draft (1)</a>");
        drafted.ShouldContain("data-says>About two days of trips to bed and the bathroom");
        drafted.ShouldContain("<p class=\"figure-card__drafted\" data-drafted-says><span class=\"figure-card__mark\">As drafted</span> The stairlift's own batteries. About three days of trips to bed and the bathroom on healthy batteries, fewer on a long stair or an old set.</p>");
        string lift = Row(drafted, Lift);
        lift.ShouldContain("data-state=\"drafted\"");
        lift.ShouldContain("data-in-force-value>2</span>", customMessage: "the value in force is still the one in force");
        lift.ShouldContain("<li class=\"desk-figure__chip\" data-chip=\"drafted\">Drafted as 3 by rob</li>");
        lift.ShouldContain("value=\"3\" required>", customMessage: "the box opens on what is drafted, to be corrected again");
        lift.ShouldContain("value=\"Stannah service manual, section 4\" required>");
        _factory.Services.GetRequiredService<ICurrentFigures>().Current.Version.ShouldBe(0);

        draftTab.ShouldContain("aria-current=\"page\" data-tab=\"draft\"");
        draftTab.ShouldContain("<li class=\"desk__draft\" data-draft=\"stairlift-batteries.stock\">");
        draftTab.ShouldContain("<h2>The stairlift's own batteries: how much there is</h2>");
        draftTab.ShouldContain("<p class=\"data\">2 days in force; 3 drafted.</p>");
        draftTab.ShouldContain("<dt>Why</dt><dd>An engineer <b>measured</b> it.</dd>");
        rawDraftTab.ShouldContain("An engineer &lt;b&gt;measured&lt;/b&gt; it.", customMessage: "a reason is written as text, whatever is in it");
        draftTab.ShouldContain("<dt>Source</dt><dd>Stannah service manual, section 4</dd>");
        draftTab.ShouldContain("Drafted by rob on ");
        draftTab.ShouldContain("Take it out of the draft");
        draftTab.ShouldContain("<p class=\"desk__lede\" data-preview-says>Tried on 6 reference households in every scenario, with nothing done and with this week's actions done: 0 of 96 Days Ready figures and 0 of 6 plan costs would move.</p>", customMessage: "a third day of batteries moves nobody, because something else runs out first, and the preview says so");
        draftTab.ShouldNotContain("data-preview-days");
        draftTab.ShouldNotContain("data-preview-costs");
        draftTab.ShouldContain("Publish these figures");

        IReadOnlyList<OperatorAction> hands = await _factory.Services.GetRequiredService<IOperationsLog>().RecentHandsAsync(5, CancellationToken.None);
        hands.ShouldContain(hand => hand.Hand == "draft-figure" && hand.OperatorName == "rob" && hand.Outcome == "stairlift-batteries.stock drafted as 3: An engineer <b>measured</b> it.");
    }

    [Fact]
    public async Task A_correction_that_cannot_stand_is_answered_at_the_figure_with_what_was_typed_still_in_the_box()
    {
        using HttpClient client = await SignedInAsync();

        string words = await DraftAsync(client, Lift, "lots", "It is lots.", "My nan", tab: "prices");
        string range = await DraftAsync(client, Lift, "1000");
        string same = await DraftAsync(client, Lift, "2");
        string unexplained = await DraftAsync(client, Lift, "3", reason: "");
        string unsourced = await DraftAsync(client, Lift, "3", source: "");
        string contradiction = await DraftAsync(client, "price.headtorches.high", "10");
        string unknown = await DraftAsync(client, "no-such-figure", "3");
        string empty = await PostAsync(client, "Draft", "work");
        string valueless = await PostAsync(client, "Draft", "work", ("id", Lift));

        string lift = Row(words, Lift);
        words.ShouldContain("aria-current=\"page\" data-tab=\"work\"", customMessage: "the figure's own tab is drawn, wherever the post came from");
        lift.ShouldContain("<details class=\"desk-figure__edit\" open=\"open\">");
        lift.ShouldContain("<p class=\"callout callout--warning\" role=\"alert\" data-figure-refusal>Give the new value as a number, such as 2 or 0.5.</p>");
        lift.ShouldContain("value=\"lots\" required>");
        lift.ShouldContain("value=\"It is lots.\" required>");
        lift.ShouldContain("value=\"My nan\" required>");
        Row(words, "demand.drinking-litres").ShouldNotContain("open=\"open\"");
        words.ShouldNotContain("class=\"callout\" role=\"status\"");
        Row(range, Lift).ShouldContain("data-figure-refusal>The stairlift's own batteries: how much there is must be between 0 and 100 days.</p>");
        Row(range, Lift).ShouldContain("value=\"1000\" required>");
        Row(same, Lift).ShouldContain("data-figure-refusal>That is the value already in force. To withdraw a drafted correction, take it out of the draft.</p>");
        Row(unexplained, Lift).ShouldContain("data-figure-refusal>Say why the figure should change, in up to 400 characters.</p>");
        Row(unsourced, Lift).ShouldContain("data-figure-refusal>Say where the value comes from, in up to 400 characters.</p>");
        contradiction.ShouldContain("aria-current=\"page\" data-tab=\"prices\"");
        Row(contradiction, "price.headtorches.high").ShouldContain("data-figure-refusal>A headtorch cannot cost more at its least than at its most.</p>");
        unknown.ShouldContain("<p class=\"callout callout--warning\" role=\"alert\">That is not one of the figures the plan works from.</p>");
        unknown.ShouldNotContain("data-figure-refusal");
        empty.ShouldContain("role=\"alert\">That is not one of the figures the plan works from.</p>");
        Row(valueless, Lift).ShouldContain("data-figure-refusal>Give the new value as a number, such as 2 or 0.5.</p>");
        (await _factory.Services.GetRequiredService<IFigureStore>().DraftsAsync(CancellationToken.None)).ShouldBeEmpty("nothing refused is kept");
    }

    [Fact]
    public async Task One_draft_is_shared_and_only_a_publisher_puts_it_into_force_or_takes_out_what_somebody_else_drafted()
    {
        using HttpClient rob = await SignedInAsync();
        using HttpClient expert = await SignedInAsync("expert");

        await DraftAsync(expert, "price.headtorches.high", "40", "They have gone up.", "Three shops on 13 November", tab: "prices");
        string pricesOnly = await GetAsync(expert, "?tab=draft");
        await DraftAsync(rob, Lift, "1", "Old batteries.", "A service engineer");
        string both = await GetAsync(expert, "?tab=draft");
        string refusedPublish = await PostAsync(expert, "Publish", "draft", ("note", "Mine."));
        string refusedDiscard = await PostAsync(expert, "Discard", "draft", ("id", Lift));
        string refusedRestore = await PostAsync(expert, "Restore", "history", ("version", "0"), ("note", "Mine."));
        string noSuchDraft = await PostAsync(expert, "Discard", "draft");
        string ownDiscard = await PostAsync(expert, "Discard", "draft", ("id", "price.headtorches.high"));

        pricesOnly.ShouldContain("One correction is drafted and waits to be published.");
        pricesOnly.ShouldContain("0 of 96 Days Ready figures and 6 of 6 plan costs would move.");
        pricesOnly.ShouldContain("data-preview-costs");
        pricesOnly.ShouldNotContain("data-preview-days");
        pricesOnly.ShouldContain("<p class=\"review__none\" data-not-publisher>Only an operator who may publish figures can put the draft into force. What you have drafted waits here for them.</p>");
        pricesOnly.ShouldNotContain("Publish these figures");

        both.ShouldContain("2 corrections are drafted and wait to be published.");
        both.ShouldContain(">The draft (2)</a>");
        both.ShouldContain("Drafted by rob on ");
        both.ShouldContain("Drafted by expert on ");
        both.ShouldContain("data-preview-days");
        both.ShouldContain("<td>An older person in a terrace with a battery stairlift and a prepayment meter</td>");
        both.ShouldContain("data-preview-costs");
        System.Text.RegularExpressions.Regex.Count(both, "Take it out of the draft</button>").ShouldBe(1, "the expert may take out their own correction and not Rob's");
        both.ShouldContain("<p class=\"review__none\" data-not-yours>Only rob or a publisher can take this out of the draft.</p>");

        refusedPublish.ShouldContain("role=\"alert\">Only an operator who may publish figures can put them into force.</p>");
        refusedPublish.ShouldContain("aria-current=\"page\" data-tab=\"draft\"");
        refusedDiscard.ShouldContain("role=\"alert\">Only the person who drafted a correction, or a publisher, can take it out of the draft.</p>");
        refusedRestore.ShouldContain("role=\"alert\">Only an operator who may publish figures can put them into force.</p>");
        refusedRestore.ShouldContain("aria-current=\"page\" data-tab=\"history\"");
        refusedRestore.ShouldNotContain("Put this set back", customMessage: "somebody who may not publish is not offered the hand");
        refusedRestore.ShouldContain("<p class=\"review__none\" data-set-standing>This is the set in force.</p>");
        noSuchDraft.ShouldContain("role=\"alert\">There is no drafted correction of that figure.</p>");
        ownDiscard.ShouldContain("role=\"status\">The correction is out of the draft.</p>");
        ownDiscard.ShouldContain("0 of 6 plan costs would move.");
        _factory.Services.GetRequiredService<ICurrentFigures>().Current.Version.ShouldBe(0, "nothing an expert posted put anything into force");

        string robsDraft = await GetAsync(rob, "?tab=draft");
        string unexplained = await PostAsync(rob, "Publish", "draft");
        string robTakesOut = await PostAsync(rob, "Discard", "draft", ("id", Lift));
        string nothingToPublish = await PostAsync(rob, "Publish", "draft", ("note", "Nothing."));

        robsDraft.ShouldContain("Publish these figures");
        robsDraft.ShouldNotContain("data-not-publisher");
        unexplained.ShouldContain("role=\"alert\">Say what this publication is for, in up to 400 characters.</p>");
        robTakesOut.ShouldContain("role=\"status\">The correction is out of the draft.</p>");
        robTakesOut.ShouldContain("<p class=\"review__none\" data-no-drafts>Nothing is drafted. Open a figure under \"How things work\" or \"What things cost\" and correct it.</p>");
        robTakesOut.ShouldNotContain("data-preview");
        nothingToPublish.ShouldContain("role=\"alert\">Nothing drafted would change a figure, so there is nothing to publish.</p>");
    }

    [Fact]
    public async Task A_published_draft_is_in_force_at_once_and_its_history_says_who_why_and_on_what_authority_and_an_earlier_set_is_put_back_as_a_new_one()
    {
        using HttpClient rob = await SignedInAsync();
        await DraftAsync(rob, Lift, "3", "An engineer measured it.", "Stannah service manual, section 4");

        string published = await PostAsync(rob, "Publish", "draft", ("note", "The stairlift figures, from the engineer's visit."));
        string work = await GetAsync(rob);
        string set1 = await GetAsync(rob, "?tab=history&set=1");
        string shipped = await GetAsync(rob, "?tab=history&set=0");
        string figure = await GetAsync(rob, "?tab=history&figure=" + Lift);
        string untouched = await GetAsync(rob, "?tab=history&figure=demand.drinking-litres");
        string noFigure = await GetAsync(rob, "?tab=history&figure=nonsense");

        published.ShouldContain("role=\"status\">Set 1 is in force. Every plan built from now on is worked out from it.</p>");
        published.ShouldContain("aria-current=\"page\" data-tab=\"history\"", customMessage: "a publication lands on the history, where the new set is");
        published.ShouldContain("data-in-force=\"1\">Set 1 is in force, published by rob on ");
        published.ShouldContain("Nothing is drafted.");
        published.ShouldContain("<li class=\"desk__set\" data-set=\"1\" data-state=\"in-force\">");
        published.ShouldContain("with one figure differing from the shipped ones.</p>");
        published.ShouldContain("<p class=\"desk__note\">The stairlift figures, from the engineer's visit.</p>");
        published.ShouldContain("<li class=\"desk__set\" data-set=\"0\" data-state=\"earlier\">");
        published.ShouldContain("<h2>The shipped figures</h2>");
        System.Text.RegularExpressions.Regex.Count(published, "Put this set back</button>").ShouldBe(1, "the set in force cannot be put back to itself");
        published.ShouldContain("<p class=\"review__none\" data-set-standing>This is the set in force.</p>");
        using HttpClient expert = await SignedInAsync("expert");
        (await GetAsync(expert, "?tab=history")).ShouldContain("<p class=\"review__none\" data-set-standing>Only an operator who may publish figures can put a set back.</p>");
        _factory.Services.GetRequiredService<ICurrentFigures>().Current[Lift].ShouldBe(3m);

        work.ShouldContain("data-says>About three days of trips to bed and the bathroom");
        work.ShouldNotContain("data-drafted-says");
        Row(work, Lift).ShouldContain("data-state=\"corrected\"");
        Row(work, Lift).ShouldContain("data-in-force-value>3</span>");
        Row(work, Lift).ShouldContain("<li class=\"desk-figure__chip\" data-chip=\"corrected\">Shipped as 2</li>");

        set1.ShouldContain("<h2 id=\"changes-heading\">Set 1</h2>");
        set1.ShouldContain("data-changes-lede>Every figure this publication changed: 1 in all.</p>");
        set1.ShouldContain("<h3>The stairlift's own batteries: how much there is</h3>");
        set1.ShouldContain("<p class=\"data\">2 to 3</p>");
        set1.ShouldContain("<dt>Why</dt><dd>An engineer measured it.</dd>");
        set1.ShouldContain("<dt>Source</dt><dd>Stannah service manual, section 4</dd>");
        set1.ShouldContain("Drafted by rob on ");
        shipped.ShouldContain("<h2 id=\"changes-heading\">The shipped figures</h2>");
        shipped.ShouldContain("No publication with that number changed a figure. The shipped figures are the values in the code, and no publication made them.");
        figure.ShouldContain("<h2 id=\"changes-heading\">The stairlift's own batteries: how much there is</h2>");
        figure.ShouldContain("Shipped as 2 days; 3 is in force. Every publication that changed it is below, newest first. On the board, under getting upstairs.");
        figure.ShouldContain("<h3>Set 1</h3>");
        untouched.ShouldContain("Shipped as 3 litres; 3 is in force.");
        untouched.ShouldNotContain("<h3>Set ");
        noFigure.ShouldContain("<h2 id=\"changes-heading\">Not a figure</h2>");
        noFigure.ShouldContain("data-changes-lede>That is not one of the figures the plan works from.</p>");

        string unexplained = await PostAsync(rob, "Restore", "history", ("version", "0"));
        string noSuchSet = await PostAsync(rob, "Restore", "history", ("version", "nine"), ("note", "Back."));
        string putBack = await PostAsync(rob, "Restore", "history", ("version", "0"), ("note", "The engineer was wrong."));
        string again = await PostAsync(rob, "Restore", "history", ("version", "1"), ("note", "The engineer was right after all."));

        unexplained.ShouldContain("role=\"alert\">Say what this publication is for, in up to 400 characters.</p>");
        noSuchSet.ShouldContain("role=\"alert\">That is not an earlier set of figures.</p>");
        putBack.ShouldContain("role=\"status\">Set 2 is in force. Every plan built from now on is worked out from it.</p>");
        putBack.ShouldContain("with every figure as it shipped. It put the shipped figures back.</p>");
        again.ShouldContain("role=\"status\">Set 3 is in force.");
        again.ShouldContain("with one figure differing from the shipped ones. It put set 1 back.</p>");
        again.ShouldContain("<li class=\"desk__set\" data-set=\"3\" data-state=\"in-force\">");
        _factory.Services.GetRequiredService<ICurrentFigures>().Current[Lift].ShouldBe(3m);
        (await GetAsync(rob, "?tab=history&figure=" + Lift)).ShouldContain("<dt>Why</dt><dd>Put back to set 1.</dd>");

        IReadOnlyList<OperatorAction> hands = await _factory.Services.GetRequiredService<IOperationsLog>().RecentHandsAsync(10, CancellationToken.None);
        hands.Select(hand => hand.Hand).ShouldBe(["restore-figures", "restore-figures", "publish-figures", "draft-figure"]);
    }

    [Fact]
    public async Task What_a_release_has_made_impossible_is_said_on_the_desk_and_never_run()
    {
        using HttpClient rob = await SignedInAsync();
        IFigureStore store = _factory.Services.GetRequiredService<IFigureStore>();
        DateTimeOffset then = new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);
        await store.PublishAsync(
            new PublishedFigures(1, then, "rob", "Before the release.", null, new Dictionary<string, decimal> { ["a-figure-a-release-removed"] = 1m, [Lift] = 3m }),
            [new FigureChange(1, "a-figure-a-release-removed", 0m, 1m, "It was a figure then.", "A manual", "expert", then)],
            clearDrafts: false,
            CancellationToken.None);
        await _factory.Services.GetRequiredService<CurrentFigures>().ReloadAsync(CancellationToken.None);
        await store.SaveDraftAsync(new FigureDraft("a-figure-a-release-removed", 2m, "It was a figure then.", "A manual", "expert", then), CancellationToken.None);

        string work = await GetAsync(rob);
        string draft = await GetAsync(rob, "?tab=draft");
        string set1 = await GetAsync(rob, "?tab=history&set=1");

        work.ShouldContain("<p class=\"callout callout--warning\" data-dropped>The correction of \"a-figure-a-release-removed\" to 1 is no longer one the plan accepts, and was not applied.</p>");
        Row(work, Lift).ShouldContain("data-in-force-value>3</span>", customMessage: "the correction that still stands is applied");
        work.ShouldNotContain("data-drafted-says");
        draft.ShouldContain("<p class=\"callout callout--warning\" role=\"alert\" data-draft-fault>The draft cannot be published as it stands. \"a-figure-a-release-removed\" is not one of the figures the plan works from.</p>");
        draft.ShouldContain("<h2>a-figure-a-release-removed</h2>");
        draft.ShouldContain("<p class=\"data\">This is no longer a figure the plan works from. Take it out of the draft.</p>");
        draft.ShouldNotContain("data-preview");
        draft.ShouldNotContain("Publish these figures");
        set1.ShouldContain("<h3>a-figure-a-release-removed</h3>");
    }

    [Fact]
    public void The_desks_sentences_and_lines_are_decided_in_one_place()
    {
        DateTimeOffset at = new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);
        DeskView desk = new(ParameterSet.Default, null, [], [], ParameterSet.Default, null, false);

        FiguresModel.InForceSentence(null).ShouldBe("The shipped figures are in force. Nothing has been published.");
        FiguresModel.InForceSentence(new PublishedFigures(4, at, "rob", "Note.", null, new Dictionary<string, decimal>())).ShouldStartWith("Set 4 is in force, published by rob on 14 Nov 2026 ");
        FiguresModel.DraftSentence(0).ShouldBe("Nothing is drafted.");
        FiguresModel.DraftSentence(1).ShouldBe("One correction is drafted and waits to be published.");
        FiguresModel.DraftSentence(12).ShouldBe("12 corrections are drafted and wait to be published.");
        FiguresModel.DraftTabName(0).ShouldBe("The draft");
        FiguresModel.DraftTabName(3).ShouldBe("The draft (3)");
        FiguresModel.FoundSentence(0, "x").ShouldBe("No figure matches \"x\".");
        FiguresModel.FoundSentence(1, "x").ShouldBe("One figure matches \"x\".");
        FiguresModel.FoundSentence(7, "x").ShouldBe("7 figures match \"x\", from both tabs.");
        FiguresModel.SetLine.Of(new PublishedFigures(4, at, "rob", "Note.", null, new Dictionary<string, decimal> { ["a"] = 1m, ["b"] = 2m }), desk).Detail.ShouldEndWith(", with 2 figures differing from the shipped ones.");
        FigureSheet.Plain(0.3500m).ShouldBe("0.35");
        FigureSheet.Plain(17000m).ShouldBe("17000");
        FigureSheet.AnchorOf(Lift).ShouldBe("f-stairlift-batteries.stock");
        FigureSheet.IsPrice(ParameterRegistry.Find("price.headtorches.low")!).ShouldBeTrue();
        FigureSheet.IsPrice(ParameterRegistry.Find(Lift)!).ShouldBeFalse();
        FigureSheet.Build([], desk, null).ShouldBeEmpty();

        // With a draft that can no longer be run, the words are the ones in force and nothing is shown as drafted.
        FigureGroup broken = FigureSheet.Build([ParameterRegistry.Find(Lift)!], desk with { Drafted = null, DraftFault = "It cannot." }, null).ShouldHaveSingleItem();
        broken.Cards.ShouldHaveSingleItem().Drafted.ShouldBeNull();
        broken.Cards[0].Rows.ShouldHaveSingleItem().Open.ShouldBeFalse();
        broken.Cards[0].Rows[0].Anchor.ShouldBe("f-stairlift-batteries.stock");

        Should.Throw<ArgumentNullException>(() => FigureSheet.IsPrice(null!));
        Should.Throw<ArgumentNullException>(() => FigureSheet.Build(null!, desk, null));
        Should.Throw<ArgumentNullException>(() => FigureSheet.Build([], null!, null));
        Should.Throw<ArgumentNullException>(() => FiguresModel.DraftLine.Of(null!, desk, "rob"));
        Should.Throw<ArgumentNullException>(() => FiguresModel.DraftLine.Of(new FigureDraft(Lift, 3m, "Why.", "Where.", "rob", at), null!, "rob"));
        Should.Throw<ArgumentNullException>(() => FiguresModel.SetLine.Of(null!, desk));
        Should.Throw<ArgumentNullException>(() => FiguresModel.SetLine.Of(new PublishedFigures(1, at, "rob", "Note.", null, new Dictionary<string, decimal>()), null!));
        Should.Throw<ArgumentNullException>(() => FiguresModel.SetLine.Shipped(null!));
        Should.Throw<ArgumentNullException>(() => FiguresModel.ChangeLine.InHistory(null!));
        Should.Throw<ArgumentNullException>(() => FiguresModel.ChangeLine.InSet(null!));
        Should.Throw<ArgumentNullException>(() => new FiguresModel(null!, NSubstitute.Substitute.For<IFigureStore>()));
        Should.Throw<ArgumentNullException>(() => new FiguresModel(_factory.Services.GetRequiredService<FigureDesk>(), null!));
    }

    public void Dispose() => _factory.Dispose();
}
