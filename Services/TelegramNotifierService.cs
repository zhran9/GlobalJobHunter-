using System.Text;
using GlobalJobHunter.Service.Data;
using GlobalJobHunter.Service.Models;
using GlobalJobHunter.Service.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace GlobalJobHunter.Service.Services;

public sealed class TelegramNotifierService : ITelegramNotifierService
{
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramOptions _telegramOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramNotifierService> _logger;

    // Telegram hard limits:
    //   30 messages/second globally
    //   1 message/second per individual chat
    // 50ms delay = max 20 msg/sec — safely under the limit with headroom.
    private const int BroadcastDelayMs = 50;

    public TelegramNotifierService(
        ITelegramBotClient botClient,
        IOptions<TelegramOptions> telegramOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramNotifierService> logger)
    {
        _botClient = botClient;
        _telegramOptions = telegramOptions.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Broadcasts a matched job alert to ALL active registered users.
    /// Falls back to the hardcoded admin ChatId if no users are registered yet.
    /// Rate-limited at 50ms per message to respect Telegram API limits.
    /// </summary>
    public async Task SendJobAlertAsync(JobRecord job, AiEvaluationResult evaluation, CancellationToken ct = default)
    {
        var message = BuildJobMessage(job, evaluation);
        var activeUsers = await GetActiveUsersAsync(ct);

        // ── Fallback: no registered users yet → send to admin only ──
        if (activeUsers.Count == 0)
        {
            _logger.LogInformation("[Telegram] No registered users. Sending to admin ChatId only.");
            await SendSafeAsync(long.Parse(_telegramOptions.ChatId), message, ct);
            return;
        }

        // ── Broadcast to all active users ──
        int sent = 0, failed = 0;

        foreach (var user in activeUsers)
        {
            ct.ThrowIfCancellationRequested();

            var success = await SendSafeAsync(user.ChatId, message, ct);
            if (success)
            {
                sent++;
                // Update last alert timestamp
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var dbUser = await db.AppUsers.FindAsync([user.ChatId], ct);
                if (dbUser is not null)
                {
                    dbUser.LastAlertAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            else
            {
                failed++;
            }

            // Throttle — respect Telegram's 30 msg/sec global limit
            await Task.Delay(BroadcastDelayMs, ct);
        }

        _logger.LogInformation(
            "[Telegram] Broadcast complete for '{Title}'. Sent: {Sent}, Failed: {Failed}",
            job.Title, sent, failed);
    }

    /// <summary>
    /// Sends the cycle summary to the admin ChatId only.
    /// </summary>
    public async Task SendSummaryAsync(int totalFetched, int newJobs, int matched, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var activeUsers = await db.AppUsers.CountAsync(u => u.IsActive, ct);
        var totalUsers  = await db.AppUsers.CountAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("📊 *Job Hunt Summary*");
        sb.AppendLine();
        sb.AppendLine($"🔍 Total fetched: {totalFetched}");
        sb.AppendLine($"🆕 New jobs: {newJobs}");
        sb.AppendLine($"✅ Matched (sent alerts): {matched}");
        sb.AppendLine($"👥 Active subscribers: {activeUsers} / {totalUsers}");
        sb.AppendLine($"⏰ Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

        // Summary only goes to admin
        await SendSafeAsync(long.Parse(_telegramOptions.ChatId), sb.ToString(), ct);
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private async Task<List<AppUser>> GetActiveUsersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AppUsers
            .Where(u => u.IsActive)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    private async Task<bool> SendSafeAsync(long chatId, string text, CancellationToken ct)
    {
        try
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: text,
                parseMode: ParseMode.Markdown,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);

            return true;
        }
        catch (Exception ex)
        {
            // Log but don't crash — one bad user shouldn't stop the broadcast
            _logger.LogWarning("[Telegram] Failed to send to {ChatId}: {Message}", chatId, ex.Message);
            return false;
        }
    }

    private static string BuildJobMessage(JobRecord job, AiEvaluationResult _)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🚀 *{EscapeMarkdown(job.Title)}*");
        sb.AppendLine($"🏢 *{EscapeMarkdown(job.Company)}*");
        sb.AppendLine($"📍 *Location:* {EscapeMarkdown(job.Location ?? "N/A")}");
        sb.AppendLine($"🌐 *Source:* {EscapeMarkdown(job.SourcePlatform)}");
        sb.AppendLine($"🔗 *Apply:* [Click here]({job.Url})");
        return sb.ToString();
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("_", "\\_")
            .Replace("*", "\\*")
            .Replace("[", "\\[")
            .Replace("`", "\\`");
    }
}
