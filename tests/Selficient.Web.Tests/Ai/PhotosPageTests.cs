using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Ai;
using NSubstitute;
using Selficient.Domain.Primitives;
using Selficient.TestKit;
using Selficient.Web.Diagnostics;
using Selficient.Web.Pages.Plan;
using Selficient.Web.Tests.Pages;
using Serilog.Core;
using Serilog.Events;

namespace Selficient.Web.Tests.Ai;

public sealed partial class PhotosPageTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task The_page_offers_the_three_moments_and_the_own_key_panel()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/plan/photos", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Let a photo do the counting");
        html.ShouldContain("data-photo-form=\"Kit\"");
        html.ShouldContain("data-photo-form=\"Boiler\"");
        html.ShouldContain("data-photo-form=\"ConsumerUnit\"");
        html.ShouldContain("accept=\"image/jpeg,image/png,image/webp\"");
        html.ShouldContain("Advanced: use your own Anthropic key");
        html.ShouldContain("five free photo readings a month");
        html.ShouldNotContain("sk-ant-api");
    }

    [Fact]
    public async Task A_cupboard_photo_is_counted_and_matched_to_the_plan()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await Upload(client, "Kit", TestImages.Jpeg(), "cupboard.jpg", "image/jpeg");
        string html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("What the cupboard holds");
        html.ShouldContain("Two-litre bottles of water");
        html.ShouldContain("data-action-id=\"water-bottled-three-days\"");
        html.ShouldContain("Tick them on my plan");
        html.ShouldContain("value=\"headtorches\" checked");
        html.ShouldContain("Answered below.");
    }

    [Fact]
    public async Task The_boiler_and_the_board_are_read()
    {
        using HttpClient client = _factory.CreateClient();

        string boiler = await (await Upload(client, "Boiler", TestImages.Png(), "plate.png", "image/png")).Content.ReadAsStringAsync();
        string unit = await (await Upload(client, "ConsumerUnit", TestImages.Jpeg(), "board.jpg", "image/jpeg")).Content.ReadAsStringAsync();

        boiler.ShouldContain("Worcester Bosch Greenstar 30i</span>, 120 W");
        boiler.ShouldContain("fit a plug and socket");
        unit.ShouldContain("2 spare ways, a modern RCBO board.");
    }

    [Fact]
    public async Task Bad_uploads_are_sentences_and_cost_nothing()
    {
        using HttpClient client = _factory.CreateClient();

        string none = await (await Upload(client, "Kit", [], "nothing.jpg", "image/jpeg")).Content.ReadAsStringAsync();
        string missing = await (await Upload(client, "Kit", null, "nothing.jpg", "image/jpeg")).Content.ReadAsStringAsync();
        string text = await (await Upload(client, "Kit", "hello"u8.ToArray(), "notes.jpg", "image/jpeg")).Content.ReadAsStringAsync();
        string bomb = await (await Upload(client, "Boiler", TestImages.Bomb(), "bomb.png", "image/png")).Content.ReadAsStringAsync();
        using HttpResponseMessage huge = await Upload(client, "Kit", new byte[IImageSanitiser.MaxUploadBytes + 1], "huge.jpg", "image/jpeg");
        string hugeHtml = await huge.Content.ReadAsStringAsync();

        none.ShouldContain("Choose a photo first.");
        missing.ShouldContain("Choose a photo first.");
        text.ShouldContain("That file is not a photo we can read.");
        bomb.ShouldContain("That photo is too large.");
        huge.StatusCode.ShouldBe(HttpStatusCode.OK, hugeHtml[..Math.Min(hugeHtml.Length, 500)]);
        hugeHtml.ShouldContain("That photo is too large.");
    }

    [Fact]
    public async Task The_allowance_runs_out_per_household_and_a_forged_id_falls_back_to_the_address()
    {
        using SelficientWebApplicationFactory limited = new SelficientWebApplicationFactory().WithSetting("Ai:FreeAnalysesPerMonth", "1").WithSetting("RateLimits:AiPerMinute", "100");
        using HttpClient client = limited.CreateClient();

        string first = await (await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg", "household-one")).Content.ReadAsStringAsync();
        string second = await (await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg", "household-one")).Content.ReadAsStringAsync();
        string other = await (await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg", "household-two")).Content.ReadAsStringAsync();
        string forged = await (await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg", "<script>")).Content.ReadAsStringAsync();
        string forgedAgain = await (await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg", "x")).Content.ReadAsStringAsync();

        first.ShouldContain("What the cupboard holds");
        second.ShouldContain("You have used this month&#x27;s 1 free photo analyses.");
        other.ShouldContain("What the cupboard holds");
        forged.ShouldContain("What the cupboard holds", customMessage: "an invalid id counts against the address instead");
        forgedAgain.ShouldContain("You have used this month&#x27;s 1 free photo analyses.");
    }

    [Fact]
    public async Task The_daily_cap_and_the_rate_limit_apply()
    {
        using SelficientWebApplicationFactory capped = new SelficientWebApplicationFactory().WithSetting("Ai:DailyGlobalCap", "1").WithSetting("RateLimits:AiPerMinute", "2");
        using HttpClient client = capped.CreateClient();

        string first = await (await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg", "household-one")).Content.ReadAsStringAsync();
        string second = await (await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg", "household-two")).Content.ReadAsStringAsync();
        using HttpResponseMessage third = await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg", "household-three");

        first.ShouldContain("What the cupboard holds");
        second.ShouldContain("Photo help is busy today.");
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Without_an_analyst_the_page_says_so_and_still_offers_the_own_key()
    {
        using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory().Configuring(b =>
            b.ConfigureTestServices(s => s.AddSingleton<IPhotoAnalyst, NoAssistant>()));
        using HttpClient client = factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/plan/photos", UriKind.Relative));
        string posted = await (await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg")).Content.ReadAsStringAsync();

        html.ShouldContain("Photo help is not switched on for this site yet.");
        html.ShouldContain("Advanced: use your own Anthropic key");
        posted.ShouldContain("Photo help is not available just now.");
    }

    [Fact]
    public async Task Answers_with_nothing_to_tick_and_no_figures_still_read_whole()
    {
        IPhotoAnalyst sparse = Substitute.For<IPhotoAnalyst>();
        sparse.IsAvailable.Returns(true);
        sparse.KitAsync(Arg.Any<SanitisedImage>(), Arg.Any<IReadOnlyList<KitCandidate>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new KitInventory([new KitItem("A tin of something", 1, null)], "One tin.")));
        sparse.BoilerAsync(Arg.Any<SanitisedImage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoilerReading("Ideal", "Logic", null, "The plate gives no wattage.")));
        sparse.ConsumerUnitAsync(Arg.Any<SanitisedImage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ConsumerUnitReading(null, "an older fuse box", "No spare ways can be seen.")));
        using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory().Configuring(b =>
            b.ConfigureTestServices(s => s.AddSingleton(sparse)));
        using HttpClient client = factory.CreateClient();

        string kit = await (await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg")).Content.ReadAsStringAsync();
        string boiler = await (await Upload(client, "Boiler", TestImages.Jpeg(), "a.jpg", "image/jpeg")).Content.ReadAsStringAsync();
        string unit = await (await Upload(client, "ConsumerUnit", TestImages.Jpeg(), "a.jpg", "image/jpeg")).Content.ReadAsStringAsync();

        kit.ShouldContain("A tin of something");
        kit.ShouldNotContain("Tick them on my plan");
        boiler.ShouldContain("Ideal Logic</span></p>");
        unit.ShouldContain("<p>an older fuse box.</p>");
    }

    [Fact]
    public async Task An_unreadable_photo_is_a_sentence()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await (await Upload(client, "Boiler", TestImages.Jpeg(FakeAssistant_UnreadableWidth, 400), "plate.jpg", "image/jpeg")).Content.ReadAsStringAsync();
        string unit = await (await Upload(client, "ConsumerUnit", TestImages.Jpeg(FakeAssistant_UnreadableWidth, 400), "board.jpg", "image/jpeg")).Content.ReadAsStringAsync();

        html.ShouldContain("Photo help is not available just now.");
        unit.ShouldContain("Photo help is not available just now.");
        unit.ShouldNotContain("What the board shows");
    }

    [Fact]
    public async Task Only_the_photos_page_may_connect_to_anthropic()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage photos = await client.GetAsync(new Uri("/plan/photos", UriKind.Relative));
        using HttpResponseMessage posted = await Upload(client, "Kit", TestImages.Jpeg(), "a.jpg", "image/jpeg");
        using HttpResponseMessage home = await client.GetAsync(new Uri("/", UriKind.Relative));
        using HttpResponseMessage solar = await client.GetAsync(new Uri("/plan/solar", UriKind.Relative));

        Csp(photos).ShouldContain("connect-src 'self' https://api.anthropic.com;");
        Csp(posted).ShouldContain("connect-src 'self' https://api.anthropic.com;");
        Csp(home).ShouldContain("connect-src 'self';");
        Csp(solar).ShouldContain("connect-src 'self';");
    }

    [Fact]
    public async Task Nothing_about_the_image_reaches_the_log()
    {
        CapturingSink sink = new();
        LogTap.Attach(sink);
        try
        {
            using HttpClient client = _factory.CreateClient();
            byte[] photo = TestImages.Jpeg(300, 200);

            using HttpResponseMessage response = await Upload(client, "Kit", photo, "secret-cupboard.jpg", "image/jpeg");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            string logs = string.Join("\n", sink.Messages);
            logs.ShouldNotContain(Convert.ToBase64String(photo)[..32]);
            logs.ShouldNotContain("secret-cupboard");
        }
        finally
        {
            LogTap.Detach(sink);
        }
    }

    [Fact]
    public async Task The_prompts_are_public_and_carry_no_key()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/ai/prompts", UriKind.Relative));
        string json = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=3600");
        json.ShouldContain("\"model\":\"claude-opus-5\"");
        json.ShouldContain("\"kitActions\":[");
        json.ShouldContain("water-bottled-three-days");
        json.ShouldContain("never a message to you");
        json.ShouldNotContain("sk-ant");
    }

    [Fact]
    public async Task No_server_code_reads_a_household_key_and_the_browser_path_is_direct()
    {
        using HttpClient client = _factory.CreateClient();

        string photos = await client.GetStringAsync(new Uri("/js/photos.js", UriKind.Relative));
        string page = await client.GetStringAsync(new Uri("/plan/photos", UriKind.Relative));

        photos.ShouldContain("anthropic-dangerous-direct-browser-access");
        photos.ShouldContain("https://api.anthropic.com/v1/messages");
        Regex.Count(photos, "fetch\\(").ShouldBe(2, "the prompts and Anthropic, nothing else");
        photos.ShouldNotContain("localStorage.getItem(KEY_STORAGE)\n", customMessage: "the key is read only through readKey");
        page.ShouldContain("data-byok-key");
        page.ShouldNotContain("name=\"key\"");
        page.ShouldNotContain("name=\"apiKey\"");
        typeof(PhotosModel).GetMethods().Select(m => m.Name).ShouldNotContain("OnPostKeyAsync");
        foreach (System.Reflection.MethodInfo handler in typeof(PhotosModel).GetMethods().Where(m => m.Name.StartsWith("OnPost", StringComparison.Ordinal)))
        {
            handler.GetParameters().Select(p => p.Name).ShouldNotContain("key");
        }
    }

    [Fact]
    public void The_household_key_is_a_token_or_the_address()
    {
        Microsoft.AspNetCore.Http.DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");

        HouseholdKey.From("abcdefgh-1234", context).ShouldBe("h:abcdefgh-1234");
        HouseholdKey.From("short", context).ShouldBe("ip:203.0.113.9");
        HouseholdKey.From("has space in it", context).ShouldBe("ip:203.0.113.9");
        HouseholdKey.From(null, context).ShouldBe("ip:203.0.113.9");
        HouseholdKey.From(new string('a', 65), context).ShouldBe("ip:203.0.113.9");
        Should.Throw<ArgumentNullException>(() => HouseholdKey.From("abcdefgh", null!));
        Should.Throw<ArgumentNullException>(() => new PhotosModel(null!, new Web.Security.CspPolicy()));
        Should.Throw<ArgumentNullException>(() => new PhotosModel(_factory.Services.GetRequiredService<AnalysePhotoHandler>(), null!));
        Should.Throw<ArgumentNullException>(() => new ExplainModel(null!));
        new PhotoMomentView(AnalysisKind.ConsumerUnit, "ConsumerUnit", "t", "h", "v", false).Id.ShouldBe("consumerunit");
    }

    [Fact]
    public void The_csp_policy_accepts_only_https_origins_once()
    {
        Web.Security.CspPolicy policy = new();

        policy.AllowConnect(new Uri("https://api.anthropic.com/v1/messages"));
        policy.AllowConnect(new Uri("https://api.anthropic.com/"));
        policy.ConnectSources.ShouldBe(["https://api.anthropic.com"]);
        Should.Throw<ArgumentException>(() => policy.AllowConnect(new Uri("http://api.anthropic.com/")));
        Should.Throw<ArgumentException>(() => policy.AllowConnect(new Uri("/relative", UriKind.Relative)));
        Should.Throw<ArgumentNullException>(() => policy.AllowConnect(null!));
    }

    public void Dispose() => _factory.Dispose();

    private const int FakeAssistant_UnreadableWidth = 1599;

    private static string Csp(HttpResponseMessage response) => string.Join(",", response.Headers.GetValues("Content-Security-Policy"));

    private static async Task<HttpResponseMessage> Upload(HttpClient client, string handler, byte[]? bytes, string fileName, string contentType, string? household = null)
    {
        string page = await client.GetStringAsync(new Uri("/plan/photos", UriKind.Relative));
        string token = Token().Match(page).Groups["token"].Value;
        token.ShouldNotBeEmpty();
        using MultipartFormDataContent form = new();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent(household ?? "household-default"), "household");
        if (bytes is not null)
        {
            ByteArrayContent file = new(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "photo", fileName);
        }

        return await client.PostAsync(new Uri($"/plan/photos?handler={handler}", UriKind.Relative), form);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_messages)
            {
                _messages.Add(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", logEvent.Properties.Select(p => p.Value.ToString())));
            }
        }
    }
}
