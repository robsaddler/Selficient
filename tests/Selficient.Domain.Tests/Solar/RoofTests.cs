using Selficient.Domain.Solar;

namespace Selficient.Domain.Tests.Solar;

public sealed class RoofTests
{
    [Theory]
    [InlineData(RoofAspect.South, 0, 180, "south")]
    [InlineData(RoofAspect.SouthWest, 45, 225, "south-west")]
    [InlineData(RoofAspect.West, 90, 270, "west")]
    [InlineData(RoofAspect.NorthWest, 135, 315, "north-west")]
    [InlineData(RoofAspect.North, 180, 0, "north")]
    [InlineData(RoofAspect.NorthEast, -135, 45, "north-east")]
    [InlineData(RoofAspect.East, -90, 90, "east")]
    [InlineData(RoofAspect.SouthEast, -45, 135, "south-east")]
    public void Each_aspect_has_a_pvgis_azimuth_a_bearing_and_words(RoofAspect aspect, int azimuth, int bearing, string words)
    {
        aspect.PvgisAzimuth().ShouldBe(azimuth);
        aspect.Bearing().ShouldBe(bearing);
        aspect.Words().ShouldBe(words);
        RoofAspects.FromBearing(bearing).ShouldBe(aspect);
    }

    [Theory]
    [InlineData(22.4, RoofAspect.North)]
    [InlineData(22.5, RoofAspect.NorthEast)]
    [InlineData(224.9, RoofAspect.SouthWest)]
    [InlineData(359.9, RoofAspect.North)]
    [InlineData(-45, RoofAspect.NorthWest)]
    [InlineData(405, RoofAspect.NorthEast)]
    public void Bearings_snap_to_the_nearest_of_eight(double bearing, RoofAspect expected)
    {
        RoofAspects.FromBearing(bearing).ShouldBe(expected);
    }

    [Fact]
    public void An_undefined_aspect_cannot_be_described()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ((RoofAspect)42).PvgisAzimuth());
        Should.Throw<ArgumentOutOfRangeException>(() => ((RoofAspect)42).Words());
        RoofAspects.All.Count.ShouldBe(8);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(35, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Pitch_is_bounded(int degrees, bool allowed)
    {
        RoofPitch.Create(degrees).IsSuccess.ShouldBe(allowed);
        if (!allowed)
        {
            RoofPitch.Create(degrees).Error.Message.ShouldBe("The pitch needs to be between 10 and 60.");
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(13, true)]
    [InlineData(40, true)]
    [InlineData(41, false)]
    public void Panel_count_is_bounded_and_sized_at_the_typical_panel(int panels, bool allowed)
    {
        PanelArray.Create(panels).IsSuccess.ShouldBe(allowed);
        if (allowed)
        {
            PanelArray.Create(panels).Value.KilowattsPeak.ShouldBe(panels * 0.43m);
        }
    }

    [Fact]
    public void A_battery_of_zero_is_no_battery_and_drops_the_backup()
    {
        Battery none = Battery.Create(0, BackupLevel.WholeHome).Value;

        none.IsPresent.ShouldBeFalse();
        none.Backup.ShouldBe(BackupLevel.None);
        none.ShouldBe(Battery.None);
        Battery.Create(13.5m, BackupLevel.WholeHome).Value.KilowattHours.ShouldBe(13.5m);
        Battery.Create(-1, BackupLevel.None).Error.Code.ShouldBe("solar.out-of-range");
        Battery.Create(31, BackupLevel.None).Error.Code.ShouldBe("solar.out-of-range");
        Battery.Create(5, (BackupLevel)9).Error.Code.ShouldBe("dwelling.unknown-choice");
    }
}
