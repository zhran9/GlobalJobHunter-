using System.Text;
using System.Text.Json;
using GlobalJobHunter.Service.Models;
using GlobalJobHunter.Service.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GlobalJobHunter.Service.Services;

public sealed class GroqEvaluatorService : IAiEvaluatorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiOptions _aiOptions;
    private readonly ILogger<GroqEvaluatorService> _logger;

    public GroqEvaluatorService(
        IHttpClientFactory httpClientFactory,
        IOptions<AiOptions> aiOptions,
        ILogger<GroqEvaluatorService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    public async Task<AiEvaluationResult?> EvaluateAsync(JobPosting job, CancellationToken ct = default)
    {
        try
        {
            var prompt = BuildPrompt(job);

            var request = new
            {
                model = "llama-3.3-70b-versatile",
                messages = new[]
                {
                    new { role = "system", content = "You are a strict exact-match job filter. Look at the job title and description. CRITICAL RULE: If the text contains '.NET', '.Net', '.net', 'dot net', or 'C#', you MUST output `isMatch: true`. If these keywords are missing, you MUST output `isMatch: false`. No exceptions. You MUST return ONLY a raw JSON object and nothing else. No markdown wrappers. Schema: { \"isMatch\": bool, \"score\": int, \"reason\": string, \"companyName\": string, \"ctoSearchLink\": string, \"customColdEmail\": string }. For ctoSearchLink, return a strictly URL-encoded Google search string like: https://www.google.com/search?q=site%3Alinkedin.com%2Fin+%22[COMPANY_NAME]%22+(CTO+OR+%22Engineering+Manager%22)." },
                    new { role = "user", content = prompt }
                },
                response_format = new { type = "json_object" }
            };

            var endpoint = "https://api.groq.com/openai/v1/chat/completions";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _aiOptions.ApiKey);
            
            var jsonPayload = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(endpoint, content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Groq] API returned {StatusCode}: {Body}",
                    (int)response.StatusCode, responseBody[..Math.Min(500, responseBody.Length)]);
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                _logger.LogWarning("[Groq] Empty choices for job: {Title}", job.Title);
                return null;
            }

            var text = choices[0].GetProperty("message").GetProperty("content").GetString();

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("[Groq] Empty response text for job: {Title}", job.Title);
                return null;
            }

            // Strip markdown code fences if wrapped
            text = text.Trim();
            if (text.StartsWith("```json"))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline > 0) text = text[(firstNewline + 1)..];
                if (text.EndsWith("```")) text = text[..^3];
                text = text.Trim();
            }
            else if (text.StartsWith("```"))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline > 0) text = text[(firstNewline + 1)..];
                if (text.EndsWith("```")) text = text[..^3];
                text = text.Trim();
            }

            var result = JsonSerializer.Deserialize<AiEvaluationResult>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result is null)
            {
                _logger.LogWarning("[Groq] Failed to deserialize AI result for: {Title}", job.Title);
                return null;
            }

            _logger.LogInformation("[Groq] Evaluated '{Title}' -> isMatch={IsMatch}, score={Score}",
                job.Title, result.IsMatch, result.Score);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Groq] Error evaluating job: {Title}", job.Title);
            return null;
        }
    }

    private static string BuildPrompt(JobPosting job)
    {
        var description = job.Description ?? "";
        // Truncate description to max 1500 chars to control token usage
        if (description.Length > 1500)
            description = description[..1500] + "... [truncated]";

        return $"Job:\nTitle: {job.Title}\nDescription:\n{description}";
    }
}
