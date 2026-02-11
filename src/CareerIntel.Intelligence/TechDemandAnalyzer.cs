using System.Text.RegularExpressions;
using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Analyzes how often each technology appears across vacancies.
/// Produces ranked tech demand rates with percentages and trend indicators.
/// </summary>
public static class TechDemandAnalyzer
{
    // Comprehensive tech keywords grouped by category
    private static readonly Dictionary<string, string[]> TechByCategory = new()
    {
        ["Languages"] = ["C#", ".NET", "Java", "Python", "Go", "Rust", "TypeScript", "JavaScript", "Kotlin", "Swift", "Ruby", "Scala", "F#", "PHP"],
        ["Frameworks"] = ["ASP.NET", "ASP.NET Core", "Entity Framework", "EF Core", "Blazor", "MAUI", "WPF", "Spring Boot", "Django", "FastAPI", "Express", "NestJS", "Next.js", "React", "Angular", "Vue.js", "Svelte"],
        ["Cloud"] = ["Azure", "AWS", "GCP", "Google Cloud", "Terraform", "Pulumi", "CloudFormation"],
        ["Databases"] = ["SQL Server", "PostgreSQL", "MySQL", "MongoDB", "Redis", "Elasticsearch", "CosmosDB", "DynamoDB", "Cassandra", "Neo4j"],
        ["Messaging"] = ["RabbitMQ", "Kafka", "Azure Service Bus", "SQS", "MassTransit", "NServiceBus"],
        ["DevOps"] = ["Docker", "Kubernetes", "K8s", "CI/CD", "GitHub Actions", "Azure DevOps", "Jenkins", "GitLab CI", "ArgoCD", "Helm"],
        ["Architecture"] = ["Microservices", "Event Sourcing", "CQRS", "DDD", "gRPC", "GraphQL", "REST", "SignalR", "WebSockets"],
        ["Testing"] = ["xUnit", "NUnit", "MSTest", "Selenium", "Playwright", "Jest", "Cypress"],
        ["Practices"] = ["Agile", "Scrum", "Kanban", "TDD", "BDD", "Clean Architecture", "SOLID"],
        ["AI/ML"] = ["OpenAI", "LLM", "Machine Learning", "AI", "ChatGPT", "Copilot", "Semantic Kernel"]
    };

    /// <summary>
    /// Analyze technology demand across all vacancies.
    /// Returns technologies ranked by how often they appear (percentage of vacancies).
    /// </summary>
    public static TechDemandReport Analyze(IReadOnlyList<JobVacancy> vacancies)
    {
        if (vacancies.Count == 0)
            return new TechDemandReport();

        var techCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var techCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Build reverse lookup: tech -> category
        foreach (var (category, techs) in TechByCategory)
        {
            foreach (var tech in techs)
            {
                techCategories[tech] = category;
                techCounts[tech] = 0;
            }
        }

        // Count occurrences
        foreach (var vacancy in vacancies)
        {
            var searchText = $"{vacancy.Title} {vacancy.Description} {string.Join(" ", vacancy.RequiredSkills ?? [])} {string.Join(" ", vacancy.PreferredSkills ?? [])}";

            foreach (var tech in techCounts.Keys.ToList())
            {
                var escaped = Regex.Escape(tech);
                if (Regex.IsMatch(searchText, $@"(?<![A-Za-z]){escaped}(?![A-Za-z])", RegexOptions.IgnoreCase))
                {
                    techCounts[tech]++;
                }
            }
        }

        // Build ranked results
        var total = vacancies.Count;
        var items = techCounts
            .Where(kv => kv.Value > 0)
            .Select(kv => new TechDemandItem
            {
                Technology = kv.Key,
                Category = techCategories.GetValueOrDefault(kv.Key, "Other"),
                Count = kv.Value,
                Percentage = Math.Round((double)kv.Value / total * 100, 1),
            })
            .OrderByDescending(t => t.Count)
            .ToList();

        // Category summary
        var categoryStats = items
            .GroupBy(i => i.Category)
            .Select(g => new CategoryDemand
            {
                Category = g.Key,
                TotalMentions = g.Sum(i => i.Count),
                TopTech = g.OrderByDescending(i => i.Count).First().Technology,
                TechCount = g.Count()
            })
            .OrderByDescending(c => c.TotalMentions)
            .ToList();

        return new TechDemandReport
        {
            TotalVacancies = total,
            Items = items,
            ByCategory = categoryStats,
            AnalyzedAt = DateTimeOffset.UtcNow
        };
    }
}

public sealed class TechDemandReport
{
    public int TotalVacancies { get; set; }
    public List<TechDemandItem> Items { get; set; } = [];
    public List<CategoryDemand> ByCategory { get; set; } = [];
    public DateTimeOffset AnalyzedAt { get; set; }
}

public sealed class TechDemandItem
{
    public string Technology { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

public sealed class CategoryDemand
{
    public string Category { get; set; } = string.Empty;
    public int TotalMentions { get; set; }
    public string TopTech { get; set; } = string.Empty;
    public int TechCount { get; set; }
}
