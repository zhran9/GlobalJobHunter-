using GlobalJobHunter.Service.Models;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

// TODO: Implement Playwright/Selenium later due to Cloudflare
public sealed class WellfoundProvider : IJobProvider
{
    public string SourcePlatform => "Wellfound";

    private readonly ILogger<WellfoundProvider> _logger;

    public WellfoundProvider(ILogger<WellfoundProvider> logger) => _logger = logger;

    public Task<IEnumerable<JobPosting>> FetchJobsAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("[{Source}] requires a headless browser (Selenium/Playwright) or a paid proxy due to Cloudflare. Skipping for now.", SourcePlatform);
        return Task.FromResult(Enumerable.Empty<JobPosting>());
    }
}
