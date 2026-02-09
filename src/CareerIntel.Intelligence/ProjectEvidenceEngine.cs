using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

public sealed class ProjectEvidenceEngine(ILogger<ProjectEvidenceEngine> logger)
{
    /// <summary>
    /// Generate market-shaped evidence for a portfolio project, linking it to
    /// market signals, skill gaps, and interview-ready narratives.
    /// </summary>
    public ProjectEvidence GenerateEvidence(
        PortfolioProject project,
        MarketSnapshot snapshot,
        List<SkillGap> gaps,
        UserProfile profile)
    {
        logger.LogInformation("Generating project evidence for '{Title}' against {GapCount} gaps",
            project.Title, gaps.Count);

        var techStackSet = new HashSet<string>(project.TechStack, StringComparer.OrdinalIgnoreCase);

        var signals = ExtractMarketSignals(techStackSet, snapshot);
        var addressedGaps = MapAddressedGaps(project, gaps);
        var demonstratedCombinations = FindDemonstratedCombinations(techStackSet, snapshot);
        var salaryImpact = ComputeSalaryImpact(techStackSet, snapshot);
        var complexity = DetermineComplexity(techStackSet, snapshot);
        var talkingPoints = GenerateTalkingPoints(project, gaps, profile);
        var starStories = GenerateStarStories(project, profile);

        var evidence = new ProjectEvidence
        {
            ProjectTitle = project.Title,
            DrivingSignals = signals,
            AddressedGaps = addressedGaps,
            DemonstratedCombinations = demonstratedCombinations,
            EstimatedSalaryImpact = salaryImpact,
            MarketAlignedComplexity = complexity,
            TalkingPoints = talkingPoints,
            StarStories = starStories
        };

        logger.LogInformation(
            "Evidence generated: {SignalCount} signals, {GapCount} addressed gaps, " +
            "{ComboCount} demonstrated combinations, complexity={Complexity}",
            signals.Count, addressedGaps.Count, demonstratedCombinations.Count, complexity);

        return evidence;
    }

    /// <summary>
    /// Rank portfolio projects by how well their tech stacks align with market demand.
    /// Returns projects sorted by market fit score descending.
    /// </summary>
    public List<PortfolioProject> RankProjectsByMarketFit(
        IReadOnlyList<PortfolioProject> projects,
        MarketSnapshot snapshot)
    {
        logger.LogInformation("Ranking {Count} projects by market fit", projects.Count);

        var scored = new List<(PortfolioProject Project, double Score)>();

        foreach (var project in projects)
        {
            var techStackSet = new HashSet<string>(project.TechStack, StringComparer.OrdinalIgnoreCase);
            double score = 0;

            // Score from individual skill frequency
            foreach (var skill in project.TechStack)
            {
                if (snapshot.SkillFrequency.TryGetValue(skill, out int freq))
                {
                    score += freq;
                }
            }

            // Bonus for matching top skill combinations
            foreach (var combo in snapshot.TopSkillCombinations)
            {
                int matchCount = combo.Skills.Count(s => techStackSet.Contains(s));
                if (matchCount == combo.Skills.Count && combo.Skills.Count > 0)
                {
                    // Full combination match: bonus weighted by frequency and salary
                    score += combo.Frequency * 5.0 + (double)combo.AverageSalary / 10_000;
                }
                else if (matchCount > 0)
                {
                    // Partial match: proportional bonus
                    double coverage = (double)matchCount / combo.Skills.Count;
                    score += combo.Frequency * coverage * 2.0;
                }
            }

            scored.Add((project, score));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Select(x => x.Project)
            .ToList();
    }

    /// <summary>
    /// Generate a 30-second elevator pitch combining the project's market
    /// relevance with the user's strengths.
    /// </summary>
    public string GenerateElevatorPitch(ProjectEvidence evidence, UserProfile profile)
    {
        logger.LogInformation("Generating elevator pitch for '{Title}'", evidence.ProjectTitle);

        var topStrengths = profile.Skills
            .Where(s => s.ProficiencyLevel >= 4)
            .OrderByDescending(s => s.YearsOfExperience)
            .Take(3)
            .Select(s => s.SkillName)
            .ToList();

        var strengthsPhrase = topStrengths.Count > 0
            ? string.Join(", ", topStrengths)
            : "modern software engineering";

        var gapPhrase = evidence.AddressedGaps.Count > 0
            ? string.Join(", ", evidence.AddressedGaps.Take(3))
            : "in-demand technologies";

        var topSignal = evidence.DrivingSignals
            .OrderByDescending(s => s.Strength)
            .FirstOrDefault();

        var marketHook = topSignal is not null
            ? $"Market data shows {topSignal.Description.ToLowerInvariant()}, and "
            : "";

        var salaryNote = evidence.EstimatedSalaryImpact > 0
            ? $" Skills demonstrated in this project correlate with a ${evidence.EstimatedSalaryImpact:N0} salary premium in current market data."
            : "";

        return $"""
            I built {evidence.ProjectTitle} to bridge a gap I identified between my strengths in {strengthsPhrase} and market demand for {gapPhrase}. {marketHook}this project directly demonstrates those capabilities through a {evidence.MarketAlignedComplexity.ToLowerInvariant()}-complexity, production-quality implementation.{salaryNote} I can walk you through any architectural decision, the trade-offs I evaluated, and the measurable outcomes I achieved.
            """;
    }

    private static List<MarketSignal> ExtractMarketSignals(
        HashSet<string> techStack,
        MarketSnapshot snapshot)
    {
        var signals = new List<MarketSignal>();

        // Skill demand signals: top skills from snapshot that overlap with project
        foreach (var (skill, frequency) in snapshot.SkillFrequency
            .Where(kv => techStack.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Take(5))
        {
            double strength = Math.Min(1.0, frequency / (double)Math.Max(1, snapshot.TotalVacancies));
            signals.Add(new MarketSignal
            {
                SignalType = "SkillDemand",
                Description = $"{skill} appears in {frequency} of {snapshot.TotalVacancies} vacancies ({strength:P0} of market)",
                Strength = strength
            });
        }

        // Skill combination signals
        foreach (var combo in snapshot.TopSkillCombinations
            .Where(c => c.Skills.Any(s => techStack.Contains(s)))
            .OrderByDescending(c => c.Frequency)
            .Take(3))
        {
            int covered = combo.Skills.Count(s => techStack.Contains(s));
            double coverage = (double)covered / combo.Skills.Count;
            signals.Add(new MarketSignal
            {
                SignalType = "SkillCombination",
                Description = $"Combination [{string.Join(" + ", combo.Skills)}] seen in {combo.Frequency} vacancies ({covered}/{combo.Skills.Count} skills covered)",
                Strength = coverage
            });
        }

        // Salary correlation signals
        foreach (var combo in snapshot.TopSkillCombinations
            .Where(c => c.AverageSalary > 0 && c.Skills.All(s => techStack.Contains(s)))
            .OrderByDescending(c => c.AverageSalary)
            .Take(2))
        {
            signals.Add(new MarketSignal
            {
                SignalType = "SalaryCorrelation",
                Description = $"Combination [{string.Join(" + ", combo.Skills)}] correlates with ${combo.AverageSalary:N0} average salary",
                Strength = Math.Min(1.0, (double)combo.AverageSalary / 200_000.0)
            });
        }

        return signals;
    }

    private static List<string> MapAddressedGaps(PortfolioProject project, List<SkillGap> gaps)
    {
        var gapNames = gaps
            .Select(g => g.SkillName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return project.TargetSkillGaps
            .Where(tsg => gapNames.Contains(tsg))
            .ToList();
    }

    private static List<string> FindDemonstratedCombinations(
        HashSet<string> techStack,
        MarketSnapshot snapshot)
    {
        return snapshot.TopSkillCombinations
            .Where(c => c.Skills.All(s => techStack.Contains(s)))
            .Select(c => string.Join(" + ", c.Skills))
            .ToList();
    }

    private static decimal ComputeSalaryImpact(
        HashSet<string> techStack,
        MarketSnapshot snapshot)
    {
        var matchingCombos = snapshot.TopSkillCombinations
            .Where(c => c.Skills.All(s => techStack.Contains(s)) && c.AverageSalary > 0)
            .ToList();

        if (matchingCombos.Count == 0)
            return 0m;

        return matchingCombos.Average(c => c.AverageSalary);
    }

    private static string DetermineComplexity(
        HashSet<string> techStack,
        MarketSnapshot snapshot)
    {
        // Count how many high-demand skills (top quartile by frequency) the project covers
        if (snapshot.SkillFrequency.Count == 0)
            return "Medium";

        var sortedFrequencies = snapshot.SkillFrequency.Values.OrderByDescending(v => v).ToList();
        int topQuartileThreshold = sortedFrequencies.Count >= 4
            ? sortedFrequencies[sortedFrequencies.Count / 4]
            : sortedFrequencies[0] / 2;

        int highDemandCount = snapshot.SkillFrequency
            .Count(kv => kv.Value >= topQuartileThreshold && techStack.Contains(kv.Key));

        return highDemandCount switch
        {
            >= 3 => "Complex",
            2 => "Medium",
            _ => "Simple"
        };
    }

    private static List<TalkingPoint> GenerateTalkingPoints(
        PortfolioProject project,
        List<SkillGap> gaps,
        UserProfile profile)
    {
        var gapLookup = gaps.ToDictionary(g => g.SkillName, StringComparer.OrdinalIgnoreCase);
        var profileSkills = profile.Skills.ToDictionary(s => s.SkillName, StringComparer.OrdinalIgnoreCase);
        var points = new List<TalkingPoint>();

        foreach (var skill in project.TechStack)
        {
            if (!gapLookup.TryGetValue(skill, out var gap))
                continue;

            bool hasExistingExperience = profileSkills.TryGetValue(skill, out var existing)
                && existing.ProficiencyLevel > 0;

            string proofStatement = hasExistingExperience
                ? $"I have {existing!.YearsOfExperience:F1} years of experience with {skill} at level {existing.ProficiencyLevel}/5, " +
                  $"and this project pushed me from level {gap.CurrentLevel} toward level {gap.RequiredLevel} " +
                  $"through hands-on implementation in a production-style codebase."
                : $"This project gave me hands-on experience with {skill}, moving from level {gap.CurrentLevel} " +
                  $"toward the market-required level {gap.RequiredLevel} through practical implementation.";

            points.Add(new TalkingPoint
            {
                Skill = skill,
                Point = $"In {project.Title}, I used {skill} to address {gap.RecommendedAction.ToLowerInvariant()}.",
                ProofStatement = proofStatement
            });
        }

        return points;
    }

    private static List<StarStory> GenerateStarStories(
        PortfolioProject project,
        UserProfile profile)
    {
        var stories = new List<StarStory>();
        var recentExperience = profile.Experiences
            .OrderByDescending(e => e.EndDate ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

        var priorContext = recentExperience is not null
            ? $"at {recentExperience.Company} as a {recentExperience.Role}"
            : "in my previous role";

        // Story 1: Architecture Decision
        stories.Add(new StarStory
        {
            Theme = "Architecture Decision",
            Situation = $"While building {project.Title}, I needed to design an architecture that would " +
                        $"handle the core challenge: {project.ProblemStatement.Split('.')[0].ToLowerInvariant()}.",
            Task = "I had to evaluate multiple architectural approaches and select one that balanced " +
                   "scalability, maintainability, and the specific technical requirements.",
            Action = $"I chose {project.Architecture.Split(',')[0].ToLowerInvariant()} after evaluating " +
                     $"alternatives. I drew on my experience {priorContext} to identify potential pitfalls " +
                     "early. I documented decision records for each significant choice, including what I rejected and why.",
            Result = $"The architecture cleanly separates concerns across {project.TechStack.Count} technologies, " +
                     "enables independent scaling of components, and has proven straightforward to extend with new features."
        });

        // Story 2: Performance Challenge
        stories.Add(new StarStory
        {
            Theme = "Performance Challenge",
            Situation = $"During development of {project.Title}, I encountered performance bottlenecks " +
                        "when integrating multiple components under realistic load conditions.",
            Task = "I needed to identify root causes and optimize without sacrificing the clean architecture " +
                   "or introducing undue complexity.",
            Action = "I implemented profiling and benchmarking using targeted metrics, identified the hot paths, " +
                     $"and applied focused optimizations. Key techniques included caching strategies, " +
                     "async processing, and efficient data access patterns.",
            Result = "Achieved measurable performance improvements while maintaining code readability. " +
                     "The benchmarks became part of the CI pipeline to prevent future regressions."
        });

        // Story 3: Team Collaboration (only if user has team experience)
        if (recentExperience is not null)
        {
            stories.Add(new StarStory
            {
                Theme = "Team Collaboration",
                Situation = $"Drawing from my experience {priorContext}, I designed {project.Title} " +
                            "with collaboration and code review practices in mind, as if working with a distributed team.",
                Task = "I needed to ensure the project followed conventions that any team member could " +
                       "understand, extend, and maintain independently.",
                Action = "I established clear module boundaries, wrote comprehensive documentation, " +
                         "created pull request templates, and set up automated quality gates. " +
                         $"I structured the {project.Backlog.Count}-item backlog into sprint-sized increments " +
                         "that could be independently delivered.",
                Result = "The project structure enables parallel development, has 100% automated test coverage " +
                         "for critical paths, and includes onboarding documentation that a new contributor " +
                         "could follow to make their first contribution within a day."
            });
        }

        return stories;
    }
}
