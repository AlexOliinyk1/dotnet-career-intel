using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

public sealed class CompanyDiscoveryEngine(ILogger<CompanyDiscoveryEngine> logger)
{
    /// <summary>
    /// Discovers Ukraine-friendly companies from scraped vacancy data.
    /// Groups by company, counts vacancy types, computes salary ranges, and flags confirmed hiring.
    /// </summary>
    public List<UkraineFriendlyCompany> DiscoverFromVacancies(IReadOnlyList<JobVacancy> vacancies)
    {
        logger.LogInformation("Discovering companies from {Count} vacancies", vacancies.Count);

        var grouped = vacancies
            .Where(v => !string.IsNullOrWhiteSpace(v.Company))
            .GroupBy(v => v.Company.Trim(), StringComparer.OrdinalIgnoreCase);

        var discovered = new List<UkraineFriendlyCompany>();

        foreach (var group in grouped)
        {
            var companyVacancies = group.ToList();

            var totalCount = companyVacancies.Count;
            var remoteCount = companyVacancies.Count(v =>
                v.RemotePolicy is RemotePolicy.FullyRemote or RemotePolicy.RemoteFriendly);
            var b2bCount = companyVacancies.Count(v =>
                v.EngagementType is EngagementType.ContractB2B or EngagementType.Freelance);

            // Confirmed if: remote + not geo-restricted + engagement is B2B/Freelance/Unknown
            var confirmedUkraineHiring = companyVacancies.Any(v =>
                v.RemotePolicy is RemotePolicy.FullyRemote or RemotePolicy.RemoteFriendly
                && v.GeoRestrictions.Count == 0
                && v.EngagementType is EngagementType.ContractB2B
                    or EngagementType.Freelance
                    or EngagementType.Unknown);

            // Compute average salary ranges from vacancies that have salary info
            var withSalaryMin = companyVacancies
                .Where(v => v.SalaryMin.HasValue)
                .Select(v => v.SalaryMin!.Value)
                .ToList();

            var withSalaryMax = companyVacancies
                .Where(v => v.SalaryMax.HasValue)
                .Select(v => v.SalaryMax!.Value)
                .ToList();

            decimal? avgSalaryMin = withSalaryMin.Count > 0
                ? Math.Round(withSalaryMin.Average(), 0)
                : null;

            decimal? avgSalaryMax = withSalaryMax.Count > 0
                ? Math.Round(withSalaryMax.Average(), 0)
                : null;

            // Collect tech stack from all vacancy RequiredSkills
            var techStack = companyVacancies
                .SelectMany(v => v.RequiredSkills)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            // Record source platforms
            var sources = companyVacancies
                .Select(v => v.SourcePlatform)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dates = companyVacancies
                .Select(v => v.PostedDate)
                .Where(d => d != default)
                .ToList();

            var company = new UkraineFriendlyCompany
            {
                Name = group.Key,
                VacancyCount = totalCount,
                RemoteVacancyCount = remoteCount,
                B2BVacancyCount = b2bCount,
                ConfirmedUkraineHiring = confirmedUkraineHiring,
                AvgSalaryMin = avgSalaryMin,
                AvgSalaryMax = avgSalaryMax,
                TechStack = techStack,
                Sources = sources,
                FirstSeen = dates.Count > 0 ? dates.Min() : DateTimeOffset.UtcNow,
                LastSeen = dates.Count > 0 ? dates.Max() : DateTimeOffset.UtcNow
            };

            discovered.Add(company);
        }

        logger.LogInformation("Discovered {Count} companies from vacancy data", discovered.Count);
        return discovered;
    }

    /// <summary>
    /// Merges newly discovered companies with an existing list.
    /// Matches by name (case-insensitive), updates counts and timestamps, preserves manual notes.
    /// </summary>
    public List<UkraineFriendlyCompany> MergeWithExisting(
        List<UkraineFriendlyCompany> existing,
        List<UkraineFriendlyCompany> discovered)
    {
        logger.LogInformation(
            "Merging {DiscoveredCount} discovered companies with {ExistingCount} existing",
            discovered.Count, existing.Count);

        var merged = new List<UkraineFriendlyCompany>(existing);
        var existingLookup = merged.ToDictionary(
            c => c.Name,
            c => c,
            StringComparer.OrdinalIgnoreCase);

        foreach (var disc in discovered)
        {
            if (existingLookup.TryGetValue(disc.Name, out var match))
            {
                // Update counts
                match.VacancyCount += disc.VacancyCount;
                match.RemoteVacancyCount += disc.RemoteVacancyCount;
                match.B2BVacancyCount += disc.B2BVacancyCount;

                // Upgrade confirmed status (never downgrade)
                if (disc.ConfirmedUkraineHiring)
                    match.ConfirmedUkraineHiring = true;

                // Recalculate average salary if new data available
                if (disc.AvgSalaryMin.HasValue)
                {
                    match.AvgSalaryMin = match.AvgSalaryMin.HasValue
                        ? (match.AvgSalaryMin + disc.AvgSalaryMin) / 2
                        : disc.AvgSalaryMin;
                }

                if (disc.AvgSalaryMax.HasValue)
                {
                    match.AvgSalaryMax = match.AvgSalaryMax.HasValue
                        ? (match.AvgSalaryMax + disc.AvgSalaryMax) / 2
                        : disc.AvgSalaryMax;
                }

                // Add new tech stack entries
                var newTech = disc.TechStack
                    .Where(t => !match.TechStack.Contains(t, StringComparer.OrdinalIgnoreCase));
                match.TechStack.AddRange(newTech);

                // Add new sources
                var newSources = disc.Sources
                    .Where(s => !match.Sources.Contains(s, StringComparer.OrdinalIgnoreCase));
                match.Sources.AddRange(newSources);

                // Update timestamps
                if (disc.FirstSeen < match.FirstSeen)
                    match.FirstSeen = disc.FirstSeen;

                if (disc.LastSeen > match.LastSeen)
                    match.LastSeen = disc.LastSeen;

                // Preserve existing Notes — do not overwrite
            }
            else
            {
                merged.Add(disc);
                existingLookup[disc.Name] = disc;
            }
        }

        logger.LogInformation("Merge complete. Total companies: {Count}", merged.Count);
        return merged;
    }

    /// <summary>
    /// Returns the top N companies ranked by a composite score:
    /// B2B count * 3 + remote count * 2 + vacancy count + (confirmed ? 10 : 0).
    /// </summary>
    public List<UkraineFriendlyCompany> GetTopCompanies(
        List<UkraineFriendlyCompany> companies,
        int top = 20)
    {
        return companies
            .OrderByDescending(c => ComputeScore(c))
            .ThenBy(c => c.Name)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// Filters companies to those whose TechStack overlaps with the given skills.
    /// </summary>
    public List<UkraineFriendlyCompany> FilterByTechStack(
        List<UkraineFriendlyCompany> companies,
        IEnumerable<string> skills)
    {
        var skillSet = new HashSet<string>(skills, StringComparer.OrdinalIgnoreCase);

        return companies
            .Where(c => c.TechStack.Any(t => skillSet.Contains(t)))
            .ToList();
    }

    private static double ComputeScore(UkraineFriendlyCompany company)
    {
        return company.B2BVacancyCount * 3
             + company.RemoteVacancyCount * 2
             + company.VacancyCount
             + (company.ConfirmedUkraineHiring ? 10 : 0);
    }
}
