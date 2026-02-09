using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

public sealed class LearningROIEngine(ILogger<LearningROIEngine> logger)
{
    private const double HoursWarningThreshold = 80.0;
    private const double HoursStopThreshold = 160.0;
    private const int HighMatchVacancyThreshold = 5;
    private const double HighMatchPercent = 70.0;
    private const double FatigueMatchThreshold = 65.0;
    private const int FatigueExperienceYears = 3;
    private const double FatigueWeeklyHoursThreshold = 20.0;
    private const int FatigueWeeksThreshold = 3;

    public LearningPlan GeneratePlan(
        UserProfile profile,
        IReadOnlyList<JobVacancy> targetVacancies,
        IReadOnlyList<InterviewFeedback> interviewHistory,
        MatchScore? latestMatchScore = null)
    {
        logger.LogInformation("Generating learning plan for user with {SkillCount} skills against {VacancyCount} target vacancies",
            profile.Skills.Count, targetVacancies.Count);

        // 1. Compute skill gaps across all target vacancies
        var allRequiredSkills = targetVacancies
            .SelectMany(v => v.RequiredSkills)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allPreferredSkills = targetVacancies
            .SelectMany(v => v.PreferredSkills)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 2. For each gap, compute ROI
        var learningStatuses = new List<LearningStatus>();

        foreach (var requiredSkill in allRequiredSkills)
        {
            var status = ComputeLearningStatus(requiredSkill, profile, targetVacancies, interviewHistory, isRequired: true);
            learningStatuses.Add(status);
        }

        foreach (var preferredSkill in allPreferredSkills)
        {
            if (learningStatuses.Any(s => s.SkillName.Equals(preferredSkill, StringComparison.OrdinalIgnoreCase)))
                continue;

            var status = ComputeLearningStatus(preferredSkill, profile, targetVacancies, interviewHistory, isRequired: false);
            learningStatuses.Add(status);
        }

        // Sort by ROI descending
        learningStatuses = learningStatuses.OrderByDescending(s => s.LearningROI).ToList();

        // 3. Compute total estimated hours
        double totalHours = EstimateTotalHours(learningStatuses);

        // 4. Detect over-learning and set stop signals
        bool overlearningDetected = false;
        ApplyStopSignals(learningStatuses, totalHours, profile, targetVacancies, latestMatchScore, ref overlearningDetected);

        // 5. Generate global recommendation
        string globalRecommendation = GenerateGlobalRecommendation(
            learningStatuses, totalHours, overlearningDetected, profile, targetVacancies, latestMatchScore);

        logger.LogInformation(
            "Learning plan generated: {SkillCount} skills, {TotalHours:F0} estimated hours, overlearning={Overlearning}",
            learningStatuses.Count, totalHours, overlearningDetected);

        return new LearningPlan
        {
            Skills = learningStatuses,
            OverlearningDetected = overlearningDetected,
            GlobalRecommendation = globalRecommendation,
            TotalEstimatedHours = (int)totalHours
        };
    }

    /// <summary>
    /// Explicit fatigue check for a user who may be over-preparing.
    /// </summary>
    public (bool ShouldStop, string Reason) CheckFatigue(
        UserProfile profile,
        IReadOnlyList<JobVacancy> vacancies,
        MatchScore? avgMatchScore)
    {
        // If avgMatchScore >= 65 and profile has 3+ years experience: ready to apply
        double totalExperienceYears = profile.Skills
            .Where(s => s.YearsOfExperience > 0)
            .Select(s => s.YearsOfExperience)
            .DefaultIfEmpty(0)
            .Max();

        if (avgMatchScore is not null
            && avgMatchScore.OverallScore >= FatigueMatchThreshold
            && totalExperienceYears >= FatigueExperienceYears)
        {
            logger.LogWarning("Fatigue check triggered: user has {Score}% match and {Years} years experience",
                avgMatchScore.OverallScore, totalExperienceYears);
            return (true, $"You're ready. With a {avgMatchScore.OverallScore:F0}% match score and {totalExperienceYears:F0}+ years of experience, further study has diminishing returns. Apply now.");
        }

        // Check for high match against multiple vacancies
        int highMatchCount = 0;
        foreach (var vacancy in vacancies)
        {
            int matchingSkills = vacancy.RequiredSkills
                .Count(rs => profile.Skills.Any(ps =>
                    ps.SkillName.Equals(rs, StringComparison.OrdinalIgnoreCase) && ps.ProficiencyLevel >= 3));
            double matchPercent = vacancy.RequiredSkills.Count > 0
                ? (double)matchingSkills / vacancy.RequiredSkills.Count * 100.0
                : 0;

            if (matchPercent >= HighMatchPercent)
                highMatchCount++;
        }

        if (highMatchCount >= HighMatchVacancyThreshold)
        {
            logger.LogWarning("Fatigue check triggered: user matches {Count} vacancies at 70%+", highMatchCount);
            return (true, $"You match {highMatchCount} vacancies at 70%+ skill coverage. Start applying NOW instead of continuing to study.");
        }

        logger.LogDebug("No fatigue detected");
        return (false, string.Empty);
    }

    private LearningStatus ComputeLearningStatus(
        string skillName,
        UserProfile profile,
        IReadOnlyList<JobVacancy> targetVacancies,
        IReadOnlyList<InterviewFeedback> interviewHistory,
        bool isRequired)
    {
        var userSkill = profile.Skills
            .FirstOrDefault(s => s.SkillName.Equals(skillName, StringComparison.OrdinalIgnoreCase));

        int currentLevel = userSkill?.ProficiencyLevel ?? 0;
        double marketDemandScore = userSkill?.MarketDemandScore ?? ComputeMarketDemand(skillName, targetVacancies);

        // Personal gap: how far from needed proficiency
        int targetLevel = isRequired ? 3 : 2;
        double personalGapScore = Math.Max(0, targetLevel - currentLevel) / 5.0 * 100.0;

        // Salary impact: how much this skill appears in high-paying vacancies
        double salaryImpact = ComputeSalaryImpact(skillName, targetVacancies);

        // Estimated learning hours based on gap size
        double estimatedHours = EstimateLearningHours(currentLevel, targetLevel);

        // ROI = marketDemand * personalGap * salaryImpact / estimatedLearningHours
        double roi = estimatedHours > 0
            ? marketDemandScore * (personalGapScore / 100.0) * salaryImpact / estimatedHours
            : 0.0;

        // Check if this is an interview weak area
        int interviewMentions = interviewHistory
            .Count(f => f.WeakAreas.Contains(skillName, StringComparer.OrdinalIgnoreCase));

        if (interviewMentions > 0)
        {
            roi *= 1.0 + (interviewMentions * 0.2); // Boost ROI for interview-flagged skills
        }

        return new LearningStatus
        {
            SkillName = skillName,
            MarketDemandScore = marketDemandScore,
            PersonalGapScore = personalGapScore,
            LearningROI = roi,
            ShouldStop = false,
            StopReason = string.Empty
        };
    }

    private static double ComputeMarketDemand(string skillName, IReadOnlyList<JobVacancy> vacancies)
    {
        if (vacancies.Count == 0)
            return 0.0;

        int appearances = vacancies.Count(v =>
            v.RequiredSkills.Contains(skillName, StringComparer.OrdinalIgnoreCase)
            || v.PreferredSkills.Contains(skillName, StringComparer.OrdinalIgnoreCase));

        return (double)appearances / vacancies.Count * 100.0;
    }

    private static double ComputeSalaryImpact(string skillName, IReadOnlyList<JobVacancy> vacancies)
    {
        var relevantVacancies = vacancies
            .Where(v => v.RequiredSkills.Contains(skillName, StringComparer.OrdinalIgnoreCase)
                        || v.PreferredSkills.Contains(skillName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (relevantVacancies.Count == 0 || vacancies.Count == 0)
            return 1.0;

        double avgSalaryWithSkill = relevantVacancies.Average(v => (double)(v.SalaryMin + v.SalaryMax) / 2);
        double avgSalaryOverall = vacancies.Average(v => (double)(v.SalaryMin + v.SalaryMax) / 2);

        return avgSalaryOverall > 0 ? avgSalaryWithSkill / avgSalaryOverall : 1.0;
    }

    private static double EstimateLearningHours(int currentLevel, int targetLevel)
    {
        if (currentLevel >= targetLevel)
            return 0.0;

        int gap = targetLevel - currentLevel;

        // Hours per level increase: level 0->1 = 20h, 1->2 = 15h, 2->3 = 20h, 3->4 = 30h, 4->5 = 40h
        double hours = 0;
        for (int level = currentLevel; level < targetLevel; level++)
        {
            hours += level switch
            {
                0 => 20.0,
                1 => 15.0,
                2 => 20.0,
                3 => 30.0,
                4 => 40.0,
                _ => 25.0
            };
        }

        return hours;
    }

    private static double EstimateTotalHours(List<LearningStatus> skills)
    {
        double total = 0;
        foreach (var skill in skills.Where(s => s.PersonalGapScore > 0))
        {
            // Approximate: personalGapScore is 0-100, map to hours
            total += skill.PersonalGapScore / 100.0 * 35.0; // ~35 hours for a full gap
        }
        return total;
    }

    private void ApplyStopSignals(
        List<LearningStatus> skills,
        double totalHours,
        UserProfile profile,
        IReadOnlyList<JobVacancy> vacancies,
        MatchScore? latestMatchScore,
        ref bool overlearningDetected)
    {
        // Total hours > 160 → STOP signal on low-ROI skills
        if (totalHours > HoursStopThreshold)
        {
            overlearningDetected = true;
            logger.LogWarning("Over-learning detected: {Hours:F0} total hours exceeds stop threshold of {Threshold}",
                totalHours, HoursStopThreshold);

            // Mark bottom-half ROI skills as should-stop
            int halfCount = skills.Count / 2;
            foreach (var skill in skills.Skip(halfCount))
            {
                skill.ShouldStop = true;
                skill.StopReason = $"Total learning plan exceeds {HoursStopThreshold} hours. Focus on higher-ROI skills first.";
            }
        }
        else if (totalHours > HoursWarningThreshold)
        {
            overlearningDetected = true;
            logger.LogWarning("Over-learning warning: {Hours:F0} total hours exceeds warning threshold of {Threshold}",
                totalHours, HoursWarningThreshold);

            // Mark bottom-quarter ROI skills as should-stop
            int quarterCount = skills.Count / 4;
            foreach (var skill in skills.Skip(skills.Count - Math.Max(1, quarterCount)))
            {
                skill.ShouldStop = true;
                skill.StopReason = $"Total learning plan exceeds {HoursWarningThreshold} hours. Consider deprioritizing this skill.";
            }
        }

        // User has >= 70% match on 5+ vacancies → "Start applying NOW"
        int highMatchCount = CountHighMatchVacancies(profile, vacancies);
        if (highMatchCount >= HighMatchVacancyThreshold)
        {
            overlearningDetected = true;
            foreach (var skill in skills.Where(s => s.PersonalGapScore < 30))
            {
                skill.ShouldStop = true;
                skill.StopReason = $"You match {highMatchCount} vacancies at 70%+. Start applying NOW rather than perfecting this skill.";
            }
        }

        // Skills where user is already at or above target
        foreach (var skill in skills.Where(s => s.PersonalGapScore == 0 && !s.ShouldStop))
        {
            skill.ShouldStop = true;
            skill.StopReason = "You already meet the required proficiency for this skill. No further study needed.";
        }
    }

    private static int CountHighMatchVacancies(UserProfile profile, IReadOnlyList<JobVacancy> vacancies)
    {
        int count = 0;
        foreach (var vacancy in vacancies)
        {
            if (vacancy.RequiredSkills.Count == 0)
                continue;

            int matchingSkills = vacancy.RequiredSkills
                .Count(rs => profile.Skills.Any(ps =>
                    ps.SkillName.Equals(rs, StringComparison.OrdinalIgnoreCase) && ps.ProficiencyLevel >= 3));

            double matchPercent = (double)matchingSkills / vacancy.RequiredSkills.Count * 100.0;
            if (matchPercent >= HighMatchPercent)
                count++;
        }
        return count;
    }

    private string GenerateGlobalRecommendation(
        List<LearningStatus> skills,
        double totalHours,
        bool overlearningDetected,
        UserProfile profile,
        IReadOnlyList<JobVacancy> vacancies,
        MatchScore? latestMatchScore)
    {
        if (overlearningDetected && totalHours > HoursStopThreshold)
        {
            return "STOP: Your learning plan exceeds 160 hours. You are at serious risk of over-preparation. "
                   + "Trim low-ROI skills and start applying immediately. Perfect is the enemy of good.";
        }

        var (fatigued, fatigueReason) = CheckFatigue(profile, vacancies, latestMatchScore);
        if (fatigued)
        {
            return $"APPLY NOW: {fatigueReason}";
        }

        if (overlearningDetected)
        {
            return "WARNING: Your learning plan is growing large. Prioritize the top skills by ROI "
                   + "and consider starting applications in parallel with your study plan.";
        }

        var activeSkills = skills.Where(s => !s.ShouldStop && s.PersonalGapScore > 0).ToList();
        if (activeSkills.Count == 0)
        {
            return "You are well-prepared across all target skills. Focus on interview practice and start applying.";
        }

        var topSkills = activeSkills.Take(3).Select(s => s.SkillName);
        return $"Focus your learning on: {string.Join(", ", topSkills)}. "
               + $"Estimated total study time: {totalHours:F0} hours. "
               + "Balance learning with active job applications for best results.";
    }
}
