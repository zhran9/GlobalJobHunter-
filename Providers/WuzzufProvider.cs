using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using GlobalJobHunter.Service.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

public sealed class WuzzufProvider : IJobProvider
{
    public string SourcePlatform => "Wuzzuf";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WuzzufProvider> _logger;

    private const string SearchUrl = "https://wuzzuf.net/search/jobs/?q=.net";
    private const string BaseUrl = "https://wuzzuf.net";

    public WuzzufProvider(IHttpClientFactory httpClientFactory, ILogger<WuzzufProvider> logger)
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
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            var html = await client.GetStringAsync(SearchUrl, ct);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Wuzzuf job cards are inside <div> with class containing "css-" patterns
            // The main job card container has an <h2> with an <a> tag for the title
            var jobCards = doc.DocumentNode.SelectNodes("//div[contains(@class,'css-')]//h2/a[@href]");

            if (jobCards is null || jobCards.Count == 0)
            {
                _logger.LogWarning("[Wuzzuf] No job cards found. Page structure may have changed.");
                return Enumerable.Empty<JobPosting>();
            }

            var postings = new List<JobPosting>();

            foreach (var anchor in jobCards)
            {
                ct.ThrowIfCancellationRequested();

                var title = HttpUtility.HtmlDecode(anchor.InnerText?.Trim() ?? "Unknown");
                var href = anchor.GetAttributeValue("href", "");
                if (string.IsNullOrWhiteSpace(href))
                    continue;

                var url = href.StartsWith("http") ? href : $"{BaseUrl}{href}";

                // Navigate up to the job card container to find company, location, date
                var cardContainer = anchor.ParentNode?.ParentNode?.ParentNode;
                if (cardContainer is null)
                    continue;

                // Company name - typically in a <a> or <span> after the title section
                var companyNode = cardContainer.SelectSingleNode(".//a[contains(@href,'/jobs/careers')]")
                                  ?? cardContainer.SelectSingleNode(".//span[contains(@class,'css-')]");
                var company = HttpUtility.HtmlDecode(companyNode?.InnerText?.Trim() ?? "Unknown");

                // Location - look for location-related span
                var locationNode = cardContainer.SelectSingleNode(".//span[contains(@class,'css-') and contains(text(),',')]")
                                   ?? cardContainer.SelectSingleNode(".//span[contains(@class,'css-')]");
                var location = HttpUtility.HtmlDecode(locationNode?.InnerText?.Trim());

                // Date - look for relative date text like "2 days ago"
                var dateNode = cardContainer.SelectSingleNode(".//*[contains(text(),'ago') or contains(text(),'day') or contains(text(),'hour')]");
                var dateText = dateNode?.InnerText?.Trim();
                var postedDate = ParseRelativeDate(dateText);

                postings.Add(new JobPosting
                {
                    Title = title,
                    Company = company,
                    Location = location,
                    WorkModel = DetermineWorkModel(location),
                    SourcePlatform = SourcePlatform,
                    Url = url,
                    PostedDate = postedDate,
                    Description = null // Wuzzuf list page doesn't include full descriptions
                });
            }

            _logger.LogInformation("[Wuzzuf] Fetched {Count} jobs.", postings.Count);
            return postings;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("[Wuzzuf] Received 403 Forbidden. Site may be blocking scrapers.");
            return Enumerable.Empty<JobPosting>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Wuzzuf] Failed to fetch jobs.");
            return Enumerable.Empty<JobPosting>();
        }
    }

    private static DateTime ParseRelativeDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DateTime.UtcNow;

        text = text.ToLowerInvariant().Trim();

        var match = Regex.Match(text, @"(\d+)\s*(second|minute|hour|day|week|month)s?\s*ago");
        if (!match.Success)
            return DateTime.UtcNow;

        var value = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups[2].Value;

        return unit switch
        {
            "second" => DateTime.UtcNow.AddSeconds(-value),
            "minute" => DateTime.UtcNow.AddMinutes(-value),
            "hour" => DateTime.UtcNow.AddHours(-value),
            "day" => DateTime.UtcNow.AddDays(-value),
            "week" => DateTime.UtcNow.AddDays(-value * 7),
            "month" => DateTime.UtcNow.AddMonths(-value),
            _ => DateTime.UtcNow
        };
    }

    private static string? DetermineWorkModel(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        var lower = location.ToLowerInvariant();
        if (lower.Contains("remote")) return "Remote";
        if (lower.Contains("hybrid")) return "Hybrid";
        return "Onsite";
    }
}
