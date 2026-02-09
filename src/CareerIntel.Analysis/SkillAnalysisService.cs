using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;

namespace CareerIntel.Analysis;

/// <summary>
/// Analyzes skill demand across job vacancies and generates market snapshots.
/// </summary>
public sealed class SkillAnalysisService : ISkillAnalyzer
{
    private readonly ILogger<SkillAnalysisService> _logger;

    public SkillAnalysisService(ILogger<SkillAnalysisService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<SkillProfile>> AnalyzeSkillDemandAsync(
        IReadOnlyList<JobVacancy> vacancies)
    {
        _logger.LogInformation("Analyzing skill demand across {Count} vacancies", vacancies.Count);

        var skillCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var vacancy in vacancies)
        {
            foreach (var skill in vacancy.RequiredSkills.Concat(vacancy.PreferredSkills))
            {
                var normalized = skill.Trim();
                if (string.IsNullOrEmpty(normalized)) continue;

                skillCounts.TryGetValue(normalized, out var count);
                skillCounts[normalized] = count + 1;
            }
        }

        var maxCount = skillCounts.Values.DefaultIfEmpty(1).Max();

        var profiles = skillCounts
            .Select(kvp => new SkillProfile
            {
                SkillName = kvp.Key,
                MarketDemandScore = Math.Round((double)kvp.Value / maxCount * 100, 1)
            })
            .OrderByDescending(p => p.MarketDemandScore)
            .ToList();

        return Task.FromResult<IReadOnlyList<SkillProfile>>(profiles);
    }

    public Task<MarketSnapshot> GenerateSnapshotAsync(
        IReadOnlyList<JobVacancy> vacancies,
        string platform = "aggregate")
    {
        _logger.LogInformation("Generating market snapshot for {Platform} from {Count} vacancies",
            platform, vacancies.Count);

        var snapshot = new MarketSnapshot
        {
            Date = DateTimeOffset.UtcNow,
            TotalVacancies = vacancies.Count,
            Platform = platform
        };

        foreach (var vacancy in vacancies)
        {
            foreach (var skill in vacancy.RequiredSkills.Concat(vacancy.PreferredSkills))
            {
                var normalized = skill.Trim();
                if (string.IsNullOrEmpty(normalized)) continue;

                snapshot.SkillFrequency.TryGetValue(normalized, out var count);
                snapshot.SkillFrequency[normalized] = count + 1;
            }
        }

        var salaryGroups = vacancies
            .Where(v => v.SeniorityLevel != SeniorityLevel.Unknown && v.SalaryMin.HasValue)
            .GroupBy(v => v.SeniorityLevel);

        foreach (var group in salaryGroups)
        {
            var avg = group.Average(v =>
                (v.SalaryMin.GetValueOrDefault() + v.SalaryMax.GetValueOrDefault(v.SalaryMin!.Value)) / 2m);
            snapshot.AverageSalaryByLevel[group.Key] = Math.Round(avg, 0);
        }

        foreach (var vacancy in vacancies)
        {
            snapshot.RemotePolicyDistribution.TryGetValue(vacancy.RemotePolicy, out var count);
            snapshot.RemotePolicyDistribution[vacancy.RemotePolicy] = count + 1;
        }

        // Skill co-occurrence analysis: find pairs of skills that appear together frequently
        var pairCounts = new Dictionary<string, (int Count, decimal TotalSalary, int SalaryCount)>(StringComparer.OrdinalIgnoreCase);

        foreach (var vacancy in vacancies)
        {
            var skills = vacancy.RequiredSkills
                .Concat(vacancy.PreferredSkills)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var avgSalary = vacancy.SalaryMin.HasValue
                ? (vacancy.SalaryMin.Value + vacancy.SalaryMax.GetValueOrDefault(vacancy.SalaryMin.Value)) / 2m
                : 0m;

            for (var i = 0; i < skills.Count; i++)
            {
                for (var j = i + 1; j < skills.Count; j++)
                {
                    var key = $"{skills[i]}|{skills[j]}";
                    if (pairCounts.TryGetValue(key, out var existing))
                    {
                        pairCounts[key] = (
                            existing.Count + 1,
                            existing.TotalSalary + avgSalary,
                            existing.SalaryCount + (avgSalary > 0 ? 1 : 0));
                    }
                    else
                    {
                        pairCounts[key] = (1, avgSalary, avgSalary > 0 ? 1 : 0);
                    }
                }
            }
        }

        snapshot.TopSkillCombinations = pairCounts
            .Where(kvp => kvp.Value.Count >= 2)
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(20)
            .Select(kvp =>
            {
                var skillNames = kvp.Key.Split('|');
                var avgSal = kvp.Value.SalaryCount > 0
                    ? kvp.Value.TotalSalary / kvp.Value.SalaryCount
                    : 0m;

                return new SkillCombination
                {
                    Skills = [skillNames[0], skillNames[1]],
                    Frequency = kvp.Value.Count,
                    AverageSalary = Math.Round(avgSal, 0)
                };
            })
            .ToList();

        return Task.FromResult(snapshot);
    }

    public IReadOnlyList<(string Skill, double GrowthRate)> IdentifyTrends(
        MarketSnapshot previous,
        MarketSnapshot current)
    {
        var trends = new List<(string Skill, double GrowthRate)>();

        foreach (var (skill, currentCount) in current.SkillFrequency)
        {
            previous.SkillFrequency.TryGetValue(skill, out var previousCount);

            var growthRate = previousCount > 0
                ? (double)(currentCount - previousCount) / previousCount * 100
                : 100.0;

            trends.Add((skill, Math.Round(growthRate, 1)));
        }

        return trends.OrderByDescending(t => t.GrowthRate).ToList();
    }
}
