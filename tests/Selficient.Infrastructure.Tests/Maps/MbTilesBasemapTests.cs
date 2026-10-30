using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Selficient.Application.Maps;
using Selficient.Infrastructure.Maps;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Maps;

public sealed class MbTilesBasemapTests : IDisposable
{
    private static readonly byte[] Raw = [0x0a, 0x05, 0x68, 0x6f, 0x75, 0x73, 0x65];
    private static readonly byte[] Gz = TestMbTiles.Gzipped(Raw);

    private readonly List<string> _archives = [];

    [Fact]
    public void An_absent_archive_is_unavailable_and_checked_again_after_the_retry_window()
    {
        ManualClock clock = new();
        string path = Path.Combine(Path.GetTempPath(), $"selficient-{Guid.NewGuid():N}.mbtiles");
        MbTilesBasemap store = Store(path, clock, retrySeconds: 30);

        store.Status().Available.ShouldBeFalse();
        _archives.Add(TestMbTiles.Create(TestMbTiles.ZoomstackMetadata));
        File.Move(_archives[0], path);
        _archives[0] = path;

        clock.Advance(TimeSpan.FromSeconds(10));
        store.Status().Available.ShouldBeFalse("the absence is remembered inside the window");
        clock.Advance(TimeSpan.FromSeconds(25));
        store.Status().Available.ShouldBeTrue("the disk is checked again once the window has passed");
    }

    [Fact]
    public void A_present_archive_reports_its_zoom_range_and_is_remembered()
    {
        string path = Archive(TestMbTiles.ZoomstackMetadata);
        MbTilesBasemap store = Store(path, new ManualClock());

        BasemapStatus first = store.Status();
        TestMbTiles.Delete(path);
        BasemapStatus second = store.Status();

        first.ShouldBe(new BasemapStatus(true, 0, 14));
        second.ShouldBeSameAs(first, "a present archive is not re-probed");
    }

    [Fact]
    public void Missing_zoom_metadata_falls_back_to_the_zoomstack_range()
    {
        string path = Archive(new Dictionary<string, string> { ["name"] = "test", ["minzoom"] = "not a number" });

        Store(path, new ManualClock()).Status().ShouldBe(new BasemapStatus(true, 0, 14));
    }

    [Fact]
    public void A_corrupt_archive_is_unavailable()
    {
        string path = TestMbTiles.CreateCorrupt();
        _archives.Add(path);

        Store(path, new ManualClock()).Status().ShouldBe(BasemapStatus.Unavailable);
    }

    [Fact]
    public async Task Tiles_are_read_with_a_parameterised_query_on_a_read_only_connection()
    {
        string path = Archive(TestMbTiles.ZoomstackMetadata, (1, 0, 0, Gz), (1, 1, 1, Raw), (2, 3, 3, []));
        MbTilesBasemap store = Store(path, new ManualClock());

        MbTilesBasemap.ConnectionString(path).ShouldContain("Mode=ReadOnly");
        await using (SqliteConnection readOnly = new(MbTilesBasemap.ConnectionString(path)))
        {
            await readOnly.OpenAsync();
            await using SqliteCommand write = readOnly.CreateCommand();
            write.CommandText = "DELETE FROM tiles";
            await Should.ThrowAsync<SqliteException>(() => write.ExecuteNonQueryAsync());
        }

        MapTile? gz = await store.GetTileAsync(TileAddress.TryCreate(1, 0, 0)!.Value, CancellationToken.None);
        MapTile? raw = await store.GetTileAsync(TileAddress.TryCreate(1, 1, 1)!.Value, CancellationToken.None);
        MapTile? empty = await store.GetTileAsync(TileAddress.TryCreate(2, 3, 3)!.Value, CancellationToken.None);
        MapTile? miss = await store.GetTileAsync(TileAddress.TryCreate(1, 1, 0)!.Value, CancellationToken.None);

        gz!.Value.Data.ToArray().ShouldBe(Gz);
        gz.Value.IsGzipped.ShouldBeTrue();
        raw!.Value.Data.ToArray().ShouldBe(Raw);
        raw.Value.IsGzipped.ShouldBeFalse();
        empty.ShouldBeNull("an empty blob is nothing to draw");
        miss.ShouldBeNull();
    }

    [Fact]
    public async Task The_row_is_flipped_so_the_north_is_at_the_top()
    {
        // XYZ (1, 0, 0) is the north-west quarter; a real archive stores it as TMS row 1.
        string path = Archive(TestMbTiles.ZoomstackMetadata, (1, 0, 0, Raw));
        await using SqliteConnection connection = new(MbTilesBasemap.ConnectionString(path));
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT tile_row FROM tiles WHERE zoom_level = 1 AND tile_column = 0";

        (await command.ExecuteScalarAsync()).ShouldBe(1L);
        (await Store(path, new ManualClock()).GetTileAsync(TileAddress.TryCreate(1, 0, 0)!.Value, CancellationToken.None)).ShouldNotBeNull();
        (await Store(path, new ManualClock()).GetTileAsync(TileAddress.TryCreate(1, 0, 1)!.Value, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Tiles_are_not_read_when_the_basemap_is_absent()
    {
        MbTilesBasemap store = Store(Path.Combine(Path.GetTempPath(), $"selficient-{Guid.NewGuid():N}.mbtiles"), new ManualClock());

        (await store.GetTileAsync(TileAddress.TryCreate(1, 0, 0)!.Value, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_tile_read_that_fails_is_a_miss()
    {
        string path = Archive(TestMbTiles.ZoomstackMetadata, (1, 0, 0, Raw));
        MbTilesBasemap store = Store(path, new ManualClock());
        store.Status().Available.ShouldBeTrue();
        TestMbTiles.Delete(path);
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11]);

        (await store.GetTileAsync(TileAddress.TryCreate(1, 0, 0)!.Value, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public void Relative_paths_resolve_against_the_content_root_or_the_app_folder()
    {
        IHostEnvironment rooted = Substitute.For<IHostEnvironment>();
        rooted.ContentRootPath.Returns(Path.GetTempPath());
        IHostEnvironment unrooted = Substitute.For<IHostEnvironment>();
        unrooted.ContentRootPath.Returns((string?)null!);

        new MbTilesBasemap(Options.Create(new MapOptions()), rooted, new ManualClock(), NullLogger<MbTilesBasemap>.Instance)
            .ArchivePath.ShouldBe(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tiles", "os-open-zoomstack.mbtiles")));
        new MbTilesBasemap(Options.Create(new MapOptions()), unrooted, new ManualClock(), NullLogger<MbTilesBasemap>.Instance)
            .ArchivePath.ShouldBe(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tiles", "os-open-zoomstack.mbtiles")));
    }

    [Fact]
    public void Guards_reject_nulls()
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        IOptions<MapOptions> options = Options.Create(new MapOptions());

        Should.Throw<ArgumentNullException>(() => new MbTilesBasemap(null!, environment, new ManualClock(), NullLogger<MbTilesBasemap>.Instance));
        Should.Throw<ArgumentNullException>(() => new MbTilesBasemap(options, null!, new ManualClock(), NullLogger<MbTilesBasemap>.Instance));
        Should.Throw<ArgumentNullException>(() => new MbTilesBasemap(options, environment, null!, NullLogger<MbTilesBasemap>.Instance));
        Should.Throw<ArgumentNullException>(() => new MbTilesBasemap(options, environment, new ManualClock(), null!));
    }

    public void Dispose()
    {
        foreach (string path in _archives)
        {
            TestMbTiles.Delete(path);
        }
    }

    private string Archive(IReadOnlyDictionary<string, string>? metadata, params (int Z, int X, int Y, byte[] Data)[] tiles)
    {
        string path = TestMbTiles.Create(metadata, tiles);
        _archives.Add(path);
        return path;
    }

    private static MbTilesBasemap Store(string path, TimeProvider clock, int retrySeconds = 60)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        return new MbTilesBasemap(Options.Create(new MapOptions { MbTilesPath = path, RetryAfterSeconds = retrySeconds }), environment, clock, NullLogger<MbTilesBasemap>.Instance);
    }
}
