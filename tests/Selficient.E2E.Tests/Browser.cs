using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Selficient.TestKit;

namespace Selficient.E2E.Tests;

/// <summary>
/// One Kestrel-hosted application on the fakes and one Chromium for the whole run. Every test
/// gets its own context at phone width, so storage never leaks between journeys. The browser is
/// installed on first use if it is missing; the workflows install it with its system
/// dependencies beforehand.
/// </summary>
public sealed class Browser : IAsyncLifetime
{
    private SelficientWebApplicationFactory? _factory;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>Where the application answers.</summary>
    public string Origin { get; private set; } = string.Empty;

    /// <summary>A fresh context at phone width.</summary>
    public Task<IBrowserContext> PhoneAsync() => SizedAsync(390, 844, mobile: true, theme: "light");

    /// <summary>A fresh context at the given size, as a phone or a desktop, in one theme.</summary>
    public async Task<IBrowserContext> SizedAsync(int width, int height, bool mobile, string theme)
    {
        IBrowserContext context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            DeviceScaleFactor = mobile ? 3 : 1,
            IsMobile = mobile,
            HasTouch = mobile,
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            Locale = "en-GB",
            TimezoneId = "Europe/London",
            BaseURL = Origin,
            AcceptDownloads = true,
            ServiceWorkers = ServiceWorkerPolicy.Allow,
        });
        context.SetDefaultTimeout(15_000);
        return context;
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _factory = new SelficientWebApplicationFactory()
            .WithSetting("RateLimits:PlanPerMinute", "10000")
            .WithSetting("RateLimits:AiPerMinute", "1000")
            .WithSetting("RateLimits:ApiPerMinute", "10000");
        Origin = _factory.StartOnKestrel();

        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
        {
            throw new InvalidOperationException("Chromium could not be installed for Playwright.");
        }

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        _factory?.Dispose();
    }
}

/// <summary>The collection that shares the browser.</summary>
[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<Browser>
{
    /// <summary>The collection name.</summary>
    public const string Name = "browser";
}
