using CareerIntel.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CareerIntel.Persistence;

/// <summary>
/// Repository for persisting and analyzing interview feedback data.
/// Provides aggregation methods for weak-area frequency and pass-rate analysis.
/// </summary>
public sealed class InterviewRepository(CareerIntelDbContext db)
{
    /// <summary>
    /// Saves a new interview feedback record to the database.
    /// </summary>
    public async Task SaveFeedbackAsync(InterviewFeedback feedback, CancellationToken ct = default)
    {
        db.InterviewFeedbacks.Add(feedback);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retrieves all interview feedback records, ordered by interview date descending.
    /// </summary>
    public async Task<List<InterviewFeedback>> GetAllFeedbackAsync(CancellationToken ct = default)
    {
        return await db.InterviewFeedbacks
            .OrderByDescending(f => f.InterviewDate)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Retrieves all interview feedback for a specific company, case-insensitive.
    /// </summary>
    public async Task<List<InterviewFeedback>> GetByCompanyAsync(string company, CancellationToken ct = default)
    {
        return await db.InterviewFeedbacks
            .Where(f => EF.Functions.Like(f.Company, company))
            .OrderByDescending(f => f.InterviewDate)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Computes frequency of each weak area across the provided feedback collection.
    /// Returns a dictionary of weak area name to occurrence count, sorted descending.
    /// </summary>
    public Dictionary<string, int> GetWeakAreaFrequency(IEnumerable<InterviewFeedback> feedbacks)
    {
        return feedbacks
            .SelectMany(f => f.WeakAreas)
            .GroupBy(area => area, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count())
            .OrderByDescending(kvp => kvp.Value)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Computes pass rate (percentage of "Passed" outcomes) for each interview round type.
    /// Returns a dictionary of round name to pass rate percentage (0-100).
    /// </summary>
    public Dictionary<string, double> GetPassRateByRound(IEnumerable<InterviewFeedback> feedbacks)
    {
        return feedbacks
            .GroupBy(f => f.Round, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any())
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var total = g.Count();
                    var passed = g.Count(f =>
                        string.Equals(f.Outcome, "Passed", StringComparison.OrdinalIgnoreCase));
                    return total > 0 ? (double)passed / total * 100 : 0;
                })
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
