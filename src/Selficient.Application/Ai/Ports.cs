using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Ai;

/// <summary>An upload after re-encoding: a JPEG of bounded size with nothing but pixels in it.</summary>
/// <param name="Jpeg">The bytes.</param>
/// <param name="Width">Pixels across.</param>
/// <param name="Height">Pixels down.</param>
public sealed record SanitisedImage(ReadOnlyMemory<byte> Jpeg, int Width, int Height)
{
    /// <summary>What the bytes are.</summary>
    public const string MediaType = "image/jpeg";
}

/// <summary>Turns whatever was uploaded into a bounded JPEG, or refuses it.</summary>
public interface IImageSanitiser
{
    /// <summary>Largest upload accepted, in bytes.</summary>
    const int MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>Most pixels accepted.</summary>
    const long MaxPixels = 24_000_000;

    /// <summary>Re-encodes the upload, dropping every byte that is not a pixel.</summary>
    Result<SanitisedImage> Sanitise(ReadOnlyMemory<byte> upload);
}

/// <summary>Reads photos. Counts and describes; never computes the plan.</summary>
public interface IPhotoAnalyst
{
    /// <summary>False for the null object, so the page never offers what it cannot do.</summary>
    bool IsAvailable { get; }

    /// <summary>What the cupboard holds, matched to the candidate actions.</summary>
    Task<Result<KitInventory>> KitAsync(SanitisedImage image, IReadOnlyList<KitCandidate> candidates, CancellationToken cancellationToken);

    /// <summary>What the boiler plate says.</summary>
    Task<Result<BoilerReading>> BoilerAsync(SanitisedImage image, CancellationToken cancellationToken);

    /// <summary>What the consumer unit shows.</summary>
    Task<Result<ConsumerUnitReading>> ConsumerUnitAsync(SanitisedImage image, CancellationToken cancellationToken);
}

/// <summary>Explains a plan in plain words. The plan is the input; nothing is recomputed.</summary>
public interface IPlanNarrator
{
    /// <summary>False for the null object.</summary>
    bool IsAvailable { get; }

    /// <summary>The narrative.</summary>
    Task<Result<PlanNarrative>> NarrateAsync(Plan plan, CancellationToken cancellationToken);
}

/// <summary>What the meter says about one more analysis.</summary>
public enum MeterOutcome
{
    /// <summary>Go ahead; the count has been taken.</summary>
    Allowed,

    /// <summary>This household has used its month.</summary>
    AllowanceUsed,

    /// <summary>The site has used its day.</summary>
    SiteBusy,
}

/// <summary>Counts analyses per household per month and for the whole site per day.</summary>
public interface IAnalysisMeter
{
    /// <summary>The free analyses each household gets a month.</summary>
    int FreePerMonth { get; }

    /// <summary>Takes one analysis for the household, or says why not.</summary>
    MeterOutcome TryTake(string householdKey);
}

/// <summary>No assistant configured. The page says so and offers the household's own key.</summary>
public sealed class NoAssistant : IPhotoAnalyst, IPlanNarrator
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<Result<KitInventory>> KitAsync(SanitisedImage image, IReadOnlyList<KitCandidate> candidates, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<KitInventory>(DomainErrors.AiUnavailable()));

    /// <inheritdoc />
    public Task<Result<BoilerReading>> BoilerAsync(SanitisedImage image, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<BoilerReading>(DomainErrors.AiUnavailable()));

    /// <inheritdoc />
    public Task<Result<ConsumerUnitReading>> ConsumerUnitAsync(SanitisedImage image, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<ConsumerUnitReading>(DomainErrors.AiUnavailable()));

    /// <inheritdoc />
    public Task<Result<PlanNarrative>> NarrateAsync(Plan plan, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<PlanNarrative>(DomainErrors.AiUnavailable()));
}
