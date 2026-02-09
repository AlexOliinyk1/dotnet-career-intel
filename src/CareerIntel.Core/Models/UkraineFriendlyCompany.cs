namespace CareerIntel.Core.Models;

public sealed class UkraineFriendlyCompany
{
    public string Name { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty; // HQ country
    public List<string> HiringRegions { get; set; } = []; // "Ukraine", "Eastern Europe", "Worldwide"
    public List<string> EngagementTypes { get; set; } = []; // "B2B", "Contractor", "Freelance", "Employment"
    public List<string> TechStack { get; set; } = [];
    public string Industry { get; set; } = string.Empty;
    public bool ConfirmedUkraineHiring { get; set; } // verified from real vacancy data
    public int VacancyCount { get; set; } // how many vacancies seen from this company
    public int RemoteVacancyCount { get; set; }
    public int B2BVacancyCount { get; set; }
    public decimal? AvgSalaryMin { get; set; }
    public decimal? AvgSalaryMax { get; set; }
    public string SalaryCurrency { get; set; } = "USD";
    public List<string> Sources { get; set; } = []; // which job boards this company was seen on
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
}
