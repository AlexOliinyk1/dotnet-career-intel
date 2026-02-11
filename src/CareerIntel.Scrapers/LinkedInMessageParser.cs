using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Parses LinkedIn data export (ZIP or messages.csv) to extract recruiter proposals.
/// Not an HTTP scraper — works with local files only.
/// </summary>
public sealed class LinkedInMessageParser
{
    // Minimum signals required to classify a conversation as a recruiter proposal
    private const int MinSignals = 2;

    private static readonly string[] JobKeywords =
    [
        "opportunity", "position", "role", "hiring", "looking for", "open role",
        "job opening", "we are looking", "your profile", "your background",
        "your experience", "great fit", "perfect match", "ideal candidate",
        "reach out", "exciting opportunity", "team is growing", "join our",
        "interested in exploring", "would you be open", "career opportunity"
    ];

    private static readonly string[] TechKeywords =
    [
        ".net", "c#", "asp.net", "azure", "backend", "developer", "engineer",
        "architect", "full-stack", "fullstack", "microservices", "docker",
        "kubernetes", "sql", "react", "angular", "typescript", "python",
        "java", "aws", "devops", "cloud", "software"
    ];

    private static readonly string[] RecruiterTitleKeywords =
    [
        "recruiter", "talent acquisition", "talent partner", "hr ", "human resources",
        "sourcing", "headhunter", "staffing", "people operations", "hiring manager",
        "tech recruiter", "it recruiter"
    ];

    private static readonly string[] NegativeKeywords =
    [
        "thank you for connecting", "thanks for connecting", "happy to connect",
        "course", "webinar", "newsletter", "subscription", "marketing",
        "free trial", "discount", "promo", "unsubscribe", "buy now"
    ];

    private static readonly string[] RemoteKeywords =
    ["remote", "wfh", "work from home", "distributed", "anywhere"];

    private static readonly string[] HybridKeywords =
    ["hybrid", "flexible", "2 days in office", "3 days in office"];

    private static readonly string[] RelocationKeywords =
    ["relocation", "relocate", "visa sponsorship", "visa support", "work permit",
     "moving", "relo package", "relocation assistance"];

    private static readonly Regex CompanyPatterns = new(
        @"(?:at|for|join|with|@)\s+([A-Z][A-Za-z0-9\s&\.\-]{1,40}?)(?:\s*[,\.!?\-]|\s+(?:as|is|and|we|team|looking|has|are))",
        RegexOptions.Compiled);

    private static readonly Regex JobTitlePatterns = new(
        @"(?:(?:Senior|Mid|Junior|Lead|Staff|Principal|Chief)\s+)?(?:(?:\.NET|C#|Backend|Full[\s-]?Stack|Software|Cloud|Platform|DevOps|Data|Frontend|React|Java|Python|Node\.?js|Go(?:lang)?)\s+)?(?:Developer|Engineer|Architect|Tech Lead|Team Lead|CTO|Manager|Consultant|Specialist)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SalaryPatterns = new(
        @"(?:\$|€|£|EUR|USD|GBP)\s*[\d,.]+(?:k|K)?(?:\s*[-–]\s*(?:\$|€|£|EUR|USD|GBP)?\s*[\d,.]+(?:k|K)?)?|[\d,.]+(?:k|K)\s*(?:\$|€|£|EUR|USD|GBP)|salary[\s:]+[\d,.]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] KnownLocations =
    [
        "Ukraine", "Kyiv", "Lviv", "Kharkiv", "Dnipro", "Odesa",
        "Germany", "Berlin", "Munich", "Hamburg", "Frankfurt",
        "Netherlands", "Amsterdam", "Rotterdam", "The Hague",
        "Poland", "Warsaw", "Krakow", "Wroclaw", "Gdansk",
        "UK", "London", "Manchester", "Edinburgh",
        "Spain", "Barcelona", "Madrid", "Valencia",
        "Portugal", "Lisbon", "Porto",
        "Czech Republic", "Prague", "Brno",
        "Austria", "Vienna", "Switzerland", "Zurich",
        "Sweden", "Stockholm", "Denmark", "Copenhagen",
        "Finland", "Helsinki", "Norway", "Oslo",
        "Ireland", "Dublin", "France", "Paris", "Lyon",
        "Italy", "Milan", "Rome", "Belgium", "Brussels",
        "Canada", "Toronto", "Vancouver",
        "US", "USA", "United States", "New York", "San Francisco",
        "EU", "Europe", "EMEA", "APAC"
    ];

    private static readonly string[] KnownTechStack =
    [
        ".NET", "C#", "ASP.NET", "Entity Framework", "Blazor", "MAUI",
        "Azure", "AWS", "GCP", "Docker", "Kubernetes", "Terraform",
        "SQL Server", "PostgreSQL", "MongoDB", "Redis", "RabbitMQ", "Kafka",
        "React", "Angular", "Vue", "TypeScript", "JavaScript", "Node.js",
        "Python", "Java", "Go", "Rust", "Scala", "Kotlin",
        "Microservices", "REST", "gRPC", "GraphQL", "SignalR",
        "CI/CD", "Jenkins", "GitHub Actions", "Azure DevOps",
        "Elasticsearch", "Grafana", "Prometheus",
        "DDD", "CQRS", "Event Sourcing", "Clean Architecture"
    ];

    /// <summary>
    /// Parse a LinkedIn data export file (ZIP or messages.csv) and return detected proposals.
    /// </summary>
    public async Task<ParseResult> ParseAsync(string filePath)
    {
        var result = new ParseResult { SourceFile = filePath };

        string csvContent;
        if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            csvContent = ExtractMessagesFromZip(filePath);
        }
        else
        {
            csvContent = await File.ReadAllTextAsync(filePath);
        }

        var rawMessages = ParseCsv(csvContent);
        result.TotalMessages = rawMessages.Count;

        // Group by conversation
        var conversations = rawMessages
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Date).ToList());

        result.TotalConversations = conversations.Count;

        foreach (var (conversationId, messages) in conversations)
        {
            if (!IsRecruiterProposal(messages))
                continue;

            var proposal = ExtractProposal(conversationId, messages, filePath);
            result.Proposals.Add(proposal);
        }

        return result;
    }

    private static string ExtractMessagesFromZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        // LinkedIn export may have messages.csv at root or in a subfolder
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals("messages.csv", StringComparison.OrdinalIgnoreCase));

        if (entry == null)
            throw new FileNotFoundException("messages.csv not found in the LinkedIn export ZIP.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static List<LinkedInMessage> ParseCsv(string csvContent)
    {
        var messages = new List<LinkedInMessage>();
        var lines = ParseCsvLines(csvContent);

        if (lines.Count < 2)
            return messages;

        // Parse header to find column indices
        var header = lines[0];
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
            colMap[header[i].Trim()] = i;

        for (var row = 1; row < lines.Count; row++)
        {
            var fields = lines[row];
            if (fields.Length < 3) continue;

            var msg = new LinkedInMessage
            {
                ConversationId = GetField(fields, colMap, "CONVERSATION ID"),
                ConversationTitle = GetField(fields, colMap, "CONVERSATION TITLE"),
                From = GetField(fields, colMap, "FROM"),
                SenderProfileUrl = GetField(fields, colMap, "SENDER PROFILE URL"),
                Subject = GetField(fields, colMap, "SUBJECT"),
                Content = GetField(fields, colMap, "CONTENT")
            };

            var dateStr = GetField(fields, colMap, "DATE");
            if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date))
            {
                msg.Date = date;
            }

            messages.Add(msg);
        }

        return messages;
    }

    /// <summary>
    /// RFC 4180-compliant CSV parser handling quoted fields with commas and newlines.
    /// </summary>
    private static List<string[]> ParseCsvLines(string csv)
    {
        var result = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        while (i < csv.Length)
        {
            var ch = csv[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                    }
                    else
                    {
                        inQuotes = false;
                        i++;
                    }
                }
                else
                {
                    field.Append(ch);
                    i++;
                }
            }
            else
            {
                if (ch == '"')
                {
                    inQuotes = true;
                    i++;
                }
                else if (ch == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    i++;
                }
                else if (ch == '\n' || (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n'))
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    if (fields.Any(f => f.Length > 0))
                        result.Add(fields.ToArray());
                    fields = [];
                    i += ch == '\r' ? 2 : 1;
                }
                else
                {
                    field.Append(ch);
                    i++;
                }
            }
        }

        // Last field/row
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            if (fields.Any(f => f.Length > 0))
                result.Add(fields.ToArray());
        }

        return result;
    }

    private static string GetField(string[] fields, Dictionary<string, int> colMap, string columnName)
    {
        if (colMap.TryGetValue(columnName, out var idx) && idx < fields.Length)
            return fields[idx].Trim();
        return string.Empty;
    }

    private static bool IsRecruiterProposal(List<LinkedInMessage> thread)
    {
        var allContent = string.Join(" ",
            thread.Select(m => $"{m.Content} {m.Subject} {m.ConversationTitle}"));
        var lower = allContent.ToLowerInvariant();

        // Negative filter first
        if (NegativeKeywords.Count(k => lower.Contains(k)) >= 2)
            return false;

        var signals = 0;

        // Job keywords
        if (JobKeywords.Any(k => lower.Contains(k)))
            signals++;

        // Tech keywords
        if (TechKeywords.Count(k => lower.Contains(k)) >= 2)
            signals++;

        // Recruiter title in conversation title
        var titleLower = thread[0].ConversationTitle.ToLowerInvariant();
        if (RecruiterTitleKeywords.Any(k => titleLower.Contains(k)))
            signals++;

        // Message length heuristic — real proposals tend to be longer
        var firstMessage = thread.OrderBy(m => m.Date).First();
        if (firstMessage.Content.Length > 200)
            signals++;

        return signals >= MinSignals;
    }

    private static LinkedInProposal ExtractProposal(
        string conversationId, List<LinkedInMessage> messages, string sourceFile)
    {
        var firstMsg = messages.First();
        var lastMsg = messages.Last();
        var allContent = string.Join("\n---\n", messages.Select(m =>
            $"[{m.Date:yyyy-MM-dd}] {m.From}: {m.Content}"));
        var combinedText = string.Join(" ", messages.Select(m => m.Content));

        return new LinkedInProposal
        {
            ConversationId = conversationId,
            RecruiterName = firstMsg.From,
            RecruiterProfileUrl = firstMsg.SenderProfileUrl,
            Company = ExtractCompany(combinedText, firstMsg.ConversationTitle),
            JobTitle = ExtractJobTitle(combinedText),
            TechStack = ExtractTechStack(combinedText),
            RemotePolicy = DetectRemotePolicy(combinedText),
            Location = DetectLocation(combinedText),
            RelocationOffered = RelocationKeywords.Any(k =>
                combinedText.Contains(k, StringComparison.OrdinalIgnoreCase)),
            SalaryHint = ExtractSalaryHint(combinedText),
            MessageSummary = firstMsg.Content.Length > 300
                ? firstMsg.Content[..300] + "..."
                : firstMsg.Content,
            FullContent = allContent,
            ProposalDate = firstMsg.Date,
            LastMessageDate = messages.Count > 1 ? lastMsg.Date : null,
            MessageCount = messages.Count,
            Status = ProposalStatus.New,
            SourceFile = sourceFile
        };
    }

    private static string ExtractCompany(string content, string conversationTitle)
    {
        // Try regex patterns on content
        var match = CompanyPatterns.Match(content);
        if (match.Success)
        {
            var company = match.Groups[1].Value.Trim();
            if (company.Length >= 2 && company.Length <= 40)
                return company;
        }

        // Try conversation title — often contains "Name at Company"
        var atMatch = Regex.Match(conversationTitle, @"\bat\s+(.+?)$", RegexOptions.IgnoreCase);
        if (atMatch.Success)
            return atMatch.Groups[1].Value.Trim();

        return string.Empty;
    }

    private static string ExtractJobTitle(string content)
    {
        var match = JobTitlePatterns.Match(content);
        return match.Success ? match.Value.Trim() : string.Empty;
    }

    private static string ExtractTechStack(string content)
    {
        var found = KnownTechStack
            .Where(t => content.Contains(t, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
        return string.Join(", ", found);
    }

    private static string DetectRemotePolicy(string content)
    {
        var lower = content.ToLowerInvariant();
        if (RemoteKeywords.Any(k => lower.Contains(k)))
            return "Remote";
        if (HybridKeywords.Any(k => lower.Contains(k)))
            return "Hybrid";
        if (lower.Contains("on-site") || lower.Contains("onsite") || lower.Contains("in office"))
            return "On-site";
        return "Unknown";
    }

    private static string DetectLocation(string content)
    {
        var found = KnownLocations
            .Where(l => content.Contains(l, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        return string.Join(", ", found);
    }

    private static string ExtractSalaryHint(string content)
    {
        var match = SalaryPatterns.Match(content);
        return match.Success ? match.Value.Trim() : string.Empty;
    }

    private sealed class LinkedInMessage
    {
        public string ConversationId { get; set; } = string.Empty;
        public string ConversationTitle { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string SenderProfileUrl { get; set; } = string.Empty;
        public DateTimeOffset Date { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}

public sealed class ParseResult
{
    public string SourceFile { get; set; } = string.Empty;
    public int TotalMessages { get; set; }
    public int TotalConversations { get; set; }
    public List<LinkedInProposal> Proposals { get; set; } = [];
}
