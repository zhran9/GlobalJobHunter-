using System.Text.Json;
using GlobalJobHunter.Service.Models;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

/// <summary>
/// Fetches remote .NET jobs from RemoteOK's free public API.
/// Replaces WellfoundProvider (Wellfound requires Cloudflare bypass + paid proxy on cloud IPs).
/// RemoteOK: free, no auth, returns JSON array.
/// </summary>
public sealed class WellfoundProvider : IJobProvider
{
    public string SourcePlatform => "RemoteOK";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WellfoundProvider> _logger;

    // "csharp" is the active tag on RemoteOK for C#/.NET jobs; "dotnet" exists but rarely has listings
    private const string ApiUrl = "https://remoteok.com/api?tag=csharp";

    public WellfoundProvider(IHttpClientFactory httpClientFactory, ILogger<WellfoundProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<JobPosting>> FetchJobsAsync(CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();

            // RemoteOK blocks the default .NET HttpClient user-agent
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var response = await client.GetAsync(ApiUrl, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("[RemoteOK] Unexpected response format.");
                return [];
            }

            var postings = new List<JobPosting>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                // First element is a metadata object, not a job — skip it
                if (!item.TryGetProperty("slug", out _)) continue;

                var title   = GetString(item, "position");
                var company = GetString(item, "company");
                var url     = GetString(item, "url");
                var location = GetString(item, "location") ?? "Remote";

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                    continue;

                // date field is ISO 8601: "2024-04-17T10:30:00Z"
                DateTime postedDate = DateTime.UtcNow;
                if (item.TryGetProperty("date", out var dateEl)
                    && dateEl.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(dateEl.GetString(), null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                        postedDate = parsed.ToUniversalTime();
                }

                postings.Add(new JobPosting
                {
                    Title          = title,
                    Company        = company ?? "Unknown",
                    Location       = location,
                    WorkModel      = "Remote",
                    SourcePlatform = SourcePlatform,
                    Url            = url,
                    PostedDate     = postedDate,
                    Description    = GetString(item, "description")
                });
            }

            _logger.LogInformation("[RemoteOK] Fetched {Count} jobs.", postings.Count);
            return postings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RemoteOK] Failed to fetch jobs.");
            return [];
        }
    }

    private static string? GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
