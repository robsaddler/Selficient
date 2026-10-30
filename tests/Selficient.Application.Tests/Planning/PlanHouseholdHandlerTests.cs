using NSubstitute;
using Selficient.Application.Composition;
using Selficient.Application.Geography;
using Selficient.Application.Planning;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;
using Selficient.Domain.Risks;

namespace Selficient.Application.Tests.Planning;

public sealed class PlanHouseholdHandlerTests
{
    private static readonly GeneratePlanCommand Command = new(4, 2, false, false, true, DwellingType.Detached, 3, HeatingType.GasBoiler, HobType.Electric, true, true, true, true, null, FloodRisk.VeryLow);
    private static readonly Locality Blackwood = new(new Coordinates(51.6655, -3.2031), Nation.Wales, "NP12", "Wales");

    private readonly IGeocoder _geocoder = Substitute.For<IGeocoder>();
    private readonly IElevationProvider _elevation = Substitute.For<IElevationProvider>();

    private PlanHouseholdHandler Handler() =>
        new(_geocoder, _elevation, new GeneratePlanHandler(new RiskAssessmentPipeline(ApplicationServiceCollectionExtensions.Assessors()), new PlanBuilder(ApplicationServiceCollectionExtensions.Rules())));

    [Fact]
    public async Task Locates_measures_and_plans()
    {
        _geocoder.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Blackwood));
        _elevation.ElevationAtAsync(Blackwood.Coordinates, Arg.Any<CancellationToken>()).Returns(Elevation.Create(232m));

        Result<PlannedHousehold> result = await Handler().HandleAsync(new PlanHouseholdRequest("np12 1gw", Command), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Locality.ShouldBe(Blackwood);
        result.Value.Elevation!.Value.Metres.ShouldBe(232m);
        result.Value.Plan.Profile.Dwelling.IsExposed.ShouldBeTrue();
        await _geocoder.Received(1).LocateAsync(Arg.Is<Postcode>(p => p.Value == "NP12 1GW"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invalid_postcode_never_reaches_the_geocoder()
    {
        Result<PlannedHousehold> result = await Handler().HandleAsync(new PlanHouseholdRequest("nope", Command), CancellationToken.None);

        result.Error.Code.ShouldBe("postcode.invalid");
        await _geocoder.DidNotReceiveWithAnyArgs().LocateAsync(default, default);
    }

    [Fact]
    public async Task A_postcode_the_service_cannot_find_is_the_service_fault()
    {
        _geocoder.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<Locality>(DomainErrors.PostcodeNotFound()));

        Result<PlannedHousehold> result = await Handler().HandleAsync(new PlanHouseholdRequest("NP12 1GW", Command), CancellationToken.None);

        result.Error.Code.ShouldBe("postcode.not-found");
        await _elevation.DidNotReceiveWithAnyArgs().ElevationAtAsync(default, default);
    }

    [Fact]
    public async Task Elevation_failure_degrades_to_unknown()
    {
        _geocoder.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Blackwood));
        _elevation.ElevationAtAsync(Arg.Any<Coordinates>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<Elevation>(DomainErrors.LookupUnavailable()));

        Result<PlannedHousehold> result = await Handler().HandleAsync(new PlanHouseholdRequest("NP12 1GW", Command), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Elevation.ShouldBeNull();
        result.Value.Plan.Profile.Dwelling.IsExposed.ShouldBeFalse();
    }

    [Fact]
    public async Task Engine_faults_pass_through()
    {
        _geocoder.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Blackwood));
        _elevation.ElevationAtAsync(Arg.Any<Coordinates>(), Arg.Any<CancellationToken>()).Returns(Elevation.Create(10m));

        Result<PlannedHousehold> result = await Handler().HandleAsync(new PlanHouseholdRequest("NP12 1GW", Command with { Storeys = 0 }), CancellationToken.None);

        result.Error.Code.ShouldBe("dwelling.storeys");
    }

    [Fact]
    public async Task Rejects_missing_collaborators_and_requests()
    {
        GeneratePlanHandler engine = new(new RiskAssessmentPipeline([]), new PlanBuilder([]));

        Should.Throw<ArgumentNullException>(() => new PlanHouseholdHandler(null!, _elevation, engine));
        Should.Throw<ArgumentNullException>(() => new PlanHouseholdHandler(_geocoder, null!, engine));
        Should.Throw<ArgumentNullException>(() => new PlanHouseholdHandler(_geocoder, _elevation, null!));
        await Should.ThrowAsync<ArgumentNullException>(() => Handler().HandleAsync(null!, CancellationToken.None));
    }
}
