using System.ComponentModel.DataAnnotations;

namespace Selficient.Infrastructure.Ai;

/// <summary>Spend control for the free tier. Both caps are counts, which is what the bill follows.</summary>
public sealed class AiOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Ai";

    /// <summary>Free analyses each household gets a calendar month.</summary>
    [Range(0, 1_000)]
    public int FreeAnalysesPerMonth { get; init; } = 5;

    /// <summary>Analyses the whole site will run in a day before it says it is busy.</summary>
    [Range(1, 1_000_000)]
    public int DailyGlobalCap { get; init; } = 500;
}

/// <summary>The Anthropic key and model. Absent key means the feature is absent, never an error.</summary>
public sealed class AnthropicOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Anthropic";

    /// <summary>The API key, from the environment only.</summary>
    [RegularExpression("^sk-ant-[A-Za-z0-9_-]{20,200}$", ErrorMessage = "Anthropic:ApiKey does not look like an Anthropic API key.")]
    public string? ApiKey { get; init; }

    /// <summary>The model. The spec decided Claude Opus 5 at low effort.</summary>
    [Required]
    [RegularExpression("^claude-[a-z0-9-]{3,60}$", ErrorMessage = "Anthropic:Model does not look like a Claude model id.")]
    public string Model { get; init; } = "claude-opus-5";

    /// <summary>Where the API lives; https, or a loopback stub in a test.</summary>
    [Required]
    public Uri BaseUrl { get; init; } = new("https://api.anthropic.com/");

    /// <summary>Longest a single analysis may take.</summary>
    [Range(5, 180)]
    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>True when a key is set.</summary>
    public bool HasKey => !string.IsNullOrEmpty(ApiKey);
}
