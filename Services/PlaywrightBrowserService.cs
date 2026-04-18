using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GlobalJobHunter.Service.Services;

public sealed class PlaywrightBrowserService : IPlaywrightBrowserService
{
    private readonly ILogger<PlaywrightBrowserService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _disposed;

    // Pool of realistic Chrome UAs (Windows + Mac + Linux, Chrome 122–124)
    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
    ];

    public PlaywrightBrowserService(ILogger<PlaywrightBrowserService> logger)
    {
        _logger = logger;
    }

    public async Task<IBrowserContext> CreateStealthContextAsync(CancellationToken ct = default)
    {
        await EnsureBrowserAsync(ct);

        var ua = UserAgents[Random.Shared.Next(UserAgents.Length)];
        var width = Random.Shared.Next(1280, 1921);
        var height = Random.Shared.Next(768, 1081);

        var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = ua,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            TimezoneId = "America/New_York",
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["sec-ch-ua"] = "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"",
                ["sec-ch-ua-mobile"] = "?0",
                ["sec-ch-ua-platform"] = "\"Windows\"",
                ["Accept-Language"] = "en-US,en;q=0.9",
                ["Accept-Encoding"] = "gzip, deflate, br"
            }
        });

        // Mask automation fingerprints
        await context.AddInitScriptAsync(@"
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            Object.defineProperty(navigator, 'plugins', {
                get: () => [1, 2, 3, 4, 5].map(() => ({ name: 'Chrome PDF Plugin', filename: 'internal-pdf-viewer', description: 'Portable Document Format' }))
            });
            Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] });
            window.chrome = { runtime: {} };
        ");

        return context;
    }

    private async Task EnsureBrowserAsync(CancellationToken ct)
    {
        // Fast path — already initialized
        if (_browser != null) return;

        await _initLock.WaitAsync(ct);
        try
        {
            // Double-checked locking
            if (_browser != null) return;

            _logger.LogInformation("[PlaywrightBrowserService] Launching Chromium...");

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args =
                [
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-infobars",
                    "--disable-extensions",
                    "--disable-gpu",
                    "--window-size=1920,1080"
                ]
            });

            _logger.LogInformation("[PlaywrightBrowserService] Chromium launched successfully.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_browser != null)
            {
                await _browser.CloseAsync();
                _logger.LogInformation("[PlaywrightBrowserService] Browser closed.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PlaywrightBrowserService] Error closing browser.");
        }

        try
        {
            _playwright?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PlaywrightBrowserService] Error disposing Playwright.");
        }

        _initLock.Dispose();
    }
}
