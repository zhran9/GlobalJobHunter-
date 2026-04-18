namespace GlobalJobHunter.Service.Models;

/// <summary>
/// Represents a Telegram user who has started the bot.
/// Primary key is the Telegram ChatId (long) — globally unique per user/group.
/// </summary>
public class AppUser
{
    /// <summary>Telegram Chat ID — unique identifier for this user's private chat.</summary>
    public long ChatId { get; set; }

    /// <summary>Telegram @username (may be null if user has no username set).</summary>
    public string? Username { get; set; }

    /// <summary>User's first name from Telegram profile.</summary>
    public string? FirstName { get; set; }

    /// <summary>True = receives alerts. False = user sent /stop.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the user first sent /start.</summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last time we successfully sent them a message.</summary>
    public DateTime? LastAlertAt { get; set; }
}
