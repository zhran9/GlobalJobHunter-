using System.ServiceModel.Syndication;
using System.Xml;
using GlobalJobHunter.Service.Models;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

public sealed class WeWorkRemotelyProvider : IJobProvider
{
    public string SourcePlatform => "WeWorkRemotely";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WeWorkRemotelyProvider> _logger;

    private const string RssUrl = "https://weworkremotely.com/categories/remote-back-end-programming-jobs.rss";

    public WeWorkRemotelyProvider(IHttpClientFactory httpClientFactory, ILogger<WeWorkRemotelyProvider> logger)
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

            var stream = await client.GetStreamAsync(RssUrl, ct);

            using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings { Async = true });
            var feed = SyndicationFeed.Load(xmlReader);

            if (feed?.Items is null)
            {
                _logger.LogWarning("[WeWorkRemotely] RSS feed returned no items.");
                return Enumerable.Empty<JobPosting>();
            }

            var postings = new List<JobPosting>();

            foreach (var item in feed.Items)
            {
                ct.ThrowIfCancellationRequested();

                var link = item.Links.FirstOrDefault()?.Uri?.AbsoluteUri;
                if (string.IsNullOrWhiteSpace(link))
                    continue;

                // WWR format: "Company Name: Job Title"
                var title = item.Title?.Text ?? "Unknown";
                var company = "Unknown";
                var colonIndex = title.IndexOf(':');
                if (colonIndex > 0)
                {
                    company = title[..colonIndex].Trim();
                    title = title[(colonIndex + 1)..].Trim();
                }

                var description = item.Summary?.Text;
                var postedDate = item.PublishDate.UtcDateTime;

                // Extract region from element extensions if available
                string? location = null;
                foreach (var ext in item.ElementExtensions)
                {
                    if (ext.OuterName.Equals("region", StringComparison.OrdinalIgnoreCase))
                    {
                        location = ext.GetObject<string>();
                        break;
                    }
                }

                postings.Add(new JobPosting
                {
                    Title = title,
                    Company = company,
                    Location = location ?? "Remote",
                    WorkModel = "Remote",
                    SourcePlatform = SourcePlatform,
                    Url = link,
                    PostedDate = postedDate == default ? DateTime.UtcNow : postedDate,
                    Description = description
                });
            }

            _logger.LogInformation("[WeWorkRemotely] Fetched {Count} jobs from RSS.", postings.Count);
            return postings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WeWorkRemotely] Failed to fetch jobs.");
            return Enumerable.Empty<JobPosting>();
        }
    }
}
