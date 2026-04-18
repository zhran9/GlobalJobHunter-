namespace GlobalJobHunter.Service.Models;

public sealed class JobRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public required string Company { get; set; }
    public string? Location { get; set; }
    public string? WorkModel { get; set; }
    public required string SourcePlatform { get; set; }
    public required string Url { get; set; }
    public DateTime PostedDate { get; set; }
    public int? AiScore { get; set; }
    public bool IsProcessed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
