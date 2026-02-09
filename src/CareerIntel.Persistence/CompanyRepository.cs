using CareerIntel.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CareerIntel.Persistence;

/// <summary>
/// Repository for managing company profiles with automatic enrichment
/// from interview feedback data.
/// </summary>
public sealed class CompanyRepository(CareerIntelDbContext db)
{
    /// <summary>
    /// Saves a new company profile or updates an existing one (matched by Name).
    /// </summary>
    public async Task SaveOrUpdateAsync(CompanyProfile profile, CancellationToken ct = default)
    {
        var existing = await db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Name == profile.Name, ct);

        if (existing is null)
        {
            db.CompanyProfiles.Add(profile);
        }
        else
        {
            existing.Industry = profile.Industry;
            existing.InterviewStyle = profile.InterviewStyle;
            existing.RealTechStack = profile.RealTechStack;
            existing.InterviewRounds = profile.InterviewRounds;
            existing.DifficultyBar = profile.DifficultyBar;
            existing.CommonRejectionReasons = profile.CommonRejectionReasons;
            existing.RedFlags = profile.RedFlags;
            existing.Pros = profile.Pros;
            existing.Notes = profile.Notes;
            existing.TotalInterviews = profile.TotalInterviews;
            existing.TotalOffers = profile.TotalOffers;
            existing.LastUpdated = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retrieves a company profile by exact name match.
    /// </summary>
    public async Task<CompanyProfile?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        return await db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Name == name, ct);
    }

    /// <summary>
    /// Retrieves all company profiles, ordered alphabetically by name.
    /// </summary>
    public async Task<List<CompanyProfile>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.CompanyProfiles
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Auto-updates a company profile based on new interview feedback.
    /// Creates the company profile if it does not already exist.
    /// Increments interview count, updates difficulty bar (running average),
    /// and tracks unique rejection reasons from feedback.
    /// </summary>
    public async Task UpdateFromFeedbackAsync(InterviewFeedback feedback, CancellationToken ct = default)
    {
        var company = await db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Name == feedback.Company, ct);

        if (company is null)
        {
            company = new CompanyProfile
            {
                Name = feedback.Company,
                TotalInterviews = 0,
                TotalOffers = 0,
                DifficultyBar = feedback.DifficultyRating
            };
            db.CompanyProfiles.Add(company);
        }

        company.TotalInterviews++;

        if (string.Equals(feedback.Outcome, "Passed", StringComparison.OrdinalIgnoreCase)
            && string.Equals(feedback.Round, "Final", StringComparison.OrdinalIgnoreCase))
        {
            company.TotalOffers++;
        }

        // Update difficulty bar as a running average
        company.DifficultyBar = (int)Math.Round(
            ((company.DifficultyBar * (company.TotalInterviews - 1.0)) + feedback.DifficultyRating)
            / company.TotalInterviews);

        // Add the interview round if not already tracked
        if (!company.InterviewRounds.Contains(feedback.Round, StringComparer.OrdinalIgnoreCase))
        {
            company.InterviewRounds.Add(feedback.Round);
        }

        // Track weak areas as potential rejection reasons
        if (string.Equals(feedback.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var weak in feedback.WeakAreas)
            {
                if (!company.CommonRejectionReasons.Contains(weak, StringComparer.OrdinalIgnoreCase))
                {
                    company.CommonRejectionReasons.Add(weak);
                }
            }
        }

        company.LastUpdated = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
