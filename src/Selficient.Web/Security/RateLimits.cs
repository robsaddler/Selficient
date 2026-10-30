using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Selficient.Web.Security;

/// <summary>Per-IP fixed windows. Generous for a household, hostile to a script.</summary>
public sealed class RateLimitOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "RateLimits";

    /// <summary>Plan builds per IP per minute.</summary>
    [Range(1, 10_000)]
    public int PlanPerMinute { get; init; } = 30;

    /// <summary>API calls per IP per minute.</summary>
    [Range(1, 10_000)]
    public int ApiPerMinute { get; init; } = 60;

    /// <summary>Board redraws a minute from one client address. A tick, a card or a new dependency is one redraw, so this is generous: the fragment costs a few milliseconds.</summary>
    [Range(1, 10_000)]
    public int BoardPerMinute { get; init; } = 120;

    /// <summary>Photo analyses and narratives per IP per minute. Each one costs real money.</summary>
    [Range(1, 1_000)]
    public int AiPerMinute { get; init; } = 6;

    /// <summary>Map tiles per IP per minute. A view is a dozen tiles and a pan a dozen more.</summary>
    [Range(1, 100_000)]
    public int TilesPerMinute { get; init; } = 600;
}

/// <summary>Registers the limiter and its policies.</summary>
public static class RateLimits
{
    /// <summary>Policy for building a plan.</summary>
    public const string PlanPolicy = "plan";

    /// <summary>Policy for the board fragment, which redraws on every tick.</summary>
    public const string BoardPolicy = "board";

    /// <summary>Policy for JSON endpoints.</summary>
    public const string ApiPolicy = "api";

    /// <summary>Policy for anything that calls the model.</summary>
    public const string AiPolicy = "ai";

    /// <summary>Policy for basemap tiles, which arrive in bursts.</summary>
    public const string TilesPolicy = "tiles";

    /// <summary>Adds the limiter. Rejections carry 429 and no body, so the status-code pages middleware renders the sentence.</summary>
    public static IServiceCollection AddSelficientRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(PlanPolicy, PlanPartition);
            limiter.AddPolicy(ApiPolicy, ApiPartition);
            limiter.AddPolicy(BoardPolicy, BoardPartition);
            limiter.AddPolicy(TilesPolicy, TilesPartition);
            limiter.AddPolicy(AiPolicy, AiPartition);
        });

        return services;
    }

    /// <summary>The partition for the plan policy: one fixed window per client address.</summary>
    internal static RateLimitPartition<string> PlanPartition(HttpContext context) => Window(context, static o => o.PlanPerMinute);

    /// <summary>The partition for the board policy.</summary>
    internal static RateLimitPartition<string> BoardPartition(HttpContext context) => Window(context, static o => o.BoardPerMinute);

    /// <summary>The partition for the API policy.</summary>
    internal static RateLimitPartition<string> ApiPartition(HttpContext context) => Window(context, static o => o.ApiPerMinute);

    /// <summary>The partition for the AI policy.</summary>
    internal static RateLimitPartition<string> AiPartition(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
            ? RateLimitPartition.GetNoLimiter("ai-read")
            : Window(context, static o => o.AiPerMinute);

    /// <summary>The partition for the tiles policy.</summary>
    internal static RateLimitPartition<string> TilesPartition(HttpContext context) => Window(context, static o => o.TilesPerMinute);

    /// <summary>The remote address, or a shared bucket when the server cannot tell (as behind a misconfigured proxy).</summary>
    internal static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> Window(HttpContext context, Func<RateLimitOptions, int> permits)
    {
        RateLimitOptions options = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits(options),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }
}
