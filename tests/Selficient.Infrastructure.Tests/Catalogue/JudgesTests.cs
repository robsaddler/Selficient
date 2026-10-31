using System.Text.Json;
using Anthropic;
using Selficient.Application.Catalogue;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Infrastructure.Catalogue;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Selficient.Infrastructure.Tests.Catalogue;

public sealed class JudgesTests : IDisposable
{
    private const string Key = "sk-ant-api03-TESTKEY-0123456789abcdefghijklmnop";

    private readonly WireMockServer _server = WireMockServer.Start();

    private static Offer AnOffer(string name = "Petzl Tikkina headtorch") =>
        new(1, ActionId.From("headtorches"), name, "Amazon", Money.FromPence(2200), new DateOnly(2026, 10, 29), new Uri("https://www.amazon.co.uk/s?k=Petzl+Tikkina"), "Why.", OfferStatus.Live, 0);

    private static PageRead Page(string text, string title = "Search") => new(200, title, text, new Uri("https://www.amazon.co.uk/s?k=Petzl+Tikkina"));

    private AnthropicOfferJudge Judge() =>
        new(new AnthropicClient { ApiKey = Key, BaseUrl = _server.Url!, Timeout = TimeSpan.FromSeconds(30), MaxRetries = 0 }, "claude-opus-5");

    private static string Answer(string json) =>
        """{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":20},"content":[{"type":"text","text":""" + JsonSerializer.Serialize(json) + "}]}";

    private void Answers(string json) =>
        _server.Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Answer(json)));

    [Fact]
    public async Task With_no_model_the_rules_stand()
    {
        NoJudge judge = new();

        judge.IsAvailable.ShouldBeFalse();
        Judgement judgement = await judge.JudgeAsync(AnOffer(), Page("anything"), CancellationToken.None);
        judgement.ShouldBe(new Judgement(Verdict.Dead, "No model is configured.", null, "none"));
    }

    [Fact]
    public async Task The_fake_judge_looks_for_the_first_word_and_reads_a_price()
    {
        FakeOfferJudge judge = new();

        judge.IsAvailable.ShouldBeTrue();
        (await judge.JudgeAsync(AnOffer(), Page("PETZL Tikkina headtorch £22.50 in stock"), CancellationToken.None)).ShouldBe(new Judgement(Verdict.Landed, "The fake judge saw the item named.", Money.FromPence(2250), "fake"));
        (await judge.JudgeAsync(AnOffer(), Page("Petzl only, price on request"), CancellationToken.None)).PriceSeen.ShouldBeNull("no pound sign, no price");
        (await judge.JudgeAsync(AnOffer(), Page("Petzl £"), CancellationToken.None)).PriceSeen.ShouldBeNull("a pound sign at the end has no number after it");
        (await judge.JudgeAsync(AnOffer(), Page("Petzl £abc"), CancellationToken.None)).PriceSeen.ShouldBeNull("letters are not a price");
        (await judge.JudgeAsync(AnOffer(), Page("Petzl £0.00"), CancellationToken.None)).PriceSeen.ShouldBeNull("nought is not a shop price");
        (await judge.JudgeAsync(AnOffer("A"), Page("nothing at all"), CancellationToken.None)).Verdict.ShouldBe(Verdict.Landed, "no distinctive word means nothing can be missing");
        (await judge.JudgeAsync(AnOffer(), Page("garden hose £9.99"), CancellationToken.None)).ShouldBe(new Judgement(Verdict.WrongItem, "The fake judge saw nothing of the item.", null, "fake"));
        await Should.ThrowAsync<ArgumentNullException>(() => judge.JudgeAsync(null!, Page("x"), CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => judge.JudgeAsync(AnOffer(), null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("landed", Verdict.Landed)]
    [InlineData("wrong_item", Verdict.WrongItem)]
    [InlineData("no_results", Verdict.NoResults)]
    [InlineData("robot_wall", Verdict.RobotWall)]
    [InlineData("dead", Verdict.Dead)]
    [InlineData("something_else", Verdict.Dead)]
    public async Task The_model_answers_in_the_schema_and_its_verdict_is_read(string said, Verdict verdict)
    {
        Answers($$"""{"verdict":"{{said}}","why":"Because the page says so.","price_pence":2199,"in_stock":true}""");

        Judgement judgement = await Judge().JudgeAsync(AnOffer(), Page("Petzl Tikkina headtorch £21.99", "Petzl Tikkina"), CancellationToken.None);

        judgement.Verdict.ShouldBe(verdict);
        judgement.Why.ShouldBe("Because the page says so.");
        judgement.PriceSeen.ShouldBe(Money.FromPence(2199));
        judgement.JudgedBy.ShouldBe("model");
    }

    [Fact]
    public async Task The_question_carries_the_recommendation_the_page_and_the_schema_and_clips_a_long_page()
    {
        Answers("""{"verdict":"landed","why":"","price_pence":null,"in_stock":null}""");
        string text = new string('x', 12_000) + "TAIL-MARKER";

        Judgement judgement = await Judge().JudgeAsync(AnOffer(), Page(text, "A title"), CancellationToken.None);

        judgement.Why.ShouldBe("The model gave no reason.");
        judgement.PriceSeen.ShouldBeNull();
        WireMock.IRequestMessage request = _server.LogEntries.Single().RequestMessage.ShouldNotBeNull();
        request.Headers!["Authorization"].Single().ShouldBe("Bearer " + Key);
        string body = request.Body.ShouldNotBeNull();
        body.ShouldContain("Recommendation: Petzl Tikkina headtorch, sold by Amazon, about ");
        body.ShouldContain("22.00.");
        body.ShouldContain("Page title: A title");
        body.ShouldContain("\"type\":\"json_schema\"");
        body.ShouldContain("\"effort\":\"low\"");
        body.ShouldContain("\"cache_control\"");
        body.ShouldContain("Answer only from the page.");
        body.ShouldNotContain("TAIL-MARKER", customMessage: "the page is clipped before it is sent");
        body.ShouldNotContain(Key);
    }

    [Fact]
    public async Task A_long_reason_is_clipped_and_a_silly_price_is_dropped()
    {
        Answers($$"""{"verdict":"landed","why":"{{new string('w', 300)}}","price_pence":5000000,"in_stock":true}""");

        Judgement judgement = await Judge().JudgeAsync(AnOffer(), Page("Petzl"), CancellationToken.None);

        judgement.Why.Length.ShouldBe(240);
        judgement.PriceSeen.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"why":"no verdict","price_pence":1,"in_stock":true}""")]
    [InlineData("""{"verdict":null,"why":"null verdict","price_pence":1,"in_stock":true}""")]
    [InlineData("   ")]
    public async Task A_model_that_gives_no_verdict_has_not_answered(string json)
    {
        Answers(json);

        Judgement judgement = await Judge().JudgeAsync(AnOffer(), Page("Petzl"), CancellationToken.None);

        judgement.ShouldBe(new Judgement(Verdict.Dead, "The model did not answer.", null, "model"));
    }

    [Fact]
    public async Task A_failed_call_an_empty_answer_or_a_malformed_one_is_no_answer()
    {
        _server.Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(529).WithBody("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}"""));
        Judgement overloaded = await Judge().JudgeAsync(AnOffer(), Page("Petzl"), CancellationToken.None);

        _server.Reset();
        _server.Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":20},"content":[]}"""));
        Judgement empty = await Judge().JudgeAsync(AnOffer(), Page("Petzl"), CancellationToken.None);

        _server.Reset();
        Answers("not json at all");
        Judgement malformed = await Judge().JudgeAsync(AnOffer(), Page("Petzl"), CancellationToken.None);

        overloaded.Verdict.ShouldBe(Verdict.Dead);
        overloaded.Why.ShouldBe("The model did not answer.");
        empty.Why.ShouldBe("The model did not answer.");
        malformed.Why.ShouldBe("The model did not answer.");
    }

    [Fact]
    public async Task The_model_judge_checks_its_arguments()
    {
        Should.Throw<ArgumentNullException>(() => new AnthropicOfferJudge(null!, "claude-opus-5"));
        Should.Throw<ArgumentException>(() => new AnthropicOfferJudge(new AnthropicClient { ApiKey = Key }, ""));
        AnthropicOfferJudge judge = Judge();
        judge.IsAvailable.ShouldBeTrue();
        await Should.ThrowAsync<ArgumentNullException>(() => judge.JudgeAsync(null!, Page("x"), CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => judge.JudgeAsync(AnOffer(), null!, CancellationToken.None));
    }

    [Fact]
    public void The_answer_shape_round_trips_including_stock()
    {
        JudgeAnswer answer = JsonSerializer.Deserialize("""{"verdict":"landed","why":"Seen.","price_pence":2199,"in_stock":false}""", CatalogueAiJson.Default.JudgeAnswer)!;

        answer.ShouldBe(new JudgeAnswer("landed", "Seen.", 2199, false));
        answer.InStock.ShouldBe(false);
    }

    public void Dispose() => _server.Dispose();
}
