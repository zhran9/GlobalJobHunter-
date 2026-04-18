using System.Text.Json;
using System.Text.Json.Serialization;
using GlobalJobHunter.Service.Models;
using Microsoft.Extensions.Logging;

namespace GlobalJobHunter.Service.Providers;

public sealed class RemotiveProvider : IJobProvider
{
    public string SourcePlatform => "Remotive";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RemotiveProvider> _logger;

    private const string ApiUrl = "https://remotive.com/api/remote-jobs?category=software-dev";

    public RemotiveProvider(IHttpClientFactory httpClientFactory, ILogger<RemotiveProvider> logger)
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
            var apiResponse = JsonSerializer.Deserialize<RemotiveApiResponse>(json);

            if (apiResponse?.Jobs is null || apiResponse.Jobs.Count == 0)
            {
                _logger.LogWarning("[Remotive] No jobs returned from API.");
                return Enumerable.Empty<JobPosting>();
            }

            var postings = new List<JobPosting>();

            foreach (var job in apiResponse.Jobs)
            {
                if (string.IsNullOrWhiteSpace(job.Url))
                    continue;

                DateTime postedDate = DateTime.UtcNow;
                if (DateTime.TryParse(job.PublicationDate, out var parsed))
                    postedDate = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

                postings.Add(new JobPosting
                {
                    Title = job.Title ?? "Unknown",
                    Company = job.CompanyName ?? "Unknown",
                    Location = job.CandidateRequiredLocation,
                    WorkModel = job.JobType,
                    SourcePlatform = SourcePlatform,
                    Url = job.Url,
                    PostedDate = postedDate,
                    Description = job.Description
                });
            }

            _logger.LogInformation("[Remotive] Fetched {Count} jobs.", postings.Count);
            return postings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Remotive] Failed to fetch jobs.");
            return Enumerable.Empty<JobPosting>();
        }
    }

    // --- Internal JSON contract ---

    private sealed class RemotiveApiResponse
    {
        [JsonPropertyName("jobs")]
        public List<RemotiveJob> Jobs { get; set; } = [];
    }

    private sealed class RemotiveJob
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("company_name")]
        public string? CompanyName { get; set; }

        [JsonPropertyName("candidate_required_location")]
        public string? CandidateRequiredLocation { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("publication_date")]
        public string? PublicationDate { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("job_type")]
        public string? JobType { get; set; }
    }
}
