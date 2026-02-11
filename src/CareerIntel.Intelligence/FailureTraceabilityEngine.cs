using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Traces interview failures back to root causes and generates surgical learning plans.
/// Flow: Question → Why Failed → Missing Concept → Required Skill → Learning Unit → Exercise
/// This enables systematic improvement from every failure.
/// </summary>
public sealed class FailureTraceabilityEngine
{
    /// <summary>
    /// Analyze an interview failure and trace it to actionable learning.
    /// </summary>
    public FailureAnalysis TraceFailure(InterviewFeedback feedback)
    {
        var analysis = new FailureAnalysis
        {
            InterviewDate = feedback.InterviewDate,
            Company = feedback.Company,
            VacancyId = feedback.VacancyId
        };

        // Parse weak areas and failed questions
        foreach (var weakArea in feedback.WeakAreas)
        {
            var trace = TraceWeakArea(weakArea);
            analysis.Traces.Add(trace);
        }

        // Extract patterns across multiple failures
        analysis.Patterns = IdentifyPatterns(analysis.Traces);

        return analysis;
    }

    /// <summary>
    /// Analyze multiple feedback entries to find recurring failure patterns.
    /// </summary>
    public List<FailurePattern> IdentifyRecurringIssues(List<InterviewFeedback> feedbackHistory)
    {
        var allTraces = new List<FailureTrace>();

        foreach (var feedback in feedbackHistory)
        {
            var analysis = TraceFailure(feedback);
            allTraces.AddRange(analysis.Traces);
        }

        // Group by concept to find recurring issues
        var conceptGroups = allTraces
            .GroupBy(t => t.MissingConcept)
            .Where(g => g.Count() >= 2) // Appears in 2+ interviews
            .Select(g => new FailurePattern
            {
                Concept = g.Key,
                Frequency = g.Count(),
                RelatedSkills = g.SelectMany(t => t.RequiredSkills).Distinct().ToList(),
                FirstSeen = g.Min(t => t.InterviewDate),
                LastSeen = g.Max(t => t.InterviewDate),
                Priority = CalculatePriority(g.Count(), g.Max(t => t.InterviewDate))
            })
            .OrderByDescending(p => p.Priority)
            .ToList();

        return conceptGroups;
    }

    private static FailureTrace TraceWeakArea(string weakArea)
    {
        // Map weak area to concept → skill → learning unit
        var normalized = weakArea.ToLowerInvariant();

        var trace = new FailureTrace
        {
            Question = weakArea,
            InterviewDate = DateTimeOffset.UtcNow
        };

        // Pattern matching to identify root cause
        if (normalized.Contains("async") || normalized.Contains("await") || normalized.Contains("task"))
        {
            trace.MissingConcept = "Asynchronous Programming";
            trace.RequiredSkills = ["C#", "Async/Await", "Task Parallel Library"];
            trace.LearningUnits =
            [
                new LearningUnit
                {
                    Title = "Async/Await Fundamentals",
                    Duration = 2,
                    Type = "Tutorial",
                    Url = "https://docs.microsoft.com/dotnet/csharp/async",
                    Exercises =
                    [
                        "Convert synchronous HTTP client to async",
                        "Implement async file I/O",
                        "Handle async exceptions properly"
                    ]
                }
            ];
        }
        else if (normalized.Contains("solid") || normalized.Contains("design principle"))
        {
            trace.MissingConcept = "SOLID Principles";
            trace.RequiredSkills = ["Object-Oriented Design", "Software Architecture"];
            trace.LearningUnits =
            [
                new LearningUnit
                {
                    Title = "SOLID Principles Explained",
                    Duration = 3,
                    Type = "Video + Practice",
                    Url = "https://www.youtube.com/watch?v=pTB30aXS77U",
                    Exercises =
                    [
                        "Refactor god class using SRP",
                        "Apply Open/Closed with strategy pattern",
                        "Use dependency injection for DIP"
                    ]
                }
            ];
        }
        else if (normalized.Contains("garbage collection") || normalized.Contains("memory"))
        {
            trace.MissingConcept = "Memory Management";
            trace.RequiredSkills = [".NET Internals", "Performance Optimization"];
            trace.LearningUnits =
            [
                new LearningUnit
                {
                    Title = "Garbage Collection in .NET",
                    Duration = 2,
                    Type = "Article",
                    Url = "https://docs.microsoft.com/dotnet/standard/garbage-collection/fundamentals",
                    Exercises =
                    [
                        "Profile memory usage with dotMemory",
                        "Implement IDisposable correctly",
                        "Understand Gen 0/1/2 collections"
                    ]
                }
            ];
        }
        else if (normalized.Contains("system design") || normalized.Contains("scalability") || normalized.Contains("architecture"))
        {
            trace.MissingConcept = "System Design";
            trace.RequiredSkills = ["Distributed Systems", "Scalability", "Architecture"];
            trace.LearningUnits =
            [
                new LearningUnit
                {
                    Title = "System Design Fundamentals",
                    Duration = 4,
                    Type = "Interactive Course",
                    Url = "https://github.com/donnemartin/system-design-primer",
                    Exercises =
                    [
                        "Design a URL shortener",
                        "Design a rate limiter",
                        "Design Twitter timeline"
                    ]
                }
            ];
        }
        else if (normalized.Contains("database") || normalized.Contains("sql") || normalized.Contains("indexing"))
        {
            trace.MissingConcept = "Database Optimization";
            trace.RequiredSkills = ["SQL", "Database Design", "Performance Tuning"];
            trace.LearningUnits =
            [
                new LearningUnit
                {
                    Title = "SQL Performance Tuning",
                    Duration = 3,
                    Type = "Hands-on Lab",
                    Url = "use-the-index-luke.com",
                    Exercises =
                    [
                        "Create appropriate indexes",
                        "Optimize slow queries with EXPLAIN",
                        "Design efficient join strategies"
                    ]
                }
            ];
        }
        else
        {
            // Generic fallback
            trace.MissingConcept = weakArea;
            trace.RequiredSkills = [weakArea];
            trace.LearningUnits =
            [
                new LearningUnit
                {
                    Title = $"{weakArea} - Self Study",
                    Duration = 2,
                    Type = "Research",
                    Url = $"Search: '{weakArea} tutorial'",
                    Exercises = [$"Practice {weakArea} problems", "Build small project using {weakArea}"]
                }
            ];
        }

        trace.EstimatedHoursToFix = trace.LearningUnits.Sum(u => u.Duration);

        return trace;
    }

    private static List<FailurePattern> IdentifyPatterns(List<FailureTrace> traces)
    {
        var patterns = new List<FailurePattern>();

        // Group by concept
        var conceptGroups = traces.GroupBy(t => t.MissingConcept);

        foreach (var group in conceptGroups)
        {
            patterns.Add(new FailurePattern
            {
                Concept = group.Key,
                Frequency = group.Count(),
                RelatedSkills = group.SelectMany(t => t.RequiredSkills).Distinct().ToList(),
                FirstSeen = group.Min(t => t.InterviewDate),
                LastSeen = group.Max(t => t.InterviewDate),
                Priority = group.Count() * 10 // Simple priority calculation
            });
        }

        return patterns.OrderByDescending(p => p.Priority).ToList();
    }

    private static int CalculatePriority(int frequency, DateTimeOffset lastSeen)
    {
        var daysSinceLastFailure = (DateTimeOffset.UtcNow - lastSeen).TotalDays;

        // Higher frequency = higher priority
        // Recent failures = higher priority
        var priorityScore = frequency * 10;

        if (daysSinceLastFailure <= 7)
            priorityScore += 20; // Recent failure = urgent
        else if (daysSinceLastFailure <= 30)
            priorityScore += 10;

        return priorityScore;
    }
}

/// <summary>
/// Complete analysis of an interview failure with actionable learning plan.
/// </summary>
public sealed class FailureAnalysis
{
    public DateTimeOffset InterviewDate { get; set; }
    public string Company { get; set; } = string.Empty;
    public string VacancyId { get; set; } = string.Empty;
    public List<FailureTrace> Traces { get; set; } = [];
    public List<FailurePattern> Patterns { get; set; } = [];
}

/// <summary>
/// Traces a single failure from question → concept → skill → learning unit.
/// </summary>
public sealed class FailureTrace
{
    public string Question { get; set; } = string.Empty;
    public DateTimeOffset InterviewDate { get; set; }
    public string MissingConcept { get; set; } = string.Empty;
    public List<string> RequiredSkills { get; set; } = [];
    public List<LearningUnit> LearningUnits { get; set; } = [];
    public int EstimatedHoursToFix { get; set; }
}

/// <summary>
/// A recurring failure pattern across multiple interviews.
/// </summary>
public sealed class FailurePattern
{
    public string Concept { get; set; } = string.Empty;
    public int Frequency { get; set; }
    public List<string> RelatedSkills { get; set; } = [];
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public int Priority { get; set; }
}

/// <summary>
/// A specific, actionable learning unit with exercises.
/// </summary>
public sealed class LearningUnit
{
    public string Title { get; set; } = string.Empty;
    public int Duration { get; set; } // Hours
    public string Type { get; set; } = string.Empty; // Tutorial, Video, Article, Lab, etc.
    public string Url { get; set; } = string.Empty;
    public List<string> Exercises { get; set; } = [];
}
