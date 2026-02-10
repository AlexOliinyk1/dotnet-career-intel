using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for generating personalized skill gap learning paths.
/// Analyzes missing skills from vacancy matches and creates curated learning roadmaps.
/// Usage: career-intel learn-path [--skill skill-name] [--goal goal]
/// </summary>
public static class LearnPathCommand
{
    public static Command Create()
    {
        var skillOption = new Option<string?>(
            "--skill",
            description: "Specific skill to generate learning path for");

        var goalOption = new Option<string?>(
            "--goal",
            description: "Learning goal: junior-to-mid, mid-to-senior, career-switch");

        var formatOption = new Option<string>(
            name: "--format",
            getDefaultValue: () => "console",
            description: "Output format: console, json, markdown");

        var command = new Command("learn-path", "Generate personalized learning paths for skill gaps")
        {
            skillOption,
            goalOption,
            formatOption
        };

        command.SetHandler(ExecuteAsync, skillOption, goalOption, formatOption);

        return command;
    }

    private static async Task ExecuteAsync(string? skill, string? goal, string? format)
    {
        // Load profile to determine current skills
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Profile not found. Please create your profile first.");
            Console.ResetColor();
            return;
        }

        var profileJson = await File.ReadAllTextAsync(profilePath);
        var profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (profile == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Could not load profile.");
            Console.ResetColor();
            return;
        }

        if (!string.IsNullOrEmpty(skill))
        {
            // Generate path for specific skill
            GenerateSkillPath(skill, profile);
        }
        else if (!string.IsNullOrEmpty(goal))
        {
            // Generate path based on goal
            GenerateGoalBasedPath(goal, profile);
        }
        else
        {
            // Analyze vacancies and suggest top skills to learn
            await SuggestTopSkillsToLearn(profile);
        }
    }

    private static void GenerateSkillPath(string skillName, UserProfile profile)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n═══ Learning Path: {skillName} ═══\n");
        Console.ResetColor();

        var currentLevel = profile.Skills
            .FirstOrDefault(s => s.SkillName.Equals(skillName, StringComparison.OrdinalIgnoreCase))
            ?.ProficiencyLevel ?? 0;

        if (currentLevel >= 4)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"You already have {skillName} at proficiency level {currentLevel}/5.");
            Console.WriteLine("Focus on advanced topics and real-world projects.");
            Console.ResetColor();
            return;
        }

        var roadmap = GetSkillRoadmap(skillName, currentLevel);

        if (roadmap == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Learning path for '{skillName}' not found in our database.");
            Console.WriteLine("Showing generic software engineering learning path...");
            Console.ResetColor();
            roadmap = GetGenericRoadmap(currentLevel);
        }

        PrintRoadmap(roadmap, currentLevel);
    }

    private static void GenerateGoalBasedPath(string goal, UserProfile profile)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n═══ Learning Path: {goal} ═══\n");
        Console.ResetColor();

        var roadmap = goal.ToLowerInvariant() switch
        {
            "junior-to-mid" or "mid" => GetJuniorToMidRoadmap(profile),
            "mid-to-senior" or "senior" => GetMidToSeniorRoadmap(profile),
            "career-switch" or "switch" => GetCareerSwitchRoadmap(profile),
            _ => null
        };

        if (roadmap == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Unknown goal: {goal}");
            Console.WriteLine("Available goals: junior-to-mid, mid-to-senior, career-switch");
            Console.ResetColor();
            return;
        }

        PrintRoadmap(roadmap, 0);
    }

    private static async Task SuggestTopSkillsToLearn(UserProfile profile)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n═══ Top Skills to Learn (Based on Market Demand) ═══\n");
        Console.ResetColor();

        // Load latest vacancies to analyze skill gaps
        var latestFile = Directory.GetFiles(Program.DataDirectory, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (latestFile == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No vacancies data found. Run 'career-intel scan' first.");
            Console.ResetColor();
            return;
        }

        var json = await File.ReadAllTextAsync(latestFile);
        var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var userSkills = profile.Skills.Select(s => s.SkillName.ToLowerInvariant()).ToHashSet();

        var missingSkills = vacancies
            .SelectMany(v => v.RequiredSkills.Concat(v.PreferredSkills))
            .Where(s => !userSkills.Contains(s.ToLowerInvariant()))
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Skill = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        if (missingSkills.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("You already have all the in-demand skills! Consider deepening your expertise.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine("Based on current job market, prioritize learning:\n");

        var rank = 1;
        foreach (var skill in missingSkills)
        {
            var color = rank <= 3 ? ConsoleColor.Green : rank <= 6 ? ConsoleColor.Yellow : ConsoleColor.DarkYellow;
            Console.ForegroundColor = color;
            Console.Write($"  {rank}. {skill.Skill,-25}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" (mentioned in {skill.Count} vacancies)");
            Console.ResetColor();
            rank++;
        }

        Console.WriteLine("\nGenerate detailed learning path with:");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  career-intel learn-path --skill \"{missingSkills.First().Skill}\"");
        Console.ResetColor();
    }

    private static LearningRoadmap? GetSkillRoadmap(string skillName, int currentLevel)
    {
        var normalized = skillName.ToLowerInvariant();

        return normalized switch
        {
            "kubernetes" or "k8s" => new LearningRoadmap
            {
                SkillName = "Kubernetes",
                EstimatedWeeks = 8,
                Phases =
                [
                    new Phase("Foundation", 2, ["Docker basics", "Container concepts", "YAML syntax", "kubectl basics"]),
                    new Phase("Core Concepts", 3, ["Pods, Services, Deployments", "ConfigMaps & Secrets", "Namespaces", "Labels & Selectors"]),
                    new Phase("Advanced", 2, ["Helm charts", "Ingress controllers", "StatefulSets", "DaemonSets"]),
                    new Phase("Production", 1, ["Monitoring (Prometheus)", "Logging (ELK)", "Security best practices", "CI/CD integration"])
                ],
                Resources =
                [
                    new Resource("Kubernetes The Hard Way", "https://github.com/kelseyhightower/kubernetes-the-hard-way", "Tutorial"),
                    new Resource("Certified Kubernetes Administrator (CKA)", "Linux Foundation", "Certification"),
                    new Resource("Production Kubernetes", "Packt", "Book")
                ]
            },

            "azure" or "microsoft azure" => new LearningRoadmap
            {
                SkillName = "Microsoft Azure",
                EstimatedWeeks = 10,
                Phases =
                [
                    new Phase("Fundamentals", 2, ["Azure Portal", "Resource Groups", "Azure CLI", "ARM templates"]),
                    new Phase("Core Services", 3, ["App Services", "Azure Functions", "Storage accounts", "Azure SQL"]),
                    new Phase("Advanced Services", 3, ["AKS (Kubernetes)", "Service Bus", "API Management", "Application Insights"]),
                    new Phase("DevOps & Security", 2, ["Azure DevOps pipelines", "Key Vault", "Managed Identity", "Azure AD"])
                ],
                Resources =
                [
                    new Resource("Microsoft Learn - Azure Fundamentals", "https://learn.microsoft.com/azure", "Free Course"),
                    new Resource("AZ-204: Azure Developer Associate", "Microsoft", "Certification"),
                    new Resource("Azure Architecture Center", "https://learn.microsoft.com/azure/architecture", "Documentation")
                ]
            },

            "react" or "reactjs" => new LearningRoadmap
            {
                SkillName = "React",
                EstimatedWeeks = 6,
                Phases =
                [
                    new Phase("Basics", 2, ["JSX syntax", "Components", "Props & State", "Event handling"]),
                    new Phase("Hooks", 2, ["useState", "useEffect", "useContext", "Custom hooks"]),
                    new Phase("Advanced", 1, ["React Router", "State management (Redux/Zustand)", "Performance optimization", "useCallback, useMemo"]),
                    new Phase("Production", 1, ["Testing (Jest, React Testing Library)", "TypeScript with React", "Next.js basics", "Deployment"])
                ],
                Resources =
                [
                    new Resource("React Official Tutorial", "https://react.dev", "Tutorial"),
                    new Resource("Epic React by Kent C. Dodds", "https://epicreact.dev", "Course"),
                    new Resource("React Patterns", "https://reactpatterns.com", "Reference")
                ]
            },

            _ => null
        };
    }

    private static LearningRoadmap GetGenericRoadmap(int currentLevel) => new()
    {
        SkillName = "Generic Software Engineering Path",
        EstimatedWeeks = 12,
        Phases =
        [
            new Phase("Fundamentals", 3, ["Data structures", "Algorithms", "OOP principles", "Version control (Git)"]),
            new Phase("Web Development", 3, ["HTTP/REST", "Databases (SQL)", "API design", "Authentication"]),
            new Phase("Modern Stack", 4, ["Frontend framework", "Backend framework", "Cloud platform", "CI/CD"]),
            new Phase("Best Practices", 2, ["Testing", "Code review", "Documentation", "Debugging"])
        ],
        Resources =
        [
            new Resource("CS50", "Harvard", "Course"),
            new Resource("The Pragmatic Programmer", "Book", "Book"),
            new Resource("System Design Primer", "GitHub", "Repository")
        ]
    };

    private static LearningRoadmap GetJuniorToMidRoadmap(UserProfile profile) => new()
    {
        SkillName = "Junior → Mid-Level Transition",
        EstimatedWeeks = 16,
        Phases =
        [
            new Phase("Deepen Technical Skills", 4, ["Master your primary language", "Advanced data structures", "Design patterns", "Performance optimization"]),
            new Phase("System Design Basics", 4, ["Caching strategies", "Database indexing", "API design", "Microservices intro"]),
            new Phase("Ownership & Leadership", 4, ["Code reviews", "Mentoring juniors", "Technical documentation", "Project estimation"]),
            new Phase("Production Skills", 4, ["Debugging production issues", "Monitoring & logging", "Security fundamentals", "On-call rotation"])
        ],
        Resources =
        [
            new Resource("Designing Data-Intensive Applications", "Martin Kleppmann", "Book"),
            new Resource("System Design Interview", "Alex Xu", "Book"),
            new Resource("Staff Engineer", "Will Larson", "Book")
        ]
    };

    private static LearningRoadmap GetMidToSeniorRoadmap(UserProfile profile) => new()
    {
        SkillName = "Mid → Senior Transition",
        EstimatedWeeks = 24,
        Phases =
        [
            new Phase("Technical Leadership", 6, ["Architecture decisions", "Tech debt management", "Team technical direction", "RFC writing"]),
            new Phase("System Design Mastery", 6, ["Distributed systems", "Scalability patterns", "Database sharding", "Event-driven architecture"]),
            new Phase("Soft Skills", 6, ["Stakeholder management", "Cross-team collaboration", "Influencing without authority", "Conflict resolution"]),
            new Phase("Strategic Thinking", 6, ["Roadmap planning", "Build vs buy decisions", "Cost optimization", "Risk assessment"])
        ],
        Resources =
        [
            new Resource("The Staff Engineer's Path", "Tanya Reilly", "Book"),
            new Resource("Fundamentals of Software Architecture", "Mark Richards", "Book"),
            new Resource("Distributed Systems Course", "MIT 6.824", "Course")
        ]
    };

    private static LearningRoadmap GetCareerSwitchRoadmap(UserProfile profile) => new()
    {
        SkillName = "Career Switch to Software Engineering",
        EstimatedWeeks = 36,
        Phases =
        [
            new Phase("Programming Fundamentals", 8, ["Choose a language (Python/JavaScript)", "Variables, loops, functions", "Data structures", "OOP basics"]),
            new Phase("Web Development", 10, ["HTML/CSS/JavaScript", "Frontend framework (React)", "Backend basics (Node.js/Express)", "Databases (SQL)"]),
            new Phase("Projects & Portfolio", 10, ["Build 3-5 real projects", "Deploy to production", "Write documentation", "Create GitHub portfolio"]),
            new Phase("Job Prep", 8, ["LeetCode (100+ problems)", "System design basics", "Behavioral interview prep", "Resume & LinkedIn optimization"])
        ],
        Resources =
        [
            new Resource("The Odin Project", "https://theodinproject.com", "Free Bootcamp"),
            new Resource("CS50", "Harvard", "Course"),
            new Resource("100 Days of Code Challenge", "Community", "Challenge"),
            new Resource("Cracking the Coding Interview", "Gayle McDowell", "Book")
        ]
    };

    private static void PrintRoadmap(LearningRoadmap roadmap, int currentLevel)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Estimated Time: {roadmap.EstimatedWeeks} weeks");
        Console.ResetColor();
        Console.WriteLine();

        var phaseNumber = 1;
        foreach (var phase in roadmap.Phases)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Phase {phaseNumber}: {phase.Name} ({phase.Weeks} weeks)");
            Console.ResetColor();

            foreach (var topic in phase.Topics)
            {
                Console.WriteLine($"  □ {topic}");
            }
            Console.WriteLine();
            phaseNumber++;
        }

        if (roadmap.Resources.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Recommended Resources:");
            Console.ResetColor();

            foreach (var resource in roadmap.Resources)
            {
                Console.WriteLine($"  • {resource.Title} ({resource.Type})");
                if (!string.IsNullOrEmpty(resource.Url))
                    Console.WriteLine($"    {resource.Url}");
            }
        }
    }

    private sealed record LearningRoadmap
    {
        public string SkillName { get; init; } = string.Empty;
        public int EstimatedWeeks { get; init; }
        public List<Phase> Phases { get; init; } = [];
        public List<Resource> Resources { get; init; } = [];
    }

    private sealed record Phase(string Name, int Weeks, List<string> Topics);
    private sealed record Resource(string Title, string Url, string Type);
}
