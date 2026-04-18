using GlobalJobHunter.Service.Models;

namespace GlobalJobHunter.Service.Services;

public interface IAiEvaluatorService
{
    Task<AiEvaluationResult?> EvaluateAsync(JobPosting job, CancellationToken ct = default);
}
