using System.Text.Json.Serialization;

namespace GlobalJobHunter.Service.Models;

public sealed class AiEvaluationResult
{
    [JsonPropertyName("isMatch")]
    public bool IsMatch { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = string.Empty;

    [JsonPropertyName("ctoSearchLink")]
    public string CtoSearchLink { get; set; } = string.Empty;

    [JsonPropertyName("customColdEmail")]
    public string CustomColdEmail { get; set; } = string.Empty;
}
