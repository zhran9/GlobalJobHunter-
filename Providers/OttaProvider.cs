using System.Text.Json;
using GlobalJobHunter.Service.Models;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

/// <summary>
/// Fetches remote .NET jobs from Jobicy's free public API.
/// Replaces OttaProvider (Otta.com was acquired by Greenhouse and shut down).
/// Jobicy: free, no auth, returns JSON with a "jobs" array.
/// </summary>
public sealed class OttaProvider : IJobProvider
{
    public string SourcePlatform => "Jobicy";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OttaProvider> _logger;

    // industry= param returns 400 — tag alone is enough
    private const string ApiUrl = "https://jobicy.com/api/v2/remote-jobs?count=50";

    private static readonly string[] DotNetKeywords =
        [".net", "dotnet", "c#", "csharp", "asp.net", "blazor", "entity framework", "ef core"];

    public OttaProvider(IHttpClientFactory httpClientFactory, ILogger<OttaProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<JobPosting>> FetchJobsAsync(CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var response = await client.GetAsync(ApiUrl, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("jobs", out var jobsElement)
                || jobsElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("[Jobicy] Unexpected response format.");
                return [];
            }

            var postings = new List<JobPosting>();

            foreach (var job in jobsElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                var title       = GetString(job, "jobTitle");
                var company     = GetString(job, "companyName");
                var url         = GetString(job, "url");
                var location    = GetString(job, "jobGeo") ?? "Remote";

                if (string.IsNullOrWhiteSpace(url)) continue;

                // No tag filter in URL — filter client-side so a bad tag never causes 400
                var titleLower = (title ?? string.Empty).ToLowerInvariant();
                if (!DotNetKeywords.Any(kw => titleLower.Contains(kw))) continue;

                // pubDate format: "2024-04-17 14:30:00" (no timezone — treat as UTC)
                DateTime postedDate = DateTime.UtcNow;
                if (job.TryGetProperty("pubDate", out var pubDateEl)
                    && pubDateEl.ValueKind == JsonValueKind.String)
                {
                    var str = pubDateEl.GetString();
                    if (DateTime.TryParse(str, null,
                            System.Globalization.DateTimeStyles.AssumeUniversal
                            | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out var parsed))
                        postedDate = parsed;
                }

                postings.Add(new JobPosting
                {
                    Title          = title ?? "Unknown",
                    Company        = company ?? "Unknown",
                    Location       = location,
                    WorkModel      = "Remote",
                    SourcePlatform = SourcePlatform,
                    Url            = url,
                    PostedDate     = postedDate,
                    Description    = null
                });
            }

            _logger.LogInformation("[Jobicy] Fetched {Count} jobs.", postings.Count);
            return postings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Jobicy] Failed to fetch jobs.");
            return [];
        }
    }

    private static string? GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
