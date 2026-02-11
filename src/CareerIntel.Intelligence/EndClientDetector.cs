using System.Text.Json;
using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Detects end clients from intermediary job postings using pattern matching.
/// Identifies outsourcing/staffing companies and extracts the actual client name
/// so you can apply directly and skip the middleman.
/// </summary>
public sealed class EndClientDetector
{
    private readonly ILogger _logger;
    private readonly List<IntermediaryCompany> _intermediaries;

    // Ordered by confidence: explicit mention patterns first
    private static readonly (string Pattern, DetectionMethod Method, double Confidence)[] DetectionPatterns =
    [
        (@"(?:our\s+client|the\s+client)[,\s]+([A-Z][A-Za-z0-9\s&.\-]{2,40}?)(?:\s*[,.]|\s+is\s|\s+are\s|\s+seeks|\s+looking|\s+needs)", DetectionMethod.ExplicitMention, 0.95),
        (@"on\s+behalf\s+of\s+([A-Z][A-Za-z0-9\s&.\-]{2,40}?)(?:\s*[,.]|\s+we\s|\s+is\s)", DetectionMethod.ExplicitMention, 0.95),
        (@"client:\s*([A-Z][A-Za-z0-9\s&.\-]{2,40}?)(?:\s*[,.\n])", DetectionMethod.ExplicitMention, 0.92),
        (@"(?:working|work)\s+(?:for|with)\s+([A-Z][A-Za-z0-9\s&.\-]{2,40}?)(?:\s*[,.]|\s+to\s|\s+on\s|\s+as\s)", DetectionMethod.ExplicitMention, 0.85),
        (@"project\s+(?:for|at|with)\s+([A-Z][A-Za-z0-9\s&.\-]{2,40}?)(?:\s*[,.]|\s+is\s)", DetectionMethod.ExplicitMention, 0.80),
        (@"(?:partner|partnered\s+with|partnering\s+with)\s+([A-Z][A-Za-z0-9\s&.\-]{2,40}?)(?:\s*[,.]|\s+to\s)", DetectionMethod.ExplicitMention, 0.75),
        (@"(?:end\s+client|final\s+client|direct\s+client)(?:\s+is)?\s*:?\s*([A-Z][A-Za-z0-9\s&.\-]{2,40}?)(?:\s*[,.\n])", DetectionMethod.ExplicitMention, 0.95),
    ];

    // Patterns for words that indicate we caught a non-company phrase
    private static readonly HashSet<string> FalsePositiveWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Senior", "Junior", "Mid", "Lead", "Principal", "Staff", "Our", "The", "This",
        "Remote", "Hybrid", "Full", "Part", "Time", "Looking", "Seeking", "Based",
        "International", "European", "American", "Global", "Fortune"
    };

    public EndClientDetector(ILogger logger, string dataDirectory)
    {
        _logger = logger;
        _intermediaries = LoadIntermediaries(dataDirectory);
    }

    public IReadOnlyList<IntermediaryCompany> KnownIntermediaries => _intermediaries;

    /// <summary>
    /// Checks if a vacancy is posted by a known intermediary company.
    /// </summary>
    public bool IsIntermediary(JobVacancy vacancy)
    {
        return FindIntermediary(vacancy) is not null;
    }

    /// <summary>
    /// Finds the matching intermediary company for a vacancy, or null.
    /// </summary>
    public IntermediaryCompany? FindIntermediary(JobVacancy vacancy)
    {
        var company = vacancy.Company?.Trim();
        if (string.IsNullOrEmpty(company))
            return null;

        // Check against known intermediaries (name + aliases)
        foreach (var intermediary in _intermediaries)
        {
            if (company.Contains(intermediary.Name, StringComparison.OrdinalIgnoreCase))
                return intermediary;

            foreach (var alias in intermediary.Aliases)
            {
                if (company.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    return intermediary;
            }
        }

        // Check for common intermediary keywords in company name
        var intermediaryKeywords = new[] { "outsourc", "staffing", "consulting", "solutions", "services", "recruitment", "agency" };
        if (intermediaryKeywords.Any(k => company.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return new IntermediaryCompany
            {
                Name = company,
                Type = "Unknown",
                EstimatedMarkup = 0.30,
                Notes = "Detected by keyword pattern"
            };
        }

        return null;
    }

    /// <summary>
    /// Attempts to extract the end client name from a vacancy description.
    /// Returns null if no client can be detected.
    /// </summary>
    public EndClientDetection? DetectEndClient(JobVacancy vacancy)
    {
        var intermediary = FindIntermediary(vacancy);
        if (intermediary is null)
            return null;

        // Try description patterns
        var description = vacancy.Description ?? string.Empty;
        foreach (var (pattern, method, confidence) in DetectionPatterns)
        {
            var match = Regex.Match(description, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success)
            {
                var clientName = CleanClientName(match.Groups[1].Value);
                if (IsValidClientName(clientName, intermediary))
                {
                    _logger.LogInformation("Detected client '{Client}' for vacancy '{Title}' from {Intermediary}",
                        clientName, vacancy.Title, intermediary.Name);

                    return new EndClientDetection
                    {
                        OriginalVacancy = vacancy,
                        Intermediary = intermediary,
                        DetectedClientName = clientName,
                        Method = method,
                        Confidence = confidence,
                        MatchedSnippet = match.Value.Trim(),
                        EstimatedUpliftPercent = intermediary.EstimatedMarkup * 100
                    };
                }
            }
        }

        // Try title patterns: [ClientName] or (ClientName)
        var titleDetection = DetectFromTitle(vacancy, intermediary);
        if (titleDetection is not null)
            return titleDetection;

        // Intermediary confirmed but no client detected
        return new EndClientDetection
        {
            OriginalVacancy = vacancy,
            Intermediary = intermediary,
            DetectedClientName = string.Empty,
            Method = DetectionMethod.ContextualHint,
            Confidence = 0,
            MatchedSnippet = "Intermediary detected but client name not found in description",
            EstimatedUpliftPercent = intermediary.EstimatedMarkup * 100
        };
    }

    /// <summary>
    /// Analyzes a batch of vacancies for intermediary postings and detected clients.
    /// </summary>
    public List<EndClientDetection> AnalyzeBatch(IEnumerable<JobVacancy> vacancies)
    {
        var results = new List<EndClientDetection>();
        foreach (var vacancy in vacancies)
        {
            var detection = DetectEndClient(vacancy);
            if (detection is not null)
                results.Add(detection);
        }
        return results;
    }

    private EndClientDetection? DetectFromTitle(JobVacancy vacancy, IntermediaryCompany intermediary)
    {
        var title = vacancy.Title ?? string.Empty;

        // [ClientName] in title
        var bracketMatch = Regex.Match(title, @"\[([A-Za-z0-9\s&.\-]{2,30})\]");
        if (bracketMatch.Success)
        {
            var clientName = CleanClientName(bracketMatch.Groups[1].Value);
            if (IsValidClientName(clientName, intermediary))
            {
                return new EndClientDetection
                {
                    OriginalVacancy = vacancy,
                    Intermediary = intermediary,
                    DetectedClientName = clientName,
                    Method = DetectionMethod.TitleBrackets,
                    Confidence = 0.80,
                    MatchedSnippet = bracketMatch.Value,
                    EstimatedUpliftPercent = intermediary.EstimatedMarkup * 100
                };
            }
        }

        // (ClientName) in title - but not common patterns like (Remote), (Full-time), etc.
        var parenMatch = Regex.Match(title, @"\(([A-Z][A-Za-z0-9\s&.\-]{2,30})\)");
        if (parenMatch.Success)
        {
            var candidate = parenMatch.Groups[1].Value.Trim();
            var skipWords = new[] { "Remote", "Hybrid", "On-site", "Full-time", "Part-time", "Contract", "B2B", "Senior", "Junior", "Lead" };
            if (!skipWords.Any(w => candidate.Equals(w, StringComparison.OrdinalIgnoreCase)))
            {
                var clientName = CleanClientName(candidate);
                if (IsValidClientName(clientName, intermediary))
                {
                    return new EndClientDetection
                    {
                        OriginalVacancy = vacancy,
                        Intermediary = intermediary,
                        DetectedClientName = clientName,
                        Method = DetectionMethod.TitleParentheses,
                        Confidence = 0.70,
                        MatchedSnippet = parenMatch.Value,
                        EstimatedUpliftPercent = intermediary.EstimatedMarkup * 100
                    };
                }
            }
        }

        return null;
    }

    private static string CleanClientName(string raw)
    {
        var cleaned = raw.Trim().TrimEnd('.', ',', ';', ':');
        // Remove trailing common words that leak into the capture
        var trailingNoise = new[] { " is", " are", " seeks", " looking", " needs", " we" };
        foreach (var noise in trailingNoise)
        {
            if (cleaned.EndsWith(noise, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[..^noise.Length].Trim();
        }
        return cleaned;
    }

    private bool IsValidClientName(string clientName, IntermediaryCompany intermediary)
    {
        if (string.IsNullOrWhiteSpace(clientName) || clientName.Length < 2)
            return false;

        // Don't match the intermediary itself
        if (clientName.Contains(intermediary.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var alias in intermediary.Aliases)
        {
            if (clientName.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check for false positive lead words
        var firstWord = clientName.Split(' ')[0];
        if (FalsePositiveWords.Contains(firstWord))
            return false;

        return true;
    }

    private List<IntermediaryCompany> LoadIntermediaries(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "intermediaries.json");
        if (!File.Exists(path))
        {
            _logger.LogWarning("Intermediaries database not found at {Path}", path);
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<IntermediaryCompany>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load intermediaries database");
            return [];
        }
    }
}
