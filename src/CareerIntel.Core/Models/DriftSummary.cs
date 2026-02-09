namespace CareerIntel.Core.Models;

public sealed class DriftSummary
{
    public List<(string Skill, int AddedCount)> RisingSkills { get; set; } = [];
    public List<(string Skill, int RemovedCount)> FadingSkills { get; set; } = [];
    public string SeniorityTrend { get; set; } = "Stable"; // "Rising", "Falling", "Stable"
    public string SalaryTrend { get; set; } = "Stable"; // "Rising", "Falling", "Stable"
    public int TotalChanges { get; set; }
    public int VacanciesChanged { get; set; }
    public DateTimeOffset AnalyzedDate { get; set; } = DateTimeOffset.UtcNow;
}
