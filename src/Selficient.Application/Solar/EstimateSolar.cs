using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;
using Selficient.Domain.Solar;

namespace Selficient.Application.Solar;

/// <summary>What the solar form submits.</summary>
/// <param name="Postcode">Where the roof is.</param>
/// <param name="Aspect">Which way the slope faces.</param>
/// <param name="PitchDegrees">Degrees from horizontal.</param>
/// <param name="Panels">How many panels.</param>
/// <param name="BatteryKilowattHours">Battery size, zero for none.</param>
/// <param name="Backup">What the battery keeps running in a cut.</param>
/// <param name="Storeys">How tall the house is.</param>
/// <param name="People">How many live there, for the demand estimate.</param>
/// <param name="HasElectricCar">A car charged at home.</param>
/// <param name="HasHeatPump">A heat pump.</param>
/// <param name="AnnualKilowattHours">The household's own figure from a bill, or null to estimate.</param>
public sealed record EstimateSolarRequest(
    string Postcode,
    RoofAspect Aspect,
    int PitchDegrees,
    int Panels,
    decimal BatteryKilowattHours,
    BackupLevel Backup,
    int Storeys,
    int People,
    bool HasElectricCar,
    bool HasHeatPump,
    decimal? AnnualKilowattHours);

/// <summary>The estimate with where the house is, so the page can say so and pick the right grants.</summary>
/// <param name="Estimate">The numbers.</param>
/// <param name="Locality">Where the roof is.</param>
/// <param name="Grants">Help with the money in that nation.</param>
/// <param name="Aspect">Which way the roof faces, as asked.</param>
public sealed record SolarReport(SolarEstimate Estimate, Locality Locality, IReadOnlyList<Grant> Grants, RoofAspect Aspect);

/// <summary>
/// Validate every input, find the postcode, fetch the roof's yield, run the model. A bad input is
/// a fault with a sentence naming the field; an upstream failure is a fault the page can retry.
/// </summary>
public sealed class EstimateSolarHandler
{
    private readonly IGeocoder _geocoder;
    private readonly ISolarYieldProvider _yields;

    /// <summary>Creates the handler.</summary>
    public EstimateSolarHandler(IGeocoder geocoder, ISolarYieldProvider yields)
    {
        ArgumentNullException.ThrowIfNull(geocoder);
        ArgumentNullException.ThrowIfNull(yields);
        _geocoder = geocoder;
        _yields = yields;
    }

    /// <summary>Runs the journey.</summary>
    public async Task<Result<SolarReport>> HandleAsync(EstimateSolarRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Postcode> postcode = Postcode.Create(request.Postcode);
        if (postcode.IsFailure)
        {
            return Result.Failure<SolarReport>(postcode.Error);
        }

        if (!Enum.IsDefined(request.Aspect))
        {
            return Result.Failure<SolarReport>(DomainErrors.UnknownChoice());
        }

        Result<RoofPitch> pitch = RoofPitch.Create(request.PitchDegrees);
        if (pitch.IsFailure)
        {
            return Result.Failure<SolarReport>(pitch.Error);
        }

        Result<PanelArray> array = PanelArray.Create(request.Panels);
        if (array.IsFailure)
        {
            return Result.Failure<SolarReport>(array.Error);
        }

        Result<Battery> battery = Battery.Create(request.BatteryKilowattHours, request.Backup);
        if (battery.IsFailure)
        {
            return Result.Failure<SolarReport>(battery.Error);
        }

        if (request.Storeys is < 1 or > Domain.Households.Dwelling.MaxStoreys)
        {
            return Result.Failure<SolarReport>(DomainErrors.StoreysOutOfRange());
        }

        Result<HouseholdSize> size = HouseholdSize.Create(request.People);
        if (size.IsFailure)
        {
            return Result.Failure<SolarReport>(size.Error);
        }

        Result<AnnualDemand> demand = request.AnnualKilowattHours is { } given
            ? AnnualDemand.Create(given)
            : Result.Success(AnnualDemand.Estimate(size.Value, request.HasElectricCar, request.HasHeatPump));
        if (demand.IsFailure)
        {
            return Result.Failure<SolarReport>(demand.Error);
        }

        Result<Locality> located = await _geocoder.LocateAsync(postcode.Value, cancellationToken);
        if (located.IsFailure)
        {
            return Result.Failure<SolarReport>(located.Error);
        }

        Result<SolarYield> yield = await _yields.YieldAsync(located.Value.Coordinates, request.Aspect, pitch.Value, cancellationToken);
        if (yield.IsFailure)
        {
            return Result.Failure<SolarReport>(yield.Error);
        }

        SolarScenario scenario = new(yield.Value, array.Value, battery.Value, demand.Value, request.Storeys, new TariffContext(battery.Value.IsPresent, request.HasElectricCar, request.HasHeatPump));
        SolarEstimate estimate = SolarEstimator.Estimate(scenario);
        return Result.Success(new SolarReport(estimate, located.Value, GrantBook.For(located.Value.Nation), request.Aspect));
    }
}
