using GlobalJobHunter.Service.Models;
using GlobalJobHunter.Service.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GlobalJobHunter.Service.Providers;

public abstract class PlaywrightJobProvider : IJobProvider
{
    protected readonly IPlaywrightBrowserService BrowserService;
    protected readonly ILogger Logger;

    protected PlaywrightJobProvider(IPlaywrightBrowserService browserService, ILogger logger)
    {
        BrowserService = browserService;
        Logger = logger;
    }

    public abstract string SourcePlatform { get; }

    public async Task<IEnumerable<JobPosting>> FetchJobsAsync(CancellationToken ct = default)
    {
        IBrowserContext? context = null;
        try
        {
            context = await BrowserService.CreateStealthContextAsync(ct);
            return await FetchWithBrowserAsync(context, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Source}] Browser scrape failed.", SourcePlatform);
            return [];
        }
        finally
        {
            if (context != null)
            {
                try { await context.CloseAsync(); }
                catch { /* swallow — best effort cleanup */ }
            }
        }
    }

    protected abstract Task<IEnumerable<JobPosting>> FetchWithBrowserAsync(
        IBrowserContext context, CancellationToken ct);

    // ── Shared helpers ──────────────────────────────────────────────

    protected static Task HumanDelayAsync(CancellationToken ct)
        => Task.Delay(Random.Shared.Next(800, 2501), ct);

    protected static DateTime ParseRelativeDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DateTime.UtcNow;

        text = text.Trim().ToLowerInvariant();

        if (text.Contains("just posted") || text.Contains("today") || text.Contains("hour"))
            return DateTime.UtcNow;

        if (text.Contains("yesterday"))
            return DateTime.UtcNow.AddDays(-1);

        // "X days ago"
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[0], out var n))
        {
            if (text.Contains("day"))   return DateTime.UtcNow.AddDays(-n);
            if (text.Contains("week"))  return DateTime.UtcNow.AddDays(-n * 7);
            if (text.Contains("month")) return DateTime.UtcNow.AddDays(-n * 30);
        }

        return DateTime.UtcNow;
    }

    protected static string DetermineWorkModel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Unknown";
        text = text.ToLowerInvariant();
        if (text.Contains("remote"))  return "Remote";
        if (text.Contains("hybrid"))  return "Hybrid";
        if (text.Contains("on-site") || text.Contains("onsite") || text.Contains("in-office"))
            return "Onsite";
        return "Unknown";
    }
}
