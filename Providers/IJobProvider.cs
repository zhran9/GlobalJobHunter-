using GlobalJobHunter.Service.Models;

namespace GlobalJobHunter.Service.Providers;

public interface IJobProvider
{
    string SourcePlatform { get; }
    Task<IEnumerable<JobPosting>> FetchJobsAsync(CancellationToken ct = default);
}
