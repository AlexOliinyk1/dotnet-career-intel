using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Aggregates and analyzes salary data from scraped vacancies to provide
/// salary intelligence: market rates by skill, seniority, company, and platform.
/// All salary comparisons are normalized to USD.
/// </summary>
public sealed class SalaryIntelligenceEngine(ILogger<SalaryIntelligenceEngine> logger)
{
    // ── Currency conversion rates (approximate, target = USD) ───────────

    private static readonly Dictionary<string, double> CurrencyToUsdMultiplier =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1.0,
            ["EUR"] = 1.08,
            ["GBP"] = 1.27,
            ["PLN"] = 1.0 / 4.0,
            ["UAH"] = 1.0 / 41.0,
            ["CZK"] = 1.0 / 23.0,
        };

    // ═══════════════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a comprehensive salary report from the provided vacancy set.
    /// If a <paramref name="profile"/> is supplied, the report includes the
    /// user's estimated percentile position within the market distribution.
    /// </summary>
    public SalaryReport GenerateReport(
        IReadOnlyList<JobVacancy> vacancies,
        UserProfile? profile = null)
    {
        logger.LogInformation(
            "Generating salary report from {Count} vacancies", vacancies.Count);

        var normalized = NormalizeVacancies(vacancies);

        if (normalized.Count == 0)
        {
            logger.LogWarning("No vacancies with salary data found");
            return EmptyReport(vacancies.Count);
        }

        var allMins = normalized.Select(v => v.MinUsd).OrderBy(x => x).ToList();
        var allMaxes = normalized.Select(v => v.MaxUsd).OrderBy(x => x).ToList();

        var medianMin = Median(allMins);
        var medianMax = Median(allMaxes);
        var avgMin = allMins.Average();
        var avgMax = allMaxes.Average();

        var bySeniority = BuildSeniorityBands(normalized);
        var bySkill = BuildSkillEntries(normalized, avgMin);
        var byCompany = BuildCompanyEntries(normalized);
        var byPlatform = BuildPlatformEntries(normalized);

        double? userPercentile = null;
        if (profile is not null)
        {
            var userMid = (double)(profile.Preferences.TargetSalaryUsd > 0
                ? profile.Preferences.TargetSalaryUsd
                : profile.Preferences.MinSalaryUsd);

            if (userMid > 0)
            {
                var midpoints = normalized
                    .Select(v => (v.MinUsd + v.MaxUsd) / 2.0)
                    .OrderBy(x => x)
                    .ToList();
                userPercentile = ComputePercentile(midpoints, userMid);
            }
        }

        var summary = BuildMarketSummary(
            normalized.Count, vacancies.Count, medianMin, medianMax,
            bySeniority, bySkill, byCompany);

        var primaryCurrency = vacancies
            .Where(v => !string.IsNullOrWhiteSpace(v.SalaryCurrency))
            .GroupBy(v => v.SalaryCurrency, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "USD";

        return new SalaryReport(
            TotalVacanciesWithSalary: normalized.Count,
            TotalVacanciesAnalyzed: vacancies.Count,
            MedianSalaryMin: Math.Round(medianMin, 2),
            MedianSalaryMax: Math.Round(medianMax, 2),
            AverageSalaryMin: Math.Round(avgMin, 2),
            AverageSalaryMax: Math.Round(avgMax, 2),
            PrimaryCurrency: primaryCurrency,
            BySeniority: bySeniority,
            BySkill: bySkill,
            ByCompany: byCompany,
            ByPlatform: byPlatform,
            UserPositionPercentile: userPercentile.HasValue
                ? Math.Round(userPercentile.Value, 1)
                : null,
            MarketSummary: summary);
    }

    /// <summary>
    /// Returns salary statistics for vacancies matching a specific set of skills.
    /// A vacancy matches if it requires at least one of the requested skills.
    /// </summary>
    public SkillSalaryData GetSalaryForSkills(
        IReadOnlyList<JobVacancy> vacancies,
        IReadOnlyList<string> skills)
    {
        logger.LogInformation(
            "Computing salary for skills [{Skills}] across {Count} vacancies",
            string.Join(", ", skills), vacancies.Count);

        var normalized = NormalizeVacancies(vacancies);
        var skillSet = new HashSet<string>(skills, StringComparer.OrdinalIgnoreCase);

        var matching = normalized
            .Where(v => v.AllSkills.Any(s => skillSet.Contains(s)))
            .ToList();

        if (matching.Count == 0)
        {
            return new SkillSalaryData(
                Skills: skills,
                MinSalary: 0,
                MaxSalary: 0,
                MedianSalary: 0,
                MatchingVacancies: 0,
                TopCompanies: []);
        }

        var midpoints = matching.Select(v => (v.MinUsd + v.MaxUsd) / 2.0).OrderBy(x => x).ToList();

        var topCompanies = matching
            .Where(v => !string.IsNullOrWhiteSpace(v.Original.Company))
            .GroupBy(v => v.Original.Company, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Average(v => (v.MinUsd + v.MaxUsd) / 2.0))
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        return new SkillSalaryData(
            Skills: skills,
            MinSalary: Math.Round(matching.Min(v => v.MinUsd), 2),
            MaxSalary: Math.Round(matching.Max(v => v.MaxUsd), 2),
            MedianSalary: Math.Round(Median(midpoints), 2),
            MatchingVacancies: matching.Count,
            TopCompanies: topCompanies);
    }

    /// <summary>
    /// Estimates a user's market value by finding vacancies with at least 60%
    /// skill overlap and matching seniority, then computing a salary distribution.
    /// </summary>
    public MarketValueEstimate EstimateMarketValue(
        IReadOnlyList<JobVacancy> vacancies,
        UserProfile profile)
    {
        logger.LogInformation(
            "Estimating market value for {Name}", profile.Personal.Name);

        var normalized = NormalizeVacancies(vacancies);

        if (normalized.Count == 0)
        {
            return new MarketValueEstimate(
                EstimatedMinSalary: 0,
                EstimatedMaxSalary: 0,
                Confidence: 0,
                Reasoning: "No vacancies with salary data available for estimation.",
                ValueBoostSkills: [],
                PremiumSkills: [],
                Percentile: 0);
        }

        var userSkills = new HashSet<string>(
            profile.Skills.Select(s => s.SkillName),
            StringComparer.OrdinalIgnoreCase);

        var userSeniority = profile.Preferences.MinSeniority;

        // Find vacancies with >= 60% skill overlap
        var matched = normalized
            .Select(v =>
            {
                var required = v.Original.RequiredSkills;
                if (required.Count == 0) return (Vacancy: v, Overlap: 0.0);
                var overlap = required.Count(s => userSkills.Contains(s))
                              / (double)required.Count;
                return (Vacancy: v, Overlap: overlap);
            })
            .Where(x => x.Overlap >= 0.60)
            .ToList();

        // Further filter by seniority if sufficient matches remain
        var seniorityMatched = matched
            .Where(x => x.Vacancy.Original.SeniorityLevel == userSeniority
                        || x.Vacancy.Original.SeniorityLevel == SeniorityLevel.Unknown)
            .ToList();

        // Fall back to all skill-matched if seniority filter leaves too few
        var pool = seniorityMatched.Count >= 3 ? seniorityMatched : matched;

        if (pool.Count == 0)
        {
            // Last resort: match by seniority only
            pool = normalized
                .Where(v => v.Original.SeniorityLevel == userSeniority
                            || v.Original.SeniorityLevel == SeniorityLevel.Unknown)
                .Select(v => (Vacancy: v, Overlap: 0.0))
                .ToList();
        }

        if (pool.Count == 0)
        {
            return new MarketValueEstimate(
                EstimatedMinSalary: 0,
                EstimatedMaxSalary: 0,
                Confidence: 0,
                Reasoning: "No matching vacancies found for your profile. Try broadening your skill set or seniority criteria.",
                ValueBoostSkills: FindValueBoostSkills(normalized, userSkills),
                PremiumSkills: [],
                Percentile: 0);
        }

        var mins = pool.Select(x => x.Vacancy.MinUsd).OrderBy(x => x).ToList();
        var maxes = pool.Select(x => x.Vacancy.MaxUsd).OrderBy(x => x).ToList();
        var midpoints = pool.Select(x => (x.Vacancy.MinUsd + x.Vacancy.MaxUsd) / 2.0)
            .OrderBy(x => x).ToList();

        var estimatedMin = Median(mins);
        var estimatedMax = Median(maxes);

        // Confidence based on pool size and overlap quality
        var avgOverlap = pool.Average(x => x.Overlap);
        var sizeConfidence = Math.Min(pool.Count / 20.0, 1.0) * 50;
        var overlapConfidence = avgOverlap * 50;
        var confidence = Math.Min(sizeConfidence + overlapConfidence, 100);

        // User's salary target percentile within the matched pool
        var userTarget = (double)(profile.Preferences.TargetSalaryUsd > 0
            ? profile.Preferences.TargetSalaryUsd
            : profile.Preferences.MinSalaryUsd);
        var percentile = userTarget > 0
            ? ComputePercentile(midpoints, userTarget)
            : ComputePercentile(midpoints, Median(midpoints));

        // Premium skills: user's skills that command above-average salaries
        var overallAvgMid = normalized.Average(v => (v.MinUsd + v.MaxUsd) / 2.0);
        var premiumSkills = userSkills
            .Where(skill =>
            {
                var skillVacancies = normalized
                    .Where(v => v.AllSkills.Contains(skill, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                if (skillVacancies.Count < 2) return false;
                var skillAvg = skillVacancies.Average(v => (v.MinUsd + v.MaxUsd) / 2.0);
                return skillAvg > overallAvgMid * 1.05; // >5% premium
            })
            .ToList();

        var valueBoostSkills = FindValueBoostSkills(normalized, userSkills);

        var reasoning = BuildEstimateReasoning(pool.Count, avgOverlap,
            seniorityMatched.Count >= 3, userSeniority, estimatedMin, estimatedMax);

        return new MarketValueEstimate(
            EstimatedMinSalary: Math.Round(estimatedMin, 2),
            EstimatedMaxSalary: Math.Round(estimatedMax, 2),
            Confidence: Math.Round(confidence, 1),
            Reasoning: reasoning,
            ValueBoostSkills: valueBoostSkills,
            PremiumSkills: premiumSkills,
            Percentile: Math.Round(percentile, 1));
    }

    /// <summary>
    /// Discovers 2-3 skill combinations that co-occur in the highest-salary
    /// vacancies, ranked by frequency-weighted average salary.
    /// </summary>
    public List<SkillCombinationValue> FindHighValueCombinations(
        IReadOnlyList<JobVacancy> vacancies,
        int topN = 10)
    {
        logger.LogInformation(
            "Finding top {N} high-value skill combinations from {Count} vacancies",
            topN, vacancies.Count);

        var normalized = NormalizeVacancies(vacancies);

        if (normalized.Count < 3)
            return [];

        var overallAvg = normalized.Average(v => (v.MinUsd + v.MaxUsd) / 2.0);

        // Build 2-skill combinations from required skills
        var combos = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in normalized)
        {
            var skills = v.Original.RequiredSkills
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var midSalary = (v.MinUsd + v.MaxUsd) / 2.0;

            // 2-skill combinations
            for (var i = 0; i < skills.Count; i++)
            {
                for (var j = i + 1; j < skills.Count; j++)
                {
                    var key = $"{skills[i]}+{skills[j]}";
                    if (!combos.TryGetValue(key, out var list))
                    {
                        list = [];
                        combos[key] = list;
                    }
                    list.Add(midSalary);
                }
            }

            // 3-skill combinations (only if vacancy has enough skills)
            if (skills.Count >= 3)
            {
                for (var i = 0; i < skills.Count; i++)
                {
                    for (var j = i + 1; j < skills.Count; j++)
                    {
                        for (var k = j + 1; k < skills.Count; k++)
                        {
                            var key = $"{skills[i]}+{skills[j]}+{skills[k]}";
                            if (!combos.TryGetValue(key, out var list))
                            {
                                list = [];
                                combos[key] = list;
                            }
                            list.Add(midSalary);
                        }
                    }
                }
            }
        }

        // Require at least 2 occurrences for statistical relevance
        var results = combos
            .Where(kv => kv.Value.Count >= 2)
            .Select(kv =>
            {
                var avg = kv.Value.Average();
                // Frequency-weighted score: salary premium * log(count) for ranking
                var premium = (avg - overallAvg) / overallAvg * 100;
                var weightedScore = premium * Math.Log2(kv.Value.Count + 1);
                var skillNames = kv.Key.Split('+').ToList();
                return (Skills: (IReadOnlyList<string>)skillNames,
                        AvgSalary: avg,
                        Count: kv.Value.Count,
                        Premium: premium,
                        WeightedScore: weightedScore);
            })
            .OrderByDescending(x => x.WeightedScore)
            .Take(topN)
            .Select(x => new SkillCombinationValue(
                Skills: x.Skills,
                AverageSalary: Math.Round(x.AvgSalary, 2),
                VacancyCount: x.Count,
                PremiumOverBaseline: Math.Round(x.Premium, 2)))
            .ToList();

        logger.LogInformation("Found {Count} high-value combinations", results.Count);
        return results;
    }

    /// <summary>
    /// Builds salary bands grouped by seniority level, with min/max/median
    /// statistics for each band.
    /// </summary>
    public List<SeniorityBand> GetSeniorityBands(IReadOnlyList<JobVacancy> vacancies)
    {
        logger.LogInformation(
            "Computing seniority bands from {Count} vacancies", vacancies.Count);

        var normalized = NormalizeVacancies(vacancies);
        return BuildSeniorityBands(normalized);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Salary normalization
    // ═══════════════════════════════════════════════════════════════════

    private List<NormalizedVacancy> NormalizeVacancies(IReadOnlyList<JobVacancy> vacancies)
    {
        var result = new List<NormalizedVacancy>();

        foreach (var v in vacancies)
        {
            if (!v.SalaryMin.HasValue && !v.SalaryMax.HasValue)
                continue;

            var rate = GetUsdRate(v.SalaryCurrency);

            double minUsd, maxUsd;

            if (v.SalaryMin.HasValue && v.SalaryMax.HasValue)
            {
                minUsd = (double)v.SalaryMin.Value * rate;
                maxUsd = (double)v.SalaryMax.Value * rate;
            }
            else if (v.SalaryMin.HasValue)
            {
                minUsd = (double)v.SalaryMin.Value * rate;
                maxUsd = minUsd * 1.35;
            }
            else
            {
                maxUsd = (double)v.SalaryMax!.Value * rate;
                minUsd = maxUsd * 0.75;
            }

            var allSkills = v.RequiredSkills
                .Concat(v.PreferredSkills)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.Add(new NormalizedVacancy(v, minUsd, maxUsd, allSkills));
        }

        return result;
    }

    private static double GetUsdRate(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return 1.0; // assume USD

        return CurrencyToUsdMultiplier.TryGetValue(currency, out var rate) ? rate : 1.0;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Report section builders
    // ═══════════════════════════════════════════════════════════════════

    private static List<SeniorityBand> BuildSeniorityBands(List<NormalizedVacancy> normalized)
    {
        return normalized
            .Where(v => v.Original.SeniorityLevel != SeniorityLevel.Unknown)
            .GroupBy(v => v.Original.SeniorityLevel)
            .OrderBy(g => (int)g.Key)
            .Select(g =>
            {
                var mins = g.Select(v => v.MinUsd).OrderBy(x => x).ToList();
                var maxes = g.Select(v => v.MaxUsd).OrderBy(x => x).ToList();
                var midpoints = g.Select(v => (v.MinUsd + v.MaxUsd) / 2.0).OrderBy(x => x).ToList();

                return new SeniorityBand(
                    Level: g.Key.ToString(),
                    MinSalary: Math.Round(mins.First(), 2),
                    MaxSalary: Math.Round(maxes.Last(), 2),
                    MedianSalary: Math.Round(Median(midpoints), 2),
                    VacancyCount: g.Count());
            })
            .ToList();
    }

    private static List<SkillSalaryEntry> BuildSkillEntries(
        List<NormalizedVacancy> normalized,
        double overallAverageMin)
    {
        var overallAvgMid = normalized.Average(v => (v.MinUsd + v.MaxUsd) / 2.0);

        // Collect per-skill salary data
        var skillMap = new Dictionary<string, List<NormalizedVacancy>>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in normalized)
        {
            foreach (var skill in v.AllSkills)
            {
                if (!skillMap.TryGetValue(skill, out var list))
                {
                    list = [];
                    skillMap[skill] = list;
                }
                list.Add(v);
            }
        }

        return skillMap
            .Where(kv => kv.Value.Count >= 2) // require at least 2 data points
            .Select(kv =>
            {
                var vacs = kv.Value;
                var avgMin = vacs.Average(v => v.MinUsd);
                var avgMax = vacs.Average(v => v.MaxUsd);
                var midpoints = vacs.Select(v => (v.MinUsd + v.MaxUsd) / 2.0).OrderBy(x => x).ToList();
                var medianMid = Median(midpoints);
                var skillAvgMid = midpoints.Average();
                var premium = (skillAvgMid - overallAvgMid) / overallAvgMid * 100;

                return new SkillSalaryEntry(
                    Skill: kv.Key,
                    AverageSalaryMin: Math.Round(avgMin, 2),
                    AverageSalaryMax: Math.Round(avgMax, 2),
                    MedianSalary: Math.Round(medianMid, 2),
                    VacancyCount: vacs.Count,
                    SalaryPremiumPercent: Math.Round(premium, 2));
            })
            .OrderByDescending(e => e.SalaryPremiumPercent)
            .ToList();
    }

    private static List<CompanySalaryEntry> BuildCompanyEntries(
        List<NormalizedVacancy> normalized)
    {
        return normalized
            .Where(v => !string.IsNullOrWhiteSpace(v.Original.Company))
            .GroupBy(v => v.Original.Company, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 1)
            .Select(g =>
            {
                var avgMin = g.Average(v => v.MinUsd);
                var avgMax = g.Average(v => v.MaxUsd);
                var topSkills = g
                    .SelectMany(v => v.Original.RequiredSkills)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .GroupBy(s => s.Trim(), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(sg => sg.Count())
                    .Take(5)
                    .Select(sg => sg.Key);

                return new CompanySalaryEntry(
                    Company: g.Key,
                    AverageSalaryMin: Math.Round(avgMin, 2),
                    AverageSalaryMax: Math.Round(avgMax, 2),
                    VacancyCount: g.Count(),
                    TopSkillsRequired: string.Join(", ", topSkills));
            })
            .OrderByDescending(e => (e.AverageSalaryMin + e.AverageSalaryMax) / 2.0)
            .ToList();
    }

    private static List<PlatformSalaryEntry> BuildPlatformEntries(
        List<NormalizedVacancy> normalized)
    {
        return normalized
            .Where(v => !string.IsNullOrWhiteSpace(v.Original.SourcePlatform))
            .GroupBy(v => v.Original.SourcePlatform, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PlatformSalaryEntry(
                Platform: g.Key,
                AverageSalaryMin: Math.Round(g.Average(v => v.MinUsd), 2),
                AverageSalaryMax: Math.Round(g.Average(v => v.MaxUsd), 2),
                VacancyCount: g.Count()))
            .OrderByDescending(e => (e.AverageSalaryMin + e.AverageSalaryMax) / 2.0)
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Market value helpers
    // ═══════════════════════════════════════════════════════════════════

    private static List<string> FindValueBoostSkills(
        List<NormalizedVacancy> normalized,
        HashSet<string> userSkills)
    {
        var overallAvgMid = normalized.Average(v => (v.MinUsd + v.MaxUsd) / 2.0);

        // Find skills user does NOT have that command a salary premium
        var allMarketSkills = normalized
            .SelectMany(v => v.AllSkills)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return allMarketSkills
            .Where(s => !userSkills.Contains(s))
            .Select(skill =>
            {
                var vacs = normalized
                    .Where(v => v.AllSkills.Contains(skill, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                if (vacs.Count < 3) return (Skill: skill, Premium: 0.0);
                var avg = vacs.Average(v => (v.MinUsd + v.MaxUsd) / 2.0);
                return (Skill: skill, Premium: (avg - overallAvgMid) / overallAvgMid * 100);
            })
            .Where(x => x.Premium > 5) // meaningful premium only
            .OrderByDescending(x => x.Premium)
            .Take(5)
            .Select(x => x.Skill)
            .ToList();
    }

    private static string BuildEstimateReasoning(
        int poolSize,
        double avgOverlap,
        bool seniorityFiltered,
        SeniorityLevel seniority,
        double estimatedMin,
        double estimatedMax)
    {
        var parts = new List<string>
        {
            $"Based on {poolSize} matching vacancies with {avgOverlap:P0} average skill overlap"
        };

        if (seniorityFiltered)
            parts.Add($"filtered to {seniority} level");

        parts.Add($"the estimated salary range is ${estimatedMin:N0}-${estimatedMax:N0} USD");

        if (poolSize < 5)
            parts.Add("low sample size reduces confidence");
        else if (poolSize >= 20)
            parts.Add("strong sample size provides high confidence");

        return string.Join("; ", parts) + ".";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Market summary
    // ═══════════════════════════════════════════════════════════════════

    private static string BuildMarketSummary(
        int withSalary,
        int totalAnalyzed,
        double medianMin,
        double medianMax,
        IReadOnlyList<SeniorityBand> seniorityBands,
        IReadOnlyList<SkillSalaryEntry> skillEntries,
        IReadOnlyList<CompanySalaryEntry> companyEntries)
    {
        var parts = new List<string>();

        parts.Add($"Analysis of {withSalary} vacancies with salary data (out of {totalAnalyzed} total).");
        parts.Add($"Overall median salary range: ${medianMin:N0}-${medianMax:N0} USD.");

        // Top seniority band info
        var seniorBand = seniorityBands.FirstOrDefault(b =>
            string.Equals(b.Level, nameof(SeniorityLevel.Senior), StringComparison.OrdinalIgnoreCase));
        if (seniorBand is not null)
        {
            parts.Add($"Senior roles: median ${seniorBand.MedianSalary:N0}, " +
                       $"range ${seniorBand.MinSalary:N0}-${seniorBand.MaxSalary:N0} " +
                       $"({seniorBand.VacancyCount} vacancies).");
        }

        // Top-paying skills
        var topSkills = skillEntries
            .Where(s => s.SalaryPremiumPercent > 0)
            .Take(3)
            .Select(s => s.Skill)
            .ToList();
        if (topSkills.Count > 0)
            parts.Add($"Top-paying skills: {string.Join(", ", topSkills)}.");

        // Highest-paying companies
        var topCompanies = companyEntries.Take(3).Select(c => c.Company).ToList();
        if (topCompanies.Count > 0)
            parts.Add($"Companies offering highest salaries: {string.Join(", ", topCompanies)}.");

        return string.Join(" ", parts);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Statistics helpers
    // ═══════════════════════════════════════════════════════════════════

    private static double Median(List<double> sorted)
    {
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    /// <summary>
    /// Computes the percentile rank of <paramref name="value"/> within a sorted list.
    /// Returns 0-100.
    /// </summary>
    private static double ComputePercentile(List<double> sorted, double value)
    {
        if (sorted.Count == 0) return 0;
        var below = sorted.Count(x => x < value);
        return (double)below / sorted.Count * 100;
    }

    private static SalaryReport EmptyReport(int totalAnalyzed) =>
        new(
            TotalVacanciesWithSalary: 0,
            TotalVacanciesAnalyzed: totalAnalyzed,
            MedianSalaryMin: 0,
            MedianSalaryMax: 0,
            AverageSalaryMin: 0,
            AverageSalaryMax: 0,
            PrimaryCurrency: "USD",
            BySeniority: [],
            BySkill: [],
            ByCompany: [],
            ByPlatform: [],
            UserPositionPercentile: null,
            MarketSummary: "No salary data available in the analyzed vacancies.");

    // ═══════════════════════════════════════════════════════════════════
    //  Internal types
    // ═══════════════════════════════════════════════════════════════════

    private sealed record NormalizedVacancy(
        JobVacancy Original,
        double MinUsd,
        double MaxUsd,
        List<string> AllSkills);
}

// ═════════════════════════════════════════════════════════════════════════
//  Public result records
// ═════════════════════════════════════════════════════════════════════════

/// <summary>
/// Comprehensive salary report aggregated across all analyzed vacancies.
/// </summary>
public sealed record SalaryReport(
    int TotalVacanciesWithSalary,
    int TotalVacanciesAnalyzed,
    double MedianSalaryMin,
    double MedianSalaryMax,
    double AverageSalaryMin,
    double AverageSalaryMax,
    string PrimaryCurrency,
    IReadOnlyList<SeniorityBand> BySeniority,
    IReadOnlyList<SkillSalaryEntry> BySkill,
    IReadOnlyList<CompanySalaryEntry> ByCompany,
    IReadOnlyList<PlatformSalaryEntry> ByPlatform,
    double? UserPositionPercentile,
    string MarketSummary);

/// <summary>
/// Salary statistics for a single seniority level.
/// </summary>
public sealed record SeniorityBand(
    string Level,
    double MinSalary,
    double MaxSalary,
    double MedianSalary,
    int VacancyCount);

/// <summary>
/// Salary statistics for a single skill, including premium over baseline.
/// </summary>
public sealed record SkillSalaryEntry(
    string Skill,
    double AverageSalaryMin,
    double AverageSalaryMax,
    double MedianSalary,
    int VacancyCount,
    double SalaryPremiumPercent);

/// <summary>
/// Salary statistics grouped by company.
/// </summary>
public sealed record CompanySalaryEntry(
    string Company,
    double AverageSalaryMin,
    double AverageSalaryMax,
    int VacancyCount,
    string TopSkillsRequired);

/// <summary>
/// Salary statistics grouped by source platform (e.g., djinni, linkedin).
/// </summary>
public sealed record PlatformSalaryEntry(
    string Platform,
    double AverageSalaryMin,
    double AverageSalaryMax,
    int VacancyCount);

/// <summary>
/// Salary data filtered to a specific set of skills.
/// </summary>
public sealed record SkillSalaryData(
    IReadOnlyList<string> Skills,
    double MinSalary,
    double MaxSalary,
    double MedianSalary,
    int MatchingVacancies,
    IReadOnlyList<string> TopCompanies);

/// <summary>
/// Estimated market value for a user profile, with confidence and actionable insights.
/// </summary>
public sealed record MarketValueEstimate(
    double EstimatedMinSalary,
    double EstimatedMaxSalary,
    double Confidence,
    string Reasoning,
    IReadOnlyList<string> ValueBoostSkills,
    IReadOnlyList<string> PremiumSkills,
    double Percentile);

/// <summary>
/// A skill combination (2-3 skills) and its associated salary premium.
/// </summary>
public sealed record SkillCombinationValue(
    IReadOnlyList<string> Skills,
    double AverageSalary,
    int VacancyCount,
    double PremiumOverBaseline);
