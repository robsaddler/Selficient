using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Anthropic;
using Anthropic.Models.Messages;
using Selficient.Application.Ai;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Ai;

/// <summary>
/// Claude through the official SDK. One request per analysis: a system prompt (cached), one image
/// or one block of plan text, a JSON schema the answer must fit, low effort. The answer is parsed
/// into strict records and range-checked again here, because a schema says what shape the words
/// take, not whether an action id is one of ours. Nothing about the image is ever logged.
/// </summary>
public sealed class AnthropicAssistant : IPhotoAnalyst, IPlanNarrator
{
    /// <summary>Enough for any of the four answers; the schema keeps them short anyway.</summary>
    public const int MaxTokens = 2_000;

    private readonly AnthropicClient _client;
    private readonly string _model;

    /// <summary>Creates the adapter over a configured client.</summary>
    public AnthropicAssistant(AnthropicClient client, string model)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(model);
        _client = client;
        _model = model;
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public async Task<Result<KitInventory>> KitAsync(SanitisedImage image, IReadOnlyList<KitCandidate> candidates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(candidates);
        string? answer = await AskAsync(AnalysisPrompts.KitSystem(candidates), AnalysisPrompts.KitSchema, ImageTurn(image, AnalysisKind.Kit), cancellationToken);
        KitAnswer? parsed = Parse(answer, AiJson.Default.KitAnswer);
        if (parsed?.Items is null || parsed.Summary is null)
        {
            return Result.Failure<KitInventory>(DomainErrors.AiUnavailable());
        }

        HashSet<string> known = [.. candidates.Select(static c => c.Id.Value)];
        List<KitItem> items = [];
        foreach (KitItemAnswer item in parsed.Items.Take(AnalysisPrompts.MaxItems))
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Count is < 1 or > 999)
            {
                continue;
            }

            ActionId? action = item.Action is not null && known.Contains(item.Action) ? ActionId.From(item.Action) : null;
            items.Add(new KitItem(Clip(item.Name, 80), item.Count, action));
        }

        return Result.Success(new KitInventory(items, Clip(parsed.Summary, AnalysisPrompts.MaxTextLength)));
    }

    /// <inheritdoc />
    public async Task<Result<BoilerReading>> BoilerAsync(SanitisedImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        string? answer = await AskAsync(AnalysisPrompts.BoilerSystem, AnalysisPrompts.BoilerSchema, ImageTurn(image, AnalysisKind.Boiler), cancellationToken);
        BoilerAnswer? parsed = Parse(answer, AiJson.Default.BoilerAnswer);
        if (parsed?.Advice is null)
        {
            return Result.Failure<BoilerReading>(DomainErrors.AiUnavailable());
        }

        int? watts = parsed.Watts is (>= 1 and <= 5000) ? parsed.Watts : null;
        return Result.Success(new BoilerReading(Clip(parsed.Make ?? string.Empty, 60), Clip(parsed.Model ?? string.Empty, 60), watts, Clip(parsed.Advice, AnalysisPrompts.MaxTextLength)));
    }

    /// <inheritdoc />
    public async Task<Result<ConsumerUnitReading>> ConsumerUnitAsync(SanitisedImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        string? answer = await AskAsync(AnalysisPrompts.ConsumerUnitSystem, AnalysisPrompts.ConsumerUnitSchema, ImageTurn(image, AnalysisKind.ConsumerUnit), cancellationToken);
        ConsumerUnitAnswer? parsed = Parse(answer, AiJson.Default.ConsumerUnitAnswer);
        if (parsed?.Advice is null)
        {
            return Result.Failure<ConsumerUnitReading>(DomainErrors.AiUnavailable());
        }

        int? spare = parsed.SpareWays is (>= 0 and <= 40) ? parsed.SpareWays : null;
        return Result.Success(new ConsumerUnitReading(spare, Clip(parsed.Kind ?? string.Empty, 80), Clip(parsed.Advice, AnalysisPrompts.MaxTextLength)));
    }

    /// <inheritdoc />
    public async Task<Result<PlanNarrative>> NarrateAsync(Plan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        List<ContentBlockParam> turn = [new TextBlockParam { Text = AnalysisPrompts.PlanAsText(plan) }];
        string? answer = await AskAsync(AnalysisPrompts.NarrativeSystem, AnalysisPrompts.NarrativeSchema, turn, cancellationToken);
        NarrativeAnswer? parsed = Parse(answer, AiJson.Default.NarrativeAnswer);
        if (parsed?.Paragraphs is not { Count: > 0 })
        {
            return Result.Failure<PlanNarrative>(DomainErrors.AiUnavailable());
        }

        List<string> paragraphs = parsed.Paragraphs.Where(static p => !string.IsNullOrWhiteSpace(p)).Take(6).Select(static p => Clip(p, 900)).ToList();
        return paragraphs.Count == 0
            ? Result.Failure<PlanNarrative>(DomainErrors.AiUnavailable())
            : Result.Success(new PlanNarrative(paragraphs));
    }

    private static List<ContentBlockParam> ImageTurn(SanitisedImage image, AnalysisKind kind) =>
    [
        new ImageBlockParam { Source = new Base64ImageSource { MediaType = SanitisedImage.MediaType, Data = Convert.ToBase64String(image.Jpeg.Span) } },
        new TextBlockParam { Text = AnalysisPrompts.UserTurn(kind) },
    ];

    /// <summary>One call. Any failure, from a timeout to a refusal, is null, and the caller turns null into a sentence.</summary>
    private async Task<string?> AskAsync(string system, string schema, List<ContentBlockParam> turn, CancellationToken cancellationToken)
    {
        try
        {
            Message response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = MaxTokens,
                System = new List<TextBlockParam> { new() { Text = system, CacheControl = new CacheControlEphemeral() } },
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = new OutputConfig
                {
                    Effort = Effort.Low,
                    Format = new JsonOutputFormat { Schema = JsonSerializer.Deserialize(schema, AiJson.Default.DictionaryStringJsonElement)! },
                },
                Messages = [new UserMessageParam { Content = turn }],
            }, cancellationToken);

            return response.Content.Select(static b => b.Value).OfType<TextBlock>().Select(static t => t.Text).FirstOrDefault();
        }
        catch (Exception)
        {
            // A refusal, a rate limit, a timeout, a dropped connection or an unexpected shape: the page
            // says the same sentence for all of them, and the exception type is the SDK's business.
            return null;
        }
    }

    private static T? Parse<T>(string? answer, JsonTypeInfo<T> type)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(answer, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Clip(string text, int max)
    {
        string trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

/// <summary>One cupboard item as the model wrote it.</summary>
public sealed record KitItemAnswer([property: JsonPropertyName("name")] string? Name, [property: JsonPropertyName("count")] int Count, [property: JsonPropertyName("action")] string? Action);

/// <summary>The cupboard answer.</summary>
public sealed record KitAnswer([property: JsonPropertyName("items")] IReadOnlyList<KitItemAnswer>? Items, [property: JsonPropertyName("summary")] string? Summary);

/// <summary>The boiler answer.</summary>
public sealed record BoilerAnswer([property: JsonPropertyName("make")] string? Make, [property: JsonPropertyName("model")] string? Model, [property: JsonPropertyName("watts")] int? Watts, [property: JsonPropertyName("advice")] string? Advice);

/// <summary>The consumer unit answer.</summary>
public sealed record ConsumerUnitAnswer([property: JsonPropertyName("spareWays")] int? SpareWays, [property: JsonPropertyName("kind")] string? Kind, [property: JsonPropertyName("advice")] string? Advice);

/// <summary>The narrative answer.</summary>
public sealed record NarrativeAnswer([property: JsonPropertyName("paragraphs")] IReadOnlyList<string>? Paragraphs);

/// <summary>Source-generated serializer context for the model's answers and the schemas.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(KitAnswer))]
[JsonSerializable(typeof(BoilerAnswer))]
[JsonSerializable(typeof(ConsumerUnitAnswer))]
[JsonSerializable(typeof(NarrativeAnswer))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public sealed partial class AiJson : JsonSerializerContext;
