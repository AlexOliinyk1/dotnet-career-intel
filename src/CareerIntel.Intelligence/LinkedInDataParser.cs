using System.Globalization;
using System.Text.RegularExpressions;
using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Parses LinkedIn data export CSV files to extract proposals, connections, and profile data.
/// </summary>
public sealed class LinkedInDataParser
{
    private readonly string _dataDir;
    private static readonly string[] SalaryPatterns =
    [
        @"\$\s?(\d[\d,\.]+)\s*[-–]\s*\$?\s?(\d[\d,\.]+)",           // $5,500 - $6,000
        @"(\d[\d,\.]+)\s*\$\s*[-–]\s*(\d[\d,\.]+)\s*\$",             // 5500$ - 6000$
        @"\$\s?(\d[\d,\.]+)\s*(gross|net|/hr|/hour|per hour)?",       // $6600 gross
        @"(\d[\d,\.]+)\s*\$\s*(gross|net|/hr|/hour|per hour)?",       // 6600$ gross
        @"€\s?(\d[\d,\.]+)\s*[-–]\s*€?\s?(\d[\d,\.]+)",              // €30-€35
        @"(\d[\d,\.]+)\s*[-–]\s*(\d[\d,\.]+)\s*\$/hour",             // 30-35$/hour
        @"від\s+(\d[\d,\.]+)\s*\$",                                   // від 5000$
        @"до\s+(\d[\d,\.]+)\s*\$",                                    // до 6000$
        @"budget[:\s]+\$?\s?(\d[\d,\.]+)",                             // budget: $6600
        @"бюджет[:\s]+(\d[\d,\.]+)",                                   // бюджет 5500
        @"(\d[\d,\.]+)\s*[-–]\s*(\d[\d,\.]+)\s*к",                    // 5.5k-6k
        @"в межах\s+(\d[\d,\.]+)\s*[-–]\s*(\d[\d,\.]+)\s*\$",         // в межах 5500-6000$
    ];

    private static readonly string[] ProposalKeywords =
    [
        // English
        "developer", "engineer", "architect", "vacancy", "position", "role",
        "hiring", "opportunity", "looking for", "job opportunity", "open role",
        "we're building", "tech lead", "senior", "full-stack", "backend", "frontend",
        "stack:", "requirements:", "key requirements", "what we offer",
        // Ukrainian
        "вакансія", "позиція", "шукаємо", "розробник", "інженер",
        "пропозиція", "відкрита позиція", "запрошую", "стек", "технології",
        "пишу вам з вакансією", "open to work", "рекрутер",
    ];

    private static readonly string[] TechKeywords =
    [
        "C#", ".NET", "ASP.NET", "React", "Angular", "TypeScript", "JavaScript",
        "Node.js", "Python", "Java", "Go", "Rust", "Ruby", "PHP", "Scala",
        "AWS", "Azure", "GCP", "Docker", "Kubernetes", "K8s",
        "PostgreSQL", "SQL Server", "MongoDB", "Redis", "Elasticsearch",
        "Kafka", "RabbitMQ", "gRPC", "GraphQL", "REST",
        "Microservices", "DDD", "CQRS", "Event-driven",
        "CI/CD", "Terraform", "GitHub Actions", "Azure DevOps",
        "Entity Framework", "EF Core", "Dapper", "SignalR", "WebSockets",
        "Identity Server", "OAuth", "JWT", "SAML",
        "xUnit", "NUnit", "TDD", "Clean Architecture", "SOLID",
        "SQS", "S3", "EC2", "Lambda", "CloudFront",
    ];

    private static readonly string[] RecruiterTitleKeywords =
    [
        "recruiter", "recruiting", "talent", "hr ", "human resources",
        "sourcing", "staffing", "headhunter", "people", "acquisition",
        "рекрутер", "hr manager", "hiring",
    ];

    public LinkedInDataParser(string dataDir)
    {
        _dataDir = dataDir;
    }

    /// <summary>
    /// Parses messages.csv and extracts recruiter proposals.
    /// </summary>
    public async Task<List<LinkedInProposal>> ParseProposalsAsync(string messagesPath)
    {
        if (!File.Exists(messagesPath))
            return [];

        var lines = await File.ReadAllLinesAsync(messagesPath);
        if (lines.Length < 2)
            return [];

        // Group messages by conversation
        var conversations = new Dictionary<string, List<MessageRow>>();
        foreach (var row in ParseCsvRows(lines.Skip(1)))
        {
            if (row.ConversationId == null) continue;
            if (!conversations.ContainsKey(row.ConversationId))
                conversations[row.ConversationId] = [];
            conversations[row.ConversationId].Add(row);
        }

        var proposals = new List<LinkedInProposal>();
        var id = 1;

        foreach (var (convId, messages) in conversations)
        {
            // Sort messages chronologically (oldest first)
            messages.Sort((a, b) => a.Date.CompareTo(b.Date));

            // Find the first inbound message (not from Oleksandr)
            var firstInbound = messages.FirstOrDefault(m =>
                !m.From.Contains("Oleksandr", StringComparison.OrdinalIgnoreCase));

            if (firstInbound == null) continue;

            // Check if this conversation looks like a recruiter proposal
            var allContent = string.Join(" ", messages.Select(m => m.Content));
            if (!IsProposalConversation(allContent, messages)) continue;

            // Extract structured data from the conversation
            var proposal = ExtractProposal(convId, messages, firstInbound, id++);
            proposals.Add(proposal);
        }

        return proposals.OrderByDescending(p => p.ProposalDate).ToList();
    }

    /// <summary>
    /// Parses Connections.csv and returns structured connection data.
    /// </summary>
    public async Task<List<LinkedInConnection>> ParseConnectionsAsync(string connectionsPath)
    {
        if (!File.Exists(connectionsPath))
            return [];

        var lines = await File.ReadAllLinesAsync(connectionsPath);
        var connections = new List<LinkedInConnection>();

        // Skip notes lines and header
        var dataStarted = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("First Name,Last Name"))
            {
                dataStarted = true;
                continue;
            }
            if (!dataStarted || string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseCsvLine(line);
            if (fields.Count < 7) continue;

            var conn = new LinkedInConnection
            {
                FirstName = fields[0],
                LastName = fields[1],
                ProfileUrl = fields[2],
                Email = fields[3],
                Company = fields[4],
                Position = fields[5],
                ConnectedOn = ParseDateFlexible(fields[6]),
                IsRecruiter = IsRecruiterPosition(fields[5]),
            };

            connections.Add(conn);
        }

        return connections;
    }

    /// <summary>
    /// Parses Profile.csv for profile analysis.
    /// </summary>
    public async Task<LinkedInProfile?> ParseProfileAsync(string profilePath)
    {
        if (!File.Exists(profilePath))
            return null;

        var lines = await File.ReadAllLinesAsync(profilePath);
        if (lines.Length < 2)
            return null;

        var fields = ParseCsvLine(lines[1]);
        if (fields.Count < 8)
            return null;

        return new LinkedInProfile
        {
            FirstName = fields[0],
            LastName = fields[1],
            Headline = fields.Count > 5 ? fields[5] : "",
            Summary = fields.Count > 6 ? fields[6] : "",
            Industry = fields.Count > 7 ? fields[7] : "",
            GeoLocation = fields.Count > 9 ? fields[9] : "",
        };
    }

    /// <summary>
    /// Parses Skills.csv for skills list.
    /// </summary>
    public async Task<List<string>> ParseSkillsAsync(string skillsPath)
    {
        if (!File.Exists(skillsPath))
            return [];

        var lines = await File.ReadAllLinesAsync(skillsPath);
        return lines.Skip(1)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
    }

    /// <summary>
    /// Parses Positions.csv for work history.
    /// </summary>
    public async Task<List<LinkedInPosition>> ParsePositionsAsync(string positionsPath)
    {
        if (!File.Exists(positionsPath))
            return [];

        var lines = await File.ReadAllLinesAsync(positionsPath);
        var positions = new List<LinkedInPosition>();

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ParseCsvLine(line);
            if (fields.Count < 4) continue;

            positions.Add(new LinkedInPosition
            {
                Company = fields[0],
                Title = fields[1],
                Description = fields.Count > 2 ? fields[2] : "",
                Location = fields.Count > 3 ? fields[3] : "",
                StartDate = fields.Count > 4 ? fields[4] : "",
                EndDate = fields.Count > 5 ? fields[5] : "",
            });
        }

        return positions;
    }

    /// <summary>
    /// Parses Company Follows.csv
    /// </summary>
    public async Task<List<LinkedInCompanyFollow>> ParseCompanyFollowsAsync(string path)
    {
        if (!File.Exists(path))
            return [];

        var lines = await File.ReadAllLinesAsync(path);
        return lines.Skip(1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l =>
            {
                var fields = ParseCsvLine(l);
                return new LinkedInCompanyFollow
                {
                    Organization = fields.Count > 0 ? fields[0] : "",
                    FollowedOn = fields.Count > 1 ? ParseDateFlexible(fields[1]) : null,
                };
            })
            .Where(f => !string.IsNullOrEmpty(f.Organization))
            .ToList();
    }

    /// <summary>
    /// Analyzes LinkedIn profile and generates improvement recommendations.
    /// </summary>
    public LinkedInProfileAnalysis AnalyzeProfile(
        LinkedInProfile? profile,
        List<string> skills,
        List<LinkedInPosition> positions,
        List<LinkedInConnection> connections,
        List<LinkedInProposal> proposals)
    {
        var analysis = new LinkedInProfileAnalysis();

        // Headline analysis
        if (profile != null)
        {
            analysis.HeadlineScore = ScoreHeadline(profile.Headline);
            analysis.HeadlineIssues = GetHeadlineIssues(profile.Headline);
            analysis.SummaryScore = ScoreSummary(profile.Summary);
            analysis.SummaryIssues = GetSummaryIssues(profile.Summary);
        }

        // Title progression analysis
        analysis.TitleIssues = GetTitleIssues(positions);

        // Skills analysis
        analysis.SkillsScore = ScoreSkills(skills);
        analysis.MissingHighValueSkills = GetMissingHighValueSkills(skills);

        // Network analysis
        analysis.TotalConnections = connections.Count;
        analysis.RecruiterConnections = connections.Count(c => c.IsRecruiter);
        analysis.RecruiterPercentage = connections.Count > 0
            ? (double)analysis.RecruiterConnections / connections.Count * 100
            : 0;

        // Proposal conversion analysis
        analysis.TotalProposals = proposals.Count;
        analysis.ProposalsWithSalary = proposals.Count(p => !string.IsNullOrEmpty(p.SalaryHint));
        analysis.AverageSalaryOffered = CalculateAverageSalary(proposals);

        // Generate recommendations
        analysis.Recommendations = GenerateRecommendations(profile, skills, positions, connections, proposals);

        return analysis;
    }

    /// <summary>
    /// Analyzes connections to recommend HR/recruiter targets at high-paying companies.
    /// </summary>
    public List<ConnectionTarget> GenerateConnectionTargets(
        List<LinkedInConnection> connections,
        List<LinkedInProposal> proposals)
    {
        var targets = new List<ConnectionTarget>();

        // High-paying companies from proposals
        var highPayCompanies = proposals
            .Where(p => !string.IsNullOrEmpty(p.SalaryHint))
            .Select(p => new { p.Company, Salary = ExtractNumericSalary(p.SalaryHint) })
            .Where(x => x.Salary > 0)
            .OrderByDescending(x => x.Salary)
            .ToList();

        // Known top-paying companies for .NET
        var topCompanies = new[]
        {
            "Microsoft", "Google", "Amazon", "Meta", "Apple", "Netflix", "Stripe",
            "Databricks", "Snowflake", "Confluent", "HashiCorp", "Elastic",
            "JetBrains", "GitLab", "GitHub", "Cloudflare", "Datadog",
            "MongoDB", "Redis Labs", "Cockroach Labs", "PlanetScale",
            "Toptal", "Turing", "Crossover", "Andela", "Terminal",
            "Canonical", "EPAM", "Luxoft", "GlobalLogic", "SoftServe",
            "DashDevs", "N-iX", "Intellias", "Sigma Software",
        };

        foreach (var company in topCompanies)
        {
            var existingConnections = connections
                .Where(c => c.Company?.Contains(company, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            var recruiterConnections = existingConnections.Where(c => c.IsRecruiter).ToList();
            var proposalFromCompany = proposals
                .FirstOrDefault(p => p.Company.Contains(company, StringComparison.OrdinalIgnoreCase));

            targets.Add(new ConnectionTarget
            {
                Company = company,
                ExistingConnections = existingConnections.Count,
                RecruiterConnections = recruiterConnections.Count,
                HasProposal = proposalFromCompany != null,
                SalaryHint = proposalFromCompany?.SalaryHint ?? "",
                Priority = CalculateTargetPriority(company, existingConnections.Count, recruiterConnections.Count, proposalFromCompany),
                Recommendation = GetConnectionRecommendation(company, recruiterConnections.Count, proposalFromCompany),
            });
        }

        return targets.OrderByDescending(t => t.Priority).ToList();
    }

    #region Private Helpers

    private bool IsProposalConversation(string allContent, List<MessageRow> messages)
    {
        var lowerContent = allContent.ToLowerInvariant();
        var matchCount = ProposalKeywords.Count(k => lowerContent.Contains(k.ToLowerInvariant()));

        // Need at least 2 keyword matches to be considered a proposal
        if (matchCount < 2) return false;

        // Must have at least one message from someone other than Oleksandr
        var hasInbound = messages.Any(m =>
            !m.From.Contains("Oleksandr", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(m.Content));

        return hasInbound;
    }

    private LinkedInProposal ExtractProposal(string convId, List<MessageRow> messages, MessageRow firstInbound, int id)
    {
        var allContent = string.Join("\n---\n", messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => $"[{m.From}] {m.Content}"));

        var recruiterMessages = messages
            .Where(m => !m.From.Contains("Oleksandr", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var recruiterContent = string.Join(" ", recruiterMessages.Select(m => m.Content));

        var company = ExtractCompany(messages, recruiterContent);
        var salary = ExtractSalary(allContent);
        var techStack = ExtractTechStack(allContent);
        var jobTitle = ExtractJobTitle(messages, recruiterContent);
        var remotePolicy = ExtractRemotePolicy(allContent);
        var status = DetermineStatus(messages);

        return new LinkedInProposal
        {
            Id = id,
            ConversationId = convId,
            RecruiterName = firstInbound.From,
            RecruiterProfileUrl = firstInbound.SenderProfileUrl,
            Company = company,
            JobTitle = jobTitle,
            TechStack = techStack,
            RemotePolicy = remotePolicy,
            SalaryHint = salary,
            MessageSummary = TruncateContent(recruiterMessages.FirstOrDefault()?.Content ?? "", 300),
            FullContent = allContent,
            ProposalDate = messages.Min(m => m.Date),
            LastMessageDate = messages.Max(m => m.Date),
            MessageCount = messages.Count,
            Status = status,
            SourceFile = "messages.csv",
        };
    }

    private static string ExtractCompany(List<MessageRow> messages, string recruiterContent)
    {
        // Try conversation title first (often contains company or role name)
        var title = messages.FirstOrDefault()?.ConversationTitle ?? "";
        if (!string.IsNullOrWhiteSpace(title) && title.Length > 3)
        {
            // Extract company from patterns like "[Company] Role" or "Company - Role"
            var bracketMatch = Regex.Match(title, @"\[(\w[\w\s]+)\]");
            if (bracketMatch.Success) return bracketMatch.Groups[1].Value;
        }

        // Look for company mentions in recruiter messages
        var companyPatterns = new[]
        {
            @"(?:at|@|company|компанії?|компанія)\s+(\w[\w\s\.&]+?)(?:\.|,|\s+(?:is|we|і|та|зараз))",
            @"(?:from|з)\s+(\w[\w\s\.&]+?)(?:\.|,|\s+because|because|бо|тому)",
            @"([\w\s\.]+?)\s+(?:is hiring|is looking|шукає|набирає)",
        };

        foreach (var pattern in companyPatterns)
        {
            var match = Regex.Match(recruiterContent, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var company = match.Groups[1].Value.Trim();
                if (company.Length > 2 && company.Length < 50)
                    return company;
            }
        }

        // Try to extract from URL mentions
        var urlMatch = Regex.Match(recruiterContent, @"https?://(?:www\.)?([a-z0-9-]+)\.\w+/(?:careers?|jobs)", RegexOptions.IgnoreCase);
        if (urlMatch.Success)
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(urlMatch.Groups[1].Value.Replace("-", " "));

        // Fall back to conversation title or recruiter name
        if (!string.IsNullOrWhiteSpace(title))
            return title;

        return "Unknown";
    }

    private static string ExtractSalary(string content)
    {
        foreach (var pattern in SalaryPatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // Get surrounding context (up to 40 chars)
                var start = Math.Max(0, match.Index - 20);
                var end = Math.Min(content.Length, match.Index + match.Length + 20);
                var context = content[start..end].Replace("\n", " ").Trim();
                return context;
            }
        }

        return "";
    }

    private static string ExtractTechStack(string content)
    {
        var found = TechKeywords
            .Where(k => content.Contains(k, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Take(12)
            .ToList();

        return string.Join(", ", found);
    }

    private static string ExtractJobTitle(List<MessageRow> messages, string recruiterContent)
    {
        // Check conversation subject/title first
        var subject = messages.FirstOrDefault()?.Subject ?? "";
        if (!string.IsNullOrWhiteSpace(subject))
        {
            var title = messages.FirstOrDefault()?.ConversationTitle ?? "";
            if (!string.IsNullOrWhiteSpace(title) && title.Length > 3)
                return title;
        }

        // Look for job title patterns
        var titlePatterns = new[]
        {
            @"(?:looking for|hiring|position|role|вакансія|позиція)\s*[:\-]?\s*(.{10,60}?)(?:\.|$|\n)",
            @"((?:Senior|Staff|Principal|Lead|Junior|Middle)\s+[\w\s/.#]+(?:Developer|Engineer|Architect))",
            @"((?:Tech|Team)\s+Lead\s*[\w\s/.]*)",
            @"(Solution\s+Architect[\w\s]*)",
        };

        foreach (var pattern in titlePatterns)
        {
            var match = Regex.Match(recruiterContent, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var title = match.Groups[1].Value.Trim();
                if (title.Length > 5 && title.Length < 80)
                    return title;
            }
        }

        // Fallback to conversation title
        var convTitle = messages.FirstOrDefault()?.ConversationTitle ?? "";
        return !string.IsNullOrWhiteSpace(convTitle) ? convTitle : "Software Engineer";
    }

    private static string ExtractRemotePolicy(string content)
    {
        var lower = content.ToLowerInvariant();
        if (lower.Contains("fully remote") || lower.Contains("100% remote") || lower.Contains("full-time remote"))
            return "Remote";
        if (lower.Contains("remote") || lower.Contains("віддалено") || lower.Contains("remotely"))
            return "Remote";
        if (lower.Contains("hybrid") || lower.Contains("гібридний"))
            return "Hybrid";
        if (lower.Contains("on-site") || lower.Contains("onsite") || lower.Contains("office") || lower.Contains("офіс"))
            return "On-site";
        return "Unknown";
    }

    private static ProposalStatus DetermineStatus(List<MessageRow> messages)
    {
        var lastMessage = messages.MaxBy(m => m.Date);
        var hasReply = messages.Any(m => m.From.Contains("Oleksandr", StringComparison.OrdinalIgnoreCase));
        var allContent = string.Join(" ", messages.Select(m => m.Content)).ToLowerInvariant();

        if (allContent.Contains("interview") || allContent.Contains("інтервʼю") || allContent.Contains("інтерв'ю")
            || allContent.Contains("технічне") || allContent.Contains("technical"))
            return ProposalStatus.Interviewing;

        if (hasReply)
            return ProposalStatus.Replied;

        return ProposalStatus.New;
    }

    private static int ScoreHeadline(string headline)
    {
        if (string.IsNullOrWhiteSpace(headline)) return 0;

        var score = 20; // base
        var lower = headline.ToLowerInvariant();

        if (lower.Contains("staff") || lower.Contains("principal") || lower.Contains("architect"))
            score += 20;
        else if (lower.Contains("senior") || lower.Contains("tech lead") || lower.Contains("lead"))
            score += 15;

        if (lower.Contains("cloud") || lower.Contains("azure") || lower.Contains("aws"))
            score += 10;
        if (lower.Contains("microservices") || lower.Contains("distributed"))
            score += 10;
        if (lower.Contains("full-stack") || lower.Contains("fullstack"))
            score += 5;
        if (headline.Contains("|") || headline.Contains("•"))
            score += 5; // structured format
        if (lower.Contains("remote"))
            score += 5;

        // Penalty for .NET-only branding
        if (lower.Contains(".net") && !lower.Contains("full-stack") && !lower.Contains("react"))
            score -= 10;

        return Math.Min(100, Math.Max(0, score));
    }

    private static List<string> GetHeadlineIssues(string headline)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(headline))
        {
            issues.Add("Missing headline - this is the most visible part of your profile");
            return issues;
        }

        var lower = headline.ToLowerInvariant();
        if (lower.Contains(".net software engineer") || lower.Contains(".net developer"))
            issues.Add("'.NET Developer' title anchors salary to $80K-$150K. Use 'Staff Software Engineer' or 'Solutions Architect' instead");
        if (!lower.Contains("staff") && !lower.Contains("principal") && !lower.Contains("architect"))
            issues.Add("Missing high-value title keywords (Staff, Principal, Architect) that attract $200K+ roles");
        if (headline.Length > 120)
            issues.Add("Headline is too long - LinkedIn truncates after ~120 chars on mobile");
        if (!lower.Contains("remote"))
            issues.Add("Consider adding 'Remote' to attract global opportunities");
        if (lower.Contains("open to work"))
            issues.Add("'Open to Work' can signal desperation - use LinkedIn's green badge feature instead");

        return issues;
    }

    private static int ScoreSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return 0;

        var score = 20;
        var lower = summary.ToLowerInvariant();

        if (summary.Length > 200) score += 10;
        if (summary.Length > 500) score += 10;
        if (lower.Contains("microservices") || lower.Contains("distributed")) score += 10;
        if (lower.Contains("cloud") || lower.Contains("azure") || lower.Contains("aws")) score += 10;
        if (lower.Contains("team") || lower.Contains("led") || lower.Contains("leadership")) score += 10;
        if (summary.Contains("•") || summary.Contains("✓") || summary.Contains("✔")) score += 5;
        if (Regex.IsMatch(summary, @"\d+\+?\s*years?")) score += 5;
        if (lower.Contains("scale") || lower.Contains("million") || lower.Contains("enterprise")) score += 10;

        return Math.Min(100, Math.Max(0, score));
    }

    private static List<string> GetSummaryIssues(string summary)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(summary))
        {
            issues.Add("Missing summary - add 3-5 paragraphs showcasing your impact");
            return issues;
        }

        var lower = summary.ToLowerInvariant();
        if (summary.Length < 300)
            issues.Add("Summary is too short - aim for 500+ characters with concrete achievements");
        if (!Regex.IsMatch(summary, @"\d+[KkMm%]|\d+\s*(users|requests|transactions|services)"))
            issues.Add("Add concrete metrics (e.g., '40K+ documents/day', '10x performance improvement')");
        if (!lower.Contains("led") && !lower.Contains("architected") && !lower.Contains("spearheaded"))
            issues.Add("Use leadership verbs: 'Architected', 'Led', 'Designed' instead of 'Worked on', 'Helped'");
        if (!lower.Contains("remote"))
            issues.Add("Mention remote work experience to attract global opportunities");

        return issues;
    }

    private static List<string> GetTitleIssues(List<LinkedInPosition> positions)
    {
        var issues = new List<string>();

        var allSameTitle = positions.All(p =>
            p.Title.Contains(".Net Software Engineer", StringComparison.OrdinalIgnoreCase));

        if (allSameTitle && positions.Count > 1)
            issues.Add("CRITICAL: All positions show '.Net Software Engineer' - no career progression visible. Update recent roles to 'Senior Software Engineer', 'Tech Lead', or 'Staff Engineer'");

        if (positions.Any(p => p.Title.Contains(".Net", StringComparison.OrdinalIgnoreCase)))
            issues.Add("Technology-specific titles (.NET) limit your market. Use generic titles like 'Software Engineer' or 'Solutions Architect'");

        if (!positions.Any(p =>
            p.Title.Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
            p.Title.Contains("Senior", StringComparison.OrdinalIgnoreCase) ||
            p.Title.Contains("Staff", StringComparison.OrdinalIgnoreCase) ||
            p.Title.Contains("Principal", StringComparison.OrdinalIgnoreCase) ||
            p.Title.Contains("Architect", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("No leadership or senior titles visible - recruiters scanning profiles will skip you for senior roles");
        }

        return issues;
    }

    private static int ScoreSkills(List<string> skills)
    {
        if (skills.Count == 0) return 0;
        var score = Math.Min(40, skills.Count); // up to 40 for having skills

        var highValue = new[] { "Microservices", "Kubernetes", "Docker", "AWS", "Azure", "React", "TypeScript", "GraphQL", "gRPC" };
        score += highValue.Count(hv => skills.Any(s => s.Contains(hv, StringComparison.OrdinalIgnoreCase))) * 5;

        return Math.Min(100, score);
    }

    private static List<string> GetMissingHighValueSkills(List<string> skills)
    {
        var highValue = new[]
        {
            "System Design", "Software Architecture", "Technical Leadership",
            "Distributed Systems", "Event-Driven Architecture", "Domain-Driven Design",
            "Cloud Architecture", "Platform Engineering", "Site Reliability Engineering",
            "Performance Optimization", "Scalability", "High Availability",
        };

        return highValue
            .Where(hv => !skills.Any(s => s.Contains(hv, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static double CalculateAverageSalary(List<LinkedInProposal> proposals)
    {
        var salaries = proposals
            .Where(p => !string.IsNullOrEmpty(p.SalaryHint))
            .Select(p => ExtractNumericSalary(p.SalaryHint))
            .Where(s => s > 0)
            .ToList();

        return salaries.Count > 0 ? salaries.Average() : 0;
    }

    private static double ExtractNumericSalary(string salaryHint)
    {
        var match = Regex.Match(salaryHint, @"(\d[\d,\.]+)");
        if (match.Success && double.TryParse(match.Groups[1].Value.Replace(",", ""), out var value))
            return value;
        return 0;
    }

    private static List<ProfileRecommendation> GenerateRecommendations(
        LinkedInProfile? profile,
        List<string> skills,
        List<LinkedInPosition> positions,
        List<LinkedInConnection> connections,
        List<LinkedInProposal> proposals)
    {
        var recs = new List<ProfileRecommendation>();

        // Headline recommendations
        if (profile != null)
        {
            var lower = profile.Headline.ToLowerInvariant();
            if (lower.Contains(".net developer") || lower.Contains(".net software engineer"))
            {
                recs.Add(new ProfileRecommendation
                {
                    Category = "Headline",
                    Priority = "Critical",
                    Title = "Rebrand your headline for $40K-$80K salary uplift",
                    CurrentState = profile.Headline,
                    RecommendedState = "Staff Software Engineer | Cloud & Distributed Systems | C#, .NET, React | Architecting Scalable Platforms",
                    Impact = "+$40K-$80K salary range shift",
                });
            }
        }

        // Title progression
        var allSameTitle = positions.All(p => p.Title.Contains(".Net Software Engineer", StringComparison.OrdinalIgnoreCase));
        if (allSameTitle && positions.Count > 1)
        {
            recs.Add(new ProfileRecommendation
            {
                Category = "Experience Titles",
                Priority = "Critical",
                Title = "Update job titles to show career progression",
                CurrentState = "All positions: '.Net Software Engineer'",
                RecommendedState = "Latest: 'Senior Software Engineer / Tech Lead' → Previous: 'Senior Software Engineer' → Earlier: 'Software Engineer'",
                Impact = "Recruiters filter by title - current titles block you from Staff/Lead searches",
            });
        }

        // Skills gaps
        var missingSkills = GetMissingHighValueSkills(skills);
        if (missingSkills.Count > 3)
        {
            recs.Add(new ProfileRecommendation
            {
                Category = "Skills",
                Priority = "High",
                Title = "Add high-value skills that attract $200K+ roles",
                CurrentState = $"{skills.Count} skills listed, missing architectural keywords",
                RecommendedState = $"Add: {string.Join(", ", missingSkills.Take(6))}",
                Impact = "These skills appear in 70%+ of Staff/Principal job descriptions",
            });
        }

        // Network analysis
        var recruiterPct = connections.Count > 0
            ? (double)connections.Count(c => c.IsRecruiter) / connections.Count * 100
            : 0;

        if (recruiterPct < 15)
        {
            recs.Add(new ProfileRecommendation
            {
                Category = "Network",
                Priority = "High",
                Title = "Increase recruiter connections for more inbound proposals",
                CurrentState = $"{recruiterPct:F0}% of connections are recruiters ({connections.Count(c => c.IsRecruiter)} of {connections.Count})",
                RecommendedState = "Target 20-30% recruiter connections. Connect with recruiters at FAANG, fintech, and top remote companies",
                Impact = "More recruiter connections = more inbound proposals with salary info",
            });
        }

        // Content activity
        recs.Add(new ProfileRecommendation
        {
            Category = "Content",
            Priority = "Medium",
            Title = "Start posting technical content for profile visibility",
            CurrentState = "No visible posting activity",
            RecommendedState = "Post 2-3 technical articles/month about .NET architecture, cloud patterns, or system design",
            Impact = "Active profiles get 3-5x more recruiter views",
        });

        return recs;
    }

    private static int CalculateTargetPriority(string company, int existingCount, int recruiterCount, LinkedInProposal? proposal)
    {
        var priority = 50;
        if (proposal != null) priority += 20;
        if (recruiterCount == 0) priority += 15; // Need to connect
        if (existingCount == 0) priority += 10;

        var topTier = new[] { "Microsoft", "Google", "Amazon", "Meta", "Stripe", "Netflix" };
        if (topTier.Any(t => company.Contains(t, StringComparison.OrdinalIgnoreCase)))
            priority += 15;

        return priority;
    }

    private static string GetConnectionRecommendation(string company, int recruiterCount, LinkedInProposal? proposal)
    {
        if (recruiterCount > 0 && proposal != null)
            return "Already connected - follow up on existing proposal";
        if (recruiterCount > 0)
            return "Already have recruiter connection - send a message about open roles";
        if (proposal != null)
            return "Have proposal but no recruiter connection - connect with their HR team";
        return "No connections yet - search and connect with recruiters/HR at this company";
    }

    private static bool IsRecruiterPosition(string position)
    {
        if (string.IsNullOrWhiteSpace(position)) return false;
        var lower = position.ToLowerInvariant();
        return RecruiterTitleKeywords.Any(k => lower.Contains(k));
    }

    private static DateTimeOffset? ParseDateFlexible(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;

        // Try various formats
        var formats = new[]
        {
            "dd MMM yyyy", "MMM dd yyyy", "d MMM yyyy",
            "yyyy-MM-dd HH:mm:ss 'UTC'",
            "M/d/yy, h:mm tt",
            "ddd MMM dd HH:mm:ss 'UTC' yyyy",
        };

        foreach (var fmt in formats)
        {
            if (DateTimeOffset.TryParseExact(dateStr.Trim(), fmt, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var result))
                return result;
        }

        if (DateTimeOffset.TryParse(dateStr.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var fallback))
            return fallback;

        return null;
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;
        return content[..maxLength] + "...";
    }

    #endregion

    #region CSV Parsing

    private IEnumerable<MessageRow> ParseCsvRows(IEnumerable<string> lines)
    {
        var buffer = new List<string>();

        foreach (var line in lines)
        {
            buffer.Add(line);

            // Check if we have a complete CSV row (balanced quotes)
            var combined = string.Join("\n", buffer);
            if (IsBalancedCsvRow(combined))
            {
                var fields = ParseCsvLine(combined);
                if (fields.Count >= 10)
                {
                    yield return new MessageRow
                    {
                        ConversationId = fields[0],
                        ConversationTitle = fields[1],
                        From = fields[2],
                        SenderProfileUrl = fields[3],
                        To = fields[4],
                        RecipientProfileUrls = fields[5],
                        Date = DateTimeOffset.TryParse(fields[6], out var d) ? d : DateTimeOffset.MinValue,
                        Subject = fields[7],
                        Content = fields[8],
                        Folder = fields[9],
                        Attachments = fields.Count > 10 ? fields[10] : "",
                    };
                }
                buffer.Clear();
            }
        }
    }

    private static bool IsBalancedCsvRow(string text)
    {
        var inQuotes = false;
        foreach (var ch in text)
        {
            if (ch == '"') inQuotes = !inQuotes;
        }
        return !inQuotes;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    #endregion

    private sealed class MessageRow
    {
        public string ConversationId { get; init; } = "";
        public string ConversationTitle { get; init; } = "";
        public string From { get; init; } = "";
        public string SenderProfileUrl { get; init; } = "";
        public string To { get; init; } = "";
        public string RecipientProfileUrls { get; init; } = "";
        public DateTimeOffset Date { get; init; }
        public string Subject { get; init; } = "";
        public string Content { get; init; } = "";
        public string Folder { get; init; } = "";
        public string Attachments { get; init; } = "";
    }
}

#region Supporting Models

public sealed class LinkedInConnection
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string ProfileUrl { get; set; } = "";
    public string Email { get; set; } = "";
    public string Company { get; set; } = "";
    public string Position { get; set; } = "";
    public DateTimeOffset? ConnectedOn { get; set; }
    public bool IsRecruiter { get; set; }
    public string FullName => $"{FirstName} {LastName}".Trim();
}

public sealed class LinkedInProfile
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Industry { get; set; } = "";
    public string GeoLocation { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}".Trim();
}

public sealed class LinkedInPosition
{
    public string Company { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Location { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
}

public sealed class LinkedInCompanyFollow
{
    public string Organization { get; set; } = "";
    public DateTimeOffset? FollowedOn { get; set; }
}

public sealed class LinkedInProfileAnalysis
{
    public int HeadlineScore { get; set; }
    public List<string> HeadlineIssues { get; set; } = [];
    public int SummaryScore { get; set; }
    public List<string> SummaryIssues { get; set; } = [];
    public int SkillsScore { get; set; }
    public List<string> MissingHighValueSkills { get; set; } = [];
    public List<string> TitleIssues { get; set; } = [];
    public int TotalConnections { get; set; }
    public int RecruiterConnections { get; set; }
    public double RecruiterPercentage { get; set; }
    public int TotalProposals { get; set; }
    public int ProposalsWithSalary { get; set; }
    public double AverageSalaryOffered { get; set; }
    public List<ProfileRecommendation> Recommendations { get; set; } = [];
}

public sealed class ProfileRecommendation
{
    public string Category { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Title { get; set; } = "";
    public string CurrentState { get; set; } = "";
    public string RecommendedState { get; set; } = "";
    public string Impact { get; set; } = "";
}

public sealed class ConnectionTarget
{
    public string Company { get; set; } = "";
    public int ExistingConnections { get; set; }
    public int RecruiterConnections { get; set; }
    public bool HasProposal { get; set; }
    public string SalaryHint { get; set; } = "";
    public int Priority { get; set; }
    public string Recommendation { get; set; } = "";
}

#endregion
