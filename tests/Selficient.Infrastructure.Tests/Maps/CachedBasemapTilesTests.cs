using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Selficient.Application.Maps;
using Selficient.Infrastructure.Maps;

namespace Selficient.Infrastructure.Tests.Maps;

public sealed class CachedBasemapTilesTests
{
    private static readonly byte[] Bytes = [1, 2, 3];
    private static readonly TileAddress Hit = TileAddress.TryCreate(3, 1, 2)!.Value;
    private static readonly TileAddress Sea = TileAddress.TryCreate(3, 0, 0)!.Value;

    [Fact]
    public async Task Hits_and_misses_are_both_served_from_memory_after_the_first_read()
    {
        IBasemapTiles inner = Substitute.For<IBasemapTiles>();
        inner.GetTileAsync(Hit, Arg.Any<CancellationToken>()).Returns(new MapTile(Bytes));
        inner.GetTileAsync(Sea, Arg.Any<CancellationToken>()).Returns((MapTile?)null);
        using ServiceProvider provider = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        CachedBasemapTiles cached = new(inner, provider.GetRequiredService<HybridCache>());

        MapTile? first = await cached.GetTileAsync(Hit, CancellationToken.None);
        MapTile? second = await cached.GetTileAsync(Hit, CancellationToken.None);
        MapTile? sea = await cached.GetTileAsync(Sea, CancellationToken.None);
        MapTile? seaAgain = await cached.GetTileAsync(Sea, CancellationToken.None);

        first!.Value.Data.ToArray().ShouldBe(Bytes);
        second!.Value.Data.ToArray().ShouldBe(Bytes);
        sea.ShouldBeNull();
        seaAgain.ShouldBeNull();
        await inner.Received(1).GetTileAsync(Hit, Arg.Any<CancellationToken>());
        await inner.Received(1).GetTileAsync(Sea, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Status_passes_straight_through_and_nulls_are_refused()
    {
        IBasemapTiles inner = Substitute.For<IBasemapTiles>();
        inner.Status().Returns(new BasemapStatus(true, 0, 14));
        using ServiceProvider provider = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        HybridCache cache = provider.GetRequiredService<HybridCache>();

        new CachedBasemapTiles(inner, cache).Status().ShouldBe(new BasemapStatus(true, 0, 14));
        Should.Throw<ArgumentNullException>(() => new CachedBasemapTiles(null!, cache));
        Should.Throw<ArgumentNullException>(() => new CachedBasemapTiles(inner, null!));
    }
}
