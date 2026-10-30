using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Planning;

/// <summary>What the wizard submits: a postcode and the household facts, elevation unknown.</summary>
public sealed record PlanHouseholdRequest(string Postcode, GeneratePlanCommand Command);

/// <summary>The plan with where the house is and how high it sits.</summary>
public sealed record PlannedHousehold(Plan Plan, Locality Locality, Elevation? Elevation);

/// <summary>
/// The full journey from the form to the plan: validate the postcode, find it, look up the
/// elevation, then run the engine. Elevation failure degrades to unknown; a bad postcode is a fault.
/// </summary>
public sealed class PlanHouseholdHandler
{
    private readonly IGeocoder _geocoder;
    private readonly IElevationProvider _elevation;
    private readonly GeneratePlanHandler _engine;

    /// <summary>Creates the handler.</summary>
    public PlanHouseholdHandler(IGeocoder geocoder, IElevationProvider elevation, GeneratePlanHandler engine)
    {
        ArgumentNullException.ThrowIfNull(geocoder);
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(engine);
        _geocoder = geocoder;
        _elevation = elevation;
        _engine = engine;
    }

    /// <summary>Runs the journey.</summary>
    public async Task<Result<PlannedHousehold>> HandleAsync(PlanHouseholdRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Postcode> postcode = Postcode.Create(request.Postcode);
        if (postcode.IsFailure)
        {
            return Result.Failure<PlannedHousehold>(postcode.Error);
        }

        Result<Locality> located = await _geocoder.LocateAsync(postcode.Value, cancellationToken);
        if (located.IsFailure)
        {
            return Result.Failure<PlannedHousehold>(located.Error);
        }

        Result<Elevation> height = await _elevation.ElevationAtAsync(located.Value.Coordinates, cancellationToken);
        Elevation? elevation = height.IsSuccess ? height.Value : null;

        GeneratePlanCommand command = request.Command with { ElevationMetres = elevation?.Metres };
        return _engine.Handle(command).Map(plan => new PlannedHousehold(plan, located.Value, elevation));
    }
}
