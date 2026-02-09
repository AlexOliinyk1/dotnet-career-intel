using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

public sealed class OfferReadinessEngine(ILogger<OfferReadinessEngine> logger)
{
    public OfferReadiness Compute(
        JobVacancy vacancy,
        UserProfile profile,
        MatchScore matchScore,
        IReadOnlyList<InterviewFeedback> interviewHistory,
        CompanyProfile? companyProfile)
    {
        logger.LogInformation("Computing offer readiness for vacancy {VacancyId} at {Company}",
            vacancy.Id, vacancy.Company);

        // 1. Base skill match from the MatchScore
        double baseSkillMatch = matchScore.SkillMatchScore / 100.0;

        // 2. Identify critical skill gaps (required skills where user proficiency < 3)
        var criticalGaps = ComputeCriticalGaps(vacancy, profile);

        // 3. Seniority fit factor
        double seniorityFit = ComputeSeniorityFit(vacancy, profile);

        // 4. Interview history factor
        double interviewHistoryFactor = ComputeInterviewHistoryFactor(interviewHistory, companyProfile);

        // 5. Company difficulty factor
        double companyDifficultyFactor = ComputeCompanyDifficultyFactor(companyProfile);

        // 6. Compute offer probability
        double offerProbability = Math.Clamp(
            baseSkillMatch * seniorityFit * interviewHistoryFactor * companyDifficultyFactor,
            0.0,
            1.0);

        double readinessPercent = offerProbability * 100.0;

        // 7. Estimate weeks to ready
        int estimatedWeeks = EstimateWeeksToReady(criticalGaps, interviewHistory);

        // 8. Generate prep actions
        var prepActions = GeneratePrepActions(criticalGaps, interviewHistory, companyProfile);

        // 9. Determine timing
        var timing = DetermineTiming(readinessPercent);

        logger.LogInformation(
            "Offer readiness for {Company}: {Readiness:F1}%, probability {Probability:F2}, timing {Timing}",
            vacancy.Company, readinessPercent, offerProbability, timing);

        return new OfferReadiness
        {
            ReadinessPercent = readinessPercent,
            EstimatedWeeksToReady = estimatedWeeks,
            CriticalGaps = criticalGaps,
            PrepActions = prepActions,
            OfferProbability = offerProbability,
            Timing = timing
        };
    }

    private static List<SkillGap> ComputeCriticalGaps(JobVacancy vacancy, UserProfile profile)
    {
        var gaps = new List<SkillGap>();

        foreach (var requiredSkill in vacancy.RequiredSkills)
        {
            var userSkill = profile.Skills
                .FirstOrDefault(s => s.SkillName.Equals(requiredSkill, StringComparison.OrdinalIgnoreCase));

            int currentLevel = userSkill?.ProficiencyLevel ?? 0;

            if (currentLevel < 3)
            {
                gaps.Add(new SkillGap
                {
                    SkillName = requiredSkill,
                    CurrentLevel = currentLevel,
                    RequiredLevel = 3,
                    ImpactWeight = currentLevel == 0 ? 1.0 : (3.0 - currentLevel) / 3.0,
                    RecommendedAction = currentLevel == 0
                        ? $"Learn {requiredSkill} fundamentals through tutorials and practice projects"
                        : $"Deepen {requiredSkill} skills from level {currentLevel} to at least level 3"
                });
            }
        }

        return gaps.OrderByDescending(g => g.ImpactWeight).ToList();
    }

    private static double ComputeSeniorityFit(JobVacancy vacancy, UserProfile profile)
    {
        int vacancyLevel = (int)vacancy.SeniorityLevel;
        int profileLevel = (int)profile.Preferences.MinSeniority;

        int difference = Math.Abs(vacancyLevel - profileLevel);

        return difference switch
        {
            0 => 1.0,
            1 => 0.8,
            2 => 0.5,
            _ => 0.3
        };
    }

    private static double ComputeInterviewHistoryFactor(
        IReadOnlyList<InterviewFeedback> interviewHistory,
        CompanyProfile? companyProfile)
    {
        double factor = 1.0;

        foreach (var feedback in interviewHistory)
        {
            bool isSameCompanyStyle = companyProfile is not null
                && feedback.Company.Equals(companyProfile.Name, StringComparison.OrdinalIgnoreCase);

            if (feedback.Outcome.Equals("Pass", StringComparison.OrdinalIgnoreCase)
                || feedback.Outcome.Equals("Passed", StringComparison.OrdinalIgnoreCase))
            {
                factor += 0.05;
            }
            else if (feedback.Outcome.Equals("Reject", StringComparison.OrdinalIgnoreCase)
                     || feedback.Outcome.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                     || feedback.Outcome.Equals("Fail", StringComparison.OrdinalIgnoreCase)
                     || feedback.Outcome.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                factor -= isSameCompanyStyle ? 0.15 : 0.1;
            }
        }

        return Math.Clamp(factor, 0.3, 1.5);
    }

    private static double ComputeCompanyDifficultyFactor(CompanyProfile? companyProfile)
    {
        if (companyProfile is null)
            return 1.0;

        // Centered at difficulty 5: factor = 1.0 - (bar - 5) * 0.05
        return Math.Clamp(1.0 - (companyProfile.DifficultyBar - 5) * 0.05, 0.5, 1.25);
    }

    private static int EstimateWeeksToReady(
        List<SkillGap> criticalGaps,
        IReadOnlyList<InterviewFeedback> interviewHistory)
    {
        // Each missing critical skill = 1-2 weeks depending on gap size
        double weeks = 0;
        foreach (var gap in criticalGaps)
        {
            int levelDiff = gap.RequiredLevel - gap.CurrentLevel;
            weeks += levelDiff switch
            {
                >= 3 => 2.0,
                2 => 1.5,
                _ => 1.0
            };
        }

        // Each weak area from feedback = 0.5-1 week
        var weakAreas = interviewHistory
            .SelectMany(f => f.WeakAreas)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Area: g.Key, Count: g.Count()));

        foreach (var (_, count) in weakAreas)
        {
            weeks += count >= 2 ? 1.0 : 0.5;
        }

        return (int)Math.Ceiling(weeks);
    }

    private static List<PrepAction> GeneratePrepActions(
        List<SkillGap> criticalGaps,
        IReadOnlyList<InterviewFeedback> interviewHistory,
        CompanyProfile? companyProfile)
    {
        var actions = new List<PrepAction>();
        int priority = 1;

        // Actions for critical skill gaps
        foreach (var gap in criticalGaps)
        {
            actions.Add(new PrepAction
            {
                Action = gap.RecommendedAction,
                Category = "Skill Development",
                Priority = priority++,
                EstimatedHours = (gap.RequiredLevel - gap.CurrentLevel) * 8
            });
        }

        // Actions for interview weak areas
        var repeatingWeakAreas = interviewHistory
            .SelectMany(f => f.WeakAreas)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key);

        foreach (var weakArea in repeatingWeakAreas)
        {
            actions.Add(new PrepAction
            {
                Action = $"Practice {weakArea} through mock interviews and targeted exercises",
                Category = "Interview Prep",
                Priority = priority++,
                EstimatedHours = 6
            });
        }

        // Company-specific actions
        if (companyProfile is not null)
        {
            if (!string.IsNullOrEmpty(companyProfile.InterviewStyle))
            {
                actions.Add(new PrepAction
                {
                    Action = $"Prepare for {companyProfile.InterviewStyle} interview format at {companyProfile.Name}",
                    Category = "Company Research",
                    Priority = priority++,
                    EstimatedHours = 4
                });
            }

            foreach (var reason in companyProfile.CommonRejectionReasons)
            {
                actions.Add(new PrepAction
                {
                    Action = $"Address common rejection reason: {reason}",
                    Category = "Risk Mitigation",
                    Priority = priority++,
                    EstimatedHours = 3
                });
            }
        }

        return actions.OrderBy(a => a.Priority).ToList();
    }

    private static RecommendedTiming DetermineTiming(double readinessPercent)
    {
        return readinessPercent switch
        {
            >= 80.0 => RecommendedTiming.ApplyNow,
            >= 60.0 => RecommendedTiming.ApplyIn1To2Weeks,
            >= 40.0 => RecommendedTiming.ApplyIn3To4Weeks,
            >= 20.0 => RecommendedTiming.SkillUpFirst,
            _ => RecommendedTiming.Skip
        };
    }
}
