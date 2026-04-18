using GlobalJobHunter.Service.Data;
using GlobalJobHunter.Service.Models;
using GlobalJobHunter.Service.Providers;
using GlobalJobHunter.Service.Services;
using Microsoft.EntityFrameworkCore;

namespace GlobalJobHunter.Service;

public sealed class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Worker> _logger;

    private const int IntervalHours = 4;
    private const int RecencyHours = 48; // Strictly filter for last 48 hours
    private const int MinAiScore = 65;

    public Worker(IServiceScopeFactory scopeFactory, ILogger<Worker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GlobalJobHunter Worker started. Interval: {Hours}h, Recency: {Recency}h, Min Score: {Score}",
            IntervalHours, RecencyHours, MinAiScore);

        // Run immediately on startup, then on interval
        using var timer = new PeriodicTimer(TimeSpan.FromHours(IntervalHours));

        do
        {
            await RunCycleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("=== Starting job processing cycle at {Time} UTC ===", DateTime.UtcNow);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var providers = scope.ServiceProvider.GetRequiredService<IEnumerable<IJobProvider>>();
            var aiService = scope.ServiceProvider.GetRequiredService<IAiEvaluatorService>();
            var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramNotifierService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // ─── PHASE 1: FETCH from all providers ───
            var allPostings = new List<JobPosting>();

            foreach (var provider in providers)
            {
                try
                {
                    _logger.LogInformation("Fetching from {Source}...", provider.SourcePlatform);
                    var jobs = await provider.FetchJobsAsync(ct);
                    var jobList = jobs.ToList();
                    _logger.LogInformation("  -> {Count} jobs from {Source}", jobList.Count, provider.SourcePlatform);
                    allPostings.AddRange(jobList);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Provider {Source} failed. Continuing with others.", provider.SourcePlatform);
                }
            }

            _logger.LogInformation("Total fetched from all providers: {Count}", allPostings.Count);

            // ─── PHASE 2: STRICT 48-HOUR RECENCY FILTER ───
            var cutoff = DateTime.UtcNow.AddHours(-48);
            var recentPostings = allPostings
                .Where(j => j.PostedDate >= cutoff)
                .ToList();

            _logger.LogInformation("After 48h recency filter: {Count} jobs remain.", recentPostings.Count);

            // ─── PHASE 3: DEDUPLICATE against SQLite (single batch query) ───
            var candidateUrls = recentPostings.Select(p => p.Url).Distinct().ToList();
            var existingUrls = (await dbContext.JobRecords
                .Where(j => candidateUrls.Contains(j.Url))
                .Select(j => j.Url)
                .ToListAsync(ct))
                .ToHashSet();

            var newPostings = recentPostings
                .Where(p => !existingUrls.Contains(p.Url))
                .ToList();

            _logger.LogInformation("After dedup: {Count} new jobs to evaluate.", newPostings.Count);

            // ─── PHASE 4: AI EVALUATION + PHASE 5: NOTIFY ───
            var matchedCount = 0;

            foreach (var posting in newPostings)
            {
                ct.ThrowIfCancellationRequested();

                // Enforce exact 48-hour rule
                if (posting.PostedDate < DateTime.UtcNow.AddHours(-48))
                {
                    continue;
                }

                // Evaluate with AI
                AiEvaluationResult? evaluation = null;
                try
                {
                    evaluation = await aiService.EvaluateAsync(posting, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI evaluation failed for: {Title}", posting.Title);
                }

                if (evaluation == null)
                {
                    _logger.LogWarning("Skipping DB save for '{Title}' due to AI failure. It will be retried next cycle.", posting.Title);

                    // CRITICAL: Prevent Groq API 429 Rate Limit
                    await Task.Delay(15000, ct);
                    continue;
                }

                // Create and persist the job record
                var record = new JobRecord
                {
                    Title = posting.Title,
                    Company = posting.Company,
                    Location = posting.Location,
                    WorkModel = posting.WorkModel,
                    SourcePlatform = posting.SourcePlatform,
                    Url = posting.Url,
                    PostedDate = posting.PostedDate,
                    AiScore = evaluation?.Score,
                    IsProcessed = true
                };

                dbContext.JobRecords.Add(record);

                try
                {
                    await dbContext.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true)
                {
                    _logger.LogWarning("Duplicate URL detected (race condition): {Url}", posting.Url);
                    dbContext.Entry(record).State = EntityState.Detached;
                    continue;
                }

                // Send Telegram if match criteria met
                if (evaluation is { IsMatch: true, Score: >= MinAiScore })
                {
                    await telegramService.SendJobAlertAsync(record, evaluation, ct);
                    matchedCount++;

                    // Respect Telegram rate limits
                    await Task.Delay(1000, ct);
                }

                // CRITICAL: Prevent Groq API 429 Rate Limit
                await Task.Delay(15000, ct);
            }

            // ─── PHASE 6: SUMMARY ───
            await telegramService.SendSummaryAsync(allPostings.Count, newPostings.Count, matchedCount, ct);

            _logger.LogInformation("=== Cycle complete. Fetched: {Total}, New: {New}, Matched: {Matched} ===",
                allPostings.Count, newPostings.Count, matchedCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Worker cycle cancelled — shutting down.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in processing cycle. Will retry at next interval.");
        }
    }
}
