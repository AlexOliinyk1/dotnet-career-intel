using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

public sealed class PortfolioGenerator(ILogger<PortfolioGenerator> logger)
{
    private static readonly Dictionary<string, ProjectTemplate> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["microservices-cqrs"] = new ProjectTemplate
        {
            Title = "Order Processing System",
            ProblemStatement = "A distributed order management platform that handles high-throughput e-commerce transactions "
                + "with event-driven architecture, demonstrating CQRS pattern separation between read and write models, "
                + "saga-based distributed transactions, and real-time inventory synchronization across services.",
            Architecture = "Microservices architecture with API Gateway (Ocelot/YARP), 4 bounded contexts "
                + "(Orders, Inventory, Payments, Notifications), event bus (RabbitMQ/MassTransit), "
                + "CQRS with MediatR, separate read/write databases (PostgreSQL + Redis), "
                + "and saga orchestrator for distributed transactions.",
            TechStack = [".NET 10", "C#", "MassTransit", "RabbitMQ", "PostgreSQL", "Redis", "Docker", "MediatR", "Entity Framework Core", "gRPC"],
            TriggerSkills = ["Microservices", "CQRS", "Event-Driven", "DDD", "Message Queue", "RabbitMQ", "MassTransit"],
            BacklogItems =
            [
                "Set up solution structure with 4 microservice projects and shared contracts library",
                "Implement Order Service with CQRS command/query separation using MediatR",
                "Build Inventory Service with event-sourced stock management",
                "Create Payment Service with idempotent payment processing and retry logic",
                "Implement saga orchestrator for order fulfillment workflow",
                "Add API Gateway with rate limiting, authentication, and request aggregation",
                "Set up Docker Compose for local development with all services and infrastructure",
                "Write integration tests covering the full order lifecycle across services"
            ]
        },
        ["cloud-kubernetes"] = new ProjectTemplate
        {
            Title = "Cloud-Native Deployment Platform",
            ProblemStatement = "A self-service platform that automates cloud infrastructure provisioning and application "
                + "deployment to Kubernetes clusters, featuring GitOps workflows, infrastructure-as-code, "
                + "automated scaling policies, and comprehensive observability with distributed tracing.",
            Architecture = "Cloud-native platform with Kubernetes operator pattern, Helm chart management, "
                + "Terraform modules for infrastructure, ArgoCD for GitOps, Prometheus + Grafana for monitoring, "
                + "and OpenTelemetry for distributed tracing. Control plane API built with .NET Minimal APIs.",
            TechStack = [".NET 10", "C#", "Kubernetes", "Docker", "Terraform", "Azure", "Helm", "Prometheus", "Grafana", "OpenTelemetry"],
            TriggerSkills = ["Azure", "Kubernetes", "K8s", "Docker", "Cloud", "DevOps", "Infrastructure", "Terraform", "AWS", "GCP"],
            BacklogItems =
            [
                "Build control plane API with .NET Minimal APIs for deployment management",
                "Create Kubernetes operator for custom resource definitions (CRDs) managing app deployments",
                "Implement Terraform module generator for cloud infrastructure provisioning",
                "Set up GitOps pipeline with webhook handlers and ArgoCD integration",
                "Add auto-scaling engine with custom metrics from Prometheus",
                "Implement distributed tracing with OpenTelemetry across all platform components",
                "Build dashboard UI showing deployment status, health, and cost metrics",
                "Write end-to-end tests simulating full deployment lifecycle"
            ]
        },
        ["ai-ml-pipeline"] = new ProjectTemplate
        {
            Title = "Intelligent Document Processing Pipeline",
            ProblemStatement = "An AI-powered document processing system that ingests unstructured documents (PDFs, images, emails), "
                + "extracts structured data using OCR and NLP, classifies documents by type, and provides "
                + "semantic search capabilities with vector embeddings and RAG (Retrieval-Augmented Generation).",
            Architecture = "Pipeline architecture with ingestion gateway, document processing workers, "
                + "ML inference service (ONNX Runtime), vector database (Qdrant/Milvus) for semantic search, "
                + "RAG engine with LLM integration, and REST API for querying results. "
                + "Built on .NET with ML.NET and Semantic Kernel.",
            TechStack = [".NET 10", "C#", "ML.NET", "Semantic Kernel", "ONNX Runtime", "Azure AI", "PostgreSQL", "Docker", "Redis", "OpenAI"],
            TriggerSkills = ["AI", "ML", "Machine Learning", "NLP", "LLM", "OpenAI", "Semantic Kernel", "Data Science", "Python"],
            BacklogItems =
            [
                "Build document ingestion API with support for PDF, image, and email formats",
                "Implement OCR pipeline using Azure AI Document Intelligence or Tesseract",
                "Create document classification model using ML.NET for automatic categorization",
                "Build entity extraction service for pulling structured fields from documents",
                "Set up vector database and embedding generation for semantic search",
                "Implement RAG engine with Semantic Kernel for natural language querying",
                "Add batch processing mode with progress tracking and error handling",
                "Write benchmark tests comparing extraction accuracy across document types"
            ]
        },
        ["real-time-analytics"] = new ProjectTemplate
        {
            Title = "Real-Time Analytics Dashboard Engine",
            ProblemStatement = "A high-performance real-time analytics platform that ingests streaming event data, "
                + "computes aggregations on-the-fly, and pushes live updates to connected dashboards. "
                + "Handles millions of events per minute with sub-second query latency.",
            Architecture = "Stream processing architecture with Apache Kafka for event ingestion, "
                + ".NET worker services for stream processing, time-series database (TimescaleDB) for storage, "
                + "SignalR for real-time dashboard push, and materialized views for pre-computed aggregations.",
            TechStack = [".NET 10", "C#", "Kafka", "SignalR", "TimescaleDB", "PostgreSQL", "Redis", "Docker", "Blazor", "gRPC"],
            TriggerSkills = ["Kafka", "Real-Time", "Streaming", "SignalR", "Analytics", "Data Engineering", "Performance", "Scalability"],
            BacklogItems =
            [
                "Set up Kafka consumer with .NET worker service for event ingestion",
                "Build stream processing engine with windowed aggregations (tumbling, sliding, session)",
                "Implement materialized view manager for pre-computed dashboard metrics",
                "Create SignalR hub for real-time metric push to connected clients",
                "Build Blazor dashboard with live-updating charts and drill-down capability",
                "Add backpressure handling and dead-letter queue for failed events",
                "Implement time-series data compaction and retention policies",
                "Write load tests simulating 1M+ events per minute throughput"
            ]
        },
        ["security-auth"] = new ProjectTemplate
        {
            Title = "Zero-Trust Identity Gateway",
            ProblemStatement = "A comprehensive identity and access management platform implementing zero-trust principles, "
                + "supporting multi-tenant OAuth 2.0/OIDC flows, fine-grained RBAC/ABAC authorization, "
                + "API key management, and real-time threat detection with anomaly-based security alerts.",
            Architecture = "Identity platform with custom OAuth 2.0 authorization server, policy engine (OPA-style), "
                + "token service with JWT + refresh token rotation, audit log pipeline, "
                + "anomaly detection service, and admin portal. Built on ASP.NET Core Identity foundations.",
            TechStack = [".NET 10", "C#", "ASP.NET Core", "OAuth 2.0", "OIDC", "PostgreSQL", "Redis", "Docker", "Blazor", "YARP"],
            TriggerSkills = ["Security", "Authentication", "Authorization", "OAuth", "Identity", "OIDC", "JWT", "Cryptography"],
            BacklogItems =
            [
                "Implement OAuth 2.0 authorization server with PKCE and client credentials flows",
                "Build policy engine supporting RBAC and attribute-based access control (ABAC)",
                "Create API key lifecycle management with scoped permissions and rotation",
                "Implement token service with refresh token rotation and revocation",
                "Add multi-tenant support with tenant isolation and cross-tenant policies",
                "Build real-time anomaly detection for suspicious login patterns",
                "Create admin portal with Blazor for user, role, and policy management",
                "Write comprehensive security tests including OWASP Top 10 checks"
            ]
        },
        ["testing-quality"] = new ProjectTemplate
        {
            Title = "Automated Quality Assurance Platform",
            ProblemStatement = "A testing infrastructure platform that provides contract testing for microservices, "
                + "automated API regression testing, performance benchmarking, and chaos engineering capabilities. "
                + "Integrates into CI/CD pipelines to enforce quality gates before deployment.",
            Architecture = "Platform with test orchestration engine, contract test registry (Pact-style), "
                + "API test runner with record/replay, performance benchmark harness (BenchmarkDotNet integration), "
                + "chaos experiment scheduler, and quality gate evaluator. Results stored in PostgreSQL with Grafana dashboards.",
            TechStack = [".NET 10", "C#", "xUnit", "BenchmarkDotNet", "Docker", "PostgreSQL", "Grafana", "gRPC", "WireMock", "Testcontainers"],
            TriggerSkills = ["Testing", "QA", "TDD", "CI/CD", "Quality", "Automation", "Performance Testing", "Chaos Engineering"],
            BacklogItems =
            [
                "Build test orchestration API for scheduling and managing test suites",
                "Implement contract test registry with provider verification workflows",
                "Create API regression test runner with record/replay capability",
                "Add performance benchmark harness with BenchmarkDotNet integration",
                "Build chaos experiment scheduler (network delays, service outages, resource limits)",
                "Implement quality gate evaluator for CI/CD pipeline integration",
                "Create Grafana dashboard templates for test result visualization",
                "Write self-testing meta-tests to validate the platform itself"
            ]
        }
    };

    /// <summary>
    /// Generate a portfolio project that targets specific skill gaps.
    /// </summary>
    public PortfolioProject GenerateProject(
        List<SkillGap> targetGaps,
        UserProfile profile,
        IReadOnlyList<JobVacancy> relevantVacancies)
    {
        logger.LogInformation("Generating portfolio project for {GapCount} skill gaps against {VacancyCount} vacancies",
            targetGaps.Count, relevantVacancies.Count);

        // 1. Identify most common required skills across vacancies
        var marketSkillFrequency = ComputeSkillFrequency(relevantVacancies);

        // 2. Select the best template based on gap alignment
        var (templateKey, template) = SelectBestTemplate(targetGaps, marketSkillFrequency);

        logger.LogInformation("Selected project template: {Template} ({Key})", template.Title, templateKey);

        // 3. Customize the template with user-specific context
        var customizedTechStack = CustomizeTechStack(template, targetGaps, profile);
        var customizedBacklog = CustomizeBacklog(template, targetGaps);

        // 4. Generate README
        string readme = GenerateReadme(template, customizedTechStack, targetGaps);

        // 5. Create interview narrative
        string narrative = GenerateInterviewNarrative(template, targetGaps, profile);

        // 6. Map target skill gaps
        var targetSkillGapNames = targetGaps.Select(g => g.SkillName).ToList();

        var project = new PortfolioProject
        {
            Title = template.Title,
            ProblemStatement = template.ProblemStatement,
            Architecture = template.Architecture,
            TechStack = customizedTechStack,
            Backlog = customizedBacklog,
            Readme = readme,
            InterviewNarrative = narrative,
            TargetSkillGaps = targetSkillGapNames
        };

        logger.LogInformation("Portfolio project generated: {Title} with {BacklogCount} backlog items",
            project.Title, project.Backlog.Count);

        return project;
    }

    private static Dictionary<string, int> ComputeSkillFrequency(IReadOnlyList<JobVacancy> vacancies)
    {
        var frequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var vacancy in vacancies)
        {
            foreach (var skill in vacancy.RequiredSkills.Concat(vacancy.PreferredSkills))
            {
                frequency[skill] = frequency.GetValueOrDefault(skill) + 1;
            }
        }

        return frequency;
    }

    private static (string Key, ProjectTemplate Template) SelectBestTemplate(
        List<SkillGap> targetGaps,
        Dictionary<string, int> marketSkillFrequency)
    {
        var gapSkillNames = targetGaps.Select(g => g.SkillName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        string bestKey = Templates.Keys.First();
        double bestScore = -1;

        foreach (var (key, template) in Templates)
        {
            // Score = number of trigger skills that match gaps, weighted by impact and market demand
            double score = 0;

            foreach (var triggerSkill in template.TriggerSkills)
            {
                // Direct gap match
                var matchingGap = targetGaps.FirstOrDefault(g =>
                    g.SkillName.Equals(triggerSkill, StringComparison.OrdinalIgnoreCase)
                    || triggerSkill.Contains(g.SkillName, StringComparison.OrdinalIgnoreCase)
                    || g.SkillName.Contains(triggerSkill, StringComparison.OrdinalIgnoreCase));

                if (matchingGap is not null)
                {
                    score += 10.0 * matchingGap.ImpactWeight;
                }

                // Market demand match
                if (marketSkillFrequency.TryGetValue(triggerSkill, out int freq))
                {
                    score += freq * 2.0;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestKey = key;
            }
        }

        return (bestKey, Templates[bestKey]);
    }

    private static List<string> CustomizeTechStack(
        ProjectTemplate template,
        List<SkillGap> targetGaps,
        UserProfile profile)
    {
        var stack = new List<string>(template.TechStack);

        // Add any gap skills that aren't already in the tech stack
        foreach (var gap in targetGaps)
        {
            if (!stack.Contains(gap.SkillName, StringComparer.OrdinalIgnoreCase))
            {
                // Only add if it's a technology (not a soft skill)
                if (IsTechnicalSkill(gap.SkillName))
                {
                    stack.Add(gap.SkillName);
                }
            }
        }

        // Add user's strong skills that complement the project
        foreach (var skill in profile.Skills.Where(s => s.ProficiencyLevel >= 4))
        {
            if (!stack.Contains(skill.SkillName, StringComparer.OrdinalIgnoreCase)
                && IsTechnicalSkill(skill.SkillName))
            {
                stack.Add(skill.SkillName);
                if (stack.Count >= 12) break; // Cap tech stack size
            }
        }

        return stack;
    }

    private static bool IsTechnicalSkill(string skillName)
    {
        var nonTechnical = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Communication", "Leadership", "Teamwork", "Problem Solving",
            "Time Management", "Agile", "Scrum", "Project Management",
            "Mentoring", "Presentation", "Negotiation", "Behavioral"
        };

        return !nonTechnical.Contains(skillName);
    }

    private static List<string> CustomizeBacklog(
        ProjectTemplate template,
        List<SkillGap> targetGaps)
    {
        var backlog = new List<string>(template.BacklogItems);

        // Add gap-specific tasks if gaps aren't well-covered by existing backlog
        foreach (var gap in targetGaps.Where(g => g.ImpactWeight > 0.5))
        {
            bool coveredByExisting = backlog.Any(item =>
                item.Contains(gap.SkillName, StringComparison.OrdinalIgnoreCase));

            if (!coveredByExisting && IsTechnicalSkill(gap.SkillName))
            {
                backlog.Add($"Integrate {gap.SkillName} to demonstrate proficiency (addresses identified skill gap)");
            }
        }

        return backlog;
    }

    private static string GenerateReadme(
        ProjectTemplate template,
        List<string> techStack,
        List<SkillGap> targetGaps)
    {
        var gapsList = string.Join(", ", targetGaps.Select(g => g.SkillName));
        var stackBadges = string.Join(" ", techStack.Select(t => $"![{t}](https://img.shields.io/badge/-{t.Replace(" ", "_")}-blue)"));

        return $"""
            # {template.Title}

            {stackBadges}

            ## Problem Statement

            {template.ProblemStatement}

            ## Architecture

            {template.Architecture}

            ## Tech Stack

            {string.Join(Environment.NewLine, techStack.Select(t => $"- **{t}**"))}

            ## Skills Demonstrated

            This project specifically targets and demonstrates competency in: {gapsList}.

            ## Getting Started

            ### Prerequisites

            - .NET 10 SDK
            - Docker & Docker Compose
            - Your preferred IDE (Visual Studio 2025 / Rider / VS Code)

            ### Running Locally

            ```bash
            # Clone the repository
            git clone https://github.com/yourusername/{template.Title.ToLowerInvariant().Replace(" ", "-")}.git

            # Start infrastructure dependencies
            docker-compose up -d

            # Run the application
            dotnet run --project src/Api
            ```

            ## Project Structure

            ```
            src/
              Api/            - REST API entry point
              Core/           - Domain models and interfaces
              Infrastructure/ - Data access and external integrations
              Workers/        - Background processing services
            tests/
              Unit/           - Unit tests
              Integration/    - Integration tests
              Performance/    - Benchmark tests
            ```

            ## License

            MIT
            """;
    }

    private static string GenerateInterviewNarrative(
        ProjectTemplate template,
        List<SkillGap> targetGaps,
        UserProfile profile)
    {
        var strongSkills = profile.Skills
            .Where(s => s.ProficiencyLevel >= 4)
            .Select(s => s.SkillName)
            .Take(3)
            .ToList();

        var gapNames = targetGaps.Select(g => g.SkillName).Take(3).ToList();

        return $"""
            When discussing {template.Title} in interviews, use this narrative:

            OPENING: "I built {template.Title} to solve a real problem I observed: {template.ProblemStatement.Split('.')[0].ToLowerInvariant()}."

            TECHNICAL DEPTH: "The architecture uses {template.Architecture.Split(',')[0].ToLowerInvariant()}, which I chose because it directly addresses the scalability and maintainability requirements. I specifically focused on {string.Join(" and ", gapNames)} because these were areas I wanted to strengthen."

            CHALLENGES: "The biggest challenge was designing the system to handle production-level concerns like error handling, observability, and graceful degradation. I leveraged my existing strength in {string.Join(" and ", strongSkills)} while pushing myself to gain hands-on experience with {string.Join(", ", gapNames)}."

            RESULTS: "The project demonstrates production-quality patterns including comprehensive testing, CI/CD integration, and documentation. I can walk through any component in detail and explain the trade-offs I made."

            KEY TALKING POINTS:
            - Why you chose this architecture over alternatives
            - How you handled error scenarios and edge cases
            - What you would change if building it again (shows growth mindset)
            - Specific metrics or benchmarks you measured
            """;
    }

    /// <summary>
    /// Internal template structure for project generation.
    /// </summary>
    private sealed class ProjectTemplate
    {
        public string Title { get; init; } = string.Empty;
        public string ProblemStatement { get; init; } = string.Empty;
        public string Architecture { get; init; } = string.Empty;
        public List<string> TechStack { get; init; } = [];
        public List<string> TriggerSkills { get; init; } = [];
        public List<string> BacklogItems { get; init; } = [];
    }
}
