using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Infers likely interview questions from job descriptions, required skills,
/// seniority signals, and company intelligence. Generates question archetypes,
/// concrete questions, expected depth, red flags, and strong answer descriptions.
/// </summary>
public sealed class InterviewPrepEngine(ILogger<InterviewPrepEngine> logger)
{
    /// <summary>
    /// Generates a complete interview prep plan for a specific vacancy.
    /// </summary>
    public InterviewPrepPlan GeneratePrepPlan(
        JobVacancy vacancy,
        UserProfile profile,
        CompanyProfile? companyProfile = null,
        IReadOnlyList<InterviewFeedback>? interviewHistory = null)
    {
        logger.LogInformation("Generating interview prep for '{Title}' at {Company} ({Seniority})",
            vacancy.Title, vacancy.Company, vacancy.SeniorityLevel);

        var skillSets = new List<SkillQuestionSet>();

        // Generate questions for each required skill
        foreach (var skill in vacancy.RequiredSkills)
        {
            var depth = InferExpectedDepth(skill, vacancy.SeniorityLevel, companyProfile);
            var userSkill = profile.Skills
                .FirstOrDefault(s => s.SkillName.Equals(skill, StringComparison.OrdinalIgnoreCase));
            var questions = GenerateSkillQuestions(skill, depth, vacancy.SeniorityLevel, companyProfile);
            var redFlags = InferRedFlags(skill, depth, vacancy.SeniorityLevel);
            var strongAnswer = DescribeStrongAnswer(skill, depth, vacancy.SeniorityLevel);

            skillSets.Add(new SkillQuestionSet
            {
                SkillName = skill,
                IsRequired = true,
                ExpectedDepth = depth,
                DepthLabel = DepthToLabel(depth),
                Questions = questions,
                RedFlags = redFlags,
                StrongAnswerSummary = strongAnswer
            });
        }

        // Generate questions for preferred skills (lower depth expectation)
        foreach (var skill in vacancy.PreferredSkills)
        {
            if (skillSets.Any(s => s.SkillName.Equals(skill, StringComparison.OrdinalIgnoreCase)))
                continue;

            var depth = Math.Max(1, InferExpectedDepth(skill, vacancy.SeniorityLevel, companyProfile) - 1);
            var questions = GenerateSkillQuestions(skill, depth, vacancy.SeniorityLevel, companyProfile);

            skillSets.Add(new SkillQuestionSet
            {
                SkillName = skill,
                IsRequired = false,
                ExpectedDepth = depth,
                DepthLabel = DepthToLabel(depth),
                Questions = questions,
                RedFlags = [],
                StrongAnswerSummary = DescribeStrongAnswer(skill, depth, vacancy.SeniorityLevel)
            });
        }

        // Behavioral questions based on seniority + company style
        var behavioral = GenerateBehavioralQuestions(vacancy, companyProfile);

        // System design questions based on description + seniority
        var systemDesign = GenerateSystemDesignQuestions(vacancy, companyProfile);

        // Global red flags
        var globalRedFlags = InferGlobalRedFlags(vacancy, companyProfile, interviewHistory);

        // Strategy
        var strategy = BuildOverallStrategy(vacancy, profile, companyProfile, skillSets);

        // Estimate prep hours
        int prepHours = EstimatePrepHours(skillSets, profile, vacancy.SeniorityLevel);

        logger.LogInformation("Prep plan: {SkillCount} skill sets, {BehavioralCount} behavioral, {DesignCount} design, {Hours}h estimated",
            skillSets.Count, behavioral.Count, systemDesign.Count, prepHours);

        return new InterviewPrepPlan
        {
            VacancyId = vacancy.Id,
            VacancyTitle = vacancy.Title,
            Company = vacancy.Company,
            SkillSets = skillSets,
            BehavioralQuestions = behavioral,
            SystemDesignQuestions = systemDesign,
            RedFlags = globalRedFlags,
            OverallStrategy = strategy,
            EstimatedPrepHours = prepHours
        };
    }

    /// <summary>
    /// Infers the expected interview depth for a skill based on seniority and company profile.
    /// Returns 1-5: 1=awareness, 2=usage, 3=internals, 4=tradeoffs, 5=expert design.
    /// </summary>
    private static int InferExpectedDepth(string skill, SeniorityLevel seniority, CompanyProfile? company)
    {
        int baseDepth = seniority switch
        {
            SeniorityLevel.Intern => 1,
            SeniorityLevel.Junior => 2,
            SeniorityLevel.Middle => 3,
            SeniorityLevel.Senior => 4,
            SeniorityLevel.Lead or SeniorityLevel.Architect or SeniorityLevel.Principal => 5,
            _ => 3
        };

        // Adjust for company difficulty
        if (company is not null && company.DifficultyBar >= 8)
            baseDepth = Math.Min(5, baseDepth + 1);

        // Core skills get higher depth expectations
        if (IsCoreSkill(skill))
            baseDepth = Math.Min(5, baseDepth + 1);

        return Math.Clamp(baseDepth, 1, 5);
    }

    private static bool IsCoreSkill(string skill)
    {
        var lower = skill.ToLowerInvariant();
        return lower is "c#" or ".net" or "asp.net" or "asp.net core" or "entity framework"
            or "sql" or "sql server" or "system design" or "architecture"
            or "algorithms" or "data structures";
    }

    private static List<InferredQuestion> GenerateSkillQuestions(
        string skill, int depth, SeniorityLevel seniority, CompanyProfile? company)
    {
        var questions = new List<InferredQuestion>();
        var archetypeTemplates = GetArchetypeTemplates(skill);

        // Always include "why" and "how" archetypes
        questions.Add(CreateQuestion(skill, QuestionArchetype.WhyChoice, depth, seniority, archetypeTemplates));
        questions.Add(CreateQuestion(skill, QuestionArchetype.HowItWorks, depth, seniority, archetypeTemplates));

        // Depth >= 3: add performance and failure modes
        if (depth >= 3)
        {
            questions.Add(CreateQuestion(skill, QuestionArchetype.Performance, depth, seniority, archetypeTemplates));
            questions.Add(CreateQuestion(skill, QuestionArchetype.FailureModes, depth, seniority, archetypeTemplates));
        }

        // Depth >= 4: add tradeoffs and debugging
        if (depth >= 4)
        {
            questions.Add(CreateQuestion(skill, QuestionArchetype.Tradeoffs, depth, seniority, archetypeTemplates));
            questions.Add(CreateQuestion(skill, QuestionArchetype.Debugging, depth, seniority, archetypeTemplates));
        }

        // Depth 5: add security and live coding
        if (depth >= 5)
        {
            questions.Add(CreateQuestion(skill, QuestionArchetype.Security, depth, seniority, archetypeTemplates));
            questions.Add(CreateQuestion(skill, QuestionArchetype.LiveCoding, depth, seniority, archetypeTemplates));
        }

        // High-difficulty companies add extra questions
        if (company is not null && company.DifficultyBar >= 7)
        {
            questions.Add(CreateQuestion(skill, QuestionArchetype.LiveCoding, depth, seniority, archetypeTemplates));
        }

        return questions;
    }

    private static InferredQuestion CreateQuestion(
        string skill, QuestionArchetype archetype, int depth,
        SeniorityLevel seniority, Dictionary<QuestionArchetype, ArchetypeTemplate> templates)
    {
        var template = templates.GetValueOrDefault(archetype) ?? GetDefaultTemplate(archetype);

        var question = string.Format(template.QuestionTemplate, skill);
        var strongAnswer = string.Format(template.StrongAnswerTemplate, skill);
        var weakAnswer = string.Format(template.WeakAnswerTemplate, skill);
        var whyAsked = string.Format(template.WhyAskedTemplate, skill);

        // Adjust difficulty based on depth + seniority
        int difficulty = archetype switch
        {
            QuestionArchetype.WhyChoice => Math.Min(5, depth),
            QuestionArchetype.HowItWorks => Math.Min(5, depth + 1),
            QuestionArchetype.Performance => Math.Min(5, depth + 1),
            QuestionArchetype.FailureModes => Math.Min(5, depth),
            QuestionArchetype.Tradeoffs => Math.Min(5, depth + 1),
            QuestionArchetype.Debugging => Math.Min(5, depth),
            QuestionArchetype.Security => Math.Min(5, depth + 1),
            QuestionArchetype.LiveCoding => Math.Min(5, depth + 1),
            QuestionArchetype.SystemDesign => Math.Min(5, depth + 1),
            QuestionArchetype.Behavioral => 3,
            _ => depth
        };

        return new InferredQuestion
        {
            Question = question,
            Archetype = archetype,
            ArchetypeLabel = ArchetypeToLabel(archetype),
            Difficulty = difficulty,
            WhyAsked = whyAsked,
            StrongAnswer = strongAnswer,
            WeakAnswer = weakAnswer
        };
    }

    private static Dictionary<QuestionArchetype, ArchetypeTemplate> GetArchetypeTemplates(string skill)
    {
        // Return skill-specific templates if available, otherwise defaults
        var lower = skill.ToLowerInvariant();

        var templates = new Dictionary<QuestionArchetype, ArchetypeTemplate>
        {
            [QuestionArchetype.WhyChoice] = new(
                $"Why would you choose {skill} for this project? What alternatives did you consider?",
                $"A strong answer discusses specific tradeoffs, mentions concrete alternatives to {skill}, and ties the choice to project requirements (team size, latency, throughput, maintainability).",
                $"A weak answer is vague ('it's popular', 'the team already uses it') without demonstrating understanding of {skill}'s strengths and weaknesses relative to alternatives.",
                $"Tests whether the candidate has independent judgment about {skill} vs alternatives, not just familiarity."
            ),
            [QuestionArchetype.HowItWorks] = new(
                $"How does {skill} work under the hood? Walk me through the key internals.",
                $"A strong answer explains {skill}'s core architecture, key abstractions, and how they interact — demonstrating understanding beyond API surface.",
                $"A weak answer stays at the API surface ('you call this method, it returns that') without understanding what happens internally.",
                $"Tests depth of understanding of {skill} — can the candidate debug, optimize, and make informed design decisions?"
            ),
            [QuestionArchetype.Performance] = new(
                $"How would you diagnose and fix a performance problem related to {skill}?",
                $"A strong answer names specific profiling tools, common {skill} bottlenecks, and concrete optimization strategies with measured impact.",
                $"A weak answer is generic ('add caching', 'optimize queries') without {skill}-specific knowledge or measurement approach.",
                $"Tests whether the candidate can handle production performance issues with {skill}."
            ),
            [QuestionArchetype.FailureModes] = new(
                $"What are the common failure modes when using {skill}? How do you handle them?",
                $"A strong answer describes specific failure scenarios in {skill} (resource exhaustion, race conditions, data corruption), detection methods, and recovery strategies.",
                $"A weak answer is generic ('handle exceptions', 'add retry logic') without understanding {skill}-specific failure patterns.",
                $"Tests production readiness — has the candidate operated {skill} in anger and handled real failures?"
            ),
            [QuestionArchetype.Tradeoffs] = GetTradeoffTemplate(lower, skill),
            [QuestionArchetype.Debugging] = new(
                $"A {skill}-related component is returning incorrect results intermittently. Walk me through your debugging approach.",
                $"A strong answer uses a systematic approach: reproduce, isolate, instrument, hypothesize, verify. Mentions {skill}-specific debugging tools and techniques.",
                $"A weak answer jumps to conclusions or uses trial-and-error without systematic reasoning.",
                $"Tests diagnostic thinking and {skill}-specific debugging skills."
            ),
            [QuestionArchetype.Security] = new(
                $"What are the security considerations when using {skill}? What would you audit?",
                $"A strong answer identifies {skill}-specific attack vectors, secure configuration practices, and audit points relevant to the technology.",
                $"A weak answer is generic ('use HTTPS', 'sanitize input') without {skill}-specific security knowledge.",
                $"Tests security awareness specifically for {skill} in production systems."
            ),
            [QuestionArchetype.LiveCoding] = new(
                $"Implement a component using {skill} that handles [specific scenario]. Explain your design choices as you code.",
                $"A strong answer demonstrates fluent {skill} usage, clean code structure, handles edge cases, and articulates design decisions while coding.",
                $"A weak answer struggles with {skill} syntax, misses edge cases, or writes code without explaining the reasoning.",
                $"Tests practical fluency with {skill} under pressure."
            )
        };

        return templates;
    }

    private static ArchetypeTemplate GetTradeoffTemplate(string lowerSkill, string skill)
    {
        // Provide contextual tradeoff comparisons for known technologies
        string comparison = lowerSkill switch
        {
            "c#" => "C# vs Go or Java",
            ".net" or "asp.net" or "asp.net core" => ".NET vs Node.js or Spring Boot",
            "entity framework" or "ef core" => "EF Core vs Dapper vs raw ADO.NET",
            "sql server" => "SQL Server vs PostgreSQL",
            "postgresql" or "postgres" => "PostgreSQL vs SQL Server",
            "mongodb" => "MongoDB vs PostgreSQL",
            "redis" => "Redis vs Memcached",
            "rabbitmq" => "RabbitMQ vs Kafka",
            "kafka" => "Kafka vs RabbitMQ",
            "docker" => "Docker vs direct deployment",
            "kubernetes" or "k8s" => "Kubernetes vs simpler orchestration",
            "azure" => "Azure vs AWS",
            "aws" => "AWS vs Azure",
            "react" => "React vs Angular vs Vue",
            "angular" => "Angular vs React",
            "grpc" => "gRPC vs REST",
            "graphql" => "GraphQL vs REST",
            "microservices" => "microservices vs monolith",
            _ => $"{skill} vs its main alternatives"
        };

        return new ArchetypeTemplate(
            $"Compare {comparison}. When would you choose each? What are the tradeoffs?",
            $"A strong answer provides concrete criteria (latency, throughput, team expertise, ecosystem) and explains when each option wins with real examples.",
            $"A weak answer picks a favorite without articulating tradeoffs or only states opinions without technical backing.",
            $"Tests architectural thinking — can the candidate make informed technology choices for different scenarios?"
        );
    }

    private static ArchetypeTemplate GetDefaultTemplate(QuestionArchetype archetype)
    {
        return new ArchetypeTemplate(
            $"Tell me about your experience with this technology. ({archetype})",
            "A strong answer provides specific examples with measurable outcomes.",
            "A weak answer is vague and lacks concrete examples.",
            "Tests practical experience and depth of understanding."
        );
    }

    private static List<InferredQuestion> GenerateBehavioralQuestions(
        JobVacancy vacancy, CompanyProfile? company)
    {
        var questions = new List<InferredQuestion>();
        var seniority = vacancy.SeniorityLevel;

        // Universal behavioral questions
        questions.Add(new InferredQuestion
        {
            Question = "Tell me about a time you disagreed with a technical decision. How did you handle it?",
            Archetype = QuestionArchetype.Behavioral,
            ArchetypeLabel = "Behavioral / Conflict",
            Difficulty = 3,
            WhyAsked = "Tests communication skills, ability to influence without authority, and how you handle disagreement professionally.",
            StrongAnswer = "Uses STAR format. Shows active listening, data-driven argumentation, and willingness to commit even after disagreement. Highlights a positive outcome.",
            WeakAnswer = "Complains about the other person, shows inability to compromise, or claims they never disagree."
        });

        questions.Add(new InferredQuestion
        {
            Question = "Describe a production incident you were involved in. What happened and how did you respond?",
            Archetype = QuestionArchetype.Behavioral,
            ArchetypeLabel = "Behavioral / Incident",
            Difficulty = 3,
            WhyAsked = "Tests incident response skills, calmness under pressure, and postmortem mindset.",
            StrongAnswer = "Describes a systematic approach: triage, communicate, fix, postmortem. Shows ownership without blame. Mentions what changed to prevent recurrence.",
            WeakAnswer = "Blames others, doesn't mention postmortem or prevention, or claims they've never had a production incident."
        });

        // Seniority-specific behavioral questions
        if (seniority >= SeniorityLevel.Senior)
        {
            questions.Add(new InferredQuestion
            {
                Question = "Tell me about a time you mentored a junior developer. What was your approach and what was the outcome?",
                Archetype = QuestionArchetype.Behavioral,
                ArchetypeLabel = "Behavioral / Leadership",
                Difficulty = 3,
                WhyAsked = "Tests mentorship skills and willingness to invest in team growth — expected at senior+ level.",
                StrongAnswer = "Describes a structured approach to mentoring, tailored to the mentee's learning style, with measurable growth outcomes.",
                WeakAnswer = "Vague ('I helped them with code reviews') or shows impatience with junior developers."
            });
        }

        if (seniority >= SeniorityLevel.Lead)
        {
            questions.Add(new InferredQuestion
            {
                Question = "How do you balance technical debt reduction with feature delivery? Give me a real example.",
                Archetype = QuestionArchetype.Behavioral,
                ArchetypeLabel = "Behavioral / Prioritization",
                Difficulty = 4,
                WhyAsked = "Tests strategic thinking and ability to make pragmatic engineering decisions at the team/org level.",
                StrongAnswer = "Shows a framework for evaluating tech debt ROI, demonstrates stakeholder communication, and gives a concrete example with outcome.",
                WeakAnswer = "Either 'always pay off debt' or 'features always first' — missing nuance and context-dependent thinking."
            });
        }

        // Company style adjustments
        if (company?.InterviewStyle is "FAANG-like")
        {
            questions.Add(new InferredQuestion
            {
                Question = "Tell me about the most complex system you designed or significantly contributed to. What were the key technical challenges?",
                Archetype = QuestionArchetype.Behavioral,
                ArchetypeLabel = "Behavioral / Impact",
                Difficulty = 4,
                WhyAsked = "FAANG-style companies look for evidence of large-scale impact and complex problem-solving ability.",
                StrongAnswer = "Describes a system with clear scale metrics, explains key design decisions and their tradeoffs, and quantifies impact.",
                WeakAnswer = "Describes a small feature without scale considerations or can't articulate why design decisions were made."
            });
        }

        return questions;
    }

    private static List<InferredQuestion> GenerateSystemDesignQuestions(
        JobVacancy vacancy, CompanyProfile? company)
    {
        var questions = new List<InferredQuestion>();

        // System design is expected at Middle+ level
        if (vacancy.SeniorityLevel < SeniorityLevel.Middle)
            return questions;

        // Infer relevant design scenarios from description keywords
        var lower = (vacancy.Description + " " + vacancy.Title).ToLowerInvariant();

        if (lower.Contains("api") || lower.Contains("rest") || lower.Contains("microservice"))
        {
            questions.Add(new InferredQuestion
            {
                Question = "Design a scalable API gateway for a microservices architecture. How would you handle authentication, rate limiting, and routing?",
                Archetype = QuestionArchetype.SystemDesign,
                ArchetypeLabel = "System Design / API",
                Difficulty = 4,
                WhyAsked = "Tests ability to design distributed system components that are production-ready.",
                StrongAnswer = "Covers load balancing, auth propagation, circuit breaking, observability. Discusses tradeoffs between reverse proxy vs custom gateway.",
                WeakAnswer = "Only describes basic routing without considering failure modes, security, or operational concerns."
            });
        }

        if (lower.Contains("database") || lower.Contains("sql") || lower.Contains("data"))
        {
            questions.Add(new InferredQuestion
            {
                Question = "Design a data pipeline that processes millions of records daily. How do you ensure reliability and exactly-once processing?",
                Archetype = QuestionArchetype.SystemDesign,
                ArchetypeLabel = "System Design / Data",
                Difficulty = 4,
                WhyAsked = "Tests understanding of data processing at scale, consistency guarantees, and fault tolerance.",
                StrongAnswer = "Discusses idempotency, checkpointing, dead letter queues, partitioning strategy. Considers both batch and stream processing tradeoffs.",
                WeakAnswer = "Describes a simple ETL without considering failures, scalability, or exactly-once semantics."
            });
        }

        if (lower.Contains("event") || lower.Contains("queue") || lower.Contains("message") || lower.Contains("async"))
        {
            questions.Add(new InferredQuestion
            {
                Question = "Design an event-driven system for order processing. How do you handle eventual consistency and out-of-order events?",
                Archetype = QuestionArchetype.SystemDesign,
                ArchetypeLabel = "System Design / Events",
                Difficulty = 4,
                WhyAsked = "Tests understanding of event-driven architectures and distributed systems challenges.",
                StrongAnswer = "Covers saga pattern, event sourcing, idempotent consumers, ordering guarantees vs performance tradeoffs. Discusses compensating transactions.",
                WeakAnswer = "Describes fire-and-forget messaging without addressing consistency, ordering, or failure recovery."
            });
        }

        if (lower.Contains("cache") || lower.Contains("performance") || lower.Contains("scale"))
        {
            questions.Add(new InferredQuestion
            {
                Question = "Design a caching strategy for a high-traffic application. How do you handle cache invalidation and consistency?",
                Archetype = QuestionArchetype.SystemDesign,
                ArchetypeLabel = "System Design / Caching",
                Difficulty = 4,
                WhyAsked = "Tests understanding of caching patterns, consistency tradeoffs, and performance engineering.",
                StrongAnswer = "Discusses cache-aside, write-through, TTL strategies, stampede prevention. Articulates when caching hurts more than it helps.",
                WeakAnswer = "Says 'use Redis' without discussing invalidation strategy, consistency model, or when NOT to cache."
            });
        }

        // Senior+ always gets at least one system design question
        if (questions.Count == 0 && vacancy.SeniorityLevel >= SeniorityLevel.Senior)
        {
            questions.Add(new InferredQuestion
            {
                Question = "Design a notification system that supports email, SMS, and push notifications with delivery guarantees and user preferences.",
                Archetype = QuestionArchetype.SystemDesign,
                ArchetypeLabel = "System Design / General",
                Difficulty = 4,
                WhyAsked = "Standard system design question testing ability to design scalable, reliable distributed systems.",
                StrongAnswer = "Covers pub/sub architecture, delivery guarantees, preference management, rate limiting, template system, observability.",
                WeakAnswer = "Describes synchronous notification sending without considering scale, reliability, or user preferences."
            });
        }

        return questions;
    }

    private static List<string> InferRedFlags(string skill, int depth, SeniorityLevel seniority)
    {
        var redFlags = new List<string>();

        // Universal red flags
        redFlags.Add($"Cannot explain basic concepts of {skill} at conversational level");

        if (depth >= 3)
            redFlags.Add($"Uses {skill} APIs but cannot explain what happens underneath");

        if (depth >= 4)
        {
            redFlags.Add($"Cannot articulate tradeoffs of {skill} vs alternatives");
            redFlags.Add($"No experience with {skill} failure modes in production");
        }

        if (seniority >= SeniorityLevel.Senior)
            redFlags.Add($"Cannot explain when NOT to use {skill}");

        return redFlags;
    }

    private static string DescribeStrongAnswer(string skill, int depth, SeniorityLevel seniority)
    {
        return depth switch
        {
            1 => $"Can explain what {skill} is, when it's used, and has seen it in action. Awareness-level understanding is sufficient.",
            2 => $"Has used {skill} in real projects, can write working code, knows the common APIs and patterns. Usage-level proficiency.",
            3 => $"Understands {skill} internals, can diagnose issues, knows performance characteristics. Can explain why {skill} works the way it does.",
            4 => $"Can compare {skill} to alternatives with concrete tradeoffs, has production experience including failure handling. Makes informed architecture decisions involving {skill}.",
            5 => $"Expert-level {skill} knowledge: can design systems around it, mentor others, contribute to the ecosystem. Knows edge cases, performance tuning, and security implications.",
            _ => $"Demonstrates practical experience with {skill} proportional to the role's seniority level."
        };
    }

    private static List<string> InferGlobalRedFlags(
        JobVacancy vacancy, CompanyProfile? company, IReadOnlyList<InterviewFeedback>? history)
    {
        var flags = new List<string>();

        if (company is not null)
        {
            foreach (var reason in company.CommonRejectionReasons)
                flags.Add($"Company pattern: {reason}");

            if (company.DifficultyBar >= 8)
                flags.Add("High-difficulty interviewer — expect deep probing and follow-up questions on every answer");
        }

        if (history is not null)
        {
            var repeating = history
                .SelectMany(h => h.WeakAreas)
                .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= 2)
                .Select(g => g.Key);

            foreach (var weakness in repeating)
                flags.Add($"YOUR repeating weakness: {weakness} — prepare extra for this");
        }

        if (vacancy.SeniorityLevel >= SeniorityLevel.Lead)
            flags.Add("Leadership role — expect questions about team management, code review philosophy, and architectural decision-making");

        return flags;
    }

    private static string BuildOverallStrategy(
        JobVacancy vacancy, UserProfile profile,
        CompanyProfile? company, List<SkillQuestionSet> skillSets)
    {
        var parts = new List<string>();

        // Identify strongest and weakest areas
        var requiredSkills = skillSets.Where(s => s.IsRequired).ToList();
        var strongSkills = requiredSkills
            .Where(s => profile.Skills.Any(ps =>
                ps.SkillName.Equals(s.SkillName, StringComparison.OrdinalIgnoreCase)
                && ps.ProficiencyLevel >= s.ExpectedDepth))
            .Select(s => s.SkillName)
            .ToList();

        var gapSkills = requiredSkills
            .Where(s => !profile.Skills.Any(ps =>
                ps.SkillName.Equals(s.SkillName, StringComparison.OrdinalIgnoreCase)
                && ps.ProficiencyLevel >= s.ExpectedDepth))
            .Select(s => s.SkillName)
            .ToList();

        if (strongSkills.Count > 0)
            parts.Add($"Lead with your strengths: {string.Join(", ", strongSkills.Take(3))}. Use these to build confidence early.");

        if (gapSkills.Count > 0)
            parts.Add($"Prepare extra for gaps: {string.Join(", ", gapSkills.Take(3))}. Focus on fundamentals and common failure modes.");

        if (company?.InterviewStyle is "FAANG-like")
            parts.Add("Expect structured interviews with strict time boxes. Practice thinking aloud and structuring answers with clear frameworks.");
        else if (company?.InterviewStyle is "Startup")
            parts.Add("Expect practical, hands-on questions. Emphasize speed of delivery, pragmatism, and ability to work across the stack.");
        else if (company?.InterviewStyle is "Outsource")
            parts.Add("Expect breadth over depth. Demonstrate versatility across technologies and ability to ramp up quickly on new projects.");

        if (vacancy.SeniorityLevel >= SeniorityLevel.Senior)
            parts.Add("At this seniority, 'why' matters more than 'how'. Explain your reasoning, tradeoffs considered, and decisions made — not just implementation details.");

        return string.Join(" ", parts);
    }

    private static int EstimatePrepHours(
        List<SkillQuestionSet> skillSets, UserProfile profile, SeniorityLevel seniority)
    {
        int hours = 0;

        foreach (var set in skillSets)
        {
            var userSkill = profile.Skills
                .FirstOrDefault(s => s.SkillName.Equals(set.SkillName, StringComparison.OrdinalIgnoreCase));

            int userLevel = userSkill?.ProficiencyLevel ?? 0;
            int gap = Math.Max(0, set.ExpectedDepth - userLevel);

            // Hours per depth gap level
            hours += gap switch
            {
                0 => 1, // quick review
                1 => 3,
                2 => 6,
                3 => 12,
                _ => 20
            };
        }

        // Add behavioral + system design prep time
        if (seniority >= SeniorityLevel.Middle)
            hours += 4; // system design prep
        hours += 2; // behavioral prep

        return hours;
    }

    private static string DepthToLabel(int depth) => depth switch
    {
        1 => "Awareness — know what it is and when it's used",
        2 => "Usage — can write working code and use common APIs",
        3 => "Internals — understands how it works under the hood",
        4 => "Tradeoffs — can compare alternatives and make architecture decisions",
        5 => "Expert — can design systems, mentor others, handle edge cases",
        _ => "Unknown depth"
    };

    private static string ArchetypeToLabel(QuestionArchetype archetype) => archetype switch
    {
        QuestionArchetype.WhyChoice => "Why / Decision Reasoning",
        QuestionArchetype.HowItWorks => "How / Internal Knowledge",
        QuestionArchetype.Performance => "Performance Engineering",
        QuestionArchetype.FailureModes => "Failure Modes & Recovery",
        QuestionArchetype.SystemDesign => "System Design",
        QuestionArchetype.Tradeoffs => "Tradeoff Analysis",
        QuestionArchetype.Debugging => "Diagnostic Thinking",
        QuestionArchetype.Behavioral => "Behavioral / STAR",
        QuestionArchetype.LiveCoding => "Live Coding",
        QuestionArchetype.Security => "Security Awareness",
        _ => archetype.ToString()
    };

    private sealed record ArchetypeTemplate(
        string QuestionTemplate,
        string StrongAnswerTemplate,
        string WeakAnswerTemplate,
        string WhyAskedTemplate);
}
