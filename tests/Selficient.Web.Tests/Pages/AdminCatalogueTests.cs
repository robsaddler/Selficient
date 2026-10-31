using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Selficient.Application.Catalogue;
using Selficient.Application.Kit;
using Selficient.TestKit;

namespace Selficient.Web.Tests.Pages;

public sealed partial class AdminCatalogueTests : IDisposable
{
    private const string Page = "/admin/catalogue";

    private readonly IPageReader _reader = Substitute.For<IPageReader>();
    private readonly SelficientWebApplicationFactory _factory;

    public AdminCatalogueTests()
    {
        // Every link lands, as far as the reader is concerned: the verifier must never reach a real shop from a test.
        _reader.ReadAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(call => new PageRead(200, "Shop", "Petzl Ledlenser Roberts Water Lifesystems Kidde Campingaz Ugreen Anker Bluetti EcoFlow Jackery Honda Hyundai Mountain Warehouse Highlander Vango Energizer Duracell Panasonic Eneloop Bosch Makita Karcher Tesco Costco Wickes Toolstation Screwfix Halfords Argos Amazon Safelincs Draper Silverline Stanley Ultimate Snugpak Thermarest Rab Jerry Cans Trangia Coleman Primus still water multipack bottled tins candles matches lighter headtorch lantern radio blanket extinguisher stove gas cartridge power bank station battery solar panel inverter generator jerry can wind-up crank torch first aid kit tarp rope duct tape sleeping bag hot water bottle thermal flask storage container crate carrier", call.Arg<Uri>()));
        _factory = new SelficientWebApplicationFactory()
            .Configuring(b => b.ConfigureTestServices(s => s.AddSingleton(_reader)));
    }

    [Fact]
    public async Task The_operators_hands_hide_show_and_verify_and_the_pages_follow()
    {
        using HttpClient client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await ReadyAsync();
        string review = await SignInAsync(client);
        string token = Token().Match(review).Groups["token"].Value;
        ICatalogueStore store = _factory.Services.GetRequiredService<ICatalogueStore>();
        Offer offer = (await store.ShownAsync(CancellationToken.None))[0];
        IKitCatalogue catalogue = _factory.Services.GetRequiredService<IKitCatalogue>();
        int before = catalogue.PicksFor(offer.Action).Count;
        await store.RecordClicksAsync([new ClickBatch(offer.Id, DateOnly.FromDateTime(DateTime.UtcNow), 1)], CancellationToken.None);
        string withClick = await client.GetStringAsync(new Uri(Page, UriKind.Relative));

        using HttpResponseMessage hidden = await client.PostAsync(new Uri(Page + $"?handler=Hide&id={offer.Id}", UriKind.Relative), Form(token));
        string afterHide = await hidden.Content.ReadAsStringAsync();
        int whileHidden = catalogue.PicksFor(offer.Action).Count;
        using HttpResponseMessage shown = await client.PostAsync(new Uri(Page + $"?handler=Show&id={offer.Id}", UriKind.Relative), Form(token));
        string afterShow = await shown.Content.ReadAsStringAsync();
        using HttpResponseMessage missing = await client.PostAsync(new Uri(Page + "?handler=Hide&id=987654321", UriKind.Relative), Form(token));
        string afterMissing = await missing.Content.ReadAsStringAsync();
        using HttpResponseMessage verified = await client.PostAsync(new Uri(Page + "?handler=Verify", UriKind.Relative), Form(token));
        string afterVerify = await verified.Content.ReadAsStringAsync();

        withClick.ShouldContain("<td class=\"data\">1</td>", customMessage: "the click shows against its offer");
        afterHide.ShouldContain($"{offer.Name} is hidden.");
        afterHide.ShouldContain("data-status=\"hidden\"");
        whileHidden.ShouldBe(before - 1, "the plan page stops showing it at once");
        afterShow.ShouldContain($"{offer.Name} is shown.");
        afterShow.ShouldNotContain("data-status=\"hidden\"");
        catalogue.PicksFor(offer.Action).Count.ShouldBe(before);
        afterMissing.ShouldContain("There is no such offer.");
        VerifyNotice().IsMatch(afterVerify).ShouldBeTrue(afterVerify.Length > 400 ? afterVerify[..400] : afterVerify);
        afterVerify.ShouldNotContain("Last verified never.");
        afterVerify.ShouldContain("Landed (fake):", customMessage: "the last check sits on every row");
        await _reader.Received().ReadAsync(offer.Link, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Signing_out_closes_the_review_and_a_hand_without_a_session_is_not_found()
    {
        using HttpClient client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await ReadyAsync();
        string review = await SignInAsync(client);
        string token = Token().Match(review).Groups["token"].Value;

        using HttpResponseMessage signedOut = await client.PostAsync(new Uri("/admin/sign-out", UriKind.Relative), Form(token));
        string afterSignOut = await signedOut.Content.ReadAsStringAsync();
        using HttpResponseMessage hide = await client.PostAsync(new Uri(Page + "?handler=Hide&id=1", UriKind.Relative), Form(token));
        using HttpResponseMessage verify = await client.PostAsync(new Uri(Page + "?handler=Verify", UriKind.Relative), Form(token));
        using HttpResponseMessage forged = await client.SendAsync(WithCookie(Page, "selficient.operator=forged-value"));

        afterSignOut.ShouldContain("Sign in", customMessage: "signing out lands on the sign-in");
        afterSignOut.ShouldNotContain("review__offers");
        // This client follows redirects, so a refused hand shows up as having been taken to the
        // sign-in rather than as a 302.
        hide.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/admin/sign-in", "a hand with no session is sent to the sign-in");
        verify.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/admin/sign-in");
        (await forged.Content.ReadAsStringAsync()).ShouldContain("Sign in", customMessage: "a forged cookie is a signed-out page");
    }

    private async Task<string> SignInAsync(HttpClient client)
    {
        await _factory.CreateOperatorAsync();
        string review = await _factory.SignInAsync(client);
        review.ShouldContain("review__offers", customMessage: $"sign-in must open the review; it answered the page titled {Regex.Match(review, "<title>(?<t>[^<]*)</title>").Groups["t"].Value}");
        return review;
    }

    [Fact]
    public async Task A_readers_proposal_waits_at_the_foot_of_the_table_and_is_shown_only_once_it_has_a_price()
    {
        await ReadyAsync();
        using HttpClient client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        ICatalogueStore store = _factory.Services.GetRequiredService<ICatalogueStore>();
        long id = (await store.ProposeAsync(Selficient.Domain.Plans.ActionId.From("headtorches"), new Uri("https://www.argos.co.uk/product/123"), "argos.co.uk", DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None))!.Value;
        string review = await SignInAsync(client);
        string token = Token().Match(review).Groups["token"].Value;

        using HttpResponseMessage priceless = await client.PostAsync(new Uri(Page + $"?handler=Show&id={id}", UriKind.Relative), Form(token));
        string afterPriceless = await priceless.Content.ReadAsStringAsync();
        await store.DescribeAsync(id, "Petzl Tikkina head torch | Argos", Selficient.Domain.Measures.Money.FromPence(1999), DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);
        using HttpResponseMessage shown = await client.PostAsync(new Uri(Page + $"?handler=Show&id={id}", UriKind.Relative), Form(token));
        string afterShown = await shown.Content.ReadAsStringAsync();

        review.ShouldContain("One suggestion from a reader waits for you at the foot of the table");
        review.ShouldContain("data-status=\"proposed\"");
        review.ShouldContain("Suggested link");
        afterPriceless.ShouldContain("Suggested link has no price yet, so it cannot be shown. Wait for the checker to read one, or hide it.");
        afterShown.ShouldContain("Petzl Tikkina head torch | Argos is shown.");
        afterShown.ShouldContain("No suggestions from readers wait for you.");
        (await store.FindAsync(id, CancellationToken.None))!.Status.ShouldBe(OfferStatus.Live);
        _factory.Services.GetRequiredService<IKitCatalogue>().PicksFor(Selficient.Domain.Plans.ActionId.From("headtorches")).ShouldContain(p => p.OfferId == id, "an approved proposal is on the plan at once");
        Selficient.Web.Pages.Admin.CatalogueModel.ProposalsSentence(2).ShouldStartWith("2 suggestions from readers wait for you");
    }

    [Fact]
    public async Task The_reviewer_shows_readiness_the_last_runs_and_the_last_hands_and_every_hand_is_recorded()
    {
        await ReadyAsync();
        using HttpClient client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        string review = await SignInAsync(client);
        string token = Token().Match(review).Groups["token"].Value;
        ICatalogueStore store = _factory.Services.GetRequiredService<ICatalogueStore>();
        Offer offer = (await store.ShownAsync(CancellationToken.None))[0];
        await _factory.Services.GetRequiredService<Selficient.Infrastructure.Catalogue.CatalogueWorker>().WakeAsync(CancellationToken.None);

        using HttpResponseMessage hidden = await client.PostAsync(new Uri(Page + $"?handler=Hide&id={offer.Id}", UriKind.Relative), Form(token));
        string afterHide = await hidden.Content.ReadAsStringAsync();
        IReadOnlyList<Selficient.Application.Operations.OperatorAction> hands = await _factory.Services.GetRequiredService<Selficient.Application.Operations.IOperationsLog>().RecentHandsAsync(10, CancellationToken.None);
        using HttpResponseMessage signedOut = await client.PostAsync(new Uri(Page + "?handler=SignOut", UriKind.Relative), Form(token));

        afterHide.ShouldContain("<li data-check=\"database\" data-status=\"healthy\"><strong>database</strong>: Healthy. The database answers and holds");
        afterHide.ShouldContain("data-check=\"catalogue\" data-status=\"healthy\"");
        afterHide.ShouldContain("data-check=\"basemap\" data-status=\"degraded\"", customMessage: "the test host has no basemap archive and says so to the operator");
        afterHide.ShouldContain("<li data-outcome=\"succeeded\">");
        afterHide.ShouldContain("wake: succeeded,");
        // Signing in and out are not hands on the catalogue any more; they are sign-in attempts,
        // kept with the account that made them. A hand is a change to the catalogue, and it now
        // names the operator who made it.
        afterHide.ShouldContain($"rob, hidden on offer {offer.Id}: {offer.Name} is hidden.");
        hands.Select(h => h.Hand).ShouldBe(["hidden"]);
        hands[0].OfferId.ShouldBe(offer.Id);
        hands[0].OperatorName.ShouldBe("rob");
        IReadOnlyList<Selficient.Application.Security.SignInAttempt> attempts =
            await _factory.Services.GetRequiredService<Selficient.Application.Security.IOperatorStore>().AttemptsAsync(10, CancellationToken.None);
        attempts.ShouldContain(a => a.Outcome == "code" && a.Username == "rob");
    }

    [Fact]
    public void The_operations_lines_are_decided_in_one_place()
    {
        DateTimeOffset at = new(2026, 10, 31, 2, 10, 0, TimeSpan.Zero);
        Selficient.Application.Operations.JobRun running = new(1, "wake", at, null, Selficient.Application.Operations.JobOutcome.Running, "");
        Selficient.Application.Operations.JobRun done = new(2, "wake", at, at.AddMinutes(3), Selficient.Application.Operations.JobOutcome.Succeeded, "Verified 38.");

        Selficient.Web.Pages.Admin.CatalogueModel.RunLine(running).ShouldEndWith("wake: running, still running.");
        Selficient.Web.Pages.Admin.CatalogueModel.RunLine(done).ShouldContain("wake: succeeded, ");
        Selficient.Web.Pages.Admin.CatalogueModel.RunLine(done).ShouldEndWith(". Verified 38.");
        Selficient.Web.Pages.Admin.CatalogueModel.HandLine(new(at, "hide", 7, "Hidden.", "rob")).ShouldEndWith(" rob, hide on offer 7: Hidden.");
        Selficient.Web.Pages.Admin.CatalogueModel.HandLine(new(at, "verify", null, "Opened 38.", "rob")).ShouldEndWith(" rob, verify: Opened 38.");
        Selficient.Web.Pages.Admin.CatalogueModel.HandLine(new(at, "verify", null, "Opened 38.", null)).ShouldEndWith(" nobody recorded, verify: Opened 38.", customMessage: "a hand from before accounts existed says so rather than inventing a name");
        Selficient.Web.Pages.Admin.CatalogueModel.HandLine(new(at, "verify", null, "Opened 38.", " ")).ShouldContain("nobody recorded");
        Should.Throw<ArgumentNullException>(() => Selficient.Web.Pages.Admin.CatalogueModel.RunLine(null!));
        Should.Throw<ArgumentNullException>(() => Selficient.Web.Pages.Admin.CatalogueModel.HandLine(null!));
        Selficient.Web.Pages.Admin.CatalogueModel.AttemptLine(new(at, "rob", "code", "Signed in.", "203.0.113.7", "a browser"))
            .ShouldEndWith(" rob, code, from 203.0.113.7: Signed in.", customMessage: "an operator is staff, so the trail says where from");
        Selficient.Web.Pages.Admin.CatalogueModel.AttemptLine(new(at, "rob", "code", "Signed in."))
            .ShouldEndWith(" rob, code, from no address recorded: Signed in.");
        Selficient.Web.Pages.Admin.CatalogueModel.AttemptLine(new(at, "rob", "code", "Signed in.", " "))
            .ShouldContain("no address recorded");
        Should.Throw<ArgumentNullException>(() => Selficient.Web.Pages.Admin.CatalogueModel.AttemptLine(null!));
    }

    [Fact]
    public void The_rows_phrases_are_decided_in_one_place()
    {
        Offer live = new(1, Selficient.Domain.Plans.ActionId.From("headtorches"), "Petzl Tikkina", "Amazon", Selficient.Domain.Measures.Money.FromPence(2200), new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=Petzl"), "Why.", OfferStatus.Live, 0);
        OfferCheck check = new(new DateTimeOffset(2026, 10, 31, 2, 10, 0, TimeSpan.Zero), Verdict.Landed, 200, "The page answered and names the item.", null, "rule");

        new Selficient.Web.Pages.Admin.CatalogueModel.OfferRow(live, null, 0).StatusText.ShouldBe("Live");
        new Selficient.Web.Pages.Admin.CatalogueModel.OfferRow(live with { Status = OfferStatus.Failing, Failures = 1 }, null, 0).StatusText.ShouldBe("Failing, 1 failed");
        new Selficient.Web.Pages.Admin.CatalogueModel.OfferRow(live, null, 0).CheckText.ShouldBe("Not yet checked.");
        new Selficient.Web.Pages.Admin.CatalogueModel.OfferRow(live, check, 0).CheckText.ShouldBe("Landed (rule): The page answered and names the item.");
        new Selficient.Web.Pages.Admin.CatalogueModel.OfferRow(live, null, 0).CanShow.ShouldBeFalse();
        new Selficient.Web.Pages.Admin.CatalogueModel.OfferRow(live with { Status = OfferStatus.Hidden }, null, 0).CanShow.ShouldBeTrue();
        new Selficient.Web.Pages.Admin.CatalogueModel.OfferRow(live with { Status = OfferStatus.Proposed }, null, 0).CanShow.ShouldBeTrue();
        new Selficient.Web.Pages.Admin.CatalogueModel.OfferRow(live, null, 0).Hand.ShouldBe("Hide");
        new Selficient.Web.Pages.Admin.CatalogueModel.OfferRow(live with { Status = OfferStatus.Hidden }, null, 0).Hand.ShouldBe("Show");
        Selficient.Web.Pages.Admin.CatalogueModel.LastVerifiedSentence(null).ShouldBe("never");
        Selficient.Web.Pages.Admin.CatalogueModel.LastVerifiedSentence(new DateTimeOffset(2026, 10, 31, 2, 10, 0, TimeSpan.Zero)).ShouldStartWith("31 Oct 2026 ");
    }

    /// <summary>The worker seeds the store off the request path; a test that reads the store waits for the seed.</summary>
    private Task ReadyAsync() => _factory.Services.GetRequiredService<CatalogueOperations>().ReloadAsync(CancellationToken.None);

    private static async Task<string> TokenFrom(HttpClient client, string page)
    {
        string html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        string token = Token().Match(html).Groups["token"].Value;
        token.ShouldNotBeEmpty();
        return token;
    }

    private static FormUrlEncodedContent Form(string token, params (string Name, string Value)[] fields)
    {
        Dictionary<string, string> body = new() { ["__RequestVerificationToken"] = token };
        foreach ((string name, string value) in fields)
        {
            body[name] = value;
        }

        return new FormUrlEncodedContent(body);
    }

    private static HttpRequestMessage WithCookie(string page, string cookie)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(page, UriKind.Relative));
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static int OffersOnPage(string html) => Regex.Count(html, "<tr data-status=\"");

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();

    [GeneratedRegex(@"Opened \d+: \d+ landed, \d+ behind a robot wall, \d+ hidden\.")]
    private static partial Regex VerifyNotice();

    public void Dispose() => _factory.Dispose();
}
