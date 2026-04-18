namespace GlobalJobHunter.Service.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";
    public required string BotToken { get; set; }
    public required string ChatId { get; set; }
}
