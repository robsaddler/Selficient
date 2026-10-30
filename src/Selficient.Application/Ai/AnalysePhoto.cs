using Selficient.Application.Planning;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Ai;

/// <summary>
/// One photo, one answer: the upload is re-encoded, the meter is asked, the analyst reads it. The
/// image lives in memory for the length of the call and is never written anywhere. The order
/// matters: a bad upload costs nothing from the allowance.
/// </summary>
public sealed class AnalysePhotoHandler
{
    private readonly IImageSanitiser _sanitiser;
    private readonly IAnalysisMeter _meter;
    private readonly IPhotoAnalyst _analyst;

    /// <summary>Creates the handler.</summary>
    public AnalysePhotoHandler(IImageSanitiser sanitiser, IAnalysisMeter meter, IPhotoAnalyst analyst)
    {
        ArgumentNullException.ThrowIfNull(sanitiser);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(analyst);
        _sanitiser = sanitiser;
        _meter = meter;
        _analyst = analyst;
    }

    /// <summary>Whether photo help is on at all.</summary>
    public bool IsAvailable => _analyst.IsAvailable;

    /// <summary>What the cupboard holds.</summary>
    public Task<Result<KitInventory>> KitAsync(ReadOnlyMemory<byte> upload, string householdKey, CancellationToken cancellationToken) =>
        RunAsync(upload, householdKey, image => _analyst.KitAsync(image, KitCandidates.All, cancellationToken));

    /// <summary>What the boiler plate says.</summary>
    public Task<Result<BoilerReading>> BoilerAsync(ReadOnlyMemory<byte> upload, string householdKey, CancellationToken cancellationToken) =>
        RunAsync(upload, householdKey, image => _analyst.BoilerAsync(image, cancellationToken));

    /// <summary>What the consumer unit shows.</summary>
    public Task<Result<ConsumerUnitReading>> ConsumerUnitAsync(ReadOnlyMemory<byte> upload, string householdKey, CancellationToken cancellationToken) =>
        RunAsync(upload, householdKey, image => _analyst.ConsumerUnitAsync(image, cancellationToken));

    private async Task<Result<T>> RunAsync<T>(ReadOnlyMemory<byte> upload, string householdKey, Func<SanitisedImage, Task<Result<T>>> analyse)
    {
        ArgumentNullException.ThrowIfNull(householdKey);
        if (!_analyst.IsAvailable)
        {
            return Result.Failure<T>(DomainErrors.AiUnavailable());
        }

        Result<SanitisedImage> image = _sanitiser.Sanitise(upload);
        if (image.IsFailure)
        {
            return Result.Failure<T>(image.Error);
        }

        switch (_meter.TryTake(householdKey))
        {
            case MeterOutcome.AllowanceUsed:
                return Result.Failure<T>(DomainErrors.AiAllowanceUsed(_meter.FreePerMonth));
            case MeterOutcome.SiteBusy:
                return Result.Failure<T>(DomainErrors.AiBusy());
            default:
                return await analyse(image.Value);
        }
    }
}

/// <summary>Builds the plan the household already has and asks for it in plain words.</summary>
public sealed class ExplainPlanHandler
{
    private readonly PlanHouseholdHandler _plan;
    private readonly IAnalysisMeter _meter;
    private readonly IPlanNarrator _narrator;

    /// <summary>Creates the handler.</summary>
    public ExplainPlanHandler(PlanHouseholdHandler plan, IAnalysisMeter meter, IPlanNarrator narrator)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(narrator);
        _plan = plan;
        _meter = meter;
        _narrator = narrator;
    }

    /// <summary>Whether the narrative is on at all.</summary>
    public bool IsAvailable => _narrator.IsAvailable;

    /// <summary>The plan and its narrative.</summary>
    public async Task<Result<(Plan Plan, PlanNarrative Narrative)>> HandleAsync(PlanHouseholdRequest request, string householdKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(householdKey);
        if (!_narrator.IsAvailable)
        {
            return Result.Failure<(Plan, PlanNarrative)>(DomainErrors.AiUnavailable());
        }

        Result<PlannedHousehold> planned = await _plan.HandleAsync(request, cancellationToken);
        if (planned.IsFailure)
        {
            return Result.Failure<(Plan, PlanNarrative)>(planned.Error);
        }

        switch (_meter.TryTake(householdKey))
        {
            case MeterOutcome.AllowanceUsed:
                return Result.Failure<(Plan, PlanNarrative)>(DomainErrors.AiAllowanceUsed(_meter.FreePerMonth));
            case MeterOutcome.SiteBusy:
                return Result.Failure<(Plan, PlanNarrative)>(DomainErrors.AiBusy());
            default:
                Result<PlanNarrative> narrative = await _narrator.NarrateAsync(planned.Value.Plan, cancellationToken);
                return narrative.Map(n => (planned.Value.Plan, n));
        }
    }
}
