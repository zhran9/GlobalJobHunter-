using Microsoft.Playwright;

namespace GlobalJobHunter.Service.Services;

public interface IPlaywrightBrowserService : IAsyncDisposable
{
    Task<IBrowserContext> CreateStealthContextAsync(CancellationToken ct = default);
}
