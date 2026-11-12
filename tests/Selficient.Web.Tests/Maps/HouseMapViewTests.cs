using Selficient.Application.Geography;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Maps;

public sealed class HouseMapViewTests
{
    private static readonly Locality Blackwood = new(new Coordinates(51.6655, -3.2031), Nation.Wales, "NP12", "Wales");

    [Fact]
    public void Coordinates_are_fixed_to_five_decimals_in_invariant_culture()
    {
        HouseMapView view = new(Blackwood, Elevation.Create(232m).Value);

        view.Latitude.ShouldBe("51.66550");
        view.Longitude.ShouldBe("-3.20310");
        view.Covered.ShouldBeTrue();
        view.Sentence.ShouldBe("Your postcode is in NP12, Wales, about 232 m above sea level. That is high and exposed ground, which is why wind and cold sit near the top of your risks.");
    }

    [Fact]
    public void Low_ground_and_unknown_height_each_get_their_own_sentence()
    {
        new HouseMapView(Blackwood with { Region = string.Empty }, Elevation.Create(12.4m).Value).Sentence.ShouldBe("Your postcode is in NP12, about 12 m above sea level.");
        new HouseMapView(Blackwood, null).Sentence.ShouldBe("Your postcode is in NP12, Wales. We could not find how high it sits, so the plan assumes level ground.");
    }

    [Fact]
    public void The_place_phrase_drops_an_empty_region()
    {
        Selficient.Web.Formatting.PlaceCopy.Place(Blackwood).ShouldBe("NP12, Wales");
        Selficient.Web.Formatting.PlaceCopy.Place(Blackwood with { Region = string.Empty }).ShouldBe("NP12");
        Should.Throw<ArgumentNullException>(() => Selficient.Web.Formatting.PlaceCopy.Place(null!));
    }

    [Fact]
    public void Northern_ireland_is_outside_the_basemap()
    {
        new HouseMapView(Blackwood with { Nation = Nation.NorthernIreland, Outcode = "BT1" }, null).Covered.ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => new HouseMapView(null!, null));
    }
}
