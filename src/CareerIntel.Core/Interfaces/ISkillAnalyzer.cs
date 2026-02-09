using CareerIntel.Core.Models;

namespace CareerIntel.Core.Interfaces;

/// <summary>
/// Contract for skill analysis operations on job vacancy data.
/// </summary>
public interface ISkillAnalyzer
{
    /// <summary>
    /// Extracts and normalizes skills from a collection of job vacancies.
    /// </summary>
    /// <param name="vacancies">Vacancies to analyze.</param>
    /// <returns>Skill profiles with market demand scores.</returns>
    Task<IReadOnlyList<SkillProfile>> AnalyzeSkillDemandAsync(
        IReadOnlyList<JobVacancy> vacancies);

    /// <summary>
    /// Generates a market snapshot from a collection of vacancies.
    /// </summary>
    /// <param name="vacancies">Vacancies to aggregate.</param>
    /// <param name="platform">Platform name for the snapshot.</param>
    /// <returns>Market snapshot with aggregated statistics.</returns>
    Task<MarketSnapshot> GenerateSnapshotAsync(
        IReadOnlyList<JobVacancy> vacancies,
        string platform = "aggregate");

    /// <summary>
    /// Identifies trending skills by comparing two snapshots.
    /// </summary>
    /// <param name="previous">Earlier snapshot.</param>
    /// <param name="current">Current snapshot.</param>
    /// <returns>Skills sorted by growth rate (descending).</returns>
    IReadOnlyList<(string Skill, double GrowthRate)> IdentifyTrends(
        MarketSnapshot previous,
        MarketSnapshot current);
}
