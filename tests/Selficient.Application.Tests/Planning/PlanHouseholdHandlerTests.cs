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
    private readonly IFloodRiskProvider _flood = Substitute.For<IFloodRiskProvider>();

    private PlanHouseholdHandler Handler() =>
        new(_geocoder, _elevation, _flood, new GeneratePlanHandler(new RiskAssessmentPipeline(ApplicationServiceCollectionExtensions.Assessors()), new PlanBuilder(ApplicationServiceCollectionExtensions.Rules())));

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
        result.Value.Plan.Profile.Nation.ShouldBe(Nation.Wales, "the plan's rules are told the nation the postcode is in");
        await _geocoder.Received(1).LocateAsync(Arg.Is<Postcode>(p => p.Value == "NP12 1GW"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task What_the_household_said_about_flooding_stands_and_the_map_is_not_asked()
    {
        _geocoder.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Blackwood));
        _elevation.ElevationAtAsync(Blackwood.Coordinates, Arg.Any<CancellationToken>()).Returns(Elevation.Create(232m));

        Result<PlannedHousehold> result = await Handler().HandleAsync(new PlanHouseholdRequest("NP12 1GW", Command with { FloodRisk = FloodRisk.Medium }), CancellationToken.None);

        result.Value.Flood.ShouldBe(new FloodAnswer(FloodRisk.Medium, FloodOrigin.Household));
        result.Value.Plan.Profile.Dwelling.FloodRisk.ShouldBe(FloodRisk.Medium);
        result.Value.Plan.Profile.FloodFrom.ShouldBe(FloodWater.NotSaid);
        await _flood.DidNotReceiveWithAnyArgs().AtAsync(default, default, default);
    }

    [Fact]
    public async Task Look_it_up_for_me_reads_the_nations_flood_map_and_builds_the_plan_on_what_it_says()
    {
        _geocoder.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Blackwood));
        _elevation.ElevationAtAsync(Blackwood.Coordinates, Arg.Any<CancellationToken>()).Returns(Elevation.Create(232m));
        _flood.AtAsync(Blackwood.Coordinates, Nation.Wales, Arg.Any<CancellationToken>()).Returns(Result.Success(new FloodFinding(FloodRisk.High, FloodWater.SurfaceWater, "Natural Resources Wales's flood map")));

        Result<PlannedHousehold> result = await Handler().HandleAsync(new PlanHouseholdRequest("NP12 1GW", Command with { FloodRisk = FloodRisk.Unknown }), CancellationToken.None);

        result.Value.Flood.ShouldBe(new FloodAnswer(FloodRisk.High, FloodOrigin.Map, FloodWater.SurfaceWater, "Natural Resources Wales's flood map"));
        result.Value.Plan.Profile.Dwelling.FloodRisk.ShouldBe(FloodRisk.High);
        result.Value.Plan.Profile.FloodFrom.ShouldBe(FloodWater.SurfaceWater);
        result.Value.Plan.Risks.Includes(RiskKind.Flooding).ShouldBeTrue();
        result.Value.Plan.Actions.Select(a => a.Id.Value).ShouldContain("flood-protection-kit");
        result.Value.Elevation!.Value.Metres.ShouldBe(232m, "the height is still found, asked for alongside");
    }

    [Fact]
    public async Task A_flood_map_that_cannot_be_read_is_not_a_low_risk_and_the_plan_asks_the_household_to_check()
    {
        _geocoder.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Blackwood));
        _elevation.ElevationAtAsync(Blackwood.Coordinates, Arg.Any<CancellationToken>()).Returns(Elevation.Create(232m));
        _flood.AtAsync(Arg.Any<Coordinates>(), Arg.Any<Nation>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<FloodFinding>(DomainErrors.LookupUnavailable()));

        Result<PlannedHousehold> result = await Handler().HandleAsync(new PlanHouseholdRequest("NP12 1GW", Command with { FloodRisk = FloodRisk.Unknown }), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue("the plan is still built");
        result.Value.Flood.ShouldBe(new FloodAnswer(FloodRisk.Unknown, FloodOrigin.NotKnown));
        result.Value.Plan.Risks.Includes(RiskKind.Flooding).ShouldBeFalse();
        PlanAction check = result.Value.Plan.Actions.Single(a => a.Id.Value == "check-flood-risk");
        check.Link!.Address.ShouldBe(new Uri("https://naturalresources.wales/flooding/check-your-flood-risk-by-postcode/"), "a house in Wales is sent to Wales's own service");
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

        Should.Throw<ArgumentNullException>(() => new PlanHouseholdHandler(null!, _elevation, _flood, engine));
        Should.Throw<ArgumentNullException>(() => new PlanHouseholdHandler(_geocoder, null!, _flood, engine));
        Should.Throw<ArgumentNullException>(() => new PlanHouseholdHandler(_geocoder, _elevation, null!, engine));
        Should.Throw<ArgumentNullException>(() => new PlanHouseholdHandler(_geocoder, _elevation, _flood, null!));
        await Should.ThrowAsync<ArgumentNullException>(() => Handler().HandleAsync(null!, CancellationToken.None));
    }
}
