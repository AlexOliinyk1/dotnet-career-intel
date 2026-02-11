using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Matching;

/// <summary>
/// Authoritative GO/NO-GO decision engine for job applications.
/// Analyzes vacancy-profile fit and provides actionable verdicts: APPLY NOW, LEARN THEN APPLY, or SKIP.
/// This is the "brain" that prevents wasted effort on wrong positions and over-preparation paralysis.
/// </summary>
public sealed class ApplicationDecisionEngine
{
    private readonly ProfileMatcher _matcher;
    private readonly ScoringEngine _scorer;

    public ApplicationDecisionEngine(ProfileMatcher matcher, ScoringEngine scorer)
    {
        _matcher = matcher;
        _scorer = scorer;
    }

    /// <summary>
    /// Makes the final GO/NO-GO decision for a vacancy.
    /// </summary>
    public ApplicationDecision Decide(JobVacancy vacancy, UserProfile profile)
    {
        // Calculate match score
        var matchResult = _scorer.Score(vacancy, profile);
        var matchScore = (int)matchResult.OverallScore;

        // Analyze skill gaps
        var skillGaps = AnalyzeSkillGaps(vacancy, profile);
        var readinessScore = CalculateReadiness(matchScore, skillGaps);

        // Determine verdict
        var verdict = DetermineVerdict(matchScore, readinessScore, skillGaps, vacancy);
        var reasoning = BuildReasoning(verdict, matchScore, readinessScore, skillGaps, vacancy);
        var learningTime = EstimateLearningTime(skillGaps, verdict);
        var confidence = CalculateConfidence(matchScore, readinessScore, skillGaps);

        return new ApplicationDecision
        {
            Verdict = verdict,
            Reasoning = reasoning,
            MatchScore = matchScore,
            ReadinessScore = readinessScore,
            SkillGaps = skillGaps,
            EstimatedLearningHours = learningTime,
            Confidence = confidence,
            ApplyByDate = CalculateApplyByDate(verdict, learningTime, vacancy),
            CriticalMissingSkills = skillGaps
                .Where(g => g.IsCritical && g.CurrentLevel == 0)
                .Select(g => g.SkillName)
                .ToList(),
            QuickWins = skillGaps
                .Where(g => g.HoursToLearn <= 4)
                .Select(g => g.SkillName)
                .ToList()
        };
    }

    private ApplicationVerdict DetermineVerdict(
        int matchScore,
        int readinessScore,
        List<SkillGap> skillGaps,
        JobVacancy vacancy)
    {
        // SKIP criteria - hard blockers
        if (matchScore < 30)
            return ApplicationVerdict.Skip;

        if (HasDealBreakerGaps(skillGaps))
            return ApplicationVerdict.Skip;

        if (IsSalaryMismatch(vacancy))
            return ApplicationVerdict.Skip;

        if (IsGeoRestricted(vacancy))
            return ApplicationVerdict.Skip;

        // APPLY_NOW criteria - ready enough
        if (readinessScore >= 75)
            return ApplicationVerdict.ApplyNow;

        if (matchScore >= 80 && readinessScore >= 60)
            return ApplicationVerdict.ApplyNow; // Strong match, acceptable readiness

        if (skillGaps.All(g => !g.IsCritical))
            return ApplicationVerdict.ApplyNow; // No critical gaps

        // LEARN_THEN_APPLY criteria - worth the investment
        var totalLearningHours = skillGaps.Sum(g => g.HoursToLearn);

        if (totalLearningHours <= 8 && matchScore >= 60)
            return ApplicationVerdict.LearnThenApply; // Quick learning, decent match

        if (totalLearningHours <= 24 && matchScore >= 70)
            return ApplicationVerdict.LearnThenApply; // Reasonable investment for good match

        if (totalLearningHours <= 40 && matchScore >= 80 && vacancy.SalaryMax >= 120000)
            return ApplicationVerdict.LearnThenApply; // High ROI position

        // Everything else: SKIP (too much learning for questionable ROI)
        return ApplicationVerdict.Skip;
    }

    private bool HasDealBreakerGaps(List<SkillGap> gaps)
    {
        // More than 3 critical skills missing = deal breaker
        var criticalGaps = gaps.Count(g => g.IsCritical && g.CurrentLevel == 0);
        if (criticalGaps > 3)
            return true;

        // Any single skill requiring >80 hours = deal breaker
        if (gaps.Any(g => g.HoursToLearn > 80))
            return true;

        return false;
    }

    private bool IsSalaryMismatch(JobVacancy vacancy)
    {
        // Skip if salary is too low (for now, hardcoded threshold)
        // In production, this should compare to user's minimum acceptable salary
        if (vacancy.SalaryMax.HasValue && vacancy.SalaryMax.Value < 40000)
            return true;

        return false;
    }

    private bool IsGeoRestricted(JobVacancy vacancy)
    {
        // Skip if geo-restricted to incompatible regions
        // TODO: Implement GeoRestrictions enum in Core.Enums and check against user profile
        // For now, assume all vacancies are geo-accessible
        return false;
    }

    private List<SkillGap> AnalyzeSkillGaps(JobVacancy vacancy, UserProfile profile)
    {
        var gaps = new List<SkillGap>();
        var userSkills = profile.Skills.ToDictionary(
            s => s.SkillName.ToLowerInvariant(),
            s => s.ProficiencyLevel);

        // Analyze required skills
        foreach (var requiredSkill in vacancy.RequiredSkills)
        {
            var normalizedSkill = requiredSkill.ToLowerInvariant();
            var currentLevel = userSkills.TryGetValue(normalizedSkill, out var level) ? level : 0;
            var targetLevel = 4; // Required skills need high proficiency

            if (currentLevel < targetLevel)
            {
                gaps.Add(new SkillGap
                {
                    SkillName = requiredSkill,
                    CurrentLevel = currentLevel,
                    TargetLevel = targetLevel,
                    IsCritical = true,
                    HoursToLearn = EstimateHoursToLearn(normalizedSkill, currentLevel, targetLevel)
                });
            }
        }

        // Analyze preferred skills (nice-to-have)
        foreach (var preferredSkill in vacancy.PreferredSkills)
        {
            var normalizedSkill = preferredSkill.ToLowerInvariant();
            if (gaps.Any(g => g.SkillName.Equals(preferredSkill, StringComparison.OrdinalIgnoreCase)))
                continue; // Already counted as required

            var currentLevel = userSkills.TryGetValue(normalizedSkill, out var level) ? level : 0;
            var targetLevel = 3; // Preferred skills need moderate proficiency

            if (currentLevel < targetLevel)
            {
                gaps.Add(new SkillGap
                {
                    SkillName = preferredSkill,
                    CurrentLevel = currentLevel,
                    TargetLevel = targetLevel,
                    IsCritical = false,
                    HoursToLearn = EstimateHoursToLearn(normalizedSkill, currentLevel, targetLevel)
                });
            }
        }

        return gaps.OrderByDescending(g => g.IsCritical).ThenBy(g => g.HoursToLearn).ToList();
    }

    private int EstimateHoursToLearn(string skill, int currentLevel, int targetLevel)
    {
        var levelDiff = targetLevel - currentLevel;

        // Base hours per level: 0->1 = 8h, 1->2 = 12h, 2->3 = 16h, 3->4 = 20h, 4->5 = 30h
        var hoursPerLevel = new[] { 8, 12, 16, 20, 30 };
        var totalHours = 0;

        for (var i = currentLevel; i < targetLevel && i < 5; i++)
        {
            totalHours += hoursPerLevel[i];
        }

        // Adjust for skill complexity
        var complexityMultiplier = GetSkillComplexity(skill);
        totalHours = (int)(totalHours * complexityMultiplier);

        return totalHours;
    }

    private double GetSkillComplexity(string skill)
    {
        // Advanced/architectural skills take longer
        var advancedSkills = new[] { "kubernetes", "system design", "microservices", "distributed systems", "aws", "azure", "gcp" };
        var intermediateSkills = new[] { "react", "angular", "vue", "docker", "ci/cd", "sql", "nosql" };

        if (advancedSkills.Any(s => skill.Contains(s, StringComparison.OrdinalIgnoreCase)))
            return 1.5;

        if (intermediateSkills.Any(s => skill.Contains(s, StringComparison.OrdinalIgnoreCase)))
            return 1.2;

        return 1.0; // Standard complexity
    }

    private int CalculateReadiness(int matchScore, List<SkillGap> skillGaps)
    {
        // Readiness = match score adjusted by skill gaps
        var gapPenalty = skillGaps.Count(g => g.IsCritical) * 10;
        gapPenalty += skillGaps.Count(g => !g.IsCritical) * 5;

        var readiness = matchScore - gapPenalty;
        return Math.Clamp(readiness, 0, 100);
    }

    private string BuildReasoning(
        ApplicationVerdict verdict,
        int matchScore,
        int readinessScore,
        List<SkillGap> skillGaps,
        JobVacancy vacancy)
    {
        var reasons = new List<string>();

        switch (verdict)
        {
            case ApplicationVerdict.ApplyNow:
                reasons.Add($"✓ Strong fit: {matchScore}% match, {readinessScore}% ready");
                if (skillGaps.Count == 0)
                    reasons.Add("✓ No skill gaps - you meet all requirements");
                else if (skillGaps.All(g => !g.IsCritical))
                    reasons.Add($"✓ Only {skillGaps.Count} nice-to-have skills missing");
                else
                    reasons.Add($"✓ Minor gaps ({skillGaps.Count(g => g.IsCritical)} skills) won't block you");
                reasons.Add("→ APPLY NOW - you're ready enough");
                break;

            case ApplicationVerdict.LearnThenApply:
                var hours = skillGaps.Sum(g => g.HoursToLearn);
                reasons.Add($"⚠ Good fit ({matchScore}% match) but needs preparation");
                reasons.Add($"⚠ {skillGaps.Count(g => g.IsCritical)} critical gaps: {string.Join(", ", skillGaps.Where(g => g.IsCritical).Take(3).Select(g => g.SkillName))}");
                reasons.Add($"→ LEARN first (~{hours}h), then APPLY");
                if (hours <= 8)
                    reasons.Add($"💡 Quick win! Just {hours} hours of focused learning");
                break;

            case ApplicationVerdict.Skip:
                if (matchScore < 30)
                    reasons.Add($"✗ Poor match ({matchScore}%) - not aligned with your skills");
                else if (HasDealBreakerGaps(skillGaps))
                    reasons.Add($"✗ Too many critical gaps ({skillGaps.Count(g => g.IsCritical)}) - high effort, uncertain ROI");
                else if (IsSalaryMismatch(vacancy))
                    reasons.Add("✗ Salary below your threshold");
                else
                    reasons.Add($"✗ Learning time ({skillGaps.Sum(g => g.HoursToLearn)}h) not justified by match quality");
                reasons.Add("→ SKIP - focus on better opportunities");
                break;
        }

        return string.Join("\n", reasons);
    }

    private int EstimateLearningTime(List<SkillGap> skillGaps, ApplicationVerdict verdict)
    {
        if (verdict == ApplicationVerdict.ApplyNow)
            return 0;

        if (verdict == ApplicationVerdict.Skip)
            return 0;

        // For LEARN_THEN_APPLY, sum critical gaps only (ignore nice-to-haves for time estimate)
        return skillGaps.Where(g => g.IsCritical).Sum(g => g.HoursToLearn);
    }

    private int CalculateConfidence(int matchScore, int readinessScore, List<SkillGap> skillGaps)
    {
        // Confidence in this decision
        var confidence = 70; // Base confidence

        // High match/readiness = higher confidence
        if (matchScore >= 80 && readinessScore >= 80)
            confidence += 20;
        else if (matchScore >= 60 && readinessScore >= 60)
            confidence += 10;

        // Clear gaps = higher confidence
        if (skillGaps.Count == 0 || skillGaps.All(g => !g.IsCritical))
            confidence += 10;

        return Math.Clamp(confidence, 0, 100);
    }

    private DateTimeOffset? CalculateApplyByDate(ApplicationVerdict verdict, int learningHours, JobVacancy vacancy)
    {
        if (verdict == ApplicationVerdict.ApplyNow)
            return DateTimeOffset.UtcNow.AddDays(2); // Apply within 2 days

        if (verdict == ApplicationVerdict.LearnThenApply)
        {
            // Assume 2 hours/day learning capacity
            var days = (int)Math.Ceiling(learningHours / 2.0);
            return DateTimeOffset.UtcNow.AddDays(days + 1); // Learning time + 1 day buffer
        }

        return null; // SKIP = no apply date
    }
}

/// <summary>
/// Final decision verdict for a job application.
/// </summary>
public enum ApplicationVerdict
{
    /// <summary>Apply immediately - you're ready enough (75%+ readiness)</summary>
    ApplyNow,

    /// <summary>Learn critical skills first (8-40 hours), then apply</summary>
    LearnThenApply,

    /// <summary>Skip - poor match or excessive learning time required</summary>
    Skip
}

/// <summary>
/// Complete decision package for a job application.
/// </summary>
public sealed class ApplicationDecision
{
    public ApplicationVerdict Verdict { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public int MatchScore { get; set; }
    public int ReadinessScore { get; set; }
    public List<SkillGap> SkillGaps { get; set; } = [];
    public int EstimatedLearningHours { get; set; }
    public int Confidence { get; set; }
    public DateTimeOffset? ApplyByDate { get; set; }
    public List<string> CriticalMissingSkills { get; set; } = [];
    public List<string> QuickWins { get; set; } = [];
}
