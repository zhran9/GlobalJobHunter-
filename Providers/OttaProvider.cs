using GlobalJobHunter.Service.Models;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

// TODO: Implement Playwright/Selenium later due to Cloudflare
public sealed class OttaProvider : IJobProvider
{
    public string SourcePlatform => "Otta";

    private readonly ILogger<OttaProvider> _logger;

    public OttaProvider(ILogger<OttaProvider> logger) => _logger = logger;

    public Task<IEnumerable<JobPosting>> FetchJobsAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("[{Source}] requires a headless browser (Selenium/Playwright) or a paid proxy due to Cloudflare. Skipping for now.", SourcePlatform);
        return Task.FromResult(Enumerable.Empty<JobPosting>());
    }
}
