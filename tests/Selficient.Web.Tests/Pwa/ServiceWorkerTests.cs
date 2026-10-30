using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Selficient.TestKit;
using Selficient.Web.Services;
using Selficient.Web.Tests.Pages;

namespace Selficient.Web.Tests.Pwa;

public sealed partial class ServiceWorkerTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task The_worker_is_served_fresh_every_time_with_both_placeholders_filled()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/sw.js", UriKind.Relative));
        string script = await response.Content.ReadAsStringAsync();
        string template = await client.GetStringAsync(new Uri("/sw.source.js", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().ShouldBe("text/javascript; charset=utf-8");
        response.Headers.CacheControl!.ToString().ShouldBe("no-cache");
        script.ShouldNotContain("__VERSION__");
        script.ShouldNotContain("__SHELL__");
        template.ShouldContain("'__VERSION__'");
        template.ShouldContain("const SHELL = __SHELL__;");
        Version().Match(script).Groups["version"].Value.ShouldMatch("^sw-[0-9a-f]{12}$");
    }

    [Fact]
    public async Task Pages_carry_the_shell_fingerprint_the_worker_is_built_from()
    {
        using HttpClient client = _factory.CreateClient();

        string page = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        string script = await client.GetStringAsync(new Uri("/sw.js", UriKind.Relative));

        string pageBuild = Regex.Match(page, "data-sw-build=\"(?<build>[^\"]+)\"").Groups["build"].Value;
        pageBuild.ShouldMatch("^sw-[0-9a-f]{12}$");
        Version().Match(script).Groups["version"].Value.ShouldBe(pageBuild);
        page.ShouldContain("<link rel=\"manifest\" href=\"/manifest.webmanifest\">");
        page.ShouldContain("rel=\"apple-touch-icon\"");
        page.ShouldContain("data-install hidden");
        page.ShouldContain("data-offline-banner");
        page.ShouldContain("Put Selficient on this iPhone or iPad");
    }

    [Fact]
    public async Task The_shell_lists_every_static_asset_and_every_asset_the_layout_references()
    {
        using HttpClient client = _factory.CreateClient();
        string script = await client.GetStringAsync(new Uri("/sw.js", UriKind.Relative));
        string page = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        string[] shell = JsonSerializer.Deserialize<string[]>(ShellList().Match(script).Groups["shell"].Value)!;
        string webRoot = _factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;

        foreach (string directory in ShellVersion.ShellDirectories)
        {
            string full = Path.Combine(webRoot, directory);
            foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                shell.ShouldContain("/" + Path.GetRelativePath(webRoot, file).Replace('\\', '/'));
            }
        }

        foreach (Match asset in LayoutAsset().Matches(page))
        {
            shell.ShouldContain(asset.Groups["path"].Value, customMessage: $"the layout references {asset.Groups["path"].Value}");
        }

        shell.ShouldContain("/manifest.webmanifest");
        shell.ShouldContain("/lib/maplibre/maplibre-gl.mjs");
        shell.ShouldContain("/fonts/glyphs/Open Sans Semibold/0-255.pbf");
        shell.ShouldContain("/img/icon-512-maskable.png");
        shell.ShouldBe(shell.Order(StringComparer.Ordinal).ToArray(), "a fixed order keeps the fingerprint stable");
        shell.ShouldNotContain("/sw.source.js");
    }

    [Fact]
    public async Task The_worker_never_caches_api_routes_except_tiles()
    {
        using HttpClient client = _factory.CreateClient();

        string script = await client.GetStringAsync(new Uri("/sw.js", UriKind.Relative));

        script.ShouldContain("if (url.pathname.startsWith('/api/') || url.pathname === '/health' || url.pathname === '/sw.js' || url.pathname.endsWith('.ics')) return;");
        script.ShouldContain("if (url.pathname.startsWith('/api/tiles/'))");
        script.ShouldContain("response.status !== 204");
        script.ShouldContain("Content-Disposition");
        script.ShouldContain("X-SW-Release");
        script.ShouldContain("cache: 'reload'");
        script.ShouldContain("Promise.allSettled");
        script.ShouldContain("fillShellGaps");
        script.ShouldContain("'sw:version?'");
        script.ShouldContain("if (url.pathname === PLAN_PATH)");
        script.ShouldNotContain("cache.addAll");
    }

    [Fact]
    public async Task The_manifest_is_ours_and_the_icons_exist()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage manifest = await client.GetAsync(new Uri("/manifest.webmanifest", UriKind.Relative));
        JsonDocument json = JsonDocument.Parse(await manifest.Content.ReadAsStringAsync());

        manifest.Content.Headers.ContentType!.MediaType.ShouldBe("application/manifest+json");
        manifest.Headers.CacheControl!.ToString().ShouldBe("no-cache");
        json.RootElement.GetProperty("id").GetString().ShouldBe("/");
        json.RootElement.GetProperty("scope").GetString().ShouldBe("/");
        json.RootElement.GetProperty("start_url").GetString().ShouldBe("/plan");
        json.RootElement.GetProperty("display").GetString().ShouldBe("standalone");
        json.RootElement.GetProperty("theme_color").GetString().ShouldBe("#1F5F4A");
        foreach (JsonElement icon in json.RootElement.GetProperty("icons").EnumerateArray())
        {
            using HttpResponseMessage image = await client.GetAsync(new Uri(icon.GetProperty("src").GetString()!, UriKind.Relative));
            image.StatusCode.ShouldBe(HttpStatusCode.OK);
            image.Content.Headers.ContentType!.MediaType.ShouldBe(icon.GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task The_offline_page_and_banner_say_what_works()
    {
        using HttpClient client = _factory.CreateClient();

        string offline = await client.GetStringAsync(new Uri("/offline", UriKind.Relative));
        string page = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        offline.ShouldContain("You are offline, which is rather the point");
        offline.ShouldContain("Your saved plan still opens from this device");
        offline.ShouldContain("href=\"/plan\"");
        page.ShouldContain("You are offline. Your saved plan, its ticks and the map you have already seen still work");
    }

    [Fact]
    public async Task The_shell_scripts_make_no_requests_of_their_own()
    {
        using HttpClient client = _factory.CreateClient();

        string shell = await client.GetStringAsync(new Uri("/js/shell.js", UriKind.Relative));
        string install = await client.GetStringAsync(new Uri("/js/install.js", UriKind.Relative));

        shell.ShouldNotContain("fetch(");
        shell.ShouldNotContain("setInterval");
        shell.ShouldContain("register(\"/sw.js\")");
        shell.ShouldContain("RELOAD_GUARD_MS = 120_000");
        install.ShouldNotContain("fetch(");
        install.ShouldContain("QUIET_DAYS = 30");
        install.ShouldContain("display-mode: standalone");
    }

    [Fact]
    public void The_shell_version_is_scanned_once_per_window_and_changes_when_a_file_does()
    {
        string root = Path.Combine(Path.GetTempPath(), $"selficient-shell-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "css"));
        File.WriteAllText(Path.Combine(root, "css", "app.css"), "body{}");
        File.WriteAllText(Path.Combine(root, "manifest.webmanifest"), "{}");
        IWebHostEnvironment environment = Substitute.For<IWebHostEnvironment>();
        environment.WebRootPath.Returns(root);
        ManualClock clock = new();
        ShellVersion shell = new(environment, clock);

        try
        {
            ShellVersion.Shell first = shell.Current();
            File.WriteAllText(Path.Combine(root, "css", "extra.css"), "p{}");
            ShellVersion.Shell within = shell.Current();
            clock.Advance(TimeSpan.FromSeconds(31));
            ShellVersion.Shell after = shell.Current();

            first.Urls.ShouldBe(["/css/app.css", "/manifest.webmanifest"]);
            within.ShouldBeSameAs(first, "inside the window the disk is not read again");
            after.Urls.ShouldBe(["/css/app.css", "/css/extra.css", "/manifest.webmanifest"]);
            after.Version.ShouldNotBe(first.Version);
            after.Version.ShouldMatch("^sw-[0-9a-f]{12}$");
            Should.Throw<ArgumentNullException>(() => new ShellVersion(null!, clock));
            Should.Throw<ArgumentNullException>(() => new ShellVersion(environment, null!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_shell_directory_and_a_missing_manifest_are_simply_absent()
    {
        string root = Path.Combine(Path.GetTempPath(), $"selficient-shell-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "js"));
        File.WriteAllText(Path.Combine(root, "js", "app.js"), "export {};");
        IWebHostEnvironment environment = Substitute.For<IWebHostEnvironment>();
        environment.WebRootPath.Returns(root);

        try
        {
            new ShellVersion(environment, new ManualClock()).Current().Urls.ShouldBe(["/js/app.js"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task The_plan_page_still_works_as_a_post_so_the_worker_can_keep_it()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage plan = await PlanPagesTests.PostRobsHouse(client);

        plan.StatusCode.ShouldBe(HttpStatusCode.OK);
        plan.Content.Headers.ContentDisposition.ShouldBeNull("the plan is a page, never an attachment, so the worker may keep it");
    }

    public void Dispose() => _factory.Dispose();

    [GeneratedRegex("const VERSION = \"(?<version>[^\"]+)\";")]
    private static partial Regex Version();

    [GeneratedRegex(@"const SHELL = (?<shell>\[[^\]]*\]);")]
    private static partial Regex ShellList();

    [GeneratedRegex("(?:href|src)=\"(?<path>/(?:css|js|lib|img|fonts)/[^\"?]+|/manifest\\.webmanifest)")]
    private static partial Regex LayoutAsset();
}
