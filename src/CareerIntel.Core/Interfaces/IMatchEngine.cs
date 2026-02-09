using CareerIntel.Core.Models;

namespace CareerIntel.Core.Interfaces;

/// <summary>
/// Contract for matching user profiles against job vacancies.
/// </summary>
public interface IMatchEngine
{
    /// <summary>
    /// Matches a user profile against a single vacancy.
    /// </summary>
    /// <param name="vacancy">The job vacancy to match against.</param>
    /// <returns>Detailed match score.</returns>
    MatchScore ComputeMatch(JobVacancy vacancy);

    /// <summary>
    /// Matches a user profile against all provided vacancies and returns them ranked.
    /// </summary>
    /// <param name="vacancies">Vacancies to match against.</param>
    /// <param name="minimumScore">Minimum score threshold (0-100).</param>
    /// <returns>Vacancies with match scores, sorted by score descending.</returns>
    IReadOnlyList<JobVacancy> RankVacancies(
        IReadOnlyList<JobVacancy> vacancies,
        double minimumScore = 0);

    /// <summary>
    /// Reloads the user profile from the data source.
    /// </summary>
    Task ReloadProfileAsync(CancellationToken cancellationToken = default);
}
