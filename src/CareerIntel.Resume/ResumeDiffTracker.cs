using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Resume;

/// <summary>
/// Tracks all changes made during resume tailoring, producing a <see cref="ResumeDiff"/>
/// that enables human review of ATS-aware adaptations.
/// </summary>
public sealed class ResumeDiffTracker
{
    private readonly ILogger<ResumeDiffTracker> _logger;

    public ResumeDiffTracker(ILogger<ResumeDiffTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyzes the original resume, ATS tailoring result, vacancy, and user profile
    /// to produce a comprehensive diff of all changes made during tailoring.
    /// </summary>
    /// <param name="originalResume">The original markdown resume before ATS tailoring.</param>
    /// <param name="atsResult">The result from the ATS tailoring pass.</param>
    /// <param name="vacancy">The target job vacancy.</param>
    /// <param name="profile">The user's profile.</param>
    /// <returns>A <see cref="ResumeDiff"/> detailing all tracked changes.</returns>
    public ResumeDiff Track(
        string originalResume,
        AtsTailoringResult atsResult,
        JobVacancy vacancy,
        UserProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalResume);
        ArgumentNullException.ThrowIfNull(atsResult);
        ArgumentNullException.ThrowIfNull(vacancy);
        ArgumentNullException.ThrowIfNull(profile);

        _logger.LogInformation(
            "Tracking resume diff for '{Title}' at {Company}",
            vacancy.Title, vacancy.Company);

        // a) Create ResumeDiff with vacancy info
        var diff = new ResumeDiff
        {
            VacancyId = vacancy.Id,
            VacancyTitle = vacancy.Title,
            Company = vacancy.Company,
            GeneratedDate = DateTimeOffset.UtcNow
        };

        // b) Track ATS changes from atsResult
        TrackAtsChanges(diff, atsResult);

        // c) Track skill coverage
        TrackSkillCoverage(diff, vacancy, profile);

        // d) Track section reordering
        TrackSectionReordering(diff, originalResume, atsResult.TailoredResume);

        // e) Track experience emphasis
        TrackExperienceEmphasis(diff, vacancy, profile);

        // f) Track title alignment
        TrackTitleAlignment(diff, vacancy, atsResult.TailoredResume, originalResume);

        _logger.LogInformation(
            "Resume diff complete: {ChangeCount} changes tracked, " +
            "{Highlighted} highlighted skills, {Uncovered} uncovered skills",
            diff.Changes.Count, diff.HighlightedSkills.Count, diff.UncoveredSkills.Count);

        // g) Return the completed ResumeDiff
        return diff;
    }

    /// <summary>
    /// Records ATS keyword injection metadata and creates individual change entries
    /// for each injected keyword.
    /// </summary>
    private void TrackAtsChanges(ResumeDiff diff, AtsTailoringResult atsResult)
    {
        diff.AtsKeywordsInjected = atsResult.InjectedKeywords;
        diff.AtsKeywordsExisting = atsResult.ExistingKeywords;
        diff.KeywordDensityPercent = atsResult.KeywordDensityPercent;

        foreach (string keyword in atsResult.InjectedKeywords)
        {
            diff.Changes.Add(new ResumeChange
            {
                Section = "Technical Skills",
                ChangeType = "KeywordInjection",
                Reason = "ATS optimization \u2014 vacancy requires this skill",
                Description = $"Added '{keyword}' to Additional Skills"
            });
        }

        _logger.LogDebug(
            "Tracked {Count} ATS keyword injections, density {Density:F1}%",
            atsResult.InjectedKeywords.Count, atsResult.KeywordDensityPercent);
    }

    /// <summary>
    /// Determines which vacancy-required skills are covered by the user's profile
    /// and which are missing.
    /// </summary>
    private static void TrackSkillCoverage(
        ResumeDiff diff, JobVacancy vacancy, UserProfile profile)
    {
        var profileSkillNames = profile.Skills
            .Select(s => s.SkillName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        diff.HighlightedSkills = vacancy.RequiredSkills
            .Where(s => profileSkillNames.Contains(s))
            .ToList();

        diff.UncoveredSkills = vacancy.RequiredSkills
            .Where(s => !profileSkillNames.Contains(s))
            .ToList();
    }

    /// <summary>
    /// Compares section heading order between the original and tailored resumes
    /// to detect reordering.
    /// </summary>
    private void TrackSectionReordering(
        ResumeDiff diff, string originalResume, string tailoredResume)
    {
        var originalSections = ExtractSectionHeadings(originalResume);
        var tailoredSections = ExtractSectionHeadings(tailoredResume);

        if (!originalSections.SequenceEqual(tailoredSections, StringComparer.OrdinalIgnoreCase))
        {
            diff.ReorderedSections = tailoredSections;

            diff.Changes.Add(new ResumeChange
            {
                Section = "Document",
                ChangeType = "SectionReorder",
                Reason = "Sections reordered to prioritize vacancy-relevant content",
                Description = $"New order: {string.Join(", ", tailoredSections)}"
            });

            _logger.LogDebug(
                "Section reordering detected: {Sections}",
                string.Join(" -> ", tailoredSections));
        }
    }

    /// <summary>
    /// Identifies experience entries whose tech stack overlaps with the vacancy
    /// and records emphasis changes.
    /// </summary>
    private void TrackExperienceEmphasis(
        ResumeDiff diff, JobVacancy vacancy, UserProfile profile)
    {
        var vacancyTech = vacancy.RequiredSkills
            .Concat(vacancy.PreferredSkills)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var experience in profile.Experiences)
        {
            var matchedTech = experience.TechStack
                .Where(t => vacancyTech.Contains(t))
                .ToList();

            if (matchedTech.Count > 0)
            {
                diff.Changes.Add(new ResumeChange
                {
                    Section = "Experience",
                    ChangeType = "ExperienceEmphasis",
                    Reason = $"Contains relevant tech: {string.Join(", ", matchedTech)}",
                    Description = $"Prioritized {experience.Company} role for relevance"
                });

                _logger.LogDebug(
                    "Experience emphasis: {Company} matches {Count} vacancy tech items",
                    experience.Company, matchedTech.Count);
            }
        }
    }

    /// <summary>
    /// Checks whether vacancy title words were injected into the summary section
    /// and records a title alignment change if so.
    /// </summary>
    private void TrackTitleAlignment(
        ResumeDiff diff, JobVacancy vacancy, string tailoredResume, string originalResume)
    {
        if (string.IsNullOrWhiteSpace(vacancy.Title))
            return;

        string[] titleWords = vacancy.Title
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToArray();

        if (titleWords.Length == 0)
            return;

        // Check if any title words appear in the tailored resume but not in the original
        // (i.e., they were injected during the summary tailoring pass)
        bool hasTitleInjection = titleWords.Any(w =>
            tailoredResume.Contains(w, StringComparison.OrdinalIgnoreCase) &&
            !originalResume.Contains(w, StringComparison.OrdinalIgnoreCase));

        if (hasTitleInjection)
        {
            diff.Changes.Add(new ResumeChange
            {
                Section = "Summary",
                ChangeType = "TitleAlignment",
                Reason = "Recruiter skim optimization",
                Description = $"Aligned summary with vacancy title '{vacancy.Title}'"
            });

            _logger.LogDebug("Title alignment tracked for '{Title}'", vacancy.Title);
        }
    }

    /// <summary>
    /// Extracts "## " level section headings from a markdown document, preserving order.
    /// </summary>
    private static List<string> ExtractSectionHeadings(string markdown)
    {
        var headings = new List<string>();
        var matches = Regex.Matches(markdown, @"^## (.+)$", RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            headings.Add(match.Groups[1].Value.Trim());
        }

        return headings;
    }
}
