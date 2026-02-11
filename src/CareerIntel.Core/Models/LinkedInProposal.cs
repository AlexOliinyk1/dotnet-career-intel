namespace CareerIntel.Core.Models;

/// <summary>
/// A recruiter proposal extracted from LinkedIn messages export.
/// </summary>
public sealed class LinkedInProposal
{
    public int Id { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public string RecruiterName { get; set; } = string.Empty;
    public string RecruiterProfileUrl { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
    public string RemotePolicy { get; set; } = string.Empty; // "Remote", "Hybrid", "On-site", "Unknown"
    public string Location { get; set; } = string.Empty;
    public bool RelocationOffered { get; set; }
    public string SalaryHint { get; set; } = string.Empty;
    public string MessageSummary { get; set; } = string.Empty;
    public string FullContent { get; set; } = string.Empty;
    public DateTimeOffset ProposalDate { get; set; }
    public DateTimeOffset? LastMessageDate { get; set; }
    public int MessageCount { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.New;
    public string Notes { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
}

public enum ProposalStatus
{
    New,
    Interested,
    Replied,
    Interviewing,
    Declined,
    Expired,
    Converted
}
