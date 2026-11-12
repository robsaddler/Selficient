using Selficient.Application.Geography;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;

namespace Selficient.Application.Planning;

/// <summary>What the wizard submits: a postcode and the household facts, elevation unknown.</summary>
public sealed record PlanHouseholdRequest(string Postcode, GeneratePlanCommand Command);

/// <summary>Where the plan's flood band came from.</summary>
public enum FloodOrigin
{
    /// <summary>The household chose it on the form.</summary>
    Household,

    /// <summary>The household asked us to look, and the nation's flood map answered.</summary>
    Map,

    /// <summary>The household asked us to look, and the map could not be read; the plan asks them to check.</summary>
    NotKnown,
}

/// <summary>The flood band the plan was built on, where it came from, and whose map it was when it came from one.</summary>
public sealed record FloodAnswer(FloodRisk Band, FloodOrigin Origin, FloodWater From = FloodWater.NotSaid, string? Source = null);

/// <summary>The plan with where the house is, how high it sits and what was found out about flooding.</summary>
public sealed record PlannedHousehold(Plan Plan, Locality Locality, Elevation? Elevation, FloodAnswer Flood);

/// <summary>
/// The full journey from the form to the plan: validate the postcode, find it, look up the
/// elevation and, where the household did not say, the flood map, then run the engine. A lookup
/// that fails degrades to unknown, which the plan answers with an action; a bad postcode is a fault.
/// </summary>
public sealed class PlanHouseholdHandler
{
    private readonly IGeocoder _geocoder;
    private readonly IElevationProvider _elevation;
    private readonly IFloodRiskProvider _flood;
    private readonly GeneratePlanHandler _engine;

    /// <summary>Creates the handler.</summary>
    public PlanHouseholdHandler(IGeocoder geocoder, IElevationProvider elevation, IFloodRiskProvider flood, GeneratePlanHandler engine)
    {
        ArgumentNullException.ThrowIfNull(geocoder);
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(flood);
        ArgumentNullException.ThrowIfNull(engine);
        _geocoder = geocoder;
        _elevation = elevation;
        _flood = flood;
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

        Locality locality = located.Value;

        // The height and the flood map are asked at the same time: neither waits for the other.
        // What the household said about flooding stands; only "look it up for me" is looked up.
        Task<Result<Elevation>> asking = _elevation.ElevationAtAsync(locality.Coordinates, cancellationToken);
        FloodAnswer flood = new(request.Command.FloodRisk, FloodOrigin.Household);
        if (request.Command.FloodRisk == FloodRisk.Unknown)
        {
            Result<FloodFinding> found = await _flood.AtAsync(locality.Coordinates, locality.Nation, cancellationToken);
            flood = found.IsSuccess
                ? new FloodAnswer(found.Value.Band, FloodOrigin.Map, found.Value.From, found.Value.Source)
                : new FloodAnswer(FloodRisk.Unknown, FloodOrigin.NotKnown);
        }

        Result<Elevation> height = await asking;
        Elevation? elevation = height.IsSuccess ? height.Value : null;

        GeneratePlanCommand command = request.Command with { ElevationMetres = elevation?.Metres, FloodRisk = flood.Band, Nation = locality.Nation, FloodFrom = flood.From };
        return _engine.Handle(command).Map(plan => new PlannedHousehold(plan, locality, elevation, flood));
    }
}
