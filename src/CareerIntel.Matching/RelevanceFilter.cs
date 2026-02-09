using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Matching;

/// <summary>
/// Filters out vacancies that fail hard constraints defined in the user profile,
/// such as excluded companies, minimum seniority, remote policy, and salary floor.
/// </summary>
public sealed class RelevanceFilter
{
    private readonly ILogger<RelevanceFilter> _logger;

    public RelevanceFilter(ILogger<RelevanceFilter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Applies all hard-constraint filters against the provided vacancies and returns
    /// only those that pass every check.
    /// </summary>
    public IReadOnlyList<JobVacancy> Apply(
        IReadOnlyList<JobVacancy> vacancies,
        UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(vacancies);
        ArgumentNullException.ThrowIfNull(profile);

        var result = new List<JobVacancy>(vacancies.Count);

        foreach (var vacancy in vacancies)
        {
            // Hard eligibility gate — B2B/contractor, remote from Ukraine, no geo restrictions
            if (!EligibilityGate.IsEligible(vacancy))
                continue;

            if (IsExcludedCompany(vacancy, profile.Preferences))
                continue;

            if (!MeetsSeniorityRequirement(vacancy, profile.Preferences))
                continue;

            if (!MeetsRemoteRequirement(vacancy, profile.Preferences))
                continue;

            if (!MeetsSalaryFloor(vacancy, profile.Preferences))
                continue;

            result.Add(vacancy);
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// Returns true if the vacancy company is in the user's exclusion list.
    /// </summary>
    private bool IsExcludedCompany(JobVacancy vacancy, Preferences preferences)
    {
        if (preferences.ExcludeCompanies.Count == 0)
            return false;

        bool excluded = preferences.ExcludeCompanies
            .Any(c => string.Equals(c, vacancy.Company, StringComparison.OrdinalIgnoreCase));

        if (excluded)
        {
            _logger.LogDebug(
                "Filtered out '{Title}' at {Company}: company is excluded",
                vacancy.Title, vacancy.Company);
        }

        return excluded;
    }

    /// <summary>
    /// Returns true if the vacancy seniority level meets or exceeds the user's minimum.
    /// Vacancies with unknown seniority pass this check (benefit of the doubt).
    /// </summary>
    private bool MeetsSeniorityRequirement(JobVacancy vacancy, Preferences preferences)
    {
        if (vacancy.SeniorityLevel == SeniorityLevel.Unknown)
            return true;

        if (preferences.MinSeniority == SeniorityLevel.Unknown)
            return true;

        bool meets = vacancy.SeniorityLevel >= preferences.MinSeniority;

        if (!meets)
        {
            _logger.LogDebug(
                "Filtered out '{Title}' at {Company}: seniority {VacancyLevel} < minimum {MinLevel}",
                vacancy.Title, vacancy.Company, vacancy.SeniorityLevel, preferences.MinSeniority);
        }

        return meets;
    }

    /// <summary>
    /// Returns true if the vacancy meets the user's remote work preference.
    /// If the user requires remote only, OnSite vacancies are excluded.
    /// </summary>
    private bool MeetsRemoteRequirement(JobVacancy vacancy, Preferences preferences)
    {
        if (!preferences.RemoteOnly)
            return true;

        bool meets = vacancy.RemotePolicy is
            RemotePolicy.FullyRemote or
            RemotePolicy.RemoteFriendly or
            RemotePolicy.Unknown;

        if (!meets)
        {
            _logger.LogDebug(
                "Filtered out '{Title}' at {Company}: remote policy {Policy} does not meet remote-only preference",
                vacancy.Title, vacancy.Company, vacancy.RemotePolicy);
        }

        return meets;
    }

    /// <summary>
    /// Returns true if the vacancy salary meets or exceeds the user's minimum salary.
    /// Vacancies with no salary data pass this check (benefit of the doubt).
    /// </summary>
    private bool MeetsSalaryFloor(JobVacancy vacancy, Preferences preferences)
    {
        if (preferences.MinSalaryUsd <= 0)
            return true;

        if (vacancy.SalaryMax is null && vacancy.SalaryMin is null)
            return true;

        decimal offeredSalary = vacancy.SalaryMax ?? vacancy.SalaryMin ?? 0;
        bool meets = offeredSalary >= preferences.MinSalaryUsd;

        if (!meets)
        {
            _logger.LogDebug(
                "Filtered out '{Title}' at {Company}: salary {Salary} < minimum {MinSalary}",
                vacancy.Title, vacancy.Company, offeredSalary, preferences.MinSalaryUsd);
        }

        return meets;
    }
}
