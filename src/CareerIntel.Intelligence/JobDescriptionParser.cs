using System.Text.RegularExpressions;
using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Parses raw job description text/HTML into structured sections:
/// responsibilities, requirements, nice-to-have, benefits, tech stack, etc.
/// Handles common heading patterns from various job boards.
/// </summary>
public static class JobDescriptionParser
{
    // Section heading patterns mapped to their category.
    // Order matters — first match wins for ambiguous headings.
    private static readonly (string Pattern, SectionType Type)[] SectionHeadings =
    [
        // Responsibilities
        (@"(?:what\s+you(?:'ll|\s+will)\s+(?:do|be\s+doing))", SectionType.Responsibilities),
        (@"(?:your\s+(?:role|responsibilities|day[\s-]to[\s-]day|tasks|duties))", SectionType.Responsibilities),
        (@"(?:key\s+)?responsibilities", SectionType.Responsibilities),
        (@"(?:the\s+)?role", SectionType.Responsibilities),
        (@"job\s+description", SectionType.Responsibilities),
        (@"what\s+the\s+role\s+involves", SectionType.Responsibilities),
        (@"about\s+the\s+(?:role|position|job)", SectionType.Responsibilities),
        (@"scope\s+of\s+work", SectionType.Responsibilities),

        // Requirements
        (@"(?:what\s+you(?:'ll)?\s+(?:need|bring))", SectionType.Requirements),
        (@"(?:what\s+we(?:'re|\s+are)\s+looking\s+for)", SectionType.Requirements),
        (@"(?:required?\s+)?(?:skills|qualifications|experience)", SectionType.Requirements),
        (@"(?:must[\s-]have|minimum)\s+(?:requirements?|qualifications?)?", SectionType.Requirements),
        (@"requirements?", SectionType.Requirements),
        (@"who\s+you\s+are", SectionType.Requirements),
        (@"you\s+(?:have|should\s+have|bring)", SectionType.Requirements),
        (@"(?:your\s+)?(?:background|expertise|profile)", SectionType.Requirements),
        (@"we\s+expect", SectionType.Requirements),

        // Nice to have
        (@"nice[\s-]to[\s-]have", SectionType.NiceToHave),
        (@"(?:preferred|bonus|desirable|advantageous|plus)\s*(?:skills|qualifications|experience)?", SectionType.NiceToHave),
        (@"(?:it(?:'s|\s+is|would\s+be)\s+(?:a\s+)?(?:great|nice|bonus|plus)\s+if)", SectionType.NiceToHave),
        (@"(?:extra\s+)?(?:points|credit)\s+(?:for|if)", SectionType.NiceToHave),
        (@"good[\s-]to[\s-](?:have|know)", SectionType.NiceToHave),

        // Benefits
        (@"(?:what\s+we\s+offer)", SectionType.Benefits),
        (@"(?:benefits?|perks?|compensation)\s*(?:and\s+perks?|package)?", SectionType.Benefits),
        (@"why\s+(?:join\s+us|work\s+(?:with|for|at)\s+us|us)", SectionType.Benefits),
        (@"(?:we\s+(?:offer|provide))", SectionType.Benefits),
        (@"(?:our\s+offer)", SectionType.Benefits),
        (@"what(?:'s|\s+is)\s+in\s+it\s+for\s+you", SectionType.Benefits),

        // About company
        (@"about\s+(?:us|the\s+company|our\s+company)", SectionType.AboutCompany),
        (@"(?:who\s+we\s+are|company\s+(?:overview|description))", SectionType.AboutCompany),

        // Tech stack
        (@"(?:tech(?:nology)?\s+stack|technologies?\s+(?:we\s+use|used))", SectionType.TechStack),
        (@"(?:our\s+stack|tools\s+(?:we\s+use|and\s+technologies))", SectionType.TechStack),

        // Interview process
        (@"(?:interview|hiring|recruitment)\s+process", SectionType.InterviewProcess),
        (@"(?:how\s+(?:to\s+apply|we\s+hire))", SectionType.InterviewProcess),
    ];

    // Known tech keywords for stack extraction
    private static readonly HashSet<string> TechKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "C#", ".NET", "ASP.NET", "Entity Framework", "EF Core", "LINQ", "Blazor",
        "Azure", "AWS", "GCP", "Docker", "Kubernetes", "K8s", "Terraform",
        "SQL", "PostgreSQL", "MySQL", "MongoDB", "Redis", "Elasticsearch",
        "RabbitMQ", "Kafka", "gRPC", "REST", "GraphQL", "SignalR",
        "React", "Angular", "Vue", "TypeScript", "JavaScript", "Node.js",
        "Python", "Java", "Go", "Rust", "Kotlin", "Swift",
        "Git", "CI/CD", "Jenkins", "GitHub Actions", "Azure DevOps",
        "Microservices", "Event Sourcing", "CQRS", "DDD",
        "Agile", "Scrum", "Kanban", "Jira", "Confluence"
    };

    /// <summary>
    /// Parse a raw description into structured sections.
    /// </summary>
    public static JobDescriptionBreakdown Parse(string? description)
    {
        var breakdown = new JobDescriptionBreakdown();

        if (string.IsNullOrWhiteSpace(description))
            return breakdown;

        // Strip HTML tags but preserve line breaks
        var text = StripHtml(description);

        // Split into sections by headings
        var sections = SplitIntoSections(text);

        foreach (var (type, content) in sections)
        {
            var items = ExtractBulletPoints(content);

            switch (type)
            {
                case SectionType.AboutCompany:
                    breakdown.AboutCompany = string.Join(" ", items.Count > 0 ? items : [content.Trim()]);
                    break;
                case SectionType.Responsibilities:
                    breakdown.Responsibilities.AddRange(items);
                    break;
                case SectionType.Requirements:
                    breakdown.Requirements.AddRange(items);
                    break;
                case SectionType.NiceToHave:
                    breakdown.NiceToHave.AddRange(items);
                    break;
                case SectionType.Benefits:
                    breakdown.Benefits.AddRange(items);
                    break;
                case SectionType.TechStack:
                    breakdown.TechStack.AddRange(items);
                    break;
                case SectionType.InterviewProcess:
                    breakdown.InterviewProcess = string.Join(" ", items.Count > 0 ? items : [content.Trim()]);
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(content))
                        breakdown.Other += content.Trim() + "\n";
                    break;
            }
        }

        // If no structured sections were found, try to extract from the whole text
        if (!breakdown.HasStructuredData)
        {
            var allItems = ExtractBulletPoints(text);
            if (allItems.Count > 0)
            {
                // Heuristic: first section is usually about/responsibilities, rest are requirements
                breakdown.Responsibilities = allItems;
            }
        }

        // Always try to extract tech stack from full description
        if (breakdown.TechStack.Count == 0)
            breakdown.TechStack = ExtractTechStack(text);

        return breakdown;
    }

    /// <summary>
    /// Parse and attach breakdown to a vacancy.
    /// </summary>
    public static void ParseAndAttach(JobVacancy vacancy)
    {
        vacancy.Breakdown = Parse(vacancy.Description);
    }

    /// <summary>
    /// Parse and attach breakdown to a batch of vacancies.
    /// </summary>
    public static void ParseAndAttachBatch(IEnumerable<JobVacancy> vacancies)
    {
        foreach (var vacancy in vacancies)
            ParseAndAttach(vacancy);
    }

    private static List<(SectionType Type, string Content)> SplitIntoSections(string text)
    {
        var sections = new List<(SectionType Type, string Content)>();

        // Build a combined regex that captures all section headings
        var lines = text.Split('\n');
        var currentType = SectionType.Other;
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                currentContent.Add("");
                continue;
            }

            // Check if this line is a section heading
            var detectedType = DetectSectionType(trimmed);
            if (detectedType.HasValue && detectedType.Value != currentType)
            {
                // Save previous section
                var content = string.Join("\n", currentContent).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                    sections.Add((currentType, content));

                currentType = detectedType.Value;
                currentContent.Clear();
            }
            else
            {
                currentContent.Add(trimmed);
            }
        }

        // Save last section
        var lastContent = string.Join("\n", currentContent).Trim();
        if (!string.IsNullOrWhiteSpace(lastContent))
            sections.Add((currentType, lastContent));

        return sections;
    }

    private static SectionType? DetectSectionType(string line)
    {
        // Headings are typically short lines, possibly with markdown/HTML heading markers
        var cleaned = Regex.Replace(line, @"^#{1,6}\s*|^\*{1,3}\s*|^-{2,}\s*|:\s*$|^\d+\.\s*", "").Trim();

        // Skip long lines — they're content, not headings
        if (cleaned.Length > 80)
            return null;

        foreach (var (pattern, type) in SectionHeadings)
        {
            if (Regex.IsMatch(cleaned, $@"^{pattern}\s*:?\s*$", RegexOptions.IgnoreCase))
                return type;

            // Also match if the heading is at the start of the line (allows trailing colon etc.)
            if (Regex.IsMatch(cleaned, $@"^{pattern}", RegexOptions.IgnoreCase) && cleaned.Length < 50)
                return type;
        }

        return null;
    }

    private static List<string> ExtractBulletPoints(string text)
    {
        var items = new List<string>();

        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Match bullet points: -, *, •, ✅, >, numbered (1., 2.)
            var cleaned = Regex.Replace(trimmed, @"^[-*•✅>▸▹◦‣]\s+|^\d+[.)]\s+|^[a-z]\)\s+", "").Trim();

            if (!string.IsNullOrWhiteSpace(cleaned) && cleaned.Length > 3)
                items.Add(cleaned);
        }

        return items;
    }

    private static List<string> ExtractTechStack(string text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tech in TechKeywords)
        {
            // Word boundary matching (handle special chars like C#, .NET)
            var escaped = Regex.Escape(tech);
            if (Regex.IsMatch(text, $@"(?<![A-Za-z]){escaped}(?![A-Za-z])", RegexOptions.IgnoreCase))
                found.Add(tech);
        }

        return found.OrderBy(t => t).ToList();
    }

    private static string StripHtml(string html)
    {
        // Replace block elements with newlines
        var text = Regex.Replace(html, @"<(?:br|p|div|h[1-6]|li|tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        // Strip remaining tags
        text = Regex.Replace(text, @"<[^>]+>", " ");
        // Decode common entities
        text = text.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&nbsp;", " ").Replace("&#39;", "'").Replace("&quot;", "\"");
        // Collapse whitespace
        text = Regex.Replace(text, @"[ \t]+", " ");
        // Collapse multiple newlines
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private enum SectionType
    {
        Other,
        AboutCompany,
        Responsibilities,
        Requirements,
        NiceToHave,
        Benefits,
        TechStack,
        InterviewProcess
    }
}
