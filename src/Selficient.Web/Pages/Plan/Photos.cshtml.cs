using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Ai;
using Selficient.Domain.Primitives;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// The three photo moments. Each is a form that posts one file to its own handler; the result is
/// rendered on the same page. The photo is read into memory, re-encoded, sent to the analyst and
/// forgotten. Nothing is written to disk and nothing about the image reaches a log.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[EnableRateLimiting(RateLimits.AiPolicy)]
[RequestSizeLimit(IImageSanitiser.MaxUploadBytes + 64 * 1024)]
public sealed class PhotosModel : PageModel
{
    /// <summary>The origin the household's own key may talk to from the browser.</summary>
    public static Uri AnthropicOrigin { get; } = new("https://api.anthropic.com/");

    private readonly AnalysePhotoHandler _handler;
    private readonly CspPolicy _csp;

    /// <summary>Creates the page.</summary>
    public PhotosModel(AnalysePhotoHandler handler, CspPolicy csp)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(csp);
        _handler = handler;
        _csp = csp;
    }

    /// <summary>Whether our own key is configured.</summary>
    public bool Offered => _handler.IsAvailable;

    /// <summary>Which moment was just answered, if any.</summary>
    public AnalysisKind? Answered { get; private set; }

    /// <summary>The cupboard answer.</summary>
    public KitInventory? Kit { get; private set; }

    /// <summary>The boiler answer.</summary>
    public BoilerReading? Boiler { get; private set; }

    /// <summary>The consumer unit answer.</summary>
    public ConsumerUnitReading? ConsumerUnit { get; private set; }

    /// <summary>A sentence when an analysis could not run.</summary>
    public string? Notice { get; private set; }

    /// <summary>Free analyses a household gets a month, for the page's copy.</summary>
    public int FreePerMonth { get; }

    /// <summary>The questions.</summary>
    public IActionResult OnGet()
    {
        _csp.AllowConnect(AnthropicOrigin);
        return Page();
    }

    /// <summary>The cupboard.</summary>
    public Task<IActionResult> OnPostKitAsync(IFormFile? photo, string? household, CancellationToken cancellationToken) =>
        RunAsync(AnalysisKind.Kit, photo, household, async (bytes, key) =>
        {
            Result<KitInventory> result = await _handler.KitAsync(bytes, key, cancellationToken);
            Kit = result.IsSuccess ? result.Value : null;
            return result.IsSuccess ? null : result.Error;
        });

    /// <summary>The boiler plate.</summary>
    public Task<IActionResult> OnPostBoilerAsync(IFormFile? photo, string? household, CancellationToken cancellationToken) =>
        RunAsync(AnalysisKind.Boiler, photo, household, async (bytes, key) =>
        {
            Result<BoilerReading> result = await _handler.BoilerAsync(bytes, key, cancellationToken);
            Boiler = result.IsSuccess ? result.Value : null;
            return result.IsSuccess ? null : result.Error;
        });

    /// <summary>The consumer unit.</summary>
    public Task<IActionResult> OnPostConsumerUnitAsync(IFormFile? photo, string? household, CancellationToken cancellationToken) =>
        RunAsync(AnalysisKind.ConsumerUnit, photo, household, async (bytes, key) =>
        {
            Result<ConsumerUnitReading> result = await _handler.ConsumerUnitAsync(bytes, key, cancellationToken);
            ConsumerUnit = result.IsSuccess ? result.Value : null;
            return result.IsSuccess ? null : result.Error;
        });

    private async Task<IActionResult> RunAsync(AnalysisKind kind, IFormFile? photo, string? household, Func<ReadOnlyMemory<byte>, string, Task<Fault?>> analyse)
    {
        _csp.AllowConnect(AnthropicOrigin);
        Answered = kind;
        if (photo is null || photo.Length == 0)
        {
            Notice = "Choose a photo first.";
            return Page();
        }

        if (photo.Length > IImageSanitiser.MaxUploadBytes)
        {
            Notice = DomainErrors.ImageTooLarge().Message;
            return Page();
        }

        byte[] bytes;
        using (MemoryStream buffer = new((int)photo.Length))
        {
            await photo.CopyToAsync(buffer, HttpContext.RequestAborted);
            bytes = buffer.ToArray();
        }

        Fault? fault = await analyse(bytes, HouseholdKey.From(household, HttpContext));
        Notice = fault?.Message;
        return Page();
    }
}

/// <summary>
/// Who the meter counts. The browser sends an id it made up and keeps; without one, the address.
/// Neither is proof of anything, which is why the rate limit and the daily cap exist.
/// </summary>
public static class HouseholdKey
{
    /// <summary>The key, validated to a short token so it cannot carry anything else.</summary>
    public static string From(string? household, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (household is { Length: >= 8 and <= 64 } && household.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return "h:" + household;
        }

        return "ip:" + RateLimits.ClientKey(context);
    }
}
