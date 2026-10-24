using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>
/// Renders pages in headless Chromium for shops that block plain HTTP clients.
/// One browser is shared for the process lifetime; each shop run gets a fresh context.
/// </summary>
internal sealed class BrowserPageFetcher(IOptions<CatalogOptions> options, ILogger<BrowserPageFetcher> logger)
    : IPageFetcher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;

    public async Task<string> GetPageAsync(Uri url, CancellationToken cancellationToken)
    {
        var context = await GetContextAsync();
        var page = await context.NewPageAsync();
        try
        {
            var response = await page.GotoAsync(url.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = options.Value.Browser.NavigationTimeoutMs,
            });

            if (response is { Ok: false })
            {
                throw new HttpRequestException($"Browser got HTTP {response.Status} for {url}");
            }

            // Give client-rendered shops a moment to inject their JSON-LD.
            await page.WaitForTimeoutAsync(1500);
            cancellationToken.ThrowIfCancellationRequested();
            return await page.ContentAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    public async Task<byte[]> GetFileAsync(Uri url, CancellationToken cancellationToken)
    {
        var context = await GetContextAsync();
        var response = await context.APIRequest.GetAsync(url.ToString());
        if (!response.Ok)
        {
            throw new HttpRequestException($"Browser got HTTP {response.Status} for {url}");
        }

        return await response.BodyAsync();
    }

    /// <summary>Drops cookies and storage so the next shop starts clean.</summary>
    public async Task ResetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is not null)
            {
                await _context.CloseAsync();
                _context = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IBrowserContext> GetContextAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is not null)
            {
                return _context;
            }

            if (_browser is null)
            {
                var browserOptions = options.Value.Browser;
                logger.LogInformation("Starting headless Chromium for browser collection");
                _playwright ??= await Playwright.CreateAsync();
                try
                {
                    _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = browserOptions.Headless,
                        ExecutablePath = string.IsNullOrWhiteSpace(browserOptions.ExecutablePath) ? null : browserOptions.ExecutablePath,
                    });
                }
                catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
                {
                    throw new CollectorUnusableException(
                        "BrowserJsonLd: Chromium is not installed. Run `dotnet run --project src/SmartStylist.Worker -- install-browser` once.", ex);
                }
            }

            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = options.Value.UserAgent,
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            });
            return _context;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
        _gate.Dispose();
    }
}
