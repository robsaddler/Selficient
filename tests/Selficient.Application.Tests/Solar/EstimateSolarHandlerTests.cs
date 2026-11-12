using NSubstitute;
using Selficient.Application.Geography;
using Selficient.Application.Solar;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;
using Selficient.Domain.Solar;

namespace Selficient.Application.Tests.Solar;

public sealed class EstimateSolarHandlerTests
{
    private static readonly Locality Blackwood = new(new Coordinates(51.6655, -3.2031), Nation.Wales, "NP12", "Wales");
    private static readonly IReadOnlyList<decimal> SouthWest = [29.35m, 45.67m, 79.6m, 108.6m, 122.41m, 120.59m, 121.84m, 104.49m, 85.78m, 54.37m, 35.88m, 27.12m];

    private static EstimateSolarRequest Robs(string postcode = "NP12 1GW", RoofAspect aspect = RoofAspect.SouthWest, int pitch = 35, int panels = 13, decimal battery = 13.5m, BackupLevel backup = BackupLevel.WholeHome, int storeys = 3, int people = 4, decimal? kwh = 5_000) =>
        new(postcode, aspect, pitch, panels, battery, backup, storeys, people, false, false, kwh);

    private static (EstimateSolarHandler Handler, IGeocoder Geocoder, ISolarYieldProvider Yields) Build()
    {
        IGeocoder geocoder = Substitute.For<IGeocoder>();
        geocoder.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.Arg<Postcode>().Value == "ZE1 0ZZ" ? Result.Failure<Locality>(DomainErrors.PostcodeNotFound()) : Result.Success(Blackwood));
        ISolarYieldProvider yields = Substitute.For<ISolarYieldProvider>();
        yields.YieldAsync(Arg.Any<Coordinates>(), Arg.Any<RoofAspect>(), Arg.Any<RoofPitch>(), Arg.Any<CancellationToken>()).Returns(SolarYield.Create(SouthWest));
        return (new EstimateSolarHandler(geocoder, yields), geocoder, yields);
    }

    [Fact]
    public async Task Robs_roof_runs_end_to_end_with_welsh_grants()
    {
        (EstimateSolarHandler handler, _, ISolarYieldProvider yields) = Build();

        Result<SolarReport> result = await handler.HandleAsync(Robs(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Locality.ShouldBe(Blackwood);
        result.Value.Aspect.ShouldBe(RoofAspect.SouthWest);
        result.Value.Grants.Select(g => g.Title).ShouldContain("Green Homes Wales 0 percent loan");
        result.Value.Estimate.Best.AnnualSaving.Pounds.ShouldBeInRange(1_100m, 1_700m);
        await yields.Received(1).YieldAsync(Blackwood.Coordinates, RoofAspect.SouthWest, Arg.Is<RoofPitch>(p => p.Degrees == 35), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Demand_is_estimated_when_no_figure_is_given()
    {
        (EstimateSolarHandler handler, _, _) = Build();

        Result<SolarReport> result = await handler.HandleAsync(Robs(kwh: null, people: 4), CancellationToken.None);

        result.Value.Estimate.Scenario.Demand.KilowattHours.ShouldBe(4_100m);
    }

    [Theory]
    [InlineData("", "postcode.invalid")]
    [InlineData("ZE1 0ZZ", "postcode.not-found")]
    public async Task Postcode_faults_pass_through(string postcode, string code)
    {
        (EstimateSolarHandler handler, _, _) = Build();

        Result<SolarReport> result = await handler.HandleAsync(Robs(postcode: postcode), CancellationToken.None);

        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    public async Task Solar_inputs_are_bounded()
    {
        (EstimateSolarHandler handler, _, _) = Build();

        (await handler.HandleAsync(Robs(aspect: (RoofAspect)99), CancellationToken.None)).Error.Code.ShouldBe("dwelling.unknown-choice");
        (await handler.HandleAsync(Robs(pitch: 5), CancellationToken.None)).Error.Message.ShouldBe("The pitch needs to be between 10 and 60.");
        (await handler.HandleAsync(Robs(panels: 41), CancellationToken.None)).Error.Message.ShouldBe("The panels needs to be between 2 and 40.");
        (await handler.HandleAsync(Robs(battery: 31), CancellationToken.None)).Error.Code.ShouldBe("solar.out-of-range");
        (await handler.HandleAsync(Robs(storeys: 0), CancellationToken.None)).Error.Code.ShouldBe("dwelling.storeys");
        (await handler.HandleAsync(Robs(storeys: 7), CancellationToken.None)).Error.Code.ShouldBe("dwelling.storeys");
        (await handler.HandleAsync(Robs(people: 0), CancellationToken.None)).Error.Code.ShouldBe("household.empty");
        (await handler.HandleAsync(Robs(kwh: 100), CancellationToken.None)).Error.Code.ShouldBe("solar.out-of-range");
    }

    [Fact]
    public async Task A_yield_failure_is_the_yield_fault()
    {
        (EstimateSolarHandler handler, _, ISolarYieldProvider yields) = Build();
        yields.YieldAsync(Arg.Any<Coordinates>(), Arg.Any<RoofAspect>(), Arg.Any<RoofPitch>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SolarYield>(DomainErrors.YieldUnavailable()));

        Result<SolarReport> result = await handler.HandleAsync(Robs(), CancellationToken.None);

        result.Error.Code.ShouldBe("solar.yield-unavailable");
    }

    [Fact]
    public async Task Guards_reject_nulls()
    {
        (EstimateSolarHandler handler, IGeocoder geocoder, ISolarYieldProvider yields) = Build();

        Should.Throw<ArgumentNullException>(() => new EstimateSolarHandler(null!, yields));
        Should.Throw<ArgumentNullException>(() => new EstimateSolarHandler(geocoder, null!));
        await Should.ThrowAsync<ArgumentNullException>(() => handler.HandleAsync(null!, CancellationToken.None));
    }
}
