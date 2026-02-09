namespace CareerIntel.Core.Models;

public sealed class JobApplication
{
    public int Id { get; set; }
    public string VacancyId { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string VacancyTitle { get; set; } = string.Empty;
    public string VacancyUrl { get; set; } = string.Empty;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;
    public string ResumeVersion { get; set; } = string.Empty; // which tailored resume was used
    public string CoverLetterPath { get; set; } = string.Empty;
    public double MatchScore { get; set; }
    public string ApplyMethod { get; set; } = string.Empty; // "email", "platform", "manual"
    public string ApplyUrl { get; set; } = string.Empty; // actual application URL
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AppliedDate { get; set; }
    public DateTimeOffset? ResponseDate { get; set; }
    public string ResponseNotes { get; set; } = string.Empty;
}

public enum ApplicationStatus
{
    Pending,        // identified, not yet applied
    ResumeReady,    // resume tailored, ready to apply
    Applied,        // application submitted
    Viewed,         // recruiter viewed (if trackable)
    Screening,      // screening call scheduled
    Interview,      // interview stage
    Offer,          // received offer
    Rejected,       // rejected at any stage
    Withdrawn,      // candidate withdrew
    Ghosted,        // no response after 2+ weeks
    Expired         // vacancy closed before applying
}
