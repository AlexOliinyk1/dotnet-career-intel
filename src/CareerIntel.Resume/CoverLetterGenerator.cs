using System.Text;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Resume;

/// <summary>
/// Generates targeted cover letters by matching a <see cref="UserProfile"/>
/// against a specific <see cref="JobVacancy"/>.
/// </summary>
public sealed class CoverLetterGenerator
{
    private readonly ILogger<CoverLetterGenerator> _logger;

    public CoverLetterGenerator(ILogger<CoverLetterGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a concise, professional cover letter tailored to the target vacancy.
    /// The letter consists of 3-4 paragraphs: opening, body with key qualifications,
    /// and a closing with a call to action.
    /// </summary>
    /// <param name="profile">The user's full profile.</param>
    /// <param name="vacancy">The target job vacancy.</param>
    /// <returns>A cover letter as a plain-text string.</returns>
    public string Generate(UserProfile profile, JobVacancy vacancy)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(vacancy);

        _logger.LogInformation(
            "Generating cover letter for '{Title}' at {Company}",
            vacancy.Title, vacancy.Company);

        var vacancySkills = vacancy.RequiredSkills
            .Concat(vacancy.PreferredSkills)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var userSkillNames = profile.Skills
            .Select(s => s.SkillName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matchingSkills = vacancySkills
            .Where(s => userSkillNames.Contains(s))
            .ToList();

        var topSkills = profile.Skills
            .Where(s => vacancySkills.Contains(s.SkillName))
            .OrderByDescending(s => s.ProficiencyLevel)
            .ThenByDescending(s => s.YearsOfExperience)
            .Take(5)
            .ToList();

        var relevantExperience = FindMostRelevantExperience(
            profile.Experiences, vacancySkills);

        var sb = new StringBuilder(2048);

        AppendOpening(sb, profile.Personal, vacancy);
        AppendQualifications(sb, profile, vacancy, topSkills, relevantExperience);
        AppendClosing(sb, profile.Personal, vacancy);

        string letter = sb.ToString();

        _logger.LogDebug("Cover letter generated: {Length} characters", letter.Length);

        return letter;
    }

    /// <summary>
    /// Opening paragraph: expresses genuine interest in the specific role and company.
    /// </summary>
    private static void AppendOpening(
        StringBuilder sb, PersonalInfo personal, JobVacancy vacancy)
    {
        sb.Append($"Dear {vacancy.Company} Hiring Team,");
        sb.AppendLine();
        sb.AppendLine();

        sb.Append(
            $"I am writing to express my strong interest in the {vacancy.Title} " +
            $"position at {vacancy.Company}.");

        if (!string.IsNullOrWhiteSpace(vacancy.Description) &&
            vacancy.Description.Length > 50)
        {
            sb.Append(
                $" Having followed {vacancy.Company}'s work in the industry, I am " +
                $"drawn to the opportunity to contribute my experience as a " +
                $"{personal.Title} to your team.");
        }
        else
        {
            sb.Append(
                $" With my background as a {personal.Title}, I believe I can " +
                $"make meaningful contributions to your team.");
        }

        sb.AppendLine();
        sb.AppendLine();
    }

    /// <summary>
    /// Body paragraphs: highlights 2-3 key qualifications that align with the role's requirements.
    /// </summary>
    private static void AppendQualifications(
        StringBuilder sb,
        UserProfile profile,
        JobVacancy vacancy,
        List<SkillProfile> topSkills,
        Experience? relevantExperience)
    {
        if (topSkills.Count > 0)
        {
            var skillDescriptions = topSkills
                .Take(3)
                .Select(s =>
                {
                    string exp = s.YearsOfExperience >= 1
                        ? $"{s.YearsOfExperience:F0}+ years of hands-on experience with {s.SkillName}"
                        : $"practical experience with {s.SkillName}";
                    return exp;
                })
                .ToList();

            sb.Append(
                $"My technical background aligns well with your requirements. I bring " +
                $"{string.Join(", ", skillDescriptions)}.");

            if (topSkills.Count > 3)
            {
                var additionalSkills = topSkills
                    .Skip(3)
                    .Select(s => s.SkillName);
                sb.Append(
                    $" I also have solid proficiency in {string.Join(" and ", additionalSkills)}, " +
                    $"which complements the core skill set you are looking for.");
            }

            sb.AppendLine();
            sb.AppendLine();
        }

        if (relevantExperience is not null)
        {
            sb.Append(
                $"In my role as {relevantExperience.Role} at {relevantExperience.Company}");

            if (!string.IsNullOrWhiteSpace(relevantExperience.Duration))
            {
                sb.Append($" ({relevantExperience.Duration})");
            }

            sb.Append(", ");

            var topAchievement = relevantExperience.Achievements.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(topAchievement))
            {
                sb.Append(
                    $"I {NormalizeAchievement(topAchievement)} " +
                    $"This experience has prepared me to take on the challenges " +
                    $"outlined in the {vacancy.Title} role.");
            }
            else
            {
                sb.Append(
                    $"I developed deep expertise working with " +
                    $"{string.Join(", ", relevantExperience.TechStack.Take(3))} " +
                    $"in a production environment. This hands-on experience " +
                    $"directly translates to the demands of the {vacancy.Title} position.");
            }

            sb.AppendLine();
            sb.AppendLine();
        }
        else if (profile.Experiences.Count > 0)
        {
            var latest = profile.Experiences
                .OrderByDescending(e => e.StartDate)
                .First();

            sb.Append(
                $"Most recently at {latest.Company} as {latest.Role}, I have " +
                $"honed my skills in delivering production-grade solutions.");

            if (latest.TechStack.Count > 0)
            {
                sb.Append(
                    $" Working extensively with {string.Join(", ", latest.TechStack.Take(3))}, " +
                    $"I have built a strong foundation relevant to this role.");
            }

            sb.AppendLine();
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Closing paragraph: professional enthusiasm and call to action.
    /// </summary>
    private static void AppendClosing(
        StringBuilder sb, PersonalInfo personal, JobVacancy vacancy)
    {
        sb.Append(
            $"I am excited about the prospect of joining {vacancy.Company} and " +
            $"contributing to your engineering efforts. I would welcome the " +
            $"opportunity to discuss how my experience and skills align with " +
            $"your team's goals. I am available for a conversation at your convenience.");
        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("Best regards,");
        sb.AppendLine(personal.Name);
    }

    /// <summary>
    /// Finds the experience entry most relevant to the target vacancy based on
    /// tech stack overlap.
    /// </summary>
    private static Experience? FindMostRelevantExperience(
        List<Experience> experiences, HashSet<string> vacancySkills)
    {
        if (experiences.Count == 0)
            return null;

        return experiences
            .Select(e => new
            {
                Experience = e,
                Relevance = e.TechStack.Count(t =>
                    vacancySkills.Contains(t)) +
                    e.Achievements.Count(a =>
                        vacancySkills.Any(s =>
                            a.Contains(s, StringComparison.OrdinalIgnoreCase)))
            })
            .Where(x => x.Relevance > 0)
            .OrderByDescending(x => x.Relevance)
            .ThenByDescending(x => x.Experience.StartDate)
            .Select(x => x.Experience)
            .FirstOrDefault();
    }

    /// <summary>
    /// Normalizes an achievement string to work as a clause in a sentence.
    /// Ensures it starts lowercase and ends with proper punctuation.
    /// </summary>
    private static string NormalizeAchievement(string achievement)
    {
        if (string.IsNullOrWhiteSpace(achievement))
            return string.Empty;

        string normalized = achievement.TrimStart('-', '*', ' ');

        if (normalized.Length > 0 && char.IsUpper(normalized[0]))
        {
            normalized = char.ToLowerInvariant(normalized[0]) + normalized[1..];
        }

        if (!normalized.EndsWith('.') && !normalized.EndsWith('!'))
        {
            normalized += ".";
        }

        return normalized;
    }
}
