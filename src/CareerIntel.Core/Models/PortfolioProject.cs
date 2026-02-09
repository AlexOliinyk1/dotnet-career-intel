namespace CareerIntel.Core.Models;

public sealed class PortfolioProject
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProblemStatement { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public List<string> TechStack { get; set; } = [];
    public List<string> Backlog { get; set; } = [];
    public string Readme { get; set; } = string.Empty;
    public string InterviewNarrative { get; set; } = string.Empty;
    public List<string> TargetSkillGaps { get; set; } = []; // which gaps this project closes
    public string Complexity { get; set; } = "Medium"; // "Simple", "Medium", "Complex"
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
}
