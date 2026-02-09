namespace CareerIntel.Core.Models;

public sealed class InterviewFeedback
{
    public int Id { get; set; }
    public string Company { get; set; } = string.Empty;
    public string VacancyId { get; set; } = string.Empty;
    public string Round { get; set; } = string.Empty; // "Recruiter", "Technical", "SystemDesign", "Behavioral", "Final"
    public string Outcome { get; set; } = string.Empty; // "Passed", "Rejected", "Ghosted", "Withdrew"
    public string Feedback { get; set; } = string.Empty;
    public List<string> WeakAreas { get; set; } = [];
    public List<string> StrongAreas { get; set; } = [];
    public int DifficultyRating { get; set; } // 1-10
    public DateTimeOffset InterviewDate { get; set; }
    public DateTimeOffset RecordedDate { get; set; } = DateTimeOffset.UtcNow;
}
