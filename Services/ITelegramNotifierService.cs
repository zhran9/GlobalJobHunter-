using GlobalJobHunter.Service.Models;

namespace GlobalJobHunter.Service.Services;

public interface ITelegramNotifierService
{
    Task SendJobAlertAsync(JobRecord job, AiEvaluationResult evaluation, CancellationToken ct = default);
    Task SendSummaryAsync(int totalFetched, int newJobs, int matched, CancellationToken ct = default);
}
