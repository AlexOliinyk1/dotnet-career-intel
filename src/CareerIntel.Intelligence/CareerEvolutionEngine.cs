using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Analyzes job market trends to provide career evolution intelligence:
/// skill demand trends, AI automation risk assessment, and personalized roadmaps.
/// </summary>
public sealed class CareerEvolutionEngine
{
    /// <summary>
    /// Skills categorized by AI automation risk level.
    /// Based on current AI capabilities and near-term trajectory.
    /// </summary>
    private static readonly Dictionary<string, string[]> AiRiskCategories = new()
    {
        ["AI-Proof (Architecture & Leadership)"] =
        [
            "System Design", "Architecture", "Microservices", "Domain-Driven Design", "DDD",
            "Event Sourcing", "CQRS", "Solution Architecture", "Enterprise Architecture",
            "Technical Leadership", "Team Lead", "Staff Engineer", "Principal Engineer",
            "Stakeholder Management", "Cross-functional", "Mentoring"
        ],
        ["AI-Proof (Security & Compliance)"] =
        [
            "Security", "OWASP", "Penetration Testing", "OAuth", "OIDC", "Zero Trust",
            "GDPR", "SOC2", "HIPAA", "Compliance", "Threat Modeling", "IAM",
            "Encryption", "Key Management", "Audit"
        ],
        ["AI-Proof (Platform & Infrastructure)"] =
        [
            "Kubernetes", "Docker", "Terraform", "Pulumi", "Platform Engineering",
            "Site Reliability", "SRE", "Observability", "Prometheus", "Grafana",
            "Service Mesh", "Istio", "Infrastructure as Code", "IaC", "GitOps", "ArgoCD"
        ],
        ["AI-Amplified (Senior Dev Skills)"] =
        [
            "C#", ".NET", "ASP.NET Core", "Entity Framework", "Java", "Spring Boot",
            "Python", "Go", "Rust", "TypeScript", "Node.js",
            "PostgreSQL", "SQL Server", "MongoDB", "Redis", "Elasticsearch",
            "Azure", "AWS", "GCP", "Cloud", "Serverless",
            "gRPC", "GraphQL", "REST API", "WebSocket", "SignalR",
            "Performance Optimization", "Profiling", "Benchmarking"
        ],
        ["AI-Assisted (Use AI to do faster)"] =
        [
            "Unit Testing", "Integration Testing", "xUnit", "NUnit", "Jest",
            "CI/CD", "GitHub Actions", "Azure DevOps", "Jenkins",
            "React", "Angular", "Vue", "Blazor", "MAUI",
            "RabbitMQ", "Kafka", "Azure Service Bus", "MassTransit",
            "Dapper", "LINQ", "EF Core Migrations"
        ],
        ["AI-Threatened (Automate away)"] =
        [
            "CRUD", "Boilerplate", "Manual Testing", "Basic Scripting",
            "Documentation Writing", "Code Review (style)", "Simple Bug Fixes",
            "Data Entry", "Report Generation", "Basic SQL Queries",
            "Copy-Paste Integration", "Config Management"
        ],
        ["AI/ML (The New Gold)"] =
        [
            "Machine Learning", "ML.NET", "AI", "LLM", "GPT", "Claude",
            "Semantic Kernel", "RAG", "Vector Database", "Embeddings",
            "Prompt Engineering", "AI Agents", "AutoGen", "LangChain",
            "Computer Vision", "NLP", "TensorFlow", "PyTorch", "ONNX",
            "Azure AI", "Azure OpenAI", "Cognitive Services"
        ]
    };

    /// <summary>
    /// Career growth directions with descriptions and key skills.
    /// </summary>
    public static readonly List<CareerDirection> Directions =
    [
        new()
        {
            Name = "AI/ML Integration Engineer",
            Description = "Build AI-powered features into existing systems. The hottest direction for 2025-2027.",
            KeySkills = ["Semantic Kernel", "RAG", "Vector Database", "Azure OpenAI", "Prompt Engineering", "AI Agents"],
            SalaryMultiplier = 1.4,
            MarketGrowthRate = 85,
            AiResilienceScore = 95,
            Icon = "brain"
        },
        new()
        {
            Name = "Cloud/Platform Engineer",
            Description = "Design and run production infrastructure. Companies always need someone who keeps things alive.",
            KeySkills = ["Kubernetes", "Terraform", "Azure", "AWS", "Observability", "GitOps"],
            SalaryMultiplier = 1.3,
            MarketGrowthRate = 45,
            AiResilienceScore = 90,
            Icon = "cloud"
        },
        new()
        {
            Name = "Solution Architect",
            Description = "Design systems at scale. AI writes code — architects decide WHAT to build and HOW it fits together.",
            KeySkills = ["System Design", "Microservices", "DDD", "Event Sourcing", "Architecture Patterns"],
            SalaryMultiplier = 1.5,
            MarketGrowthRate = 35,
            AiResilienceScore = 95,
            Icon = "architecture"
        },
        new()
        {
            Name = "Security Engineer",
            Description = "Always in demand, hard to automate. Every breach = more budget for security.",
            KeySkills = ["OWASP", "Threat Modeling", "Zero Trust", "OAuth/OIDC", "Penetration Testing"],
            SalaryMultiplier = 1.35,
            MarketGrowthRate = 40,
            AiResilienceScore = 92,
            Icon = "shield"
        },
        new()
        {
            Name = "Staff/Principal Engineer",
            Description = "Technical leadership without management. Influence across teams, set technical direction.",
            KeySkills = ["Technical Leadership", "Architecture", "Cross-functional", "Mentoring", "System Design"],
            SalaryMultiplier = 1.6,
            MarketGrowthRate = 25,
            AiResilienceScore = 97,
            Icon = "star"
        },
        new()
        {
            Name = "Full-Stack Senior (Status Quo)",
            Description = "Stay as senior full-stack but add AI tools to your workflow. Viable but salary ceiling is lower.",
            KeySkills = ["C#", "ASP.NET Core", "React/Angular", "SQL", "Azure", "Docker"],
            SalaryMultiplier = 1.0,
            MarketGrowthRate = -5,
            AiResilienceScore = 55,
            Icon = "code"
        }
    ];

    /// <summary>
    /// Analyze market trends from vacancy data — which skills are hot, which are cooling.
    /// </summary>
    public MarketPulseReport AnalyzeMarketPulse(IReadOnlyList<JobVacancy> vacancies)
    {
        if (vacancies.Count == 0)
            return new MarketPulseReport();

        var allSkills = vacancies
            .SelectMany(v => v.RequiredSkills.Concat(v.PreferredSkills))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        // Overall skill demand
        var skillCounts = allSkills
            .GroupBy(s => NormalizeSkill(s), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count())
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Recent vs older vacancies (trend detection)
        var midpoint = vacancies
            .Select(v => v.PostedDate)
            .OrderBy(d => d)
            .ElementAt(vacancies.Count / 2);

        var olderVacancies = vacancies.Where(v => v.PostedDate < midpoint).ToList();
        var newerVacancies = vacancies.Where(v => v.PostedDate >= midpoint).ToList();

        var olderSkills = CountSkills(olderVacancies);
        var newerSkills = CountSkills(newerVacancies);

        var trends = new List<SkillTrend>();
        var allSkillNames = olderSkills.Keys.Union(newerSkills.Keys).ToHashSet();

        foreach (var skill in allSkillNames)
        {
            var olderPct = olderVacancies.Count > 0
                ? (olderSkills.GetValueOrDefault(skill, 0) * 100.0 / olderVacancies.Count)
                : 0;
            var newerPct = newerVacancies.Count > 0
                ? (newerSkills.GetValueOrDefault(skill, 0) * 100.0 / newerVacancies.Count)
                : 0;

            var change = newerPct - olderPct;
            var totalCount = skillCounts.GetValueOrDefault(skill, 0);

            if (totalCount >= 2) // Only include skills with meaningful data
            {
                trends.Add(new SkillTrend
                {
                    SkillName = skill,
                    TotalMentions = totalCount,
                    DemandPercent = vacancies.Count > 0 ? totalCount * 100.0 / vacancies.Count : 0,
                    TrendChange = change,
                    Direction = change > 2 ? "Rising" : change < -2 ? "Declining" : "Stable"
                });
            }
        }

        // Highest-paying skill combos
        var salaryBySkill = new Dictionary<string, List<decimal>>();
        foreach (var v in vacancies.Where(v => v.SalaryMin.HasValue))
        {
            var salary = v.SalaryMax.HasValue
                ? (v.SalaryMin!.Value + v.SalaryMax.Value) / 2
                : v.SalaryMin!.Value;

            foreach (var skill in v.RequiredSkills.Concat(v.PreferredSkills))
            {
                var normalized = NormalizeSkill(skill);
                if (!salaryBySkill.ContainsKey(normalized))
                    salaryBySkill[normalized] = [];
                salaryBySkill[normalized].Add(salary);
            }
        }

        var topPayingSkills = salaryBySkill
            .Where(kv => kv.Value.Count >= 2)
            .Select(kv => new SkillSalary
            {
                SkillName = kv.Key,
                AverageSalary = kv.Value.Average(),
                DataPoints = kv.Value.Count
            })
            .OrderByDescending(s => s.AverageSalary)
            .Take(20)
            .ToList();

        return new MarketPulseReport
        {
            TotalVacancies = vacancies.Count,
            SkillDemand = skillCounts,
            Trends = trends.OrderByDescending(t => t.TotalMentions).ToList(),
            RisingSkills = trends.Where(t => t.Direction == "Rising").OrderByDescending(t => t.TrendChange).Take(15).ToList(),
            DecliningSkills = trends.Where(t => t.Direction == "Declining").OrderBy(t => t.TrendChange).Take(10).ToList(),
            TopPayingSkills = topPayingSkills
        };
    }

    /// <summary>
    /// Assess AI automation risk for skills found in vacancies.
    /// </summary>
    public List<AiRiskAssessment> AssessAiRisk(IReadOnlyList<JobVacancy> vacancies)
    {
        var skillCounts = vacancies
            .SelectMany(v => v.RequiredSkills.Concat(v.PreferredSkills))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .GroupBy(s => NormalizeSkill(s), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());

        var assessments = new List<AiRiskAssessment>();

        foreach (var (category, skills) in AiRiskCategories)
        {
            var riskLevel = category switch
            {
                _ when category.StartsWith("AI-Proof") => "Low Risk",
                "AI-Amplified (Senior Dev Skills)" => "Medium Risk",
                "AI-Assisted (Use AI to do faster)" => "Medium Risk",
                "AI-Threatened (Automate away)" => "High Risk",
                "AI/ML (The New Gold)" => "Opportunity",
                _ => "Unknown"
            };

            var score = category switch
            {
                _ when category.StartsWith("AI-Proof") => 90,
                "AI-Amplified (Senior Dev Skills)" => 70,
                "AI-Assisted (Use AI to do faster)" => 50,
                "AI-Threatened (Automate away)" => 15,
                "AI/ML (The New Gold)" => 95,
                _ => 50
            };

            foreach (var skill in skills)
            {
                var demandCount = FindSkillDemand(skill, skillCounts);
                if (demandCount > 0 || IsCommonSkill(skill))
                {
                    assessments.Add(new AiRiskAssessment
                    {
                        SkillName = skill,
                        Category = category,
                        RiskLevel = riskLevel,
                        ResilienceScore = score,
                        MarketDemand = demandCount,
                        Recommendation = GetRecommendation(riskLevel, skill)
                    });
                }
            }
        }

        return assessments.OrderByDescending(a => a.MarketDemand).ToList();
    }

    /// <summary>
    /// Generate a personalized career roadmap based on current skills and market data.
    /// </summary>
    public CareerRoadmap GenerateRoadmap(
        IReadOnlyList<JobVacancy> vacancies,
        List<string> currentSkills)
    {
        var pulse = AnalyzeMarketPulse(vacancies);
        var normalizedCurrent = currentSkills
            .Select(NormalizeSkill)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Score each career direction
        var scoredDirections = Directions.Select(d =>
        {
            var matchingSkills = d.KeySkills
                .Count(ks => normalizedCurrent.Any(cs =>
                    cs.Contains(ks, StringComparison.OrdinalIgnoreCase) ||
                    ks.Contains(cs, StringComparison.OrdinalIgnoreCase)));

            var skillOverlap = d.KeySkills.Count > 0
                ? matchingSkills * 100.0 / d.KeySkills.Count
                : 0;

            var marketScore = d.MarketGrowthRate;
            var salaryScore = d.SalaryMultiplier * 30;
            var aiScore = d.AiResilienceScore * 0.4;

            // Composite score weighted toward market growth and AI resilience
            var compositeScore = (marketScore * 0.3) + (salaryScore * 0.2) +
                                 (aiScore * 0.3) + (skillOverlap * 0.2);

            return new ScoredDirection
            {
                Direction = d,
                SkillOverlapPercent = skillOverlap,
                CompositeScore = compositeScore,
                SkillsYouHave = d.KeySkills
                    .Where(ks => normalizedCurrent.Any(cs =>
                        cs.Contains(ks, StringComparison.OrdinalIgnoreCase) ||
                        ks.Contains(cs, StringComparison.OrdinalIgnoreCase)))
                    .ToList(),
                SkillsToLearn = d.KeySkills
                    .Where(ks => !normalizedCurrent.Any(cs =>
                        cs.Contains(ks, StringComparison.OrdinalIgnoreCase) ||
                        ks.Contains(cs, StringComparison.OrdinalIgnoreCase)))
                    .ToList()
            };
        }).OrderByDescending(s => s.CompositeScore).ToList();

        // Find skill gaps
        var topMarketSkills = pulse.Trends
            .Where(t => t.Direction == "Rising" || t.DemandPercent > 10)
            .OrderByDescending(t => t.DemandPercent)
            .Take(30)
            .ToList();

        var gaps = topMarketSkills
            .Where(t => !normalizedCurrent.Any(cs =>
                cs.Contains(t.SkillName, StringComparison.OrdinalIgnoreCase) ||
                t.SkillName.Contains(cs, StringComparison.OrdinalIgnoreCase)))
            .Select(t => new MarketSkillGap
            {
                SkillName = t.SkillName,
                MarketDemandPercent = t.DemandPercent,
                TrendDirection = t.Direction,
                Priority = t.Direction == "Rising" ? "High" : t.DemandPercent > 20 ? "High" : "Medium"
            })
            .Take(15)
            .ToList();

        return new CareerRoadmap
        {
            ScoredDirections = scoredDirections,
            SkillGaps = gaps,
            CurrentSkillCount = currentSkills.Count,
            RecommendedDirection = scoredDirections.FirstOrDefault()?.Direction.Name ?? "AI/ML Integration Engineer"
        };
    }

    /// <summary>
    /// Analyze skill gaps between user profile and market demand.
    /// </summary>
    public MarketSkillGapReport AnalyzeSkillGaps(
        IReadOnlyList<JobVacancy> vacancies,
        List<string> currentSkills)
    {
        var normalizedCurrent = currentSkills
            .Select(NormalizeSkill)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skillCounts = vacancies
            .SelectMany(v => v.RequiredSkills.Concat(v.PreferredSkills))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .GroupBy(s => NormalizeSkill(s), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count())
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var matchedSkills = new List<MatchedSkill>();
        var missingSkills = new List<MissingSkill>();

        foreach (var (skill, count) in skillCounts.Take(50))
        {
            var demandPct = vacancies.Count > 0 ? count * 100.0 / vacancies.Count : 0;
            var hasSkill = normalizedCurrent.Any(cs =>
                cs.Contains(skill, StringComparison.OrdinalIgnoreCase) ||
                skill.Contains(cs, StringComparison.OrdinalIgnoreCase));

            if (hasSkill)
            {
                matchedSkills.Add(new MatchedSkill
                {
                    SkillName = skill,
                    DemandCount = count,
                    DemandPercent = demandPct
                });
            }
            else
            {
                missingSkills.Add(new MissingSkill
                {
                    SkillName = skill,
                    DemandCount = count,
                    DemandPercent = demandPct,
                    Priority = demandPct > 20 ? "Critical" : demandPct > 10 ? "High" : "Medium"
                });
            }
        }

        var coveragePercent = skillCounts.Count > 0
            ? matchedSkills.Count * 100.0 / Math.Min(50, skillCounts.Count)
            : 0;

        return new MarketSkillGapReport
        {
            MarketCoverage = coveragePercent,
            MatchedSkills = matchedSkills.OrderByDescending(s => s.DemandPercent).ToList(),
            MissingSkills = missingSkills.OrderByDescending(s => s.DemandPercent).ToList(),
            TotalMarketSkills = skillCounts.Count,
            YourSkillCount = currentSkills.Count
        };
    }

    private static Dictionary<string, int> CountSkills(IReadOnlyList<JobVacancy> vacancies)
    {
        return vacancies
            .SelectMany(v => v.RequiredSkills.Concat(v.PreferredSkills))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .GroupBy(s => NormalizeSkill(s), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static string NormalizeSkill(string skill)
    {
        return skill.Trim()
            .Replace("ASP.NET Core", "ASP.NET Core")
            .Replace("Entity Framework Core", "EF Core")
            .Replace("Entity Framework", "EF Core");
    }

    private static int FindSkillDemand(string skill, Dictionary<string, int> skillCounts)
    {
        // Try exact match first, then partial
        if (skillCounts.TryGetValue(skill, out var count))
            return count;

        return skillCounts
            .Where(kv => kv.Key.Contains(skill, StringComparison.OrdinalIgnoreCase) ||
                         skill.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
            .Sum(kv => kv.Value);
    }

    private static bool IsCommonSkill(string skill) =>
        new[]
        {
            "C#", ".NET", "Azure", "AWS", "Docker", "Kubernetes", "SQL", "React",
            "System Design", "Security", "Machine Learning", "AI"
        }.Any(s => s.Equals(skill, StringComparison.OrdinalIgnoreCase));

    private static string GetRecommendation(string riskLevel, string skill) => riskLevel switch
    {
        "Low Risk" => $"Keep investing in {skill} — AI can't replace this",
        "Opportunity" => $"Learn {skill} ASAP — highest ROI skill category right now",
        "Medium Risk" => $"Use AI tools to be 3x faster at {skill}",
        "High Risk" => $"Automate {skill} with AI and move to higher-value work",
        _ => $"Evaluate your depth in {skill}"
    };
}

// --- Report Models ---

public sealed class MarketPulseReport
{
    public int TotalVacancies { get; set; }
    public Dictionary<string, int> SkillDemand { get; set; } = [];
    public List<SkillTrend> Trends { get; set; } = [];
    public List<SkillTrend> RisingSkills { get; set; } = [];
    public List<SkillTrend> DecliningSkills { get; set; } = [];
    public List<SkillSalary> TopPayingSkills { get; set; } = [];
}

public sealed class SkillTrend
{
    public string SkillName { get; set; } = string.Empty;
    public int TotalMentions { get; set; }
    public double DemandPercent { get; set; }
    public double TrendChange { get; set; }
    public string Direction { get; set; } = "Stable";
}

public sealed class SkillSalary
{
    public string SkillName { get; set; } = string.Empty;
    public decimal AverageSalary { get; set; }
    public int DataPoints { get; set; }
}

public sealed class AiRiskAssessment
{
    public string SkillName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public int ResilienceScore { get; set; }
    public int MarketDemand { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}

public sealed class CareerDirection
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> KeySkills { get; set; } = [];
    public double SalaryMultiplier { get; set; } = 1.0;
    public int MarketGrowthRate { get; set; }
    public int AiResilienceScore { get; set; }
    public string Icon { get; set; } = string.Empty;
}

public sealed class ScoredDirection
{
    public CareerDirection Direction { get; set; } = new();
    public double SkillOverlapPercent { get; set; }
    public double CompositeScore { get; set; }
    public List<string> SkillsYouHave { get; set; } = [];
    public List<string> SkillsToLearn { get; set; } = [];
}

public sealed class CareerRoadmap
{
    public List<ScoredDirection> ScoredDirections { get; set; } = [];
    public List<MarketSkillGap> SkillGaps { get; set; } = [];
    public int CurrentSkillCount { get; set; }
    public string RecommendedDirection { get; set; } = string.Empty;
}

public sealed class MarketSkillGap
{
    public string SkillName { get; set; } = string.Empty;
    public double MarketDemandPercent { get; set; }
    public string TrendDirection { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
}

public sealed class MarketSkillGapReport
{
    public double MarketCoverage { get; set; }
    public List<MatchedSkill> MatchedSkills { get; set; } = [];
    public List<MissingSkill> MissingSkills { get; set; } = [];
    public int TotalMarketSkills { get; set; }
    public int YourSkillCount { get; set; }
}

public sealed class MatchedSkill
{
    public string SkillName { get; set; } = string.Empty;
    public int DemandCount { get; set; }
    public double DemandPercent { get; set; }
}

public sealed class MissingSkill
{
    public string SkillName { get; set; } = string.Empty;
    public int DemandCount { get; set; }
    public double DemandPercent { get; set; }
    public string Priority { get; set; } = string.Empty;
}
