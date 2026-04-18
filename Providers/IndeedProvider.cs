using System.Text.Json;
using GlobalJobHunter.Service.Models;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

/// <summary>
/// Fetches .NET jobs from Arbeitnow's free public API.
/// Replaces IndeedProvider (Indeed RSS returns 404 — empty l= param no longer valid).
/// Arbeitnow: free, no auth, no bot detection, global remote jobs.
/// </summary>
public sealed class IndeedProvider : IJobProvider
{
    public string SourcePlatform => "Arbeitnow";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IndeedProvider> _logger;

    private const string ApiUrl = "https://www.arbeitnow.com/api/job-board-api?page=1";

    private static readonly string[] DotNetKeywords =
        [".net", "dotnet", "c#", "csharp", "asp.net", "blazor", "entity framework", "ef core"];

    public IndeedProvider(IHttpClientFactory httpClientFactory, ILogger<IndeedProvider> logger)
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

            if (!doc.RootElement.TryGetProperty("data", out var dataEl)
                || dataEl.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("[Arbeitnow] Unexpected response format.");
                return [];
            }

            var postings = new List<JobPosting>();

            foreach (var job in dataEl.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                var title   = GetString(job, "title") ?? string.Empty;
                var url     = GetString(job, "url");

                if (string.IsNullOrWhiteSpace(url)) continue;

                // Only keep .NET-related jobs (check title + tags array)
                if (!IsDotNetJob(title, job)) continue;

                var company  = GetString(job, "company_name") ?? "Unknown";
                var location = GetString(job, "location") ?? "Remote";

                // created_at is a Unix timestamp (seconds since epoch)
                DateTime postedDate = DateTime.UtcNow;
                if (job.TryGetProperty("created_at", out var createdEl)
                    && createdEl.ValueKind == JsonValueKind.Number
                    && createdEl.TryGetInt64(out var unixTs))
                {
                    postedDate = DateTimeOffset.FromUnixTimeSeconds(unixTs).UtcDateTime;
                }

                var isRemote = job.TryGetProperty("remote", out var remoteEl)
                               && remoteEl.ValueKind == JsonValueKind.True;

                postings.Add(new JobPosting
                {
                    Title          = title,
                    Company        = company,
                    Location       = isRemote ? "Remote" : location,
                    WorkModel      = isRemote ? "Remote" : "Unknown",
                    SourcePlatform = SourcePlatform,
                    Url            = url,
                    PostedDate     = postedDate,
                    Description    = null
                });
            }

            _logger.LogInformation("[Arbeitnow] Fetched {Count} .NET jobs.", postings.Count);
            return postings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Arbeitnow] Failed to fetch jobs.");
            return [];
        }
    }

    private static bool IsDotNetJob(string title, JsonElement job)
    {
        var titleLower = title.ToLowerInvariant();
        foreach (var kw in DotNetKeywords)
            if (titleLower.Contains(kw)) return true;

        // Also scan the tags array (e.g. ["csharp", "dotnet", "azure"])
        if (job.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagsEl.EnumerateArray())
            {
                if (tag.ValueKind != JsonValueKind.String) continue;
                var tagLower = tag.GetString()?.ToLowerInvariant() ?? string.Empty;
                foreach (var kw in DotNetKeywords)
                    if (tagLower.Contains(kw)) return true;
            }
        }

        return false;
    }

    private static string? GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
