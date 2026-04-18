using System.Text.Json;
using GlobalJobHunter.Service.Models;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

public sealed class HimalayasProvider : IJobProvider
{
    public string SourcePlatform => "Himalayas";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HimalayasProvider> _logger;

    private const string ApiUrl = "https://himalayas.app/jobs/api?limit=50";

    public HimalayasProvider(IHttpClientFactory httpClientFactory, ILogger<HimalayasProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<JobPosting>> FetchJobsAsync(CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(ApiUrl, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var jobsElement = doc.RootElement.GetProperty("jobs");
            var postings = new List<JobPosting>();

            foreach (var job in jobsElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                var title = job.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "Unknown" : "Unknown";
                var companyName = job.TryGetProperty("companyName", out var companyProp) ? companyProp.GetString() ?? "Unknown" : "Unknown";
                var url = job.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;

                DateTime postedDate = DateTime.UtcNow;
                if (job.TryGetProperty("pubDate", out var pubDateElement) && pubDateElement.ValueKind != JsonValueKind.Null)
                {
                    var pubDateValue = pubDateElement.GetInt64();
                    postedDate = DateTimeOffset.FromUnixTimeSeconds(pubDateValue).UtcDateTime;
                }

                if (string.IsNullOrWhiteSpace(url))
                    continue;

                postings.Add(new JobPosting
                {
                    Title = title,
                    Company = companyName,
                    Location = "Remote",
                    WorkModel = "Remote",
                    SourcePlatform = SourcePlatform,
                    Url = url,
                    PostedDate = postedDate,
                    Description = null 
                });
            }

            _logger.LogInformation("[Himalayas] Fetched {Count} jobs.", postings.Count);
            return postings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Himalayas] Failed to fetch jobs.");
            return Enumerable.Empty<JobPosting>();
        }
    }
}
