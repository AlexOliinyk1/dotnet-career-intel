using System.Text.RegularExpressions;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Resume;

/// <summary>
/// Simulates how three different readers (ATS, recruiter, tech lead) evaluate a resume,
/// producing actionable scores, heatmaps, and recommendations.
/// </summary>
public sealed partial class ResumeSimulator(ILogger<ResumeSimulator> logger)
{
    // ── Regex source generators (C# 14 / .NET 10) ──────────────────────────

    [GeneratedRegex(@"[^\x20-\x7E\r\n\t]", RegexOptions.Compiled)]
    private static partial Regex NonAsciiPrintableRegex();

    [GeneratedRegex(@"\b(expert|master|guru)\b.*\b(everything|all technologies|all languages)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex OverclaimingRegex();

    [GeneratedRegex(@"\d+[\.\d]*[%xX]|\$[\d,]+|\d+\s*(users|requests|rps|tps|ms|seconds|minutes)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MetricsRegex();

    [GeneratedRegex(@"\b(designed|architected|led migration|introduced|built from scratch|system design)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ArchitectureRegex();

    [GeneratedRegex(@"\b(v\d+|\.NET\s*\d+|C#\s*\d+|EF\s*Core\s*\d+|React\s*\d+|Node\s*\d+|Python\s*3\.\d+|Java\s*\d+|PostgreSQL\s*\d+|Redis\s*\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SpecificVersionRegex();

    [GeneratedRegex(@"\b(SOLID|CQRS|DDD|event[- ]?sourcing|microservices|clean architecture|hexagonal|saga|outbox)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PatternRegex();

    [GeneratedRegex(@"\b(synergy|leverage|proactive|dynamic|self[- ]starter|team[- ]player|passionate)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BuzzwordRegex();

    // ── Section header patterns ─────────────────────────────────────────────

    private static readonly string[] ExpectedSections =
        ["summary", "experience", "skills", "education"];

    // ── Public entry point ──────────────────────────────────────────────────

    /// <summary>
    /// Runs a full three-reader simulation on the provided resume markdown against
    /// the target vacancy and user profile.
    /// </summary>
    public ResumeSimulation Simulate(string resumeMarkdown, JobVacancy vacancy, UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(resumeMarkdown);
        ArgumentNullException.ThrowIfNull(vacancy);
        ArgumentNullException.ThrowIfNull(profile);

        logger.LogInformation(
            "Simulating resume evaluation for '{Title}' at {Company}",
            vacancy.Title, vacancy.Company);

        var ats = SimulateAts(resumeMarkdown, vacancy);
        var recruiter = SimulateRecruiter(resumeMarkdown, vacancy);
        var techLead = SimulateTechLead(resumeMarkdown, vacancy, profile);

        // Overall conversion = ATS gate * weighted blend of all three scores
        var conversion = (ats.PassesFilter ? 1.0 : 0.0)
            * (ats.Score / 100.0) * 0.3
            + (recruiter.Score / 100.0) * 0.3
            + (techLead.Score / 100.0) * 0.4;

        var issues = CollectCriticalIssues(ats, recruiter, techLead);
        var recommendations = BuildRecommendations(ats, recruiter, techLead, vacancy);

        logger.LogInformation(
            "Simulation complete. Conversion probability: {Conversion:P1} | ATS={Ats} Recruiter={Recruiter} TechLead={TechLead}",
            conversion, ats.Score, recruiter.Score, techLead.Score);

        return new ResumeSimulation
        {
            AtsScore = ats,
            RecruiterScore = recruiter,
            TechLeadScore = techLead,
            OverallConversionProbability = Math.Clamp(conversion, 0.0, 1.0),
            CriticalIssues = issues,
            Recommendations = recommendations
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ATS SIMULATION
    // ═══════════════════════════════════════════════════════════════════════

    private AtsScore SimulateAts(string resume, JobVacancy vacancy)
    {
        logger.LogDebug("Running ATS simulation");

        var allSkills = vacancy.RequiredSkills
            .Concat(vacancy.PreferredSkills)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ── Keyword matching ────────────────────────────────────────────
        var matchedKeywords = new List<string>();
        var missingKeywords = new List<string>();

        foreach (var skill in allSkills)
        {
            if (resume.Contains(skill, StringComparison.OrdinalIgnoreCase))
                matchedKeywords.Add(skill);
            else
                missingKeywords.Add(skill);
        }

        double keywordMatchPercent = allSkills.Count > 0
            ? (double)matchedKeywords.Count / allSkills.Count * 100.0
            : 0.0;

        // ── Format analysis ─────────────────────────────────────────────
        var formatIssues = new List<string>();
        int wordCount = resume.Split([' ', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries).Length;

        if (wordCount < 300)
            formatIssues.Add($"Resume too short ({wordCount} words). ATS may reject sparse resumes.");

        if (wordCount > 2000)
            formatIssues.Add($"Resume too long ({wordCount} words). ATS parsers may truncate content.");

        // Check for special characters that ATS systems commonly choke on
        int specialCharCount = NonAsciiPrintableRegex().Matches(resume).Count;
        if (specialCharCount > 10)
            formatIssues.Add($"Found {specialCharCount} special/non-ASCII characters that ATS may not parse.");

        // Check for expected section headers
        var resumeLower = resume.ToLowerInvariant();
        foreach (var section in ExpectedSections)
        {
            // Look for markdown headers (## Section) or plain text headers
            bool hasHeader = resumeLower.Contains($"## {section}")
                || resumeLower.Contains($"# {section}")
                || resumeLower.Contains($"**{section}**");

            if (!hasHeader)
                formatIssues.Add($"Missing expected section header: '{section}'.");
        }

        // Determine if there are critical format issues (missing 2+ sections or too many special chars)
        int missingSectionCount = ExpectedSections
            .Count(s => !resumeLower.Contains($"## {s}")
                     && !resumeLower.Contains($"# {s}")
                     && !resumeLower.Contains($"**{s}**"));

        bool hasCriticalFormatIssues = missingSectionCount >= 3 || specialCharCount > 50;

        // ── Scoring ─────────────────────────────────────────────────────
        double formatScore = 100.0;
        formatScore -= missingSectionCount * 10.0;
        formatScore -= Math.Min(specialCharCount, 30) * 1.0;
        if (wordCount < 300) formatScore -= 15.0;
        if (wordCount > 2000) formatScore -= 10.0;
        formatScore = Math.Clamp(formatScore, 0.0, 100.0);

        double score = keywordMatchPercent * 0.7 + formatScore * 0.3;
        bool passesFilter = keywordMatchPercent >= 40.0 && !hasCriticalFormatIssues;

        logger.LogDebug(
            "ATS result: Score={Score:F1}, Keywords={Matched}/{Total}, Format={Format:F1}, Pass={Pass}",
            score, matchedKeywords.Count, allSkills.Count, formatScore, passesFilter);

        return new AtsScore
        {
            Score = Math.Round(score, 1),
            KeywordMatchPercent = Math.Round(keywordMatchPercent, 1),
            MissingKeywords = missingKeywords,
            FormatIssues = formatIssues,
            PassesFilter = passesFilter
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RECRUITER SIMULATION
    // ═══════════════════════════════════════════════════════════════════════

    private RecruiterScore SimulateRecruiter(string resume, JobVacancy vacancy)
    {
        logger.LogDebug("Running recruiter simulation");

        var lines = resume.Split('\n', StringSplitOptions.TrimEntries);
        var resumeLower = resume.ToLowerInvariant();
        var vacancyKeywords = vacancy.RequiredSkills
            .Concat(vacancy.PreferredSkills)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── Build heatmap per section ───────────────────────────────────
        var heatmap = new List<SectionHeatmap>();

        // Header section - recruiter wants name + title immediately visible
        bool headerClear = lines.Length > 0
            && lines[0].Length > 3
            && lines.Length > 1
            && lines.Take(3).Any(l => l.Contains('|') || l.Contains("**"));

        heatmap.Add(new SectionHeatmap
        {
            Section = "Header",
            Attention = headerClear ? "High" : "Low",
            Recommendation = headerClear
                ? "Header is clear with name and title visible."
                : "Improve header: ensure name and title are in the first 2 lines."
        });

        // Summary section
        string summarySection = ExtractSection(resume, "summary");
        int summarySentences = summarySection.Split(['.', '!', '?'],
            StringSplitOptions.RemoveEmptyEntries).Length;
        bool summaryHasRoleKeywords = vacancyKeywords
            .Any(k => summarySection.Contains(k, StringComparison.OrdinalIgnoreCase));

        string summaryAttention = (summarySentences <= 3 && summaryHasRoleKeywords)
            ? "High"
            : summarySentences > 5
                ? "Low"
                : "Medium";

        heatmap.Add(new SectionHeatmap
        {
            Section = "Summary",
            Attention = summaryAttention,
            Recommendation = summaryAttention == "High"
                ? "Summary is concise and contains role-relevant keywords."
                : summaryAttention == "Medium"
                    ? "Consider shortening summary to 2-3 sentences with more vacancy-specific keywords."
                    : "Summary is too long or missing relevant keywords. Recruiters will skim past it."
        });

        // Recent Experience (first job entry) - always high attention
        string experienceSection = ExtractSection(resume, "experience");
        bool hasRecentExperience = !string.IsNullOrWhiteSpace(experienceSection);

        heatmap.Add(new SectionHeatmap
        {
            Section = "Recent Experience",
            Attention = hasRecentExperience ? "High" : "Low",
            Recommendation = hasRecentExperience
                ? "Most recent role is visible. Ensure it leads with impact metrics."
                : "Experience section not found or empty."
        });

        // Older Experience - typically skimmed
        heatmap.Add(new SectionHeatmap
        {
            Section = "Older Experience",
            Attention = "Low",
            Recommendation = "Recruiters skim past older roles. Keep entries brief (2-3 bullets max)."
        });

        // Skills section
        string skillsSection = ExtractSection(resume, "skills");
        bool skillsSectionExists = !string.IsNullOrWhiteSpace(skillsSection);
        int matchingSkillsInSection = skillsSectionExists
            ? vacancyKeywords.Count(k => skillsSection.Contains(k, StringComparison.OrdinalIgnoreCase))
            : 0;

        heatmap.Add(new SectionHeatmap
        {
            Section = "Skills",
            Attention = "Medium",
            Recommendation = matchingSkillsInSection > 0
                ? $"{matchingSkillsInSection} vacancy skills found in skills section. Good match."
                : "Skills section is missing vacancy-relevant keywords."
        });

        // Education - less important for senior roles
        bool isSenior = vacancy.SeniorityLevel >= SeniorityLevel.Senior;

        heatmap.Add(new SectionHeatmap
        {
            Section = "Education",
            Attention = isSenior ? "Low" : "Medium",
            Recommendation = isSenior
                ? "For senior roles, education is less important. Keep it brief."
                : "Education section gets moderate attention for this seniority level."
        });

        // ── Read time estimate ──────────────────────────────────────────
        int wordCount = resume.Split([' ', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries).Length;

        // Average recruiter spends 6-8 seconds on initial scan
        // Longer resumes get slightly more time but plateau
        double readTimeSeconds = Math.Min(6.0 + wordCount / 500.0, 12.0);

        // ── First impression (based on first 3 non-empty lines) ─────────
        var firstLines = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Take(3)
            .ToList();

        string firstImpression;
        bool hasNameInHeader = firstLines.Count > 0 && firstLines[0].StartsWith('#');
        bool hasTitleInHeader = firstLines.Count > 1
            && (firstLines[1].Contains("**") || firstLines[1].Contains('|'));

        if (hasNameInHeader && hasTitleInHeader)
            firstImpression = "Strong: clear professional identity with name and title visible immediately.";
        else if (hasNameInHeader)
            firstImpression = "Moderate: name is visible but title/role is not immediately clear.";
        else
            firstImpression = "Weak: recruiter cannot quickly identify candidate name and role.";

        // ── Concerns ────────────────────────────────────────────────────
        var concerns = new List<string>();

        // Check for employment gaps (look for date patterns and gaps)
        if (resumeLower.Contains("gap") || resumeLower.Contains("sabbatical")
            || resumeLower.Contains("career break"))
        {
            concerns.Add("Employment gap detected. Consider adding context for career breaks.");
        }

        // Title mismatch
        if (!string.IsNullOrWhiteSpace(vacancy.Title)
            && !resume.Contains(vacancy.Title, StringComparison.OrdinalIgnoreCase))
        {
            // Check for partial match (e.g. "Senior .NET Developer" vs ".NET Developer")
            var titleWords = vacancy.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int matchedTitleWords = titleWords
                .Count(w => resume.Contains(w, StringComparison.OrdinalIgnoreCase));

            if (matchedTitleWords < titleWords.Length / 2)
                concerns.Add($"Title mismatch: vacancy is for '{vacancy.Title}' but resume doesn't reflect this role.");
        }

        // No metrics in experience
        bool hasMetrics = MetricsRegex().IsMatch(experienceSection);
        if (!hasMetrics && hasRecentExperience)
            concerns.Add("No quantified achievements found. Add metrics (%, $, user counts) to strengthen impact.");

        // Resume too generic
        if (matchingSkillsInSection < 2 && vacancyKeywords.Count > 3)
            concerns.Add("Resume appears generic. Tailor skills section to this specific vacancy.");

        // ── Scoring ─────────────────────────────────────────────────────
        double sectionScoreSum = 0.0;
        double sectionWeightSum = 0.0;

        foreach (var entry in heatmap)
        {
            double weight = entry.Section switch
            {
                "Header" => 2.0,
                "Summary" => 2.5,
                "Recent Experience" => 3.0,
                "Older Experience" => 0.5,
                "Skills" => 1.5,
                "Education" => 0.5,
                _ => 1.0
            };

            double attentionScore = entry.Attention switch
            {
                "High" => 90.0,
                "Medium" => 60.0,
                "Low" => 30.0,
                "Skipped" => 0.0,
                _ => 30.0
            };

            sectionScoreSum += attentionScore * weight;
            sectionWeightSum += weight;
        }

        double recruiterScoreValue = sectionWeightSum > 0
            ? sectionScoreSum / sectionWeightSum
            : 0.0;

        // Penalty for concerns
        recruiterScoreValue -= concerns.Count * 5.0;
        recruiterScoreValue = Math.Clamp(recruiterScoreValue, 0.0, 100.0);

        logger.LogDebug(
            "Recruiter result: Score={Score:F1}, ReadTime={Time:F1}s, Concerns={Concerns}",
            recruiterScoreValue, readTimeSeconds, concerns.Count);

        return new RecruiterScore
        {
            Score = Math.Round(recruiterScoreValue, 1),
            Heatmap = heatmap,
            ReadTimeSeconds = Math.Round(readTimeSeconds, 1),
            FirstImpression = firstImpression,
            Concerns = concerns
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TECH LEAD SIMULATION
    // ═══════════════════════════════════════════════════════════════════════

    private TechLeadScore SimulateTechLead(string resume, JobVacancy vacancy, UserProfile profile)
    {
        logger.LogDebug("Running tech lead simulation");

        var impressivePoints = new List<string>();
        var redFlags = new List<string>();
        var questionsWouldAsk = new List<string>();

        // ── Technical depth analysis ────────────────────────────────────

        // Specific versions mentioned (e.g., ".NET 8", "PostgreSQL 16")
        var versionMatches = SpecificVersionRegex().Matches(resume);
        int specificVersionCount = versionMatches.Count;
        if (specificVersionCount > 0)
        {
            var versions = versionMatches.Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            impressivePoints.Add($"Mentions specific technology versions: {string.Join(", ", versions.Take(5))}.");
        }

        // Architecture and design patterns
        var patternMatches = PatternRegex().Matches(resume);
        int patternCount = patternMatches.Count;
        if (patternCount > 0)
        {
            var patterns = patternMatches.Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            impressivePoints.Add($"References architectural patterns: {string.Join(", ", patterns.Take(5))}.");
        }

        // Architecture decisions
        var archMatches = ArchitectureRegex().Matches(resume);
        int archDecisionCount = archMatches.Count;
        if (archDecisionCount > 0)
            impressivePoints.Add($"Describes {archDecisionCount} architecture/design decision(s) - shows ownership.");

        // Quantified achievements
        var metricsMatches = MetricsRegex().Matches(resume);
        int metricsCount = metricsMatches.Count;
        if (metricsCount >= 3)
            impressivePoints.Add($"Contains {metricsCount} quantified achievements - demonstrates measurable impact.");
        else if (metricsCount > 0)
            impressivePoints.Add($"Some quantified results ({metricsCount}), but could use more specific numbers.");

        // ── Red flags ───────────────────────────────────────────────────

        // Overclaiming detection
        if (OverclaimingRegex().IsMatch(resume))
            redFlags.Add("Overclaiming detected: claims expertise in 'everything' or 'all technologies'.");

        // Generic buzzwords without substance
        var buzzwordMatches = BuzzwordRegex().Matches(resume);
        int buzzwordCount = buzzwordMatches.Count;
        if (buzzwordCount > 5 && specificVersionCount < 2)
            redFlags.Add($"Heavy on buzzwords ({buzzwordCount}) but light on technical specifics.");

        // No project outcomes
        string experienceSection = ExtractSection(resume, "experience");
        if (!string.IsNullOrWhiteSpace(experienceSection) && metricsCount == 0)
            redFlags.Add("Experience section lacks concrete project outcomes or measurable results.");

        // Skills listed but not demonstrated in experience
        var allVacancySkills = vacancy.RequiredSkills
            .Concat(vacancy.PreferredSkills)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skillsMentionedInSkillsSection = allVacancySkills
            .Where(s => ExtractSection(resume, "skills")
                .Contains(s, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var skillsDemonstratedInExperience = allVacancySkills
            .Where(s => experienceSection.Contains(s, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var claimedButNotDemonstrated = skillsMentionedInSkillsSection
            .Except(skillsDemonstratedInExperience, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (claimedButNotDemonstrated.Count > 2)
            redFlags.Add($"Skills listed but not demonstrated in experience: {string.Join(", ", claimedButNotDemonstrated.Take(5))}.");

        // Too many technologies without depth
        string skillsSection = ExtractSection(resume, "skills");
        int totalSkillsMentioned = skillsSection
            .Split([',', '|', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Length;

        if (totalSkillsMentioned > 30 && specificVersionCount < 3)
            redFlags.Add($"Lists {totalSkillsMentioned} technologies but shows little depth. Jack-of-all-trades concern.");

        // ── Questions a tech lead would ask ─────────────────────────────

        // Questions from resume claims that need verification
        if (archDecisionCount > 0)
            questionsWouldAsk.Add("Walk me through one of the architecture decisions you described. What were the trade-offs?");

        if (metricsCount > 0)
        {
            var sampleMetric = metricsMatches[0].Value;
            questionsWouldAsk.Add($"You mention '{sampleMetric}' - how did you measure this and what was the baseline?");
        }

        // Questions from profile experience
        if (profile.Experiences.Count > 0)
        {
            var latestExp = profile.Experiences
                .OrderByDescending(e => e.StartDate)
                .First();

            if (latestExp.TechStack.Count > 0)
                questionsWouldAsk.Add($"In your role at {latestExp.Company}, how did you use {latestExp.TechStack.First()} at scale?");
        }

        // Questions from vacancy requirements not clearly addressed
        var uncoveredRequired = vacancy.RequiredSkills
            .Where(s => !resume.Contains(s, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        foreach (var skill in uncoveredRequired)
            questionsWouldAsk.Add($"The role requires {skill} - what's your experience with it?");

        // Generic depth question
        if (patternCount > 0)
        {
            var firstPattern = patternMatches[0].Value;
            questionsWouldAsk.Add($"You mention {firstPattern}. Can you describe a situation where this approach caused problems?");
        }

        if (claimedButNotDemonstrated.Count > 0)
            questionsWouldAsk.Add($"You list {claimedButNotDemonstrated.First()} in skills - can you describe a project where you used it?");

        // ── Depth percentage ────────────────────────────────────────────
        // Depth = how much of the resume shows real technical understanding
        double depthSignals = specificVersionCount * 2.0
            + patternCount * 3.0
            + archDecisionCount * 4.0
            + metricsCount * 2.0;

        double shallowSignals = buzzwordCount * 1.5
            + claimedButNotDemonstrated.Count * 2.0;

        double totalSignals = depthSignals + shallowSignals;
        double depthPercent = totalSignals > 0
            ? depthSignals / totalSignals * 100.0
            : 0.0;

        // ── Relevance to vacancy ────────────────────────────────────────
        int relevantSkillsCount = allVacancySkills
            .Count(s => resume.Contains(s, StringComparison.OrdinalIgnoreCase));

        double relevancePercent = allVacancySkills.Count > 0
            ? (double)relevantSkillsCount / allVacancySkills.Count * 100.0
            : 0.0;

        // ── Final score ─────────────────────────────────────────────────
        double score = depthPercent * 0.4
            + relevancePercent * 0.35
            + Math.Min(impressivePoints.Count * 10.0, 25.0);

        // Penalties
        score -= redFlags.Count * 8.0;
        score = Math.Clamp(score, 0.0, 100.0);

        logger.LogDebug(
            "Tech lead result: Score={Score:F1}, Depth={Depth:F1}%, Impressive={Impressive}, RedFlags={Flags}",
            score, depthPercent, impressivePoints.Count, redFlags.Count);

        return new TechLeadScore
        {
            Score = Math.Round(score, 1),
            DepthPercent = Math.Round(depthPercent, 1),
            ImpressivePoints = impressivePoints,
            RedFlags = redFlags,
            QuestionsWouldAsk = questionsWouldAsk
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ISSUE COLLECTION & RECOMMENDATIONS
    // ═══════════════════════════════════════════════════════════════════════

    private static List<string> CollectCriticalIssues(AtsScore ats, RecruiterScore recruiter, TechLeadScore techLead)
    {
        var issues = new List<string>();

        if (!ats.PassesFilter)
            issues.Add("CRITICAL: Resume will be filtered out by ATS before a human ever sees it.");

        if (ats.MissingKeywords.Count > 3)
            issues.Add($"Missing {ats.MissingKeywords.Count} important keywords: {string.Join(", ", ats.MissingKeywords.Take(5))}.");

        if (ats.FormatIssues.Count > 0)
        {
            foreach (var issue in ats.FormatIssues)
                issues.Add($"ATS format: {issue}");
        }

        if (recruiter.Score < 50)
            issues.Add("Resume is unlikely to pass initial recruiter screening (score below 50).");

        if (recruiter.ReadTimeSeconds < 4)
            issues.Add("Resume is too sparse - recruiter will dismiss it before fully reading.");

        if (recruiter.Concerns.Count > 2)
            issues.Add($"Recruiter has {recruiter.Concerns.Count} concerns that may lead to rejection.");

        if (techLead.RedFlags.Count > 0)
        {
            foreach (var flag in techLead.RedFlags)
                issues.Add($"Tech lead red flag: {flag}");
        }

        if (techLead.DepthPercent < 30)
            issues.Add("Technical depth is too shallow. Resume reads as surface-level.");

        return issues;
    }

    private static List<string> BuildRecommendations(
        AtsScore ats, RecruiterScore recruiter, TechLeadScore techLead, JobVacancy vacancy)
    {
        var recommendations = new List<string>();

        // ATS recommendations
        if (ats.MissingKeywords.Count > 0)
        {
            recommendations.Add(
                $"Add these missing keywords naturally into your experience bullets: " +
                $"{string.Join(", ", ats.MissingKeywords.Take(7))}.");
        }

        if (ats.KeywordMatchPercent < 60)
            recommendations.Add("Keyword match is below 60%. Mirror the job description's exact terminology.");

        if (ats.FormatIssues.Any(i => i.Contains("section header", StringComparison.OrdinalIgnoreCase)))
            recommendations.Add("Add standard section headers (Summary, Experience, Skills, Education) for ATS parsing.");

        // Recruiter recommendations
        var lowAttentionSections = recruiter.Heatmap
            .Where(h => h.Attention is "Low" && h.Section is not "Older Experience" and not "Education")
            .ToList();

        foreach (var section in lowAttentionSections)
            recommendations.Add($"Improve '{section.Section}' section: {section.Recommendation}");

        if (recruiter.Concerns.Count > 0)
        {
            foreach (var concern in recruiter.Concerns)
                recommendations.Add($"Address recruiter concern: {concern}");
        }

        // Tech lead recommendations
        if (techLead.DepthPercent < 50)
            recommendations.Add("Add specific technology versions, patterns, and architecture decisions to show depth.");

        if (techLead.ImpressivePoints.Count < 2)
            recommendations.Add("Add more quantified achievements and architecture decisions to impress technical reviewers.");

        if (techLead.RedFlags.Count > 0)
            recommendations.Add("Remove generic buzzwords and replace with specific technical accomplishments.");

        // Vacancy-specific recommendations
        if (vacancy.SeniorityLevel >= SeniorityLevel.Senior && techLead.DepthPercent < 60)
            recommendations.Add($"For a {vacancy.SeniorityLevel} role, demonstrate leadership and system-level thinking.");

        return recommendations;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extracts the content of a markdown section by header name.
    /// Captures everything between the header and the next header of same or higher level.
    /// </summary>
    private static string ExtractSection(string markdown, string sectionName)
    {
        var lines = markdown.Split('\n');
        bool capturing = false;
        int captureLevel = 0;
        var sectionLines = new List<string>();

        foreach (var line in lines)
        {
            string trimmed = line.TrimStart();

            // Detect header level
            if (trimmed.StartsWith('#'))
            {
                int level = trimmed.TakeWhile(c => c == '#').Count();
                string headerText = trimmed.TrimStart('#').Trim();

                if (headerText.Contains(sectionName, StringComparison.OrdinalIgnoreCase))
                {
                    capturing = true;
                    captureLevel = level;
                    continue;
                }

                // Stop capturing if we hit a same-or-higher-level header
                if (capturing && level <= captureLevel)
                    break;
            }

            if (capturing)
                sectionLines.Add(line);
        }

        return string.Join('\n', sectionLines).Trim();
    }
}
