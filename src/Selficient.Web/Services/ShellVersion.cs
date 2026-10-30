using System.Security.Cryptography;
using System.Text;

namespace Selficient.Web.Services;

/// <summary>
/// The deployed shell's identity: every static asset under the shell directories fingerprinted
/// into one version string. The service worker is this version (its cache name) and every page
/// carries it as <c>data-sw-build</c>, so a device can never keep running one deploy's modules
/// against another deploy's pages without noticing. Lifted from SiteWise (Oct 2026).
/// </summary>
public sealed class ShellVersion
{
    /// <summary>The directories under wwwroot that make up the shell.</summary>
    public static IReadOnlyList<string> ShellDirectories { get; } = ["css", "js", "lib", "img", "fonts"];

    /// <summary>How long a scan is kept before the disk is read again.</summary>
    public static TimeSpan ScanLifetime { get; } = TimeSpan.FromSeconds(30);

    private readonly IWebHostEnvironment _environment;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private Shell? _shell;
    private long _scannedAt;

    /// <summary>Creates the service.</summary>
    public ShellVersion(IWebHostEnvironment environment, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(clock);
        _environment = environment;
        _clock = clock;
    }

    /// <summary>A version string and the URLs it fingerprints.</summary>
    /// <param name="Version">"sw-" and twelve hex characters.</param>
    /// <param name="Urls">Every shell asset, root-relative, in a fixed order.</param>
    public sealed record Shell(string Version, IReadOnlyList<string> Urls);

    /// <summary>The current shell; the scan is cached briefly so steady state costs nothing.</summary>
    public Shell Current()
    {
        long now = _clock.GetTimestamp();
        lock (_gate)
        {
            if (_shell is not null && _clock.GetElapsedTime(_scannedAt, now) < ScanLifetime)
            {
                return _shell;
            }

            _shell = Scan();
            _scannedAt = now;
            return _shell;
        }
    }

    private Shell Scan()
    {
        string root = _environment.WebRootPath;
        List<(string Url, long Length, long Ticks)> assets = [];

        foreach (string directory in ShellDirectories)
        {
            string full = Path.Combine(root, directory);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                FileInfo info = new(path);
                string url = "/" + Path.GetRelativePath(root, path).Replace('\\', '/');
                assets.Add((url, info.Length, info.LastWriteTimeUtc.Ticks));
            }
        }

        FileInfo manifest = new(Path.Combine(root, "manifest.webmanifest"));
        if (manifest.Exists)
        {
            assets.Add(("/manifest.webmanifest", manifest.Length, manifest.LastWriteTimeUtc.Ticks));
        }

        assets.Sort(static (a, b) => string.CompareOrdinal(a.Url, b.Url));

        string fingerprint = string.Join('\n', assets.Select(static a => $"{a.Url}|{a.Length}|{a.Ticks}"));
        string version = "sw-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..12].ToLowerInvariant();
        return new Shell(version, assets.Select(static a => a.Url).ToList());
    }
}
