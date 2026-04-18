using System.Globalization;
using System.Web;
using GlobalJobHunter.Service.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

/// <summary>
/// Uses LinkedIn's unauthenticated <c>jobs-guest</c> API endpoint which returns
/// an HTML fragment of job cards — no auth or Playwright required.
/// f_TPR=r172800 → last 172 800 seconds (48 hours), matching RecencyHours in Worker.
/// </summary>
public sealed class LinkedInProvider : IJobProvider
{
    public string SourcePlatform => "LinkedIn";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LinkedInProvider> _logger;

    private const string ApiUrl =
        "https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search" +
        "?keywords=.net+developer&location=&f_TPR=r172800&start=0";

    public LinkedInProvider(IHttpClientFactory httpClientFactory, ILogger<LinkedInProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<JobPosting>> FetchJobsAsync(CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();

            // These headers are required — without them LinkedIn returns 400/403
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Referer", "https://www.linkedin.com/jobs/search/");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Requested-With", "XMLHttpRequest");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Li-Lang", "en_US");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept", "text/html,*/*;q=0.8");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept-Language", "en-US,en;q=0.9");

            var response = await client.GetAsync(ApiUrl, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[LinkedIn] jobs-guest API returned {Status}.", (int)response.StatusCode);
                return [];
            }

            var html = await response.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(html))
            {
                _logger.LogWarning("[LinkedIn] Empty response from jobs-guest API.");
                return [];
            }

            var postings = ParseJobCards(html);
            _logger.LogInformation("[LinkedIn] Fetched {Count} jobs from jobs-guest API.", postings.Count);
            return postings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LinkedIn] Failed to fetch jobs.");
            return [];
        }
    }

    private List<JobPosting> ParseJobCards(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // The fragment is a flat list of <li> elements
        var liNodes = doc.DocumentNode.SelectNodes("//li");
        if (liNodes is null || liNodes.Count == 0)
        {
            _logger.LogWarning("[LinkedIn] No <li> elements found in jobs-guest response.");
            return [];
        }

        var postings = new List<JobPosting>();

        foreach (var li in liNodes)
        {
            try
            {
                var titleNode = li.SelectSingleNode(
                    ".//h3[contains(@class,'base-search-card__title')]");
                var companyNode = li.SelectSingleNode(
                    ".//h4[contains(@class,'base-search-card__subtitle')]");
                var locationNode = li.SelectSingleNode(
                    ".//span[contains(@class,'job-search-card__location')]");
                var linkNode = li.SelectSingleNode(
                    ".//a[contains(@class,'base-card__full-link')][@href]");
                var timeNode = li.SelectSingleNode(".//time[@datetime]");

                var titleText = HttpUtility.HtmlDecode(titleNode?.InnerText?.Trim());
                if (string.IsNullOrWhiteSpace(titleText)) continue;

                var company  = HttpUtility.HtmlDecode(companyNode?.InnerText?.Trim()) ?? "Unknown";
                var location = HttpUtility.HtmlDecode(locationNode?.InnerText?.Trim());

                // Clean URL — strip tracking params
                var rawUrl = linkNode?.GetAttributeValue("href", "")?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(rawUrl)) continue;
                var cleanUrl = StripQueryString(rawUrl);

                // Parse ISO 8601 datetime attribute
                DateTime postedDate = DateTime.UtcNow;
                var datetimeAttr = timeNode?.GetAttributeValue("datetime", "");
                if (!string.IsNullOrWhiteSpace(datetimeAttr)
                    && DateTime.TryParse(datetimeAttr, null, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    postedDate = parsed.ToUniversalTime();
                }

                postings.Add(new JobPosting
                {
                    Title          = titleText,
                    Company        = company,
                    Location       = location,
                    WorkModel      = DetermineWorkModel(location),
                    SourcePlatform = SourcePlatform,
                    Url            = cleanUrl,
                    PostedDate     = postedDate,
                    Description    = null   // List page doesn't include descriptions
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[LinkedIn] Failed to parse a job card. Skipping.");
            }
        }

        return postings;
    }

    private static string StripQueryString(string url)
    {
        var idx = url.IndexOf('?');
        return idx > 0 ? url[..idx] : url;
    }

    private static string DetermineWorkModel(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return "Unknown";
        var lower = location.ToLowerInvariant();
        if (lower.Contains("remote")) return "Remote";
        if (lower.Contains("hybrid")) return "Hybrid";
        if (lower.Contains("on-site") || lower.Contains("onsite")) return "Onsite";
        return "Unknown";
    }
}
