using System.Text;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Resume;

/// <summary>
/// Builds a tailored markdown resume from a <see cref="UserProfile"/> targeting
/// a specific <see cref="JobVacancy"/>.
/// </summary>
public sealed class ResumeBuilder
{
    private readonly ILogger<ResumeBuilder> _logger;

    public ResumeBuilder(ILogger<ResumeBuilder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a targeted markdown resume that emphasizes skills and experience
    /// relevant to the given vacancy.
    /// </summary>
    /// <param name="profile">The user's full profile.</param>
    /// <param name="vacancy">The target job vacancy to tailor the resume for.</param>
    /// <returns>A complete markdown-formatted resume string.</returns>
    public string Build(UserProfile profile, JobVacancy vacancy)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(vacancy);

        _logger.LogInformation(
            "Building tailored resume for '{Title}' at {Company}",
            vacancy.Title, vacancy.Company);

        var vacancyKeywords = vacancy.RequiredSkills
            .Concat(vacancy.PreferredSkills)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder(4096);

        AppendHeader(sb, profile.Personal);
        AppendSummary(sb, profile.Personal, vacancy);
        AppendSkills(sb, profile.Skills, vacancyKeywords);
        AppendExperience(sb, profile.Experiences, vacancyKeywords);

        _logger.LogDebug("Resume generated: {Length} characters", sb.Length);

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, PersonalInfo personal)
    {
        sb.AppendLine($"# {personal.Name}");
        sb.AppendLine();

        sb.AppendLine($"**{personal.Title}** | {personal.Location}");
        sb.AppendLine();

        var contacts = new List<string>();
        if (!string.IsNullOrWhiteSpace(personal.Email))
            contacts.Add(personal.Email);
        if (!string.IsNullOrWhiteSpace(personal.LinkedInUrl))
            contacts.Add($"[LinkedIn]({personal.LinkedInUrl})");

        if (contacts.Count > 0)
        {
            sb.AppendLine(string.Join(" | ", contacts));
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendSummary(
        StringBuilder sb, PersonalInfo personal, JobVacancy vacancy)
    {
        sb.AppendLine("## Summary");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(personal.Summary))
        {
            string summary = TailorSummary(personal.Summary, vacancy);
            sb.AppendLine(summary);
        }
        else
        {
            sb.AppendLine(
                $"Experienced {personal.Title} seeking opportunities in " +
                $"{vacancy.Title} roles.");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Adjusts the summary to reference the vacancy role and company when possible.
    /// </summary>
    private static string TailorSummary(string summary, JobVacancy vacancy)
    {
        var sb = new StringBuilder(summary);

        if (!summary.Contains(vacancy.Title, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(vacancy.Title))
        {
            sb.Append($" Particularly interested in {vacancy.Title} positions");

            if (!string.IsNullOrWhiteSpace(vacancy.Company))
            {
                sb.Append($" at {vacancy.Company}");
            }

            sb.Append('.');
        }

        return sb.ToString();
    }

    private static void AppendSkills(
        StringBuilder sb, List<SkillProfile> skills, HashSet<string> vacancyKeywords)
    {
        sb.AppendLine("## Technical Skills");
        sb.AppendLine();

        var matchingSkills = skills
            .Where(s => vacancyKeywords.Contains(s.SkillName))
            .ToList();

        var otherSkills = skills
            .Where(s => !vacancyKeywords.Contains(s.SkillName))
            .ToList();

        var orderedSkills = matchingSkills.Concat(otherSkills);

        var groupedByCategory = orderedSkills
            .GroupBy(s => s.Category)
            .OrderByDescending(g =>
                g.Any(s => vacancyKeywords.Contains(s.SkillName)) ? 1 : 0)
            .ThenBy(g => g.Key.ToString());

        foreach (var group in groupedByCategory)
        {
            string categoryName = FormatCategoryName(group.Key);
            var skillNames = group
                .OrderByDescending(s => vacancyKeywords.Contains(s.SkillName) ? 1 : 0)
                .ThenByDescending(s => s.ProficiencyLevel)
                .Select(s => vacancyKeywords.Contains(s.SkillName)
                    ? $"**{s.SkillName}**"
                    : s.SkillName);

            sb.AppendLine($"- **{categoryName}:** {string.Join(", ", skillNames)}");
        }

        sb.AppendLine();
    }

    private static void AppendExperience(
        StringBuilder sb, List<Experience> experiences, HashSet<string> vacancyKeywords)
    {
        sb.AppendLine("## Professional Experience");
        sb.AppendLine();

        var relevantExperiences = experiences
            .OrderByDescending(e => ComputeExperienceRelevance(e, vacancyKeywords))
            .ThenByDescending(e => e.StartDate)
            .Where(e => ComputeExperienceRelevance(e, vacancyKeywords) > 0 ||
                        e.StartDate is not null)
            .ToList();

        if (relevantExperiences.Count == 0)
            relevantExperiences = experiences;

        foreach (var exp in relevantExperiences)
        {
            sb.AppendLine($"### {exp.Role}");
            sb.AppendLine($"**{exp.Company}** | {exp.Duration}");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(exp.Description))
            {
                sb.AppendLine(exp.Description);
                sb.AppendLine();
            }

            var relevantAchievements = exp.Achievements
                .OrderByDescending(a => CountKeywordHits(a, vacancyKeywords))
                .ToList();

            if (relevantAchievements.Count > 0)
            {
                foreach (string achievement in relevantAchievements)
                {
                    sb.AppendLine($"- {achievement}");
                }
                sb.AppendLine();
            }

            if (exp.TechStack.Count > 0)
            {
                var orderedTech = exp.TechStack
                    .OrderByDescending(t => vacancyKeywords.Contains(t) ? 1 : 0)
                    .Select(t => vacancyKeywords.Contains(t) ? $"**{t}**" : t);

                sb.AppendLine($"*Tech: {string.Join(", ", orderedTech)}*");
                sb.AppendLine();
            }
        }
    }

    /// <summary>
    /// Scores how relevant an experience entry is to the target vacancy.
    /// </summary>
    private static int ComputeExperienceRelevance(
        Experience experience, HashSet<string> vacancyKeywords)
    {
        int score = 0;

        score += experience.TechStack
            .Count(t => vacancyKeywords.Contains(t));

        score += experience.Achievements
            .Sum(a => CountKeywordHits(a, vacancyKeywords));

        return score;
    }

    private static int CountKeywordHits(string text, HashSet<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return keywords.Count(k =>
            text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatCategoryName(SkillCategory category) => category switch
    {
        SkillCategory.CoreDotNet => "Core .NET",
        SkillCategory.DevOps => "DevOps & CI/CD",
        SkillCategory.MachineLearning => "Machine Learning",
        SkillCategory.SoftSkills => "Soft Skills",
        _ => category.ToString()
    };
}
