namespace CareerIntel.Intelligence;

/// <summary>
/// Analyzes a resume to identify strengths, weaknesses, and specific improvements
/// that can increase salary positioning. Focuses on how to reframe experience
/// for higher-paying roles (Staff Engineer, Solutions Architect, etc.).
/// </summary>
public sealed class ResumeAnalyzer
{
    /// <summary>
    /// Performs a comprehensive resume analysis producing actionable recommendations.
    /// </summary>
    public ResumeAnalysisReport Analyze(ResumeData resume)
    {
        var report = new ResumeAnalysisReport
        {
            TotalYearsExperience = resume.TotalYearsExperience,
            CurrentTitle = resume.CurrentTitle,
            Skills = resume.Skills.ToList()
        };

        // Score each dimension
        report.TitleScore = ScoreTitle(resume);
        report.MetricsScore = ScoreMetrics(resume);
        report.SkillBreadthScore = ScoreSkillBreadth(resume);
        report.LeadershipScore = ScoreLeadership(resume);
        report.ArchitectureScore = ScoreArchitecture(resume);
        report.CloudScore = ScoreCloud(resume);
        report.OverallScore = (report.TitleScore + report.MetricsScore + report.SkillBreadthScore +
                               report.LeadershipScore + report.ArchitectureScore + report.CloudScore) / 6;

        // Generate issues
        report.Issues = FindIssues(resume);

        // Generate improvements
        report.Improvements = GenerateImprovements(resume, report);

        // Rewritten bullet suggestions
        report.BulletRewrites = GenerateBulletRewrites(resume);

        // Career pivots
        report.CareerPivots = AnalyzeCareerPivots(resume);

        // Suggested titles
        report.SuggestedTitles = SuggestTitles(resume);

        return report;
    }

    private static int ScoreTitle(ResumeData resume)
    {
        var title = resume.CurrentTitle.ToLowerInvariant();
        if (title.Contains("principal") || title.Contains("staff") || title.Contains("distinguished"))
            return 95;
        if (title.Contains("architect") || title.Contains("engineering manager"))
            return 90;
        if (title.Contains("tech lead") || title.Contains("team lead"))
            return 80;
        if (title.Contains("senior software engineer") || title.Contains("senior backend"))
            return 70;
        if (title.Contains("senior") && !title.Contains(".net"))
            return 65;
        if (title.Contains("senior") && title.Contains(".net"))
            return 50; // .NET-specific title anchors salary lower
        if (title.Contains(".net"))
            return 35; // Technology-specific title is limiting
        return 45;
    }

    private static int ScoreMetrics(ResumeData resume)
    {
        var score = 0;
        var totalBullets = resume.Experiences.Sum(e => e.Bullets.Count);
        if (totalBullets == 0) return 0;

        var metricsCount = 0;
        var metricPatterns = new[] { "%", "x faster", "x improvement", "$", "users", "requests",
            "rps", "tps", "latency", "uptime", "99.", "million", "thousand", "reduced", "increased by" };

        foreach (var exp in resume.Experiences)
        {
            foreach (var bullet in exp.Bullets)
            {
                var lower = bullet.ToLowerInvariant();
                if (metricPatterns.Any(p => lower.Contains(p)) ||
                    System.Text.RegularExpressions.Regex.IsMatch(bullet, @"\d+[%xX]|\$[\d,]+|\d+\s*(ms|seconds)"))
                {
                    metricsCount++;
                }
            }
        }

        var ratio = (double)metricsCount / totalBullets;
        score = ratio switch
        {
            >= 0.5 => 95,
            >= 0.35 => 80,
            >= 0.2 => 60,
            >= 0.1 => 40,
            > 0 => 25,
            _ => 10
        };

        return score;
    }

    private static int ScoreSkillBreadth(ResumeData resume)
    {
        var skills = resume.Skills.Select(s => s.ToLowerInvariant()).ToHashSet();
        var score = 0;

        // Backend languages
        if (skills.Any(s => s.Contains("c#") || s.Contains(".net"))) score += 10;
        if (skills.Any(s => s.Contains("python") || s.Contains("go") || s.Contains("rust") || s.Contains("java"))) score += 10;

        // Frontend
        if (skills.Any(s => s.Contains("react") || s.Contains("angular") || s.Contains("vue"))) score += 10;
        if (skills.Any(s => s.Contains("typescript"))) score += 5;

        // Cloud
        if (skills.Any(s => s.Contains("aws"))) score += 10;
        if (skills.Any(s => s.Contains("azure"))) score += 10;
        if (skills.Any(s => s.Contains("gcp"))) score += 5;

        // Infrastructure
        if (skills.Any(s => s.Contains("kubernetes") || s.Contains("k8s"))) score += 10;
        if (skills.Any(s => s.Contains("docker"))) score += 5;
        if (skills.Any(s => s.Contains("terraform") || s.Contains("pulumi"))) score += 5;

        // Databases
        if (skills.Any(s => s.Contains("postgresql") || s.Contains("sql server"))) score += 5;
        if (skills.Any(s => s.Contains("redis") || s.Contains("mongodb"))) score += 5;

        // Messaging
        if (skills.Any(s => s.Contains("kafka") || s.Contains("rabbitmq"))) score += 5;

        // Architecture patterns
        if (skills.Any(s => s.Contains("microservices"))) score += 5;

        return Math.Min(score, 100);
    }

    private static int ScoreLeadership(ResumeData resume)
    {
        var leadershipSignals = new[] { "led", "lead", "mentored", "coordinated", "managed",
            "team", "cross-functional", "stakeholder", "onboarding", "code review",
            "tech lead", "team lead", "principal", "staff" };

        var count = 0;
        foreach (var exp in resume.Experiences)
        {
            foreach (var bullet in exp.Bullets)
            {
                var lower = bullet.ToLowerInvariant();
                if (leadershipSignals.Any(s => lower.Contains(s)))
                    count++;
            }
        }

        return count switch
        {
            >= 8 => 90,
            >= 5 => 75,
            >= 3 => 55,
            >= 1 => 35,
            _ => 10
        };
    }

    private static int ScoreArchitecture(ResumeData resume)
    {
        var archSignals = new[] { "architect", "designed", "system design", "microservices",
            "scalable", "distributed", "event-driven", "cqrs", "ddd", "domain-driven",
            "migration", "moderniz", "from scratch", "greenfield", "platform" };

        var count = 0;
        foreach (var exp in resume.Experiences)
        {
            foreach (var bullet in exp.Bullets)
            {
                var lower = bullet.ToLowerInvariant();
                if (archSignals.Any(s => lower.Contains(s)))
                    count++;
            }
        }

        return count switch
        {
            >= 8 => 90,
            >= 5 => 75,
            >= 3 => 55,
            >= 1 => 35,
            _ => 10
        };
    }

    private static int ScoreCloud(ResumeData resume)
    {
        var cloudSignals = new[] { "aws", "azure", "gcp", "kubernetes", "docker",
            "terraform", "ci/cd", "pipeline", "devops", "serverless", "lambda",
            "cloud", "container", "aks", "eks", "ecs" };

        var count = 0;
        foreach (var exp in resume.Experiences)
        {
            foreach (var bullet in exp.Bullets)
            {
                var lower = bullet.ToLowerInvariant();
                if (cloudSignals.Any(s => lower.Contains(s)))
                    count++;
            }
        }

        return count switch
        {
            >= 8 => 90,
            >= 5 => 75,
            >= 3 => 55,
            >= 1 => 35,
            _ => 10
        };
    }

    private static List<ResumeIssue> FindIssues(ResumeData resume)
    {
        var issues = new List<ResumeIssue>();
        var titleLower = resume.CurrentTitle.ToLowerInvariant();

        // Technology-specific title
        if (titleLower.Contains(".net") || titleLower.Contains("c#"))
        {
            issues.Add(new ResumeIssue
            {
                Severity = "critical",
                Category = "Title",
                Title = "Technology-specific job title limits salary ceiling",
                Description = "\".NET Developer\" anchors recruiters to $80K-$150K range. " +
                    "\"Senior Software Engineer\" or \"Staff Engineer\" with the same skills targets $180K-$350K. " +
                    "Remove technology from title — your skills section shows your stack."
            });
        }

        // Check for metrics
        var totalBullets = resume.Experiences.Sum(e => e.Bullets.Count);
        var bulletsWithNumbers = resume.Experiences
            .SelectMany(e => e.Bullets)
            .Count(b => System.Text.RegularExpressions.Regex.IsMatch(b, @"\d"));

        if (bulletsWithNumbers < totalBullets * 0.15)
        {
            issues.Add(new ResumeIssue
            {
                Severity = "critical",
                Category = "Metrics",
                Title = "Missing quantifiable achievements",
                Description = $"Only {bulletsWithNumbers}/{totalBullets} bullets contain numbers. " +
                    "High-paying roles ($200K+) require proof of impact: latency reductions, " +
                    "user counts, cost savings, team sizes, uptime improvements."
            });
        }

        // Generic action verbs
        var genericVerbs = new[] { "improved", "enhanced", "optimized", "maintained", "developed",
            "ensured", "established" };
        var genericCount = resume.Experiences
            .SelectMany(e => e.Bullets)
            .Count(b => genericVerbs.Any(v => b.ToLowerInvariant().StartsWith(v)));

        if (genericCount > totalBullets * 0.4)
        {
            issues.Add(new ResumeIssue
            {
                Severity = "high",
                Category = "Language",
                Title = "Too many generic action verbs",
                Description = $"{genericCount}/{totalBullets} bullets start with generic verbs " +
                    "(improved, enhanced, optimized). Use specific verbs: " +
                    "\"Architected\", \"Reduced\", \"Scaled\", \"Migrated\", \"Automated\", \"Eliminated\"."
            });
        }

        // Same title across all positions
        var uniqueTitles = resume.Experiences.Select(e => e.Title.ToLowerInvariant()).Distinct().Count();
        if (uniqueTitles == 1 && resume.Experiences.Count >= 3)
        {
            issues.Add(new ResumeIssue
            {
                Severity = "high",
                Category = "Progression",
                Title = "No visible career progression",
                Description = "All positions have the same title. This suggests no growth over " +
                    $"{resume.TotalYearsExperience} years. Show progression: " +
                    "Junior -> Mid -> Senior -> Tech Lead -> Staff. Even if titles were " +
                    "the same officially, reflect the actual scope increase."
            });
        }

        // Tech Lead in headline but not in experience
        var headlineLower = resume.Headline?.ToLowerInvariant() ?? "";
        var hasLeadTitle = resume.Experiences.Any(e =>
            e.Title.ToLowerInvariant().Contains("lead") ||
            e.Title.ToLowerInvariant().Contains("principal") ||
            e.Title.ToLowerInvariant().Contains("architect"));

        if (headlineLower.Contains("tech lead") && !hasLeadTitle)
        {
            issues.Add(new ResumeIssue
            {
                Severity = "high",
                Category = "Consistency",
                Title = "\"Tech Lead\" in headline but not in experience titles",
                Description = "Recruiters notice this contradiction. Either update your most recent " +
                    "title to reflect Tech Lead responsibilities, or remove it from the headline."
            });
        }

        // Missing scale indicators
        var hasScale = resume.Experiences.SelectMany(e => e.Bullets)
            .Any(b =>
            {
                var lower = b.ToLowerInvariant();
                return lower.Contains("team of") || lower.Contains("million") ||
                       lower.Contains("enterprise") || lower.Contains("users") ||
                       System.Text.RegularExpressions.Regex.IsMatch(b, @"\d+\s*(developer|engineer|member)");
            });

        if (!hasScale)
        {
            issues.Add(new ResumeIssue
            {
                Severity = "medium",
                Category = "Scale",
                Title = "No team size or system scale mentioned",
                Description = "Hiring managers for $200K+ roles need to know: " +
                    "How big was the team? How many users? What's the traffic? " +
                    "Add: \"Led team of 5 engineers\", \"System serving 2M daily active users\"."
            });
        }

        return issues;
    }

    private static List<ResumeImprovement> GenerateImprovements(ResumeData resume, ResumeAnalysisReport report)
    {
        var improvements = new List<ResumeImprovement>();

        if (report.TitleScore < 70)
        {
            improvements.Add(new ResumeImprovement
            {
                Priority = 1,
                Category = "Title Rebranding",
                Impact = "+$40K-$80K salary range",
                Current = resume.CurrentTitle,
                Suggested = resume.TotalYearsExperience >= 10
                    ? "Staff Software Engineer | Tech Lead | Solutions Architect"
                    : "Senior Software Engineer | Backend Engineer",
                Explanation = "Technology-agnostic titles access higher salary bands. " +
                    "Companies like Teleport ($181K-$327K), Owner.com ($190K-$220K) hire " +
                    "\"Staff Software Engineers\" — same work, much higher pay."
            });
        }

        if (report.MetricsScore < 60)
        {
            improvements.Add(new ResumeImprovement
            {
                Priority = 2,
                Category = "Add Quantifiable Metrics",
                Impact = "+$20K-$50K perceived value",
                Current = "\"Designed and improved backend services using .NET 8\"",
                Suggested = "\"Architected microservices handling 500K daily transactions, " +
                    "reducing API response time from 800ms to 120ms (85% improvement)\"",
                Explanation = "Every bullet should answer: How much? How many? What was the " +
                    "before/after? Numbers are what separate $150K resumes from $250K resumes."
            });
        }

        if (report.LeadershipScore < 70)
        {
            improvements.Add(new ResumeImprovement
            {
                Priority = 3,
                Category = "Highlight Leadership",
                Impact = "+$30K-$60K for leadership roles",
                Current = "\"Coordinated efforts between development, DevOps, and product teams\"",
                Suggested = "\"Led cross-functional team of 8 engineers, DevOps, and product. " +
                    "Drove architectural decisions, conducted weekly design reviews, " +
                    "and mentored 3 junior developers to mid-level promotion.\"",
                Explanation = "Staff/Principal roles pay $200K-$350K but require proof of " +
                    "leadership. Show team sizes, mentoring outcomes, and decision-making scope."
            });
        }

        var summary = resume.Summary?.ToLowerInvariant() ?? "";
        if (summary.Contains("results-driven") || summary.Contains("proficient") ||
            summary.Contains("adept") || summary.Length < 100)
        {
            improvements.Add(new ResumeImprovement
            {
                Priority = 4,
                Category = "Rewrite Summary",
                Impact = "First impression — determines if resume gets read",
                Current = resume.Summary ?? "",
                Suggested = $"Staff-level engineer with {resume.TotalYearsExperience}+ years building distributed " +
                    "systems at scale. Architected microservices platforms handling millions of daily " +
                    "transactions across AWS and Azure. Led cross-functional teams delivering " +
                    "mission-critical logistics and sustainability platforms. Specialized in " +
                    ".NET/C#, cloud-native architecture (Kubernetes, Docker), and full-stack " +
                    "development (React, TypeScript).",
                Explanation = "Remove filler words (results-driven, proficient, adept). " +
                    "Lead with level (Staff/Principal), then impact, then tech depth."
            });
        }

        return improvements;
    }

    private static List<BulletRewrite> GenerateBulletRewrites(ResumeData resume)
    {
        var rewrites = new List<BulletRewrite>();

        // Map of common weak bullets to strong alternatives
        var weakToStrong = new Dictionary<string, (string Strong, string Explanation)>
        {
            ["Designed and improved backend services"] = (
                "Architected event-driven microservices handling 500K+ daily transactions with 99.9% uptime",
                "Add scale, pattern name, and reliability metric"),
            ["Strengthened system security by implementing"] = (
                "Eliminated unauthorized access vectors by implementing Azure AD SSO and RBAC, achieving SOC2 compliance",
                "Show the outcome (compliance achieved), not just the action"),
            ["Built and maintained CI/CD pipelines"] = (
                "Reduced deployment time from 45min to 8min by building automated CI/CD pipelines on AKS, enabling 15+ daily releases",
                "Quantify the before/after and business impact"),
            ["Coordinated efforts between development"] = (
                "Led cross-functional team of 8 (3 backend, 2 frontend, 2 DevOps, 1 QA) — drove architectural decisions and sprint planning",
                "Show team size and your decision-making scope"),
            ["Developed scalable architecture solutions"] = (
                "Designed distributed caching strategy reducing database load by 60%, supporting 3x traffic growth without infrastructure changes",
                "Replace vague 'scalable' with specific technical decision and measurable result"),
            ["Developed RESTful APIs in a microservices"] = (
                "Built 12 microservices with RESTful APIs using CQRS pattern, processing 200K environmental data points daily",
                "Add count, pattern, and throughput"),
            ["Led code reviews and implemented SOLID"] = (
                "Established code review culture across 6-person team, reducing production bugs by 40% through SOLID principles enforcement",
                "Quantify the team and the outcome of the practice"),
            ["Enhanced application performance"] = (
                "Reduced P95 API latency from 1.2s to 180ms through query optimization and Redis caching, improving user retention by 15%",
                "Always specify: from X to Y, and tie to business metric"),
            ["Optimized Redis caching and PostgreSQL"] = (
                "Redesigned caching strategy: moved from cache-aside to write-through pattern, reducing cache misses by 70% and DB read load by 45%",
                "Name the specific technique and quantify the improvement"),
        };

        foreach (var exp in resume.Experiences)
        {
            foreach (var bullet in exp.Bullets)
            {
                foreach (var (weak, (strong, explanation)) in weakToStrong)
                {
                    if (bullet.Contains(weak, StringComparison.OrdinalIgnoreCase))
                    {
                        rewrites.Add(new BulletRewrite
                        {
                            Company = exp.Company,
                            Original = bullet,
                            Suggested = strong,
                            Explanation = explanation
                        });
                        break;
                    }
                }
            }
        }

        return rewrites;
    }

    private static List<CareerPivot> AnalyzeCareerPivots(ResumeData resume)
    {
        var pivots = new List<CareerPivot>();
        var skills = resume.Skills.Select(s => s.ToLowerInvariant()).ToHashSet();
        var years = resume.TotalYearsExperience;

        // Staff/Principal Software Engineer
        var staffFit = 0;
        if (years >= 8) staffFit += 30;
        if (skills.Any(s => s.Contains("microservices") || s.Contains("architecture"))) staffFit += 20;
        if (skills.Any(s => s.Contains("kubernetes") || s.Contains("docker"))) staffFit += 15;
        if (skills.Any(s => s.Contains("aws") || s.Contains("azure"))) staffFit += 15;
        if (resume.Experiences.SelectMany(e => e.Bullets).Any(b =>
            b.ToLowerInvariant().Contains("led") || b.ToLowerInvariant().Contains("mentor"))) staffFit += 20;

        pivots.Add(new CareerPivot
        {
            TargetTitle = "Staff Software Engineer",
            SalaryRange = "$200K - $350K",
            FitScore = Math.Min(staffFit, 100),
            SkillsYouHave = GetMatchingSkills(skills, ["c#", ".net", "microservices", "kubernetes", "docker", "aws", "azure", "postgresql", "redis"]),
            SkillsNeeded = GetMissingSkills(skills, ["system design", "technical leadership", "cross-team collaboration"]),
            ActionItems =
            [
                "Reframe resume title: \"Staff Software Engineer\" instead of \".NET Developer\"",
                "Add system design examples: architecture diagrams, trade-off decisions",
                "Highlight cross-team impact and mentoring in every role",
                "Target companies that use .NET but hire under general titles (Microsoft, GitHub, Stack Overflow)"
            ]
        });

        // Solutions Architect
        var archFit = 0;
        if (years >= 10) archFit += 25;
        if (skills.Any(s => s.Contains("microservices"))) archFit += 20;
        if (skills.Any(s => s.Contains("aws") || s.Contains("azure"))) archFit += 20;
        if (skills.Any(s => s.Contains("kubernetes"))) archFit += 10;
        if (skills.Any(s => s.Contains("react") || s.Contains("angular") || s.Contains("node"))) archFit += 15;
        if (resume.Experiences.SelectMany(e => e.Bullets).Any(b =>
            b.ToLowerInvariant().Contains("architect") || b.ToLowerInvariant().Contains("design"))) archFit += 10;

        pivots.Add(new CareerPivot
        {
            TargetTitle = "Solutions Architect",
            SalaryRange = "$180K - $280K",
            FitScore = Math.Min(archFit, 100),
            SkillsYouHave = GetMatchingSkills(skills, ["c#", ".net", "aws", "azure", "microservices", "react", "angular", "sql server", "postgresql"]),
            SkillsNeeded = GetMissingSkills(skills, ["aws solutions architect certification", "architecture documentation", "stakeholder presentation"]),
            ActionItems =
            [
                "Get AWS Solutions Architect Associate cert ($150, 2-3 weeks study)",
                "Document architectural decisions from current/past projects as case studies",
                "Add \"Designed\" and \"Architected\" verbs to resume bullets",
                "Create architecture blog posts or conference talks"
            ]
        });

        // Platform/Cloud Engineer
        var cloudFit = 0;
        if (skills.Any(s => s.Contains("kubernetes"))) cloudFit += 25;
        if (skills.Any(s => s.Contains("docker"))) cloudFit += 15;
        if (skills.Any(s => s.Contains("aws"))) cloudFit += 20;
        if (skills.Any(s => s.Contains("azure"))) cloudFit += 20;
        if (skills.Any(s => s.Contains("ci/cd") || s.Contains("devops"))) cloudFit += 15;
        if (skills.Any(s => s.Contains("terraform"))) cloudFit += 10;

        pivots.Add(new CareerPivot
        {
            TargetTitle = "Platform Engineer / Cloud Architect",
            SalaryRange = "$170K - $260K",
            FitScore = Math.Min(cloudFit, 100),
            SkillsYouHave = GetMatchingSkills(skills, ["kubernetes", "docker", "aws", "azure", "ci/cd", "github actions"]),
            SkillsNeeded = GetMissingSkills(skills, ["terraform/pulumi", "service mesh (istio)", "observability (prometheus/grafana)", "gitops"]),
            ActionItems =
            [
                "Learn Terraform (1-2 weeks) — most requested IaC tool",
                "Get CKA (Certified Kubernetes Administrator) — strong salary signal",
                "Set up a production-like K8s cluster as a portfolio project",
                "Emphasize Azure AKS and CI/CD pipeline work in resume"
            ]
        });

        // Engineering Manager
        var emFit = 0;
        if (years >= 10) emFit += 25;
        if (resume.Experiences.SelectMany(e => e.Bullets).Any(b =>
        {
            var lower = b.ToLowerInvariant();
            return lower.Contains("led") || lower.Contains("team") || lower.Contains("mentor") || lower.Contains("coordinat");
        })) emFit += 30;
        if (skills.Any(s => s.Contains("agile") || s.Contains("scrum"))) emFit += 15;
        if (resume.Experiences.Count >= 5) emFit += 15;

        pivots.Add(new CareerPivot
        {
            TargetTitle = "Engineering Manager",
            SalaryRange = "$200K - $300K",
            FitScore = Math.Min(emFit, 100),
            SkillsYouHave = GetMatchingSkills(skills, ["agile", "scrum", "microservices", "ci/cd"]),
            SkillsNeeded = GetMissingSkills(skills, ["people management", "hiring/interviewing", "performance reviews", "roadmap planning", "budget ownership"]),
            ActionItems =
            [
                "Highlight all leadership bullets: team coordination, mentoring, code reviews",
                "Frame technical decisions as business outcomes",
                "Consider leading a small team (3-5) in current role as stepping stone",
                "Read: \"The Manager's Path\" by Camille Fournier"
            ]
        });

        return pivots.OrderByDescending(p => p.FitScore).ToList();
    }

    private static List<string> SuggestTitles(ResumeData resume)
    {
        var titles = new List<string>();
        var years = resume.TotalYearsExperience;

        if (years >= 12)
        {
            titles.Add("Staff Software Engineer");
            titles.Add("Principal Engineer");
            titles.Add("Solutions Architect");
        }
        if (years >= 8)
        {
            titles.Add("Senior Software Engineer");
            titles.Add("Tech Lead");
            titles.Add("Senior Backend Engineer");
        }
        if (years >= 5)
        {
            titles.Add("Software Engineer");
            titles.Add("Backend Engineer");
        }

        return titles;
    }

    private static List<string> GetMatchingSkills(HashSet<string> userSkills, string[] targetSkills) =>
        targetSkills.Where(t => userSkills.Any(u => u.Contains(t))).ToList();

    private static List<string> GetMissingSkills(HashSet<string> userSkills, string[] targetSkills) =>
        targetSkills.Where(t => !userSkills.Any(u => u.Contains(t))).ToList();
}

// ── Models ──

public sealed class ResumeData
{
    public string CurrentTitle { get; set; } = "";
    public string? Headline { get; set; }
    public string? Summary { get; set; }
    public int TotalYearsExperience { get; set; }
    public List<string> Skills { get; set; } = [];
    public List<ResumeExperience> Experiences { get; set; } = [];
}

public sealed class ResumeExperience
{
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Period { get; set; } = "";
    public List<string> Bullets { get; set; } = [];
    public List<string> Technologies { get; set; } = [];
}

public sealed class ResumeAnalysisReport
{
    public string CurrentTitle { get; set; } = "";
    public int TotalYearsExperience { get; set; }
    public List<string> Skills { get; set; } = [];

    // Scores (0-100)
    public int OverallScore { get; set; }
    public int TitleScore { get; set; }
    public int MetricsScore { get; set; }
    public int SkillBreadthScore { get; set; }
    public int LeadershipScore { get; set; }
    public int ArchitectureScore { get; set; }
    public int CloudScore { get; set; }

    public List<ResumeIssue> Issues { get; set; } = [];
    public List<ResumeImprovement> Improvements { get; set; } = [];
    public List<BulletRewrite> BulletRewrites { get; set; } = [];
    public List<CareerPivot> CareerPivots { get; set; } = [];
    public List<string> SuggestedTitles { get; set; } = [];
}

public sealed class ResumeIssue
{
    public string Severity { get; set; } = "medium"; // critical, high, medium, low
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class ResumeImprovement
{
    public int Priority { get; set; }
    public string Category { get; set; } = "";
    public string Impact { get; set; } = "";
    public string Current { get; set; } = "";
    public string Suggested { get; set; } = "";
    public string Explanation { get; set; } = "";
}

public sealed class BulletRewrite
{
    public string Company { get; set; } = "";
    public string Original { get; set; } = "";
    public string Suggested { get; set; } = "";
    public string Explanation { get; set; } = "";
}

public sealed class CareerPivot
{
    public string TargetTitle { get; set; } = "";
    public string SalaryRange { get; set; } = "";
    public int FitScore { get; set; }
    public List<string> SkillsYouHave { get; set; } = [];
    public List<string> SkillsNeeded { get; set; } = [];
    public List<string> ActionItems { get; set; } = [];
}
