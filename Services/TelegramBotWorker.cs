using GlobalJobHunter.Service.Data;
using GlobalJobHunter.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace GlobalJobHunter.Service.Services;

/// <summary>
/// Background service that polls Telegram for new messages every 2 seconds.
/// Handles /start  → registers user in DB and starts sending them job alerts.
/// Handles /stop   → marks user inactive, stops alerts.
/// Handles /status → tells user how many jobs were found.
/// </summary>
public sealed class TelegramBotWorker : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramBotWorker> _logger;

    // Tracks the last update we processed so we don't re-process old messages
    private int _lastUpdateId = 0;

    public TelegramBotWorker(
        ITelegramBotClient bot,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramBotWorker> logger)
    {
        _bot = bot;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[TelegramBotWorker] Started. Listening for /start and /stop commands...");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var updates = await _bot.GetUpdates(
                    offset: _lastUpdateId + 1,
                    limit: 100,
                    timeout: 0,
                    allowedUpdates: [UpdateType.Message],
                    cancellationToken: ct);

                foreach (var update in updates)
                {
                    _lastUpdateId = update.Id;

                    if (update.Message is not { } message) continue;
                    if (message.Text is not { } text) continue;

                    var chatId   = message.Chat.Id;
                    var username = message.Chat.Username;
                    var firstName = message.Chat.FirstName ?? "there";
                    var command  = text.Split(' ')[0].ToLowerInvariant().TrimEnd('@').Trim();

                    _logger.LogInformation("[TelegramBotWorker] Received '{Command}' from {FirstName} (ChatId: {ChatId})", command, firstName, chatId);

                    switch (command)
                    {
                        case "/start":
                            await HandleStartAsync(chatId, username, firstName, ct);
                            break;

                        case "/stop":
                            await HandleStopAsync(chatId, firstName, ct);
                            break;

                        case "/status":
                            await HandleStatusAsync(chatId, ct);
                            break;

                        default:
                            await _bot.SendMessage(
                                chatId: chatId,
                                text: "👋 I only understand:\n/start — receive job alerts\n/stop — pause alerts\n/status — see bot stats");
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TelegramBotWorker] Error polling updates. Retrying in 5s...");
            }

            await Task.Delay(2_000, ct); // Poll every 2 seconds
        }

        _logger.LogInformation("[TelegramBotWorker] Stopped.");
    }

    // ── Command Handlers ────────────────────────────────────────────────────────

    private async Task HandleStartAsync(long chatId, string? username, string firstName, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.AppUsers.FindAsync([chatId], ct);

        if (existing is null)
        {
            // New user — register them
            db.AppUsers.Add(new AppUser
            {
                ChatId       = chatId,
                Username     = username,
                FirstName    = firstName,
                IsActive     = true,
                RegisteredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("[TelegramBotWorker] New user registered: {FirstName} ({ChatId})", firstName, chatId);

            await _bot.SendMessage(
                chatId: chatId,
                text: $"🎉 *Welcome, {firstName}!*\n\n" +
                      "You're now registered for *GlobalJobHunter* alerts.\n\n" +
                      "🤖 I scan the top job boards every *4 hours* looking for .NET & Fintech roles.\n\n" +
                      "When I find a great match (AI score ≥ 65), I'll send it here instantly.\n\n" +
                      "📌 Commands:\n/stop — pause alerts\n/status — see bot stats",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
        else if (!existing.IsActive)
        {
            // Returning user who had stopped
            existing.IsActive = true;
            existing.LastAlertAt = null;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("[TelegramBotWorker] User reactivated: {FirstName} ({ChatId})", firstName, chatId);

            await _bot.SendMessage(
                chatId: chatId,
                text: $"✅ *Welcome back, {firstName}!*\n\nAlerts are re-enabled. I'll notify you on the next job cycle.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
        else
        {
            // Already active
            await _bot.SendMessage(
                chatId: chatId,
                text: $"✅ You're already registered, {firstName}! I'll alert you when great .NET jobs appear.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
    }

    private async Task HandleStopAsync(long chatId, string firstName, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.AppUsers.FindAsync([chatId], ct);
        if (user is not null)
        {
            user.IsActive = false;
            await db.SaveChangesAsync(ct);
        }

        await _bot.SendMessage(
            chatId: chatId,
            text: $"⏸ *Alerts paused*, {firstName}.\n\nSend /start anytime to resume.",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        _logger.LogInformation("[TelegramBotWorker] User deactivated: {FirstName} ({ChatId})", firstName, chatId);
    }

    private async Task HandleStatusAsync(long chatId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var totalUsers  = await db.AppUsers.CountAsync(ct);
        var activeUsers = await db.AppUsers.CountAsync(u => u.IsActive, ct);
        var totalJobs   = await db.JobRecords.CountAsync(ct);
        var matchedJobs = await db.JobRecords.CountAsync(j => j.AiScore >= 65, ct);

        await _bot.SendMessage(
            chatId: chatId,
            text: $"📊 *GlobalJobHunter Stats*\n\n" +
                  $"👥 Total users: *{totalUsers}*\n" +
                  $"✅ Active users: *{activeUsers}*\n" +
                  $"🔍 Jobs scanned: *{totalJobs}*\n" +
                  $"🎯 Matches found: *{matchedJobs}*\n" +
                  $"⏱ Scan interval: *every 4 hours*",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);
    }

}
