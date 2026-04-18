using System.Text.Json;
using GlobalJobHunter.Service.Models;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

/// <summary>
/// Fetches remote .NET jobs from WorkingNomads' free public API.
/// Replaces HimalayasProvider (Himalayas returns 403 even with browser headers — deeper bot detection).
/// WorkingNomads: free, no auth, returns JSON array for the back-end-programming category.
/// </summary>
public sealed class HimalayasProvider : IJobProvider
{
    public string SourcePlatform => "WorkingNomads";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HimalayasProvider> _logger;

    private const string ApiUrl =
        "https://www.workingnomads.com/api/exposed_jobs/?category=back-end-programming";

    private static readonly string[] DotNetKeywords =
        [".net", "dotnet", "c#", "csharp", "asp.net", "blazor", "entity framework", "ef core"];

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
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var response = await client.GetAsync(ApiUrl, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("[WorkingNomads] Unexpected response format.");
                return [];
            }

            var postings = new List<JobPosting>();

            foreach (var job in doc.RootElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                var title = GetString(job, "title") ?? string.Empty;
                var url   = GetString(job, "url");

                if (string.IsNullOrWhiteSpace(url)) continue;

                // Category includes many languages — keep only .NET jobs
                var titleLower = title.ToLowerInvariant();
                if (!DotNetKeywords.Any(kw => titleLower.Contains(kw))) continue;

                var company  = GetString(job, "company") ?? "Unknown";
                var location = GetString(job, "location") ?? "Remote";

                // pub_date is ISO 8601: "2024-04-17T10:30:00Z"
                DateTime postedDate = DateTime.UtcNow;
                if (job.TryGetProperty("pub_date", out var pubDateEl)
                    && pubDateEl.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(pubDateEl.GetString(), null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                        postedDate = parsed.ToUniversalTime();
                }

                postings.Add(new JobPosting
                {
                    Title          = title,
                    Company        = company,
                    Location       = location,
                    WorkModel      = "Remote",
                    SourcePlatform = SourcePlatform,
                    Url            = url,
                    PostedDate     = postedDate,
                    Description    = null
                });
            }

            _logger.LogInformation("[WorkingNomads] Fetched {Count} .NET jobs.", postings.Count);
            return postings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkingNomads] Failed to fetch jobs.");
            return [];
        }
    }

    private static string? GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
