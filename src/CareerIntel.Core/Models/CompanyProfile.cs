namespace CareerIntel.Core.Models;

public sealed class CompanyProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string InterviewStyle { get; set; } = string.Empty; // "FAANG-like", "EU-product", "Startup", "Outsource"
    public List<string> RealTechStack { get; set; } = [];
    public List<string> InterviewRounds { get; set; } = []; // e.g. ["Recruiter", "Technical", "SystemDesign", "Behavioral"]
    public int DifficultyBar { get; set; } // 1-10
    public List<string> CommonRejectionReasons { get; set; } = [];
    public List<string> RedFlags { get; set; } = [];
    public List<string> Pros { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
    public int TotalInterviews { get; set; }
    public int TotalOffers { get; set; }
    public double OfferRate => TotalInterviews > 0 ? (double)TotalOffers / TotalInterviews * 100 : 0;
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
