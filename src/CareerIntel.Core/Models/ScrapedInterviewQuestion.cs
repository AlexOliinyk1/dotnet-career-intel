namespace CareerIntel.Core.Models;

public sealed class ScrapedInterviewQuestion
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "dou-forum", "reddit-dotnet", etc.
    public string SourceUrl { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string TopicArea { get; set; } = string.Empty; // "dotnet-internals", "system-design", etc.
    public List<string> Tags { get; set; } = [];
    public string BestAnswer { get; set; } = string.Empty;
    public int Upvotes { get; set; }
    public string Company { get; set; } = string.Empty; // if mentioned
    public string SeniorityContext { get; set; } = string.Empty; // "senior", "lead", etc. if mentioned
    public DateTimeOffset ScrapedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedDate { get; set; }
}
