using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Stateless analytics engine for LinkedIn recruiter proposals.
/// Provides statistics, deduplication, and conversion to JobVacancy for matching.
/// </summary>
public sealed class ProposalAnalyzer
{
    /// <summary>
    /// Generate statistics from a list of proposals.
    /// </summary>
    public ProposalStats GetStatistics(List<LinkedInProposal> proposals)
    {
        var stats = new ProposalStats
        {
            TotalProposals = proposals.Count,
            DateRange = proposals.Count > 0
                ? $"{proposals.Min(p => p.ProposalDate):yyyy-MM-dd} — {proposals.Max(p => p.ProposalDate):yyyy-MM-dd}"
                : "N/A"
        };

        // Per month
        stats.ProposalsPerMonth = proposals
            .GroupBy(p => p.ProposalDate.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        // Top companies
        stats.TopCompanies = proposals
            .Where(p => !string.IsNullOrEmpty(p.Company))
            .GroupBy(p => p.Company, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .ToDictionary(g => g.Key, g => g.Count());

        // Common job titles
        stats.CommonJobTitles = proposals
            .Where(p => !string.IsNullOrEmpty(p.JobTitle))
            .GroupBy(p => p.JobTitle, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        // Remote breakdown
        stats.RemoteBreakdown = proposals
            .GroupBy(p => string.IsNullOrEmpty(p.RemotePolicy) ? "Unknown" : p.RemotePolicy)
            .ToDictionary(g => g.Key, g => g.Count());

        // Relocation offers
        stats.RelocationOffers = proposals.Count(p => p.RelocationOffered);

        // With salary hints
        stats.WithSalaryHint = proposals.Count(p => !string.IsNullOrEmpty(p.SalaryHint));

        // Tech stack frequency
        stats.TechStackFrequency = proposals
            .Where(p => !string.IsNullOrEmpty(p.TechStack))
            .SelectMany(p => p.TechStack.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .ToDictionary(g => g.Key, g => g.Count());

        // Status breakdown
        stats.StatusBreakdown = proposals
            .GroupBy(p => p.Status)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        // Average messages per thread
        stats.AverageMessagesPerThread = proposals.Count > 0
            ? proposals.Average(p => p.MessageCount)
            : 0;

        // Top locations
        stats.TopLocations = proposals
            .Where(p => !string.IsNullOrEmpty(p.Location))
            .SelectMany(p => p.Location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        return stats;
    }

    /// <summary>
    /// Convert a proposal to a JobVacancy for feeding into the match/decide pipeline.
    /// </summary>
    public static JobVacancy? ConvertToVacancy(LinkedInProposal proposal)
    {
        if (string.IsNullOrEmpty(proposal.Company) && string.IsNullOrEmpty(proposal.JobTitle))
            return null;

        var remotePolicy = proposal.RemotePolicy switch
        {
            "Remote" => RemotePolicy.FullyRemote,
            "Hybrid" => RemotePolicy.Hybrid,
            "On-site" => RemotePolicy.OnSite,
            _ => RemotePolicy.Unknown
        };

        var skills = string.IsNullOrEmpty(proposal.TechStack)
            ? new List<string>()
            : proposal.TechStack.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return new JobVacancy
        {
            Id = $"linkedin-msg:{proposal.ConversationId}",
            Title = !string.IsNullOrEmpty(proposal.JobTitle) ? proposal.JobTitle : "Unknown Position",
            Company = !string.IsNullOrEmpty(proposal.Company) ? proposal.Company : "Unknown Company",
            RemotePolicy = remotePolicy,
            RequiredSkills = skills,
            Description = proposal.MessageSummary,
            Url = proposal.RecruiterProfileUrl,
            SourcePlatform = "linkedin-message",
            PostedDate = proposal.ProposalDate,
            ScrapedDate = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Deduplicate proposals by ConversationId, keeping the one with more messages.
    /// </summary>
    public static List<LinkedInProposal> Deduplicate(
        List<LinkedInProposal> existing, List<LinkedInProposal> newProposals)
    {
        var existingIds = existing
            .ToDictionary(p => p.ConversationId, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var updated = 0;

        foreach (var proposal in newProposals)
        {
            if (existingIds.TryGetValue(proposal.ConversationId, out var existing_))
            {
                // Update if new import has more messages (follow-up messages)
                if (proposal.MessageCount > existing_.MessageCount)
                {
                    proposal.Id = existing_.Id;
                    proposal.Status = existing_.Status;
                    proposal.Notes = existing_.Notes;
                    existingIds[proposal.ConversationId] = proposal;
                    updated++;
                }
            }
            else
            {
                existingIds[proposal.ConversationId] = proposal;
                added++;
            }
        }

        var merged = existingIds.Values
            .OrderByDescending(p => p.ProposalDate)
            .ToList();

        // Reassign sequential IDs
        for (var i = 0; i < merged.Count; i++)
            merged[i].Id = i + 1;

        return merged;
    }
}

public sealed class ProposalStats
{
    public int TotalProposals { get; set; }
    public string DateRange { get; set; } = string.Empty;
    public Dictionary<string, int> ProposalsPerMonth { get; set; } = [];
    public Dictionary<string, int> TopCompanies { get; set; } = [];
    public Dictionary<string, int> CommonJobTitles { get; set; } = [];
    public Dictionary<string, int> RemoteBreakdown { get; set; } = [];
    public int RelocationOffers { get; set; }
    public int WithSalaryHint { get; set; }
    public Dictionary<string, int> TechStackFrequency { get; set; } = [];
    public Dictionary<string, int> StatusBreakdown { get; set; } = [];
    public double AverageMessagesPerThread { get; set; }
    public Dictionary<string, int> TopLocations { get; set; } = [];
}
