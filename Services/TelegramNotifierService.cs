using System.Text;
using GlobalJobHunter.Service.Models;
using GlobalJobHunter.Service.Options;
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
    private readonly ILogger<TelegramNotifierService> _logger;

    public TelegramNotifierService(
        ITelegramBotClient botClient,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<TelegramNotifierService> logger)
    {
        _botClient = botClient;
        _telegramOptions = telegramOptions.Value;
        _logger = logger;
    }

    public async Task SendJobAlertAsync(JobRecord job, AiEvaluationResult evaluation, CancellationToken ct = default)
    {
        try
        {
            var message = BuildJobMessage(job, evaluation);

            await _botClient.SendMessage(
                chatId: _telegramOptions.ChatId,
                text: message,
                parseMode: ParseMode.Markdown,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);

            _logger.LogInformation("[Telegram] Sent alert for: {Title} at {Company}", job.Title, job.Company);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Telegram] Could not send alert (Network/VPN issue): {ex.Message}");
        }
    }

    public async Task SendSummaryAsync(int totalFetched, int newJobs, int matched, CancellationToken ct = default)
    {
        try
        {
            var message = new StringBuilder();
            message.AppendLine("📊 *Job Hunt Summary*");
            message.AppendLine();
            message.AppendLine($"🔍 Total fetched: {totalFetched}");
            message.AppendLine($"🆕 New jobs: {newJobs}");
            message.AppendLine($"✅ Matched (sent alerts): {matched}");
            message.AppendLine($"⏰ Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

            await _botClient.SendMessage(
                chatId: _telegramOptions.ChatId,
                text: message.ToString(),
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Telegram] Could not send alert (Network/VPN issue): {ex.Message}");
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
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Escape Markdown special characters for Telegram Markdown (v1)
        return text
            .Replace("_", "\\_")
            .Replace("*", "\\*")
            .Replace("[", "\\[")
            .Replace("`", "\\`");
    }
}
