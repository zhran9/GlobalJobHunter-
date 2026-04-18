namespace GlobalJobHunter.Service.Models;

public sealed class JobPosting
{
    public required string Title { get; init; }
    public required string Company { get; init; }
    public string? Location { get; init; }
    public string? WorkModel { get; init; }
    public required string SourcePlatform { get; init; }
    public required string Url { get; init; }
    public DateTime PostedDate { get; init; }
    public string? Description { get; init; }
}
