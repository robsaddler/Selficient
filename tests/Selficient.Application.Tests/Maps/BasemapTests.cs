using Selficient.Application.Maps;

namespace Selficient.Application.Tests.Maps;

public sealed class BasemapTests
{
    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(14, 8_000, 5_300, true)]
    [InlineData(22, (1 << 22) - 1, (1 << 22) - 1, true)]
    [InlineData(-1, 0, 0, false)]
    [InlineData(23, 0, 0, false)]
    [InlineData(1, 2, 0, false)]
    [InlineData(1, 0, 2, false)]
    [InlineData(1, -1, 0, false)]
    [InlineData(1, 0, -1, false)]
    public void Only_addresses_inside_the_pyramid_can_be_made(int z, int x, int y, bool allowed)
    {
        TileAddress? address = TileAddress.TryCreate(z, x, y);

        (address is not null).ShouldBe(allowed);
        if (address is { } made)
        {
            made.Z.ShouldBe(z);
            made.X.ShouldBe(x);
            made.Y.ShouldBe(y);
        }
    }

    [Fact]
    public void The_tms_row_counts_from_the_south()
    {
        TileAddress.TryCreate(1, 0, 0)!.Value.TmsRow.ShouldBe(1);
        TileAddress.TryCreate(1, 0, 1)!.Value.TmsRow.ShouldBe(0);
        TileAddress.TryCreate(14, 0, 5_300)!.Value.TmsRow.ShouldBe(16_383 - 5_300);
        TileAddress.TryCreate(0, 0, 0)!.Value.TmsRow.ShouldBe(0);
    }

    [Fact]
    public void A_tile_knows_whether_it_is_gzipped_from_its_first_bytes()
    {
        new MapTile(new byte[] { 0x1f, 0x8b, 0x08, 0x00 }).IsGzipped.ShouldBeTrue();
        new MapTile(new byte[] { 0x0a, 0x8b, 0x08, 0x00 }).IsGzipped.ShouldBeFalse();
        new MapTile(new byte[] { 0x1f, 0x00, 0x08, 0x00 }).IsGzipped.ShouldBeFalse();
        new MapTile(new byte[] { 0x1f, 0x8b }).IsGzipped.ShouldBeFalse("two bytes is not a tile");
        new MapTile(ReadOnlyMemory<byte>.Empty).IsGzipped.ShouldBeFalse();
    }

    [Fact]
    public void The_unavailable_status_draws_nothing_and_the_attribution_is_the_licence_wording()
    {
        BasemapStatus.Unavailable.Available.ShouldBeFalse();
        BasemapStatus.Unavailable.MinZoom.ShouldBe(0);
        BasemapStatus.Unavailable.MaxZoom.ShouldBe(0);
        BasemapStatus.Attribution.ShouldBe("Contains OS data © Crown copyright and database right 2026");
    }
}
