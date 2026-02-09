using System.Text;
using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Resume;

/// <summary>
/// Result of ATS tailoring, containing the modified resume and metadata
/// about injected keywords.
/// </summary>
public sealed class AtsTailoringResult
{
    /// <summary>The tailored resume with ATS-friendly keyword additions.</summary>
    public string TailoredResume { get; init; } = string.Empty;

    /// <summary>Keywords that were injected or reinforced in the resume.</summary>
    public List<string> InjectedKeywords { get; init; } = [];

    /// <summary>Keywords from the vacancy already present in the original resume.</summary>
    public List<string> ExistingKeywords { get; init; } = [];

    /// <summary>Keyword density as a percentage of total words.</summary>
    public double KeywordDensityPercent { get; init; }
}

/// <summary>
/// Tailors a resume for Applicant Tracking Systems by ensuring relevant keywords
/// from the target vacancy appear naturally in the document while keeping
/// keyword density within reasonable bounds.
/// </summary>
public sealed class AtsTailorer
{
    private const double MaxKeywordDensityPercent = 5.0;
    private const double TargetKeywordDensityPercent = 3.0;

    private readonly ILogger<AtsTailorer> _logger;

    public AtsTailorer(ILogger<AtsTailorer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Tailors the given resume markdown to improve ATS compatibility against
    /// the target vacancy.
    /// </summary>
    /// <param name="resume">The original markdown resume content.</param>
    /// <param name="vacancy">The target job vacancy.</param>
    /// <returns>An <see cref="AtsTailoringResult"/> with the enhanced resume and metadata.</returns>
    public AtsTailoringResult Tailor(string resume, JobVacancy vacancy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resume);
        ArgumentNullException.ThrowIfNull(vacancy);

        _logger.LogInformation(
            "Tailoring resume for ATS compatibility: '{Title}' at {Company}",
            vacancy.Title, vacancy.Company);

        var allKeywords = vacancy.RequiredSkills
            .Concat(vacancy.PreferredSkills)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingKeywords = allKeywords
            .Where(k => ResumeContainsKeyword(resume, k))
            .ToList();

        var missingKeywords = allKeywords
            .Where(k => !ResumeContainsKeyword(resume, k))
            .ToList();

        _logger.LogDebug(
            "Keywords found: {Existing}/{Total}, missing: {Missing}",
            existingKeywords.Count, allKeywords.Count, missingKeywords.Count);

        var tailored = new StringBuilder(resume);
        var injectedKeywords = new List<string>();

        if (missingKeywords.Count > 0)
        {
            InjectKeywordsInSkillsSection(tailored, missingKeywords, injectedKeywords);
        }

        InjectTitleKeywords(tailored, vacancy);

        string result = tailored.ToString();
        double density = ComputeKeywordDensity(result, allKeywords);

        if (density > MaxKeywordDensityPercent)
        {
            _logger.LogWarning(
                "Keyword density {Density:F1}% exceeds maximum {Max}%. " +
                "Consider manual review to reduce stuffing.",
                density, MaxKeywordDensityPercent);
        }

        _logger.LogInformation(
            "ATS tailoring complete: injected {Count} keywords, density {Density:F1}%",
            injectedKeywords.Count, density);

        return new AtsTailoringResult
        {
            TailoredResume = result,
            InjectedKeywords = injectedKeywords,
            ExistingKeywords = existingKeywords,
            KeywordDensityPercent = Math.Round(density, 2)
        };
    }

    /// <summary>
    /// Injects missing keywords into the Technical Skills section of the resume
    /// under an "Additional Skills" category.
    /// </summary>
    private void InjectKeywordsInSkillsSection(
        StringBuilder resume,
        List<string> missingKeywords,
        List<string> injectedKeywords)
    {
        string content = resume.ToString();

        int skillsSectionEnd = FindSectionEnd(content, "## Technical Skills");

        if (skillsSectionEnd < 0)
        {
            skillsSectionEnd = FindSectionEnd(content, "## Skills");
        }

        if (skillsSectionEnd >= 0)
        {
            string currentContent = resume.ToString();
            double currentDensity = ComputeKeywordDensity(
                currentContent,
                missingKeywords);

            var keywordsToInject = new List<string>();

            foreach (string keyword in missingKeywords)
            {
                double projected = ComputeKeywordDensity(
                    currentContent + " " + keyword,
                    [keyword]);

                if (projected + currentDensity <= TargetKeywordDensityPercent)
                {
                    keywordsToInject.Add(keyword);
                }
            }

            if (keywordsToInject.Count > 0)
            {
                string injection =
                    $"- **Additional:** {string.Join(", ", keywordsToInject)}\n";

                resume.Insert(skillsSectionEnd, injection);
                injectedKeywords.AddRange(keywordsToInject);

                _logger.LogDebug(
                    "Injected {Count} keywords in skills section: {Keywords}",
                    keywordsToInject.Count, string.Join(", ", keywordsToInject));
            }
        }
        else
        {
            _logger.LogDebug("No skills section found; appending additional skills block");

            var block = new StringBuilder();
            block.AppendLine();
            block.AppendLine("## Additional Skills");
            block.AppendLine();
            block.AppendLine($"- {string.Join(", ", missingKeywords)}");
            block.AppendLine();

            resume.Append(block);
            injectedKeywords.AddRange(missingKeywords);
        }
    }

    /// <summary>
    /// Ensures the vacancy title keywords appear somewhere in the resume,
    /// typically in the summary.
    /// </summary>
    private void InjectTitleKeywords(StringBuilder resume, JobVacancy vacancy)
    {
        if (string.IsNullOrWhiteSpace(vacancy.Title))
            return;

        string content = resume.ToString();
        string[] titleWords = vacancy.Title
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToArray();

        var missingTitleWords = titleWords
            .Where(w => !content.Contains(w, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (missingTitleWords.Count > 0)
        {
            int summaryEnd = FindSectionEnd(content, "## Summary");
            if (summaryEnd >= 0)
            {
                string injection =
                    $"Experience aligned with {vacancy.Title} responsibilities.\n\n";
                resume.Insert(summaryEnd, injection);

                _logger.LogDebug(
                    "Injected title-related keywords in summary section");
            }
        }
    }

    /// <summary>
    /// Finds the position just before the next section heading after the specified section,
    /// or the end of the content if no subsequent section is found.
    /// </summary>
    private static int FindSectionEnd(string content, string sectionHeading)
    {
        int sectionStart = content.IndexOf(
            sectionHeading, StringComparison.OrdinalIgnoreCase);

        if (sectionStart < 0)
            return -1;

        int afterHeading = content.IndexOf('\n', sectionStart);
        if (afterHeading < 0)
            return content.Length;

        var nextSectionMatch = Regex.Match(
            content[(afterHeading + 1)..],
            @"^## ",
            RegexOptions.Multiline);

        if (nextSectionMatch.Success)
            return afterHeading + 1 + nextSectionMatch.Index;

        return content.Length;
    }

    /// <summary>
    /// Checks whether a keyword already appears in the resume text (case-insensitive).
    /// </summary>
    private static bool ResumeContainsKeyword(string resume, string keyword)
    {
        return resume.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Computes keyword density as a percentage of total words in the resume.
    /// </summary>
    private static double ComputeKeywordDensity(
        string resume, IReadOnlyCollection<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(resume) || keywords.Count == 0)
            return 0.0;

        string plainText = StripMarkdown(resume);
        string[] words = plainText.Split(
            [' ', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
            return 0.0;

        int keywordHits = 0;
        foreach (string keyword in keywords)
        {
            string[] keywordParts = keyword.Split(' ');
            if (keywordParts.Length == 1)
            {
                keywordHits += words.Count(w =>
                    string.Equals(w, keyword, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                keywordHits += CountPhraseOccurrences(plainText, keyword);
            }
        }

        return (double)keywordHits / words.Length * 100.0;
    }

    /// <summary>
    /// Counts occurrences of a multi-word phrase in the text.
    /// </summary>
    private static int CountPhraseOccurrences(string text, string phrase)
    {
        int count = 0;
        int index = 0;

        while ((index = text.IndexOf(phrase, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += phrase.Length;
        }

        return count;
    }

    /// <summary>
    /// Strips common markdown formatting for word-counting purposes.
    /// </summary>
    private static string StripMarkdown(string markdown)
    {
        string result = Regex.Replace(markdown, @"#{1,6}\s*", "");
        result = Regex.Replace(result, @"\*{1,2}(.*?)\*{1,2}", "$1");
        result = Regex.Replace(result, @"\[([^\]]+)\]\([^)]+\)", "$1");
        result = Regex.Replace(result, @"^[-*]\s+", "", RegexOptions.Multiline);
        result = Regex.Replace(result, @"---+", "");
        return result;
    }
}
