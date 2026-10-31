using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Messages;
using Selficient.Application.Catalogue;

namespace Selficient.Infrastructure.Catalogue;

/// <summary>No model to ask: the rules' verdict stands.</summary>
public sealed class NoJudge : IOfferJudge
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<Judgement> JudgeAsync(Offer offer, PageRead page, CancellationToken cancellationToken) =>
        Task.FromResult(new Judgement(Verdict.Dead, "No model is configured.", null, "none"));
}

/// <summary>A judge for development and tests: a page that names the item's first word is the item, and a price in the text is read.</summary>
public sealed class FakeOfferJudge : IOfferJudge
{
    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<Judgement> JudgeAsync(Offer offer, PageRead page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(page);
        string[] words = RuleJudge.Words(offer.Name);
        bool named = words.Length == 0 || page.Text.Contains(words[0], StringComparison.OrdinalIgnoreCase);
        int at = page.Text.IndexOf('£', StringComparison.Ordinal);
        long? pence = null;
        if (at >= 0 && at + 1 < page.Text.Length)
        {
            string digits = new(page.Text[(at + 1)..].TakeWhile(static c => char.IsDigit(c) || c == '.').ToArray());
            if (decimal.TryParse(digits, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out decimal pounds))
            {
                pence = (long)(pounds * 100);
            }
        }

        return Task.FromResult(named
            ? new Judgement(Verdict.Landed, "The fake judge saw the item named.", OfferVerifier.Sensible(pence), "fake")
            : new Judgement(Verdict.WrongItem, "The fake judge saw nothing of the item.", null, "fake"));
    }
}

/// <summary>
/// Claude reads the page for the offer and answers one structured question: is this the item, does
/// it meet what the action asks for, what does it cost, is it in stock. It never chooses what a
/// household needs and never invents a price; a price it cannot read is null.
/// </summary>
public sealed class AnthropicOfferJudge : IOfferJudge
{
    private const int MaxTokens = 600;
    private const int MaxPageCharacters = 12_000;

    private const string System = """
        You check whether a shop page is the product a household resilience plan recommends. You are given the
        recommendation (what it is, who sells it, roughly what it should cost) and the text of the page the plan
        links to. Answer only from the page. Decide whether the page shows that product, or a clearly equivalent one
        of the same kind and size, available to buy. Read the price of that product if it is on the page, in pence;
        if the page shows several products, the price of the matching one; if no price is readable, null. Never
        guess a price. Say in one sentence why. Verdicts: "landed" (the product is on the page), "wrong_item" (the
        page is a product or results page but not this product), "no_results" (the shop says it found nothing),
        "robot_wall" (the page is a verification or blocked page), "dead" (an error page or nothing readable).
        """;

    private const string Schema = """
        {"type":"object","additionalProperties":false,"required":["verdict","why","price_pence","in_stock"],
         "properties":{"verdict":{"type":"string","enum":["landed","wrong_item","no_results","robot_wall","dead"]},
                       "why":{"type":"string"},"price_pence":{"type":["integer","null"]},"in_stock":{"type":["boolean","null"]}}}
        """;

    private readonly AnthropicClient _client;
    private readonly string _model;

    /// <summary>Creates the judge over the client.</summary>
    public AnthropicOfferJudge(AnthropicClient client, string model)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(model);
        _client = client;
        _model = model;
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public async Task<Judgement> JudgeAsync(Offer offer, PageRead page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(page);
        string text = page.Text.Length > MaxPageCharacters ? page.Text[..MaxPageCharacters] : page.Text;
        string turn = $"Recommendation: {offer.Name}, sold by {offer.Retailer}{About(offer)}.\nPage title: {page.Title}\nPage text:\n{text}";
        JudgeAnswer? answer = await AskAsync(turn, cancellationToken);
        if (answer?.Verdict is null)
        {
            return new Judgement(Verdict.Dead, "The model did not answer.", null, "model");
        }

        Verdict verdict = answer.Verdict switch
        {
            "landed" => Verdict.Landed,
            "wrong_item" => Verdict.WrongItem,
            "no_results" => Verdict.NoResults,
            "robot_wall" => Verdict.RobotWall,
            _ => Verdict.Dead,
        };
        string why = string.IsNullOrWhiteSpace(answer.Why) ? "The model gave no reason." : answer.Why.Length > 240 ? answer.Why[..240] : answer.Why;
        return new Judgement(verdict, why, OfferVerifier.Sensible(answer.PricePence), "model");
    }

    /// <summary>The price the recommendation carries, or nothing for a proposal that has none yet.</summary>
    internal static string About(Offer offer) => offer.Price.Pence > 0 ? $", about £{offer.Price.Pence / 100m:0.00}" : string.Empty;

    private async Task<JudgeAnswer?> AskAsync(string turn, CancellationToken cancellationToken)
    {
        try
        {
            Message response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = MaxTokens,
                System = new List<TextBlockParam> { new() { Text = System, CacheControl = new CacheControlEphemeral() } },
                OutputConfig = new OutputConfig
                {
                    Effort = Effort.Low,
                    Format = new JsonOutputFormat { Schema = JsonSerializer.Deserialize(Schema, CatalogueAiJson.Default.DictionaryStringJsonElement)! },
                },
                Messages = [new UserMessageParam { Content = new List<ContentBlockParam> { new TextBlockParam { Text = turn } } }],
            }, cancellationToken);
            string? text = response.Content.Select(static b => b.Value).OfType<TextBlock>().Select(static t => t.Text).FirstOrDefault();
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize(text, CatalogueAiJson.Default.JudgeAnswer);
        }
        catch (Exception)
        {
            // A refusal, a rate limit, a timeout or an unexpected shape: the rules' verdict stands.
            return null;
        }
    }
}

/// <summary>The judge's answer as the schema shapes it.</summary>
public sealed record JudgeAnswer(
    [property: JsonPropertyName("verdict")] string? Verdict,
    [property: JsonPropertyName("why")] string? Why,
    [property: JsonPropertyName("price_pence")] long? PricePence,
    [property: JsonPropertyName("in_stock")] bool? InStock);

/// <summary>Source-generated JSON for the judge.</summary>
[JsonSerializable(typeof(JudgeAnswer))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public sealed partial class CatalogueAiJson : JsonSerializerContext;
