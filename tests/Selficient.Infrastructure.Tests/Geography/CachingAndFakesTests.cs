using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Selficient.Application.Geography;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;
using Selficient.Infrastructure.Geography;

namespace Selficient.Infrastructure.Tests.Geography;

public sealed class CachingAndFakesTests
{
    private static readonly Locality Blackwood = new(new Coordinates(51.6655, -3.2031), Nation.Wales, "NP12", "Wales");

    private static HybridCache Cache()
    {
        ServiceCollection services = new();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    [Fact]
    public async Task Variants_of_a_postcode_share_one_cache_entry()
    {
        IGeocoder inner = Substitute.For<IGeocoder>();
        inner.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Blackwood));
        CachedGeocoder cached = new(inner, Cache());

        Result<Locality> first = await cached.LocateAsync(Postcode.Create("np12 1gw").Value, CancellationToken.None);
        Result<Locality> second = await cached.LocateAsync(Postcode.Create("NP121GW").Value, CancellationToken.None);

        first.Value.ShouldBe(Blackwood);
        second.Value.ShouldBe(Blackwood);
        await inner.Received(1).LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Geocode_failures_are_not_cached()
    {
        IGeocoder inner = Substitute.For<IGeocoder>();
        inner.LocateAsync(Arg.Any<Postcode>(), Arg.Any<CancellationToken>()).Returns(
            Result.Failure<Locality>(DomainErrors.LookupUnavailable()),
            Result.Failure<Locality>(DomainErrors.LookupUnavailable()),
            Result.Success(Blackwood));
        CachedGeocoder cached = new(inner, Cache());
        Postcode postcode = Postcode.Create("NP12 1GW").Value;

        (await cached.LocateAsync(postcode, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await cached.LocateAsync(postcode, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Elevations_cache_by_rounded_coordinates_and_failures_are_not_cached()
    {
        IElevationProvider inner = Substitute.For<IElevationProvider>();
        inner.ElevationAtAsync(Arg.Any<Coordinates>(), Arg.Any<CancellationToken>()).Returns(
            Result.Failure<Elevation>(DomainErrors.LookupUnavailable()),
            Result.Failure<Elevation>(DomainErrors.LookupUnavailable()),
            Elevation.Create(232m),
            Elevation.Create(999m));
        CachedElevationProvider cached = new(inner, Cache());

        (await cached.ElevationAtAsync(new Coordinates(51.66551, -3.20311), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await cached.ElevationAtAsync(new Coordinates(51.66551, -3.20311), CancellationToken.None)).Value.Metres.ShouldBe(232m);
        (await cached.ElevationAtAsync(new Coordinates(51.66549, -3.20309), CancellationToken.None)).Value.Metres.ShouldBe(232m, "same four-decimal key");
    }

    [Fact]
    public void Decorators_reject_missing_arguments()
    {
        HybridCache cache = Cache();
        Should.Throw<ArgumentNullException>(() => new CachedGeocoder(null!, cache));
        Should.Throw<ArgumentNullException>(() => new CachedGeocoder(Substitute.For<IGeocoder>(), null!));
        Should.Throw<ArgumentNullException>(() => new CachedElevationProvider(null!, cache));
        Should.Throw<ArgumentNullException>(() => new CachedElevationProvider(Substitute.For<IElevationProvider>(), null!));
    }

    [Theory]
    [InlineData("NP12 1GW", Nation.Wales, 232)]
    [InlineData("CF10 1AA", Nation.Wales, null)]
    [InlineData("EH1 1AA", Nation.Scotland, null)]
    [InlineData("BT1 5GS", Nation.NorthernIreland, null)]
    [InlineData("SW1A 1AA", Nation.England, null)]
    public async Task The_fakes_are_deterministic_and_know_robs_house(string raw, Nation nation, int? metres)
    {
        Postcode postcode = Postcode.Create(raw).Value;
        FakeGeocoder geocoder = new();
        FakeElevationProvider elevation = new();

        Locality first = (await geocoder.LocateAsync(postcode, CancellationToken.None)).Value;
        Locality second = (await geocoder.LocateAsync(postcode, CancellationToken.None)).Value;
        Elevation height = (await elevation.ElevationAtAsync(first.Coordinates, CancellationToken.None)).Value;

        first.ShouldBe(second);
        first.Nation.ShouldBe(nation);
        first.Coordinates.Latitude.ShouldBeInRange(49.5, 59.5);
        first.Coordinates.Longitude.ShouldBeInRange(-8.5, 2.5);
        height.Metres.ShouldBeInRange(0m, 400m);
        if (metres is int exact)
        {
            height.Metres.ShouldBe(exact);
        }
    }

    [Fact]
    public async Task The_fake_geocoder_can_fail_to_find_a_postcode()
    {
        (await new FakeGeocoder().LocateAsync(Postcode.Create("ZE1 0ZZ").Value, CancellationToken.None)).Error.Code.ShouldBe("postcode.not-found");
    }

    [Fact]
    public async Task The_fakes_can_model_a_place_with_no_region_and_no_elevation()
    {
        Locality locality = (await new FakeGeocoder().LocateAsync(Postcode.Create("ZE2 9ZZ").Value, CancellationToken.None)).Value;

        locality.Region.ShouldBeEmpty();
        locality.Outcode.ShouldBe("ZE2");
        (await new FakeElevationProvider().ElevationAtAsync(locality.Coordinates, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
    }
}
